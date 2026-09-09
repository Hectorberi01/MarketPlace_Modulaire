# Audit des routes — Admin, Vendeur, Client, Driveur

> 9 septembre 2026. Quatre surfaces, ~350 routes, après le passage d'un découpage par
> appareil à un découpage par audience. Chaque handler a été ouvert ; rien n'a été déduit
> d'un nom de route.

---

## Verdict

**Aucun trou d'argent, aucun IDOR exploitable sur les quatre BFF.**

C'est un résultat qui mérite d'être dit tel quel, parce qu'il contraste avec l'audit de
l'hôte API mené la veille — lequel avait livré trois failles réelles, dont
`POST /api/returns/{id}/refund` ouvert à tout compte authentifié alors qu'il créditait la
cagnotte. Les BFF, eux, tiennent : l'identité vient toujours du jeton, la propriété est
vérifiée, les rôles sont exigés à la porte.

Ce qui reste est d'une autre nature : **des oracles d'énumération**, et un **rejeu de
signature** côté dispatch. Rien qui laisse voler de l'argent ; de quoi cartographier des
comptes, harceler par e-mail, et réactiver un coursier suspendu.

| Surface | Failles | Nature |
|---|---|---|
| Admin | aucune | — |
| Vendeur | 2 | énumération de comptes (routes anonymes) |
| Client | 4 | énumération, dont 1 avec envoi d'e-mail à la victime |
| Driveur | aucune | — |
| Service de dispatch | 1 | rejeu de signature HMAC |

---

## 1. Ce qui doit être corrigé

### 1.1 `POST /client/auth/email/resend` — énumération **et** bombardement e-mail

`ClientAuthEndpoints.cs` — route anonyme.

```csharp
var user = await identity.GetUserByEmailAsync(request.Email, ct);
if (user is null) return Results.Ok(new { userId = (Guid?)null });
_ = await sender.Send(new RequestEmailVerificationCommand(user.Id), ct);
return Results.Ok(new { userId = (Guid?)user.Id });
```

Sans aucun jeton, on teste une liste d'adresses et on sait lesquelles ont un compte. Et
chaque test positif **envoie un vrai e-mail à la victime** — harcèlement, et usure de la
réputation d'expéditeur du domaine. Le limiteur `auth` borne le rythme, pas la
faisabilité.

Le fichier documente lui-même la faille et le plan de correction. Il n'a pas été appliqué.

**Correction** : réponse constante `{ sent: true }`, et `ConfirmEmailCommand` prend
l'adresse plutôt que l'identifiant — ce qui suppose une livraison conjointe de
l'application, qui conserve l'e-mail saisi.

Le même fichier fait DÉJÀ ce qu'il faut sur `password/forgot` : il ignore délibérément le
résultat pour ne rien révéler. Les deux routes se contredisent à trente lignes d'écart.

### 1.2 `POST /seller/auth/verify/resend` — le même défaut, même motif

`SellerRegistrationEndpoints.cs` — route anonyme. Rend `UserId: null` si l'adresse est
inconnue, `UserId: <guid>` sinon : existence du compte, plus le GUID interne d'un tiers.

Et là encore, `ForgotPasswordAsync` dans le **même fichier** ignore son résultat pour ne
rien divulguer.

### 1.3 `POST /seller/auth/register` — quatre réponses, quatre informations

Route anonyme, quatre issues distinctes : adresse inconnue, adresse connue et éligible
(avec `UserId`), `409 already_seller`, `403 not_eligible`. On n'apprend pas seulement
qu'un compte existe : on apprend **que c'est un vendeur**, et **qu'il est suspendu**. De
la donnée concurrentielle et personnelle, servie sans authentification.

**Correction** : réponse uniforme, et les cas 409/403 déplacés APRÈS la vérification du
code envoyé par e-mail.

### 1.4 Rejeu de signature HMAC — service de dispatch

`Security/InboundSignature.cs` ne fait entrer dans la chaîne signée **ni horodatage, ni
nonce, ni identifiant de message**. Un corps signé capturé une fois — journal, proxy,
dépôt de logs — reste valable indéfiniment.

Deux des trois routes s'en accommodent : `orders/ready-for-delivery` est idempotente sur
l'identifiant de commande, `orders/cancelled` est bénigne. La troisième mord :

> **Rejouer un ancien message « standing = Active » réactive un coursier suspendu.**
> `CourierService.ApplyStandingAsync` recopie le statut sans condition — c'est
> précisément ce qu'on lui demande de faire.

**Correction** : un en-tête d'horodatage inclus dans la chaîne signée, rejet au-delà de
cinq minutes. Le même correctif vaut pour le sens inverse
(`PartnerServiceSignature`, côté marketplace), qui a exactement la même forme.

---

## 2. Incohérences à trancher : 403 contre 404

Quatre routes distinguent « interdit » de « introuvable », alors que le reste du dépôt
répond systématiquement 404 pour ne pas servir d'oracle. Aucune ne divulgue de contenu —
elles confirment seulement l'existence d'une ressource qu'on n'a pas le droit de voir.

| Route | Handler fautif | Voisin qui fait l'inverse |
|---|---|---|
| `POST /client/orders/{id}/return` | `RequestReturnCommand` : 403 `returns.not_owner` | les 4 routes `/{id}` voisines appellent `LoadOwnedOrderAsync` |
| `GET /client/conversations/{id}/messages` | 403 `messaging.not_participant` | `ChatHub` réutilise le MÊME message pour les deux cas, exprès |
| `PATCH /client/notifications/{id}/read` | 403 `notifications.not_owner` | le `DELETE` d'à côté rend 404 « pour ne pas révéler son existence » |
| `POST /client/disputes` | 403 `disputes.not_party` | — |

La première est la plus nette : dans `ClientOrderEndpoints.cs`, c'est **la seule route
`/{id}` du fichier** qui ne charge pas la commande possédée avant d'agir. Trois lignes
plus haut et trois lignes plus bas, ses voisines le font.

**Correction** : appeler `LoadOwnedOrderAsync` sur la première, mapper les trois autres
sur 404.

---

## 3. Points relevés, à arbitrer

- **Un jeton vaut sur les cinq hôtes.** Clé, émetteur et audience partagés : un jeton
  livreur volé ouvre le panier, les adresses, la cagnotte et la messagerie du même compte.
  Pas d'escalade — tout est scopé par `sub` — mais des audiences distinctes par hôte
  réduiraient la portée d'un vol. C'est aussi ce qui permet à l'application coursier de
  parler au BFF et au dispatch avec une seule connexion : le gain a un prix.
- **`POST /admin/disputes/{id}/resolve` accepte un montant de remboursement et est ouvert
  au rôle `Moderator`.** Pas exploitable sans ce rôle, mais c'est une décision d'argent
  confiée à un non-admin — le raisonnement même qui a fait retirer `/admin/outbox` aux
  modérateurs. À trancher : rôle Admin, ou plafond.
- **Aucun limiteur de débit dans le service de dispatch.** `PUT /api/courier/position`
  est l'écriture la plus fréquente du système, et les routes `/internal` lisent le corps
  entier en mémoire **avant** de vérifier la signature — borné seulement par la limite
  Kestrel par défaut, 30 Mo.
- **`POST /driver/wallet/withdrawals` n'est pas plafonné en débit** : la politique `auth`
  ne couvre que `/driver/auth`. Impact borné par le solde et la validation admin, mais
  c'est la route la plus sensible du BFF et la seule sans plafond.
- **`take` non borné** sur le relevé de cagnotte client (`take ?? 50`) : `take=10000000`
  passe tel quel au dépôt. Données propres, coût serveur libre.
- **Deux définitions concurrentes d'« implication dans une commande »** côté vendeur :
  lignes de commande portant le `SellerId`, contre existence d'une expédition. Documenté
  et assumé, mais un vendeur ayant des lignes sans expédition voit sa commande et pas son
  litige.

---

## 4. Ce qui est solide

**Admin** — les 44 groupes passent par `MapAdminGroup` (rôle Admin) ou `MapModerationGroup`.
Toutes les routes d'argent sont derrière le rôle Admin. Cookie HttpOnly, garde anti-CSRF,
rotation du refresh, CORS fermé par défaut, `JwtKeyGuard`.

**Vendeur** — le rôle est exigé à la porte sur les 18 groupes, aucune route n'accepte de
`SellerId` depuis le corps ou la query, et **chaque** mutation par identifiant appelle son
helper de propriété — offres, produits, expéditions, retours, avis, litiges, stock, pièces
KYB.

**Client** — anti-IDOR complet sur commandes, paiements, retours, litiges, adresses,
moyens de paiement, cagnotte et retraits. Le hub `/client/hubs/chat` est sain :
`JoinConversation` exige la participation et rend le même message dans les deux cas
d'échec.

**Driveur et dispatch** — l'identité vient d'un chemin unique, `UserId` et `CourierId` sont
correctement distingués, l'assignation de la course est vérifiée avant toute transition et
avant l'émission du paiement. Les trois routes `/internal` vérifient la signature en
première instruction, sur le corps **brut** lu avant désérialisation. `CourierHub`
n'expose aucune méthode appelable par le client.

Sur les quatre hôtes, la politique d'autorisation par repli transforme un
`RequireAuthorization` oublié en 401 plutôt qu'en route anonyme silencieuse.
