# Audit des routes — passe 2

> 9 septembre 2026. Reprise après la correction des points 1 et 2 de la première passe.
> Cette fois la recherche part de la **forme du défaut**, pas de la liste des routes :
> c'est ce qui avait laissé passer `reviews.not_owner`, corrigé hier après coup.

---

## Verdict

**Les corrections apportées tiennent.** Vérifiées ligne à ligne : plus aucun appelant ne
passe un Guid aux deux commandes de confirmation, aucun validateur FluentValidation ne
s'intercale pour produire une erreur différente selon l'adresse, et le contournement par
mot de passe invalide sur `/seller/auth/register` est réellement fermé.

**Mais elles étaient entourées.** Le balayage par forme trouve neuf familles de défauts
non signalées par la première passe, dont deux plus graves que tout ce qui a été corrigé :

| # | Défaut | Gravité |
|---|---|---|
| 2.1 | Deux routes d'argent ouvertes au rôle `Moderator` | **Élevée** |
| 2.2 | `POST /*/password/reset` — énumération en une requête, sur les quatre surfaces | **Élevée** |
| 2.3 | `POST /client/auth/register` — l'oracle fermé côté vendeur, resté ouvert côté client | **Élevée** |
| 2.4 | Six routes : 404 avec corps contre 404 nu | Moyenne |
| 2.5 | Dispatch : `job.not_yours`, et l'ordre dans `Accept` | Moyenne |
| 2.6 | Chronométrage BCrypt sur inscription et connexion | Moyenne |
| 2.7 | Renvoi de code sans garde de statut : bombardement de comptes vérifiés | Moyenne |
| 2.8 | `take` non borné, corps de requête non borné | Faible |

La leçon de méthode est en §3, et elle vaut d'être lue avant les corrections.

---

## 1. Ce qui a été corrigé, et qui tient

Vérifié personnellement, pas déduit :

- `ConfirmEmailCommand` et `VerifyEmailCodeCommand` prennent l'adresse. Les trois
  appelants restants passent bien `(Email, …)`. Aucune surcharge Guid ne subsiste.
- **Aucun validateur n'existe** pour ces deux commandes, et `ValidationBehavior`
  court-circuite proprement en son absence : rien ne s'exécute avant le handler qui
  pourrait varier selon l'adresse.
- Les deux handlers hachent **avant** le test de nullité, et écrasent l'erreur du domaine.
- Sur `/seller/auth/register`, faire partir `RegisterUserCommand` en premier ferme bien le
  contournement : les seuls codes que le handler et son validateur peuvent produire hors
  conflit sont dérivés de la saisie, identiques quel que soit l'état de la base. Et le
  handler n'a **aucun effet de bord observable** quand il échoue sur une adresse prise —
  ni e-mail, ni ligne d'outbox, ni compteur : le retour précède le hachage, l'insertion et
  la publication.
- Les cinq sites du point 2 répondent désormais avec le code et le message du cas
  « inexistant » voisin.

Un point de vigilance en revanche : **aucun test ne fixe ces garanties**. Il n'existe
aucun test d'uniformité de réponse dans le dépôt, ni projet d'intégration pour un seul
des BFF. Tout ce qui précède est tenu par des commentaires, et régressera en silence.

---

## 2. Ce que la passe 2 trouve

### 2.1 Deux routes d'argent ouvertes au rôle `Moderator`

`ApiAuthorization.MapAdminGroup` exige `RequireRole(AdminRole, ModeratorRole)` — Admin
**ou** Modérateur. Les groupes qui manipulent de l'argent doivent donc rechaîner un
`RequireRole("Admin")`. `SettlementEndpoints` et `BillingEndpoints` le font. Deux autres
non :

**`POST /api/returns/{id}/refund`** — `ReturnsEndpoints.cs:51`

```csharp
var backOffice = app.MapAdminGroup("/api/returns").WithTags("Returns");
```

La chaîne est réelle : `RefundAsync` → `ApproveReturnRefundCommand` →
`ReturnRefundApprovedIntegrationEvent` → `CreditCustomerWalletOnReturnRefundApprovedHandler`.
Un modérateur enchaîne `approve → received → refund` sur n'importe quel retour et crédite
la cagnotte d'un client, à la charge de la plateforme.

C'est exactement le raisonnement qui avait fait retirer `/admin/outbox` aux modérateurs.
Et la preuve que l'auteur connaissait la parade est trois fichiers plus loin :
`SettlementEndpoints.cs:18` chaîne le `RequireRole("Admin")` que celui-ci oublie.

**`POST /api/shipments/{id}/deliver`** — `ShippingEndpoints.cs:39`

Même groupe non durci. `Shipment.MarkDelivered` déclenche
`ReleaseSellerEarningsOnShipmentDeliveredHandler` : la séquestre du vendeur est libérée.
Le contraste est net — `POST /api/orders/{id}/deliver`, l'autre chemin vers la même
libération, est explicitement `RequireRole("Admin")`. La route expédition est son jumeau
non gardé.

**Correction** : une ligne sur chacun des deux groupes, calquée sur `SettlementEndpoints`.

**À trancher séparément** : `POST /admin/disputes/{id}/resolve` accepte un
`RefundAmount` et passe par `MapModerationGroup`. J'ai suivi tous les abonnés de
`DisputeResolvedIntegrationEvent` : seul Notifications le consomme, **aucun règlement
automatique**. Le modérateur fixe donc un montant qu'un humain paiera ensuite. Ce n'est
pas une fuite d'argent aujourd'hui ; ça le devient le jour où quelqu'un branche cet
événement sur Settlement.

### 2.2 `POST /*/password/reset` — l'oracle le plus économique du dépôt

`ResetPasswordCommandHandler` collapse bien le cas « adresse inconnue »… puis propage
**verbatim** l'erreur du domaine :

```csharp
if (user is null)
{
    // Message générique (pas d'énumération de comptes).
    return Result.Failure(Error.Validation("identity.user.reset_invalid", "Lien de réinitialisation invalide."));
}

var providedHash = _tokenGenerator.Hash(command.Token);
var result = user.ResetPassword(providedHash, _passwordHasher.Hash(command.NewPassword), DateTime.UtcNow);
if (result.IsFailure)
{
    return result;              // ← l'erreur du domaine, telle quelle
}
```

Et `User.ResetPassword` répond `Error.Conflict("identity.user.no_reset_pending", …)`
quand aucune réinitialisation n'est en cours.

Donc, **sans aucun appel préalable à `forgot`** :

| Adresse | Réponse |
|---|---|
| inconnue | `400` — `title: identity.user.reset_invalid` |
| connue | `409` — `title: identity.user.no_reset_pending` |

Statut différent **et** code différent. Une requête, aucun e-mail envoyé, aucun compte
créé, aucun état modifié. Un troisième état — `identity.user.reset_expired` — révèle en
prime qu'un compte a demandé une réinitialisation il y a plus d'une heure.

Ça marche sur `/client`, `/seller`, `/driver` et `/admin/auth/reset-password` — cette
dernière permettant d'énumérer les adresses **des administrateurs**.

Le commentaire « pas d'énumération de comptes » ne ment pas : il est juste posé sur la
seule branche qui est effectivement uniforme, trois lignes au-dessus de la fuite.

**Correction** : la même que `ConfirmEmailCommandHandler` — une erreur unique pour tous
les refus, et le hachage du nouveau mot de passe avant le test de nullité.

### 2.3 `POST /client/auth/register` — l'oracle fermé côté vendeur, resté ouvert côté client

```csharp
private static async Task<IResult> RegisterAsync(RegisterRequest request, ISender sender, CancellationToken ct)
{
    var result = await sender.Send(new RegisterUserCommand(…), ct);
    return result.Match(id => Results.Ok(new { id }));
}
```

Adresse prise → `409` + `title: identity.user.email_taken`. Adresse libre → `200 { id }`.
**Existence et identifiant interne, en une requête anonyme** — la paire exacte que la
correction vendeur a passé cent lignes à supprimer, trois fichiers plus loin.

La branche `identity.user.phone_taken` énumère en plus les **numéros de téléphone**, un
identifiant qu'aucune surface n'avait encore été durcie contre.

`POST /api/identity/auth/register` a la même forme, et rend le Guid dans le corps **et**
dans l'en-tête `Location`.

Un dérivé qui mérite d'être connu : sonder une adresse libre **crée un vrai compte**. On
peut donc squatter n'importe quelle adresse non inscrite — la victime ne pourra plus
s'inscrire, et recevra un code qu'elle n'a pas demandé.

### 2.4 Six routes : 404 avec corps contre 404 nu

Le point 2 d'hier portait sur 403 contre 404. Il existe une variante plus discrète : les
deux branches répondent 404, mais l'une rend un `ProblemDetails` avec le code d'erreur
dans `title`, l'autre un corps vide.

```csharp
var result = await sender.Send(new GetOrderQuery(id), ct);
if (result.IsFailure)
{
    return result.Match(order => Results.Ok(order));   // 404 + title "ordering.not_found"
}
if (!principal.IsInRole("Admin") && CurrentUserId(principal) != result.Value.BuyerId)
{
    return Results.NotFound();                          // 404, corps vide
}
```

Six sites : `GET /api/orders/{id}`, `POST /api/orders/{id}/cancel`,
`GET /api/payments/{id}`, `GET /api/payments/order/{orderId}`, `GET /api/returns/{id}`,
et `GET /seller/disputes/{id}` + `POST /seller/disputes/{id}/messages`.

Le voisin qui fait juste est dans le même dossier : `PaymentsEndpoints.InitiateAsync`
renvoie `Results.NotFound()` dans les **deux** branches, avec le commentaire qui explique
pourquoi. `DisputesEndpoints.GuardOwnDisputeAsync` aussi.

Le cas vendeur est le plus large : n'importe quel compte à rôle `Seller` énumère tous les
litiges de la plateforme.

### 2.5 Dispatch : `job.not_yours`, et l'ordre dans `Accept`

`JobProgressService.MutateAsync` est le passage unique de six routes coursier. Il répond
404 `job.not_found` pour une course inexistante et **403 `job.not_yours`** pour une course
réelle assignée à un concurrent : un coursier parcourt les identifiants et cartographie la
table des courses.

J'avais conservé ce 403 hier, en faisant valoir qu'annoncer « prise par un autre » sert le
coursier dans une course simultanée. **Cet argument ne vaut que pour `Accept`**, pas pour
les six transitions de `MutateAsync`, où l'appelant n'a par construction jamais eu la
course. Je me suis trompé de portée : à corriger.

Et `DeliveryJob.Accept` a le défaut d'ordre corrigé hier sur `Conversation.SendMessage` :

```csharp
if (Status != DeliveryJobStatus.Offered)
{
    return Result.Failure(Error.Conflict("job.already_taken", "Cette course a déjà été prise."));
}

var offer = _offers.FirstOrDefault(o => o.CourierId == courierId && o.IsLive(nowUtc));
```

Le contrôle d'**état** précède le contrôle de **participation** : un coursier à qui la
course n'a jamais été proposée en apprend le cycle de vie. Quatre réponses distinctes au
total sur cette route.

À noter, et c'est ce qui rend le tout atteignable : `/api/courier` n'exige aucun rôle, et
les cinq hôtes partagent une clé de signature. **N'importe quel jeton acheteur y entre.**

### 2.6 Chronométrage : BCrypt à facteur 12

Deux endroits où le corps est uniforme et l'horloge ne l'est pas.

`RegisterUserCommandHandler` retourne sur `email_taken` **avant** `_passwordHasher.Hash` :
adresse libre ≈ 300 ms de BCrypt, adresse prise ≈ 10–30 ms. Un ordre de grandeur, très
au-dessus de la gigue réseau. La réponse constante de `/seller/auth/register` est défaite
au chronomètre.

`LoginCommandHandler` a la même forme :

```csharp
if (user is null || !_passwordHasher.Verify(user.PasswordHash, command.Password))
```

Le `||` court-circuite : `Verify` ne s'exécute jamais pour une adresse inconnue. Sur les
cinq hôtes.

C'est l'inverse exact du soin pris hier dans `ConfirmEmailCommandHandler`, où le hachage a
été remonté avant un retour anticipé pour égaliser un écart sub-milliseconde. L'écart de
300 ms deux fichiers plus loin n'était pas vu.

**Correction** : hacher un mot de passe factice, ou vérifier contre un hash constant, sur
la branche manquante.

### 2.7 Renvoi de code : aucune garde de statut

Le commentaire de `ResendEmailAsync` parle d'« un compte existant **non vérifié** ». Le
code ne contrôle rien de tel : `RequestEmailVerificationCommand` charge le compte et
appelle `BeginEmailVerification`, qui ne valide que la non-vacuité du jeton. Ni
`EmailVerified`, ni `UserStatus`.

Conséquence : avec une liste d'adresses clients confirmées, **tout compte existant reçoit
un e-mail de vérification réel** — vérifié, actif, suspendu ou supprimé. C'est le
harcèlement et l'usure de réputation d'expéditeur que la correction 1.1 nommait comme sa
raison d'être, encore présent par le canal de l'effet de bord.

Le limiteur ne le borne pas utilement : la politique `auth` est commune aux cinq surfaces
— **30 requêtes / 60 s, partitionnées par IP seule**, file d'attente nulle. Les routes qui
envoient du courrier partagent le même seau que `login`. Trente e-mails par minute et par
IP vers des victimes choisies n'est pas une borne.

`AuthRateLimiter` le dit lui-même : « La vraie parade est un verrouillage PAR COMPTE après
N échecs… Ce n'est pas fait. »

**Correction** : garde de statut sur la commande, et budget d'envoi par adresse.

### 2.8 Entrées non bornées

`WalletRepositories.ListByOwnerAsync` plancher le `take` à 50 s'il est nul ou négatif,
mais ne le **plafonne** jamais, et les quatre BFF passent la chaîne de requête telle
quelle. `GET /client/wallet/transactions?take=100000000` matérialise tout le grand livre
de l'appelant. Épuisement de ressource, pas divulgation — le filtre `OwnerId` tient.

Le reste de la pagination est propre : `PageRequest.Normalize` borne à 100,
`SearchProductsQuery`, `SellerBff.Paginate` et `AdminMonitoringEndpoints` bornent tous.

Par ailleurs, **aucune limite de taille de corps n'existe dans le dépôt**. Les quatre
routes qui lisent le corps brut avant de vérifier la signature — obligatoire pour un HMAC
sur les octets reçus — encaissent donc jusqu'aux 30 Mo par défaut de Kestrel, et deux de
ces groupes ont explicitement `DisableRateLimiting`. Un `RequestSizeLimit` de quelques
kilo-octets suffit ; ces charges sont minuscules.

---

## 3. Ce que la méthode enseigne

La première passe a ouvert environ 350 routes et en a jugé chacune. Elle a produit un
résultat juste sur ce qu'elle a regardé, et a manqué :

- un site de la même forme que trois qu'elle avait trouvés (`reviews.not_owner`), parce
  qu'il était derrière une route qu'elle n'a pas ouverte ;
- une route d'inscription client identique à la route vendeur qu'elle a incriminée ;
- la variante « 404 contre 404 » de l'écart qu'elle a relevé en « 403 contre 404 » ;
- deux groupes d'argent mal gardés, parce que la garde est dans le `MapGroup` et pas dans
  le handler.

Les quatre sont invisibles à une lecture route par route et évidents à une recherche par
forme : tous les `Error.Forbidden`, tous les `MapAdminGroup` sans `RequireRole("Admin")`,
tous les `Take(` sans `Math.Clamp`, tous les retours de `Guid` sur une route anonyme.

**Un audit devrait chercher des motifs et vérifier des routes, pas l'inverse.**

Corollaire pratique : chacun de ces motifs tient en une commande `grep`. Ils feraient une
bien meilleure barrière en intégration continue qu'en document.

---

## 4. Ordre de réparation

> **Les neuf points sont traités (9 septembre 2026).** Ce qui suit garde l'ordre
> d'exécution, avec ce qui a réellement été fait sous chacun.


1. **2.1** — deux lignes, deux groupes. C'est le seul point où de l'argent bouge.
2. **2.2** — `ResetPasswordCommandHandler`, une erreur unique. Ferme l'oracle le moins
   cher, sur quatre surfaces dont l'admin.
3. **2.3** — appliquer le traitement vendeur à `/client/auth/register` et
   `/api/identity/auth/register`, et retirer le Guid des deux réponses.
4. **2.5** — `job.not_yours` et l'ordre dans `Accept`.
5. **2.4** — six sites, mécanique.
6. **2.6** — égaliser le coût BCrypt sur les deux branches.
7. **2.7** — garde de statut sur le renvoi, budget par adresse.
8. **2.8** — plafonner `take` (un seul endroit couvre les quatre hôtes), poser un
   `RequestSizeLimit` sur les quatre routes signées.
9. **Des tests d'uniformité**, sans quoi les points 1 à 3 régresseront sans bruit.

### Ce qui a été fait

| # | Correction | Portée |
|---|---|---|
| 2.1 | `RequireRole("Admin")` rechaîné sur `/api/returns` et `/api/shipments` | Le groupe ENTIER, pas la seule route qui paie : `approve` et `received` sont les marches qui y mènent |
| 2.2 | `InvalidReset` unique dans `ResetPasswordCommandHandler`, hachages remontés | 4 surfaces |
| 2.3 | Réponse constante et Guid retiré sur `/client/auth/register` et `/api/identity/auth/register` (201 → 200, plus d'en-tête `Location`) | 2 routes |
| 2.5 | `MutateAsync` répond « introuvable » ; `Accept` et `Decline` contrôlent la participation d'abord | 8 routes coursier |
| 2.4 | Les deux branches rendent la même chose sur les 6 sites | Ordering ×2, Payments ×2, Returns, SellerDisputes ×2 |
| 2.6 | BCrypt payé sur les deux branches à l'inscription et à la connexion | 5 hôtes |
| 2.7 | Garde de statut sur les deux renvois + délai minimal d'une minute par compte | Le délai est déduit de `expire − durée de vie`, sans migration |
| 2.8 | `Math.Clamp(…, 1, 200)` dans `WalletRepositories` ; corps signés plafonnés à 64 Kio | 1 point pour 4 hôtes ; 4 routes signées |
| 9 | `tests/Identity.UnitTests/ReponseUniformeTests.cs` | 3 handlers, dont 2 tests qui fixent le coût BCrypt |

### Écarts assumés par rapport au plan

- **`/client/auth/register` n'envoie AUCUN code quand l'adresse est déjà prise**, là où
  `/seller/auth/register` en envoie un. La différence est justifiée : côté vendeur, un
  acheteur déjà inscrit doit prouver qu'il relève la boîte avant qu'on lui rattache une
  boutique. Côté client il n'y a rien à rattacher, et envoyer un e-mail à chaque sondage
  ne servirait qu'à harceler.
- **La garde `EmailVerified` n'est posée que sur le renvoi CLIENT.** Sur la surface
  vendeur, un acheteur déjà vérifié a légitimement besoin d'un code. Ce qui borne
  l'arrosage là-bas, c'est le délai minimal par compte.
- **`BeginEmailVerification` n'a pas été touché.** La garde est au bord, pas dans le
  domaine : le domaine doit continuer d'accepter un compte déjà vérifié, sinon
  l'auto-inscription vendeur casse.
- **`job.already_taken` et `job.offer_expired` restent distincts** — mais ils ne sont plus
  atteignables que par un coursier RÉELLEMENT invité. La distinction lui est utile ; elle
  n'apprend rien à qui n'a pas été sollicité.
- **`POST /admin/disputes/{id}/resolve` n'est pas touché** : c'est la décision produit
  laissée ouverte en §2.1, pas une correction.

---

## 5. Registre — ce qui reste ouvert de la première passe

- Un jeton vaut sur les cinq hôtes (clé, émetteur, audience partagés). C'est aussi ce qui
  permet à l'app coursier de parler au BFF et au dispatch avec une seule connexion. Ce
  choix est ce qui rend §2.5 atteignable depuis un compte acheteur.
- Aucun limiteur de débit dans le service de dispatch.
- `POST /driver/wallet/withdrawals` hors de la politique `auth`.
- Deux définitions concurrentes d'« implication dans une commande » côté vendeur.
- Le BFF Driveur et le dispatch n'ont pas de garde de rôle à la porte : chaque route doit
  penser à appeler `WithCourierAsync` / `ResolveAsync`. Toutes le font aujourd'hui.
