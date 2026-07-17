# 17 — Second audit : ce que le premier avait manqué

> **Date :** 14 juillet 2026. Cet audit a été lancé APRÈS correction des 17 points du
> premier ([doc 15](15-audit-systeme-et-plan-d-action.md)), avec une consigne
> différente : **ne pas reconfirmer ce qui est réglé, chercher le neuf** — y compris les
> régressions introduites par les corrections elles-mêmes.
>
> Verdict : **une nouvelle fuite critique, un risque de double paiement, et une
> régression que j'avais moi-même introduite.** Un audit unique ne suffit jamais ; le
> second angle de vue trouve ce que le premier a normalisé.

---

## Corrigé dans cette passe

| # | Gravité | Problème | État |
|---|---|---|---|
| **B-C1** | 🔴 CRITIQUE | `/shop/{id}` **anonyme** fuyait RIB, commission et pièces KYB de tous les vendeurs | ✅ Corrigé |
| **B-C2** | 🔴 CRITIQUE | Aucun verrou de concurrence sur les agrégats financiers → double retrait, double capture, survente | ✅ Corrigé |
| **B-I1** | 🟠 IMPORTANT | **Régression :** le rate limiter tournait avant l'auth → partitionnement par compte mort | ✅ Corrigé |
| **B-I2** | 🟠 IMPORTANT | `FallbackPolicy` posée sur l'API seule, pas sur les 4 BFF | ✅ Corrigé |
| **B-I4** | 🟠 IMPORTANT | Offre acceptée à **0 XOF** (création + modification) | ✅ Corrigé |
| **B-I3** | 🔴 **RELEVÉ EN CRITIQUE** | Les coupons **n'existaient pas** : code jamais transmis, remise affichée mais jamais accordée | ✅ Corrigé |
| **B-M1** | 🟡 MINEUR | Hub SignalR : on peut rejoindre la conversation d'un tiers | ✅ Corrigé |
| **B-M2** | 🟡 MINEUR | Type de fichier uploadé cru sur parole — et **stocké** tel quel | ✅ Corrigé |

**Tous les points des deux audits sont désormais fermés.**

---

## 🔴 B-C1 — La vitrine boutique fuyait le RIB et les papiers d'identité — CORRIGÉ

Le premier audit avait fermé toutes les **écritures** anonymes. Il avait laissé passer une
**lecture** : `GET /mobile/shop/{sellerId}` et `GET /web/shop/{sellerId}`, tous deux
`AllowAnonymous`, renvoyaient le `SellerSummary` **entier** —

```csharp
decimal CommissionRate,                    // le taux négocié de la plateforme
PayoutAccountSummary? Payout,              // Provider + AccountNumber (Mobile Money EN CLAIR)
IReadOnlyList<KybDocumentSummary> Kyb;     // FileUrl : lien direct vers la pièce d'identité
```

**Scénario, sans aucun compte :** on récolte les `sellerId` sur les fiches produit
(publiques), puis on appelle `/shop/{id}` pour chacun. On moissonne, pour **tous les
vendeurs**, leur numéro de versement Mobile Money et l'URL de leurs documents KYB. Fraude
au virement, usurpation, violation de données personnelles.

Le piège tenait à un mot : le résumé du DTO disait « vue publique ». Il ne l'était pas.

**Correctif.** Un vrai record de vitrine, `SellerPublicSummary` — nom, logo, description,
note, nombre de ventes, rien d'autre. `SellerSummary.ToPublic()` fait la projection, et
les deux endpoints `/shop` l'appellent. Le `SellerSummary` complet reste réservé aux
chemins propriétaire/admin. Vérifié : plus aucun endpoint anonyme ne sérialise le DTO
complet.

> **La leçon.** Fermer l'écriture ne ferme pas la lecture. Un audit qui pense « anonyme =
> écriture » passe à côté de la moitié de la surface.

---

## 🟠 B-I1 — Ma propre régression : le rate limiter ne partitionnait plus par compte — CORRIGÉ

En corrigeant A8, j'avais placé `app.UseRateLimiter()` **avant** `app.UseAuthentication()`
sur les cinq hôtes.

Or mon limiteur partitionne par identifiant de compte quand l'utilisateur est
authentifié — c'est toute la mitigation du CGNAT béninois. Mais cette identité n'existe
qu'**après** `UseAuthentication`. Placé avant, `HttpContext.User` est encore anonyme : la
clé retombe **toujours** sur l'IP, et le partitionnement par compte que j'avais tant
commenté **ne s'appliquait jamais**.

Autrement dit : j'avais écrit le mécanisme anti-CGNAT, documenté pourquoi il était vital,
et l'avais désactivé par l'ordre des lignes. Le commentaire promettait ce que le code ne
faisait pas.

**Correctif.** `UseRateLimiter` déplacé après `UseAuthentication`/`UseAuthorization` sur
les cinq hôtes. `UseProxyForwardedHeaders` reste en premier. Ordre vérifié partout :
Forwarded → Auth → Authz → RateLimiter.

> **La leçon.** Une correction non exécutée peut sembler parfaite et ne rien faire.
> L'ordre du pipeline ASP.NET est du code, même s'il ne ressemble pas à de la logique.

---

## 🟠 B-I2 — Le filet de sécurité manquait sur les 4 BFF — CORRIGÉ

La `FallbackPolicy` (une route qui oublie son autorisation → 401 au lieu d'anonyme) n'avait
été posée que sur l'API monolithe. Les 4 BFF gardaient un `AddAuthorization()` nu.

Aucun trou **actif** aujourd'hui — chaque route déclare son intention. Mais c'est sur les
BFF que la surface bouge le plus, et le premier `MapGet` ajouté demain sans
`RequireAuthorization` serait anonyme en silence.

**Correctif.** `FallbackPolicy` répliquée sur les quatre BFF. Vérifié au passage que
**toutes** les routes publiques existantes déclarent bien `AllowAnonymous` (auth, catalogue
anonyme, santé) — aucune ne bascule en 401. Les groupes Admin et Seller étaient déjà
doublement protégés, via les helpers `MapAdminGroup`/`MapSellerGroup` qui imposent un rôle.

---

## 🟠 B-I4 — Une offre pouvait être créée à 0 XOF — CORRIGÉ

`Money.Create` ne rejette que le **négatif**. Le prix **nul** passait — à la création
(validator `>= 0`) comme à la modification (`ChangeOfferPrice` n'avait **aucun** validator).
Un vendeur pouvait lister un article gratuit, tout en engageant la logistique, la
commission et les points de fidélité.

**Correctif.** L'invariant « prix strictement positif » est posé dans le **domaine**
(`Offer.Create` et `Offer.ChangePrice`) — le seul endroit qu'aucun appelant ne peut
contourner, ni un BFF, ni un futur import en masse. Le validator `CreateOffer` passe à
`GreaterThan(0)` pour un message immédiat.

---

## 🔴 B-C2 — Pas de verrou de concurrence sur l'argent — CORRIGÉ

Aucun agrégat financier ne portait de jeton de concurrence (`grep RowVersion|xmin|
IsConcurrencyToken` sur tout `src/` → **0 résultat**). `SellerWallet.Withdraw` faisait un
lire-vérifier-soustraire sans verrou.

**Scénario, argent réel :** un vendeur au solde 100 000 envoie 5 retraits de 100 000 **en
parallèle**. Les 5 transactions lisent `Available = 100 000`, passent toutes le contrôle,
écrivent toutes `0`, et créent **5 demandes de 100 000**. À l'approbation, 500 000 partent
pour 100 000 dus. Le même schéma menaçait `Payment.Capture` (deux webhooks concurrents →
double crédit), le portefeuille plateforme, et `InventoryItem.Reserve` (survente).

### Le correctif, en trois pièces

**1. Le jeton — `xmin`, la colonne système de PostgreSQL.**
Chaque ligne Postgres porte déjà `xmin` : le numéro de la transaction qui l'a écrite en
dernier. On ne l'ajoute pas, on la lit. Le jeton demande à EF de l'inclure dans le `WHERE`
de chaque `UPDATE` ; si une autre transaction a modifié la ligne entre-temps, l'`UPDATE`
touche 0 ligne et EF lève `DbUpdateConcurrencyException`. Posé sur **`SellerWallet`,
`PlatformWallet`, `Payment`, `Order`, `InventoryItem`**.

> **Pourquoi `UsePostgresRowVersion()` et non `UseXminAsConcurrencyToken()`.** L'API Npgsql
> est marquée `[Obsolete]`, et le dépôt compile en « avertissements = erreurs » : elle
> **casse la build**. Son message d'obsolescence oriente vers `IsRowVersion()`, ce qui est
> un mauvais conseil ici — `IsRowVersion` suppose une colonne `byte[]` gérée par le
> fournisseur, que PostgreSQL n'a pas. Notre extension
> (`BuildingBlocks.Infrastructure/Persistence/ConcurrencyTokenExtensions.cs`) reproduit
> **exactement** le corps de l'API dépréciée : une propriété shadow `xmin` de type `xid`,
> générée par la base, déclarée jeton de concurrence. Rien n'est perdu, et la build passe.

**2. La traduction — 409, et surtout PAS de retry.**
`ConcurrencyExceptionHandler` (un `IExceptionHandler`, enregistré **avant**
`AddProblemDetails()` sur les 5 hôtes) transforme l'exception en **409 Conflict** avec un
message actionnable, journalisé en *Warning* — car ce n'est pas une panne, c'est le verrou
qui vient d'empêcher un double débit.

> **Aucun rejeu automatique, et c'est délibéré.** `ModuleDbContext` dispatche les événements
> de domaine et draine l'outbox **avant** `base.SaveChangesAsync`. Rejouer la commande dans
> le même scope re-dispatcherait ces événements et **dupliquerait les messages d'outbox** :
> on corrigerait un bug d'argent en en créant un autre. Le rejeu doit venir d'une requête
> neuve — ce que fait le client, et ce que font les PSP d'eux-mêmes sur un code non-2xx.

**3. `StockVersion` — parce que le jeton, seul, était INERTE sur le stock.**

C'est le piège de cette passe, et il méritait de coûter cher.

`Reserve()` et `ReleaseReservation()` ne modifient **aucune colonne** de `inventory_items` :
elles insèrent ou suppriment des lignes **enfants** (`stock_reservations`). EF n'émet donc
aucun `UPDATE inventory_items` — or un jeton de concurrence n'est évalué **que** dans la
clause `WHERE` d'un `UPDATE`.

Le verrou aurait été posé, visible dans la configuration, longuement commenté… et **sans
aucun effet sur le seul chemin qui vend**. Deux acheteurs sur le dernier article : les deux
lisent `Available = 1`, les deux passent, les deux insèrent. Survente — exactement le bug
qu'on croyait fermé.

`InventoryItem.StockVersion` est un compteur qu'**incrémente toute mutation** (`Touch()`).
Il salit la ligne parente, EF émet l'`UPDATE`, et le `xmin` est enfin confronté. **Personne
ne lit cette valeur : c'est le fait de l'écrire qui protège.** Huit tests
(`StockVersionConcurrencyTests`) gardent l'invariant, car une future mutation qui oublierait
`Touch()` ne casserait rien de visible — elle rouvrirait silencieusement la survente.

> **La leçon, et c'est la même que B-I1.** Un mécanisme de sécurité peut être présent,
> documenté, et ne jamais s'exécuter. Poser `UseXminAsConcurrencyToken()` ne suffit pas :
> il faut vérifier qu'un `UPDATE` est bel et bien émis sur le chemin qu'on prétend protéger.

### Migration requise

```bash
dotnet ef migrations add AddConcurrencyTokens -p src/Modules/Inventory/Marketplace.Modules.Inventory.Infrastructure
dotnet ef migrations add AddConcurrencyTokens -p src/Modules/Settlement/Marketplace.Modules.Settlement.Infrastructure
dotnet ef migrations add AddConcurrencyTokens -p src/Modules/Payments/Marketplace.Modules.Payments.Infrastructure
dotnet ef migrations add AddConcurrencyTokens -p src/Modules/Ordering/Marketplace.Modules.Ordering.Infrastructure
```

**À VÉRIFIER dans les migrations générées, avant de les appliquer.** `xmin` est une colonne
**système** : elle ne doit **PAS** apparaître dans un `AddColumn`. Npgsql la filtre, mais
c'est le genre d'hypothèse qu'on vérifie plutôt qu'on ne suppose — un `AddColumn("xmin")`
échouerait à l'exécution (le nom est réservé).

Attendu : les migrations Settlement / Payments / Ordering ne contiennent **aucun**
`AddColumn` ; seule celle d'Inventory en porte un — `StockVersion`, `int`, défaut `0`, donc
sans reprise de données sur les lignes existantes.

---

## 🔴 B-I3 — Les coupons n'existaient pas, et l'app affichait des remises fictives — CORRIGÉ

**L'audit avait sous-estimé ce point, et je le corrige ici en le remontant en CRITIQUE.**
Il l'avait diagnostiqué « coupon réutilisable à l'infini ». La vérité était plus grave.

### Ce que le code faisait réellement

`CartPricer` — le seul chemin par lequel un panier est valorisé — contenait ceci :

```csharp
Code: null,            // codé en dur
IsFirstOrder: false,   // codé en dur
```

Le panier n'avait **aucun champ** pour stocker un code, la commande non plus, et aucun
endpoint ne permettait d'en attacher un. Or `Promotion.AppliesTo()` rejette toute promotion
exigeant un code si le code fourni ne correspond pas — et `null` ne correspond jamais.

**Conséquence : aucune promotion codée n'a jamais pu s'appliquer. Aucune.** Le back-office
pouvait en créer, l'admin les activer, et pas un acheteur au monde n'en aurait vu la
couleur. Idem pour `IsFirstOrder`, toujours faux : toute promo « première commande » était
morte elle aussi. Deux familles entières de promotions tournaient à vide.

### Et pire : l'application mobile mentait sur le prix

`POST /mobile/cart/coupon` existait. Il calculait une remise, répondait
`"Code promo appliqué."` avec un total réduit… **et ne persistait rien**. Le commentaire
s'en justifiait — *« sans état : le client ré-applique le code au paiement »* — mais aucun
chemin de paiement n'acceptait de code.

**L'acheteur voyait donc s'afficher une réduction qu'il ne recevrait jamais, puis payait le
plein tarif.** Ce n'est pas une fonctionnalité manquante : c'est un prix affiché faux. Le
pire genre de bug — celui qui a l'air de marcher. (`DELETE /coupon` renvoyait `204` sans
rien faire. Le BFF Web, lui, renvoyait un `501` : paradoxalement plus honnête.)

### Le correctif — la chaîne entière, bout en bout

| Étage | Ce qui a été fait |
|---|---|
| **Cart (domaine)** | `PromotionCode` + `ApplyPromotionCode` / `RemovePromotionCode` |
| **CartPricer** | transmet le **vrai** code et le **vrai** `IsFirstOrder` |
| **Ordering** | `IOrderingModuleApi.HasPlacedOrderAsync` (EXISTS sur index) ; `Order` **fige** le code |
| **Événement** | `OrderConfirmedIntegrationEvent` transporte le `PromotionCode` |
| **Pricing** | `RedeemPromotionOnOrderConfirmedHandler` — **le premier appelant de `RecordUsage()`** |
| **Pricing** | table `promotion_redemptions`, `PerUserLimit`, verrou `xmin` sur `promotions` |
| **BFF** | `POST`/`DELETE /coupon` réels sur Mobile **et** Web, renvoyant le panier revalorisé |

**Décompte à la CONFIRMATION, pas au checkout.** Une commande peut être placée puis échouer
au paiement. Décompter au checkout brûlerait un coupon pour une vente qui n'a jamais eu
lieu — il faudrait le rendre en compensation, donc une saga de plus, donc une nouvelle
source de bugs d'argent. Seule une vente encaissée consomme le coupon.

**Idempotence : deux gardes, et il en faut deux.** L'outbox est *at-least-once*. La garde
applicative (`ExistsAsync`) évite le coût d'une violation de contrainte sur le chemin normal
de rejeu ; l'**index unique `(PromotionId, OrderId)`** est la seule vraie garantie, parce
qu'entre le `SELECT` et l'`INSERT` deux livraisons concurrentes peuvent toutes deux se
croire les premières.

**Concurrence : la ligne `promotions` est le point de rendez-vous.** `Redeem()` incrémente
*toujours* `UsedCount`, donc salit *toujours* la ligne, qui porte un jeton `xmin`. Toutes
les rédemptions d'une même promotion se **sérialisent** dessus : deux commandes ne peuvent
pas consommer le dernier exemplaire d'un coupon en même temps. C'est aussi ce qui rend
fiable le plafond **par acheteur**, qui repose sur un `COUNT` lu juste avant l'écriture —
sans le jeton, ce contrôle serait un vœu pieux.

> **La leçon.** Un audit qui lit le domaine trouve « le compteur n'est jamais incrémenté ».
> Un audit qui suit la donnée trouve que **le code ne quitte jamais le front**. Je n'avais
> fait que le premier. Et le bug le plus coûteux n'était pas dans le domaine : il était dans
> un endpoint qui affichait un prix que personne n'allait payer.

---

### 🟡 B-M1 — Le hub laissait écouter n'importe quelle conversation — CORRIGÉ

`ChatHub.JoinConversation(id)` n'exigeait **rien** : tout utilisateur authentifié pouvait
rejoindre le groupe SignalR de n'importe quelle conversation. L'identifiant n'était même pas
parsé — il était concaténé tel quel dans le nom du groupe.

**Ce que cela fuyait, exactement.** Le message diffusé ne contient que `{ conversationId }` :
un SIGNAL, pas le contenu. L'intrus apprenait « un message vient d'être échangé ici », et
rien de plus — le corps reste protégé par l'API, qui vérifie la participation. **Fuite de
métadonnées, pas de contenu.** Il faut le dire tel quel plutôt que de dramatiser.

**Pourquoi la corriger quand même.** Parce que ce payload minimal est une décision
d'aujourd'hui, pas une propriété du système. Le jour où quelqu'un se dira — très
raisonnablement — « pourquoi refaire un appel API alors qu'on pousse déjà l'événement ? » et
y ajoutera le corps du message, l'absence de contrôle deviendra une fuite de **contenu**.
Sans que personne ne touche à cette ligne, et sans que rien ne le signale.

> **L'autorisation se pose à la porte. Elle ne dépend pas de ce qu'on décide d'y faire
> passer aujourd'hui.**

Correctif : `IMessagingModuleApi.IsParticipantAsync` (EXISTS sur index), appelé avant
`AddToGroupAsync`. Message d'erreur identique pour « conversation inexistante » et
« conversation d'un tiers » — les distinguer ferait de ce hub un oracle d'énumération.

---

### 🟡 B-M2 — Le type d'un fichier était celui que le client déclarait — CORRIGÉ

L'ancien contrôle :

```csharp
var contentType = file.ContentType?.Split(';')[0]…   // ← déclaré par le CLIENT
if (!allowedContentTypes.Contains(contentType)) …
```

`IFormFile.ContentType` vient de l'en-tête multipart, écrit par l'appelant.
`curl --form 'file=@shell.exe;type=image/png'` suffit à le forger.

**Et l'audit avait manqué le pire : ce type n'était pas seulement VALIDÉ, il était STOCKÉ.**
Il partait sur Cloudflare R2 — et était **renvoyé aux navigateurs** à chaque
téléchargement — puis remis aux processeurs d'image, qui choisissent leur décodeur d'après
lui. Le client décidait donc comment ses propres octets seraient interprétés, plus tard, par
nos serveurs et par les navigateurs de nos utilisateurs.

Le cas qui fait mal : une pièce **KYB** annoncée « application/pdf » qui n'en est pas une,
stockée telle quelle, puis téléchargée et ouverte par un agent de conformité.

**Correctif.** `FileSignature.Detect()` lit les 16 premiers octets et reconnaît JPEG
(`FF D8 FF`), PNG, WebP (RIFF **+ marqueur WEBP** — sans quoi un WAV passerait) et PDF.
Signature inconnue → **refus** : liste blanche, jamais liste noire. Et surtout, **c'est le
type RÉEL qui est transmis au stockage**, plus jamais celui déclaré — valider les magic bytes
puis recopier le faux nom n'aurait servi à rien.

> ⚠️ **Ce n'est pas un antivirus.** Un fichier peut commencer par `FF D8 FF` *et* porter une
> charge malveillante plus loin (polyglotte, exploit de décodeur). Ce contrôle établit une
> chose, une seule : **le serveur décide de la nature du fichier, pas le client.** Nécessaire,
> pas suffisant. Un scan antivirus reste un chantier séparé.

---

## Points revérifiés et jugés SAINS

L'auditeur a balayé large. Ce qui tient :

- **Injection SQL** : aucun `FromSqlRaw`/`ExecuteSqlRaw` nulle part.
- **Path traversal upload** : le nom de fichier KYB est neutralisé (`kyb/{guid}/{sanitisé}`).
- **IDOR** : les endpoints par identifiant des 4 BFF sont cadrés au compte du jeton ; les
  helpers `Owns*` du BFF Seller sont bien appliqués.
- **Panier** : quantité `> 0` validée, ≤ 0 = retrait de ligne.
- **Avis / litiges** : propriété de la commande vérifiée, anti-doublon, statut requis.
- **Auto-attribution de rôle** : impossible (register force `Buyer`, `AssignRole` derrière
  rôle Admin).
- **Contre-passation de remboursement** : idempotente (correctif A3 confirmé sous ce nouvel
  angle).

---

## Ce que ces deux audits enseignent

1. **Un seul audit ne suffit pas.** Le second, avec une autre consigne, a trouvé une fuite
   critique que le premier avait normalisée en fermant « les écritures ».
2. **Une correction peut introduire une régression.** B-I1 était mon propre ouvrage :
   l'ordre du pipeline avait désactivé une protection que je croyais posée.
3. **Le vrai audit vérifie l'exécution, pas l'intention.** Un commentaire qui affirme
   « partitionné par compte » ne prouve rien tant qu'on n'a pas suivi l'ordre des
   middlewares.

Rien de cette passe n'a été compilé (`dotnet` indisponible ici). `dotnet build && dotnet
test` reste le juge — et B-C2 et B-I3 restent ouverts.
