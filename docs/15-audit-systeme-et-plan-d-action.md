# 15 — Audit système complet & plan d'action

> **Date :** 14 juillet 2026. **Périmètre :** API + 4 BFF (backend .NET 8), apps
> mobiles client (`Client_MP_Mobile`) et vendeur (`HbaExpressPro`), tableaux de bord
> admin (`Admin_MP_Dashboard`) et vendeur (`Seller_MP_Dashboard`).
>
> **Méthode :** lecture du code, pas d'exécution (ni `dotnet`, ni `flutter`, ni `npm` —
> bloqués dans l'environnement d'audit). Les constats les plus graves ont été
> **revérifiés à la main** ligne par ligne. Ce qui est marqué CRITIQUE l'est vraiment.
>
> **Ce document ne corrige rien.** Il dresse la liste, triée par gravité et par coût.
> Aucune de ces trouvailles ne sort d'une supposition : chacune cite un fichier.

---

## Le tableau de bord — à lire en premier

| # | Gravité | Surface | Problème en une phrase | Coût |
|---|---|---|---|---|
| **A1** | 🔴 CRITIQUE | Backend | L'API publique laisse un **anonyme** créer/supprimer le catalogue, changer les prix, manipuler le stock | Moyen |
| **A2** | 🔴 CRITIQUE | Backend | Les **paiements passent en mode simulé** si un secret manque — sans erreur, sans log | Faible |
| **A3** | 🔴 CRITIQUE | Backend | La **contre-passation de remboursement** débite le vendeur **deux fois** si l'événement est rejoué | Faible |
| **A4** | 🔴 CRITIQUE | Mobile | Les deux apps sont **signées avec la clé debug** → refus du Play Store | Faible |
| **A5** | 🔴 CRITIQUE | Mobile | L'app **client** autorise le **trafic HTTP en clair** (Android + iOS) | Trivial |
| **A6** | 🔴 CRITIQUE | Dashboard vendeur | **~35 écritures** affichent « succès » sur une erreur serveur (prix, RIB, mot de passe, MFA…) | Moyen |
| **A7** | 🟠 IMPORTANT | Mobile client | Pas de **suppression de compte in-app** → refus de l'App Store | Faible |
| **A8** | 🟠 IMPORTANT | Backend | **Rate limiting** absent sur l'API ; et inefficace partout (IP du proxy) | Faible |
| **A9** | 🟠 IMPORTANT | Backend | **Outbox** sans retry borné ni dead-letter : un message empoisonné tourne en boucle | Moyen |
| **A10** | 🟠 IMPORTANT | Backend | **E-mail de vérification jamais envoyé** (événement sans consommateur) | Faible |
| **A11** | 🟠 IMPORTANT | Transverse | **Refresh tokens en localStorage** (2 dashboards) — exfiltrables par XSS | Moyen |
| **A12** | 🟠 IMPORTANT | Mobile + dashboards | **URL de staging par défaut** dans les builds (CI mobile, MAUI vendeur) | Faible |
| **A13** | 🟠 IMPORTANT | Backend | Secrets à **roter** (présents en clair sur disque + clé JWT dans un fichier versionné) | Faible |
| **A14** | 🟠 IMPORTANT | Mobile | Permission **POST_NOTIFICATIONS** manquante → push muet sur Android 13+ | Trivial |
| **A15** | 🟡 MINEUR | Backend | **16 endpoints BFF** en `501` (reset mot de passe web, catégories web…) | Variable |
| **A16** | 🟡 MINEUR | Backend | **Zéro test** sur les flux argent (règlement, retrait, contre-passation) | Élevé |
| **A17** | 🟡 MINEUR | Backend | CORS ouvert par défaut ; `InitiatePayment` sans contrôle de propriété | Faible |

---

## 🔴 CRITIQUE — à corriger avant toute mise en production

### A1 — L'API publique est une porte ouverte sur le catalogue, les prix et le stock

**C'est la faille la plus grave du système, et elle est exploitable à distance sans compte.**

Trois faits qui, ensemble, forment le trou :

1. L'API monolithe est **routée publiquement** : `deploy/docker-compose.prod.yml:147`,
   `Host(\`api.${ROOT_DOMAIN}\`)`.
2. Elle partage **la même clé de signature JWT** que les BFF (`x-app-env`,
   `Jwt__SigningKey` identique) : un jeton obtenu par n'importe quel acheteur sur
   `/mobile/auth/login` est accepté par `api.`.
3. `Program.cs:99` fait `AddAuthorization()` **sans `FallbackPolicy`** : toute route sans
   `RequireAuthorization()` explicite est **totalement anonyme**.

Résultat, vérifié dans `CatalogEndpoints.cs:42` : le groupe `/api/catalog/products` n'a
**aucune** autorisation. Un anonyme peut :

- **créer** un produit (au nom d'un `SellerId` fourni dans le corps),
- **modifier** ou **supprimer** (cascade variantes + médias) le produit de n'importe quel
  vendeur,
- via `OfferEndpoints.cs:26` : **changer le prix** de l'offre d'un concurrent, la suspendre,
- via `InventoryEndpoints.cs:28` : **fixer le stock** à une valeur arbitraire, libérer les
  réservations d'autrui — ce qui casse la saga de commande.

Le BFF Seller, lui, exige rôle + propriété (`SellerBff.cs:47`, helpers `OwnsProductAsync`…).
**Le trou n'est pas dans les BFF, il est dans l'API monolithe exposée à côté d'eux.**

**Correctifs, du plus sûr au plus rapide :**
- **Idéal** — ne **pas** router `api.` publiquement. Les clients passent par les BFF ;
  l'API n'a aucune raison d'être joignable de l'extérieur.
- **À défaut** — poser une `FallbackPolicy` exigeant l'authentification, **et** un rôle
  (`Admin`/`Seller`) sur chaque groupe d'écriture de l'API.

---

### A2 — Un paiement sur deux pourrait être une fiction

`PaymentsModuleInstaller.cs:63-116` : pour **chaque** PSP, si la clé n'est pas configurée,
l'installer enregistre **silencieusement** la passerelle **simulée** (`if IsConfigured …
else → SimulatedGateway`). Vérifié : aucun garde `IsProduction()`, aucun log d'alerte — au
contraire des payouts et du push, qui préviennent.

Or `appsettings.json:18-78` livre **toutes les clés PSP vides**. Un déploiement qui oublie
d'injecter les secrets démarre donc **normalement**, et :

- `SimulatedPaymentGateway` renvoie **toujours** `Captured` — la commande est marquée payée ;
- `RefundAsync` renvoie **toujours** `Success` ;
- la vérification de signature webhook est **court-circuitée** si le secret est vide.

**Conséquence :** la plateforme encaisse des commandes « payées » **sans qu'un franc ne
bouge**, et accepte n'importe quel faux webhook. La panne est totale et **silencieuse** —
la pire espèce.

**Correctif :** au démarrage, si `Environment.IsProduction()` **et** qu'un PSP retombe sur
un stub → **refuser de démarrer** (`throw`). Un site mort est infiniment préférable à un
site qui simule des paiements.

---

### A3 — Le remboursement peut débiter le vendeur deux fois

`ReverseEarningsOnReturnRefundedHandler.cs` (le handler que nous venons d'écrire) n'a
**aucun garde d'idempotence** — sa seule protection est `RefundAmount <= 0`.

Or l'outbox est **at-least-once** : si un autre handler du même événement échoue, le
dispatcher **rejoue tout le message**, y compris celui-ci. Le voisin `AccrueEarnings`, lui,
se protège avec `ExistsForOrderAsync`. Celui-ci ne le fait pas.

Pire, l'écriture au grand livre (`WalletMutations.DebitSellerForRefundAsync`) est référencée
par `("order", orderId)` **sans le `ReturnRequestId`** : deux retours sur la même commande
sont indistinguables, et un rejeu est invisible.

**Conséquence :** au rejeu, le vendeur est débité deux fois et la commission restituée deux
fois. Une perte comptable réelle — dans le code même qui était censé fiabiliser le
remboursement. *Mea culpa : je l'ai écrit sans ce garde.*

**Correctif :** ajouter une garde « ce remboursement a-t-il déjà été contre-passé ? » (une
entité `ProcessedRefund`, ou une ligne de grand livre portant le `ReturnRequestId` en clé
d'unicité). **Et écrire le test qui rejoue l'événement** — il n'en existe aucun (voir A16).

---

### A4 — Les deux apps mobiles sont signées avec la clé debug

`Client_MP_Mobile/android/app/build.gradle.kts:44` et
`HbaExpressPro/android/app/build.gradle.kts:37` :

```kotlin
release {
    // TODO: Add your own signing config for the release build.
    signingConfig = signingConfigs.getByName("debug")
}
```

Un AAB signé avec le keystore debug (public, mot de passe `android`) est **refusé par le
Play Store**. Bloquant, pour les deux apps.

**Correctif :** créer un keystore de release, le référencer, garder le mot de passe hors du
dépôt.

---

### A5 — L'app client accepte le trafic en clair

`Client_MP_Mobile/android/.../AndroidManifest.xml:8` :
`android:usesCleartextTraffic="true"` **dans le manifeste de production** (un commentaire dit
« à retirer en prod » — il est resté). Et `ios/.../Info.plist:8-12` :
`NSAllowsArbitraryLoads = true`, qui **désactive ATS globalement**.

**Conséquence :** interception réseau possible (l'app parle HTTPS, mais rien ne l'empêche de
parler HTTP), **et** motif de rejet App Store. L'app **vendeur**, elle, est propre — c'est
l'app cliente qui n'a pas été nettoyée après la phase de développement local.

**Correctif (trivial) :** retirer les deux lignes.

---

### A6 — Le dashboard vendeur ment sur ~35 actions

C'est **exactement le bug du « 400 affiché en vert »** que vous aviez déjà signalé. Il a été
corrigé à fond dans le dashboard **admin** (helper `Send()` qui lit le statut et le
`ProblemDetails`), mais il est **toujours présent dans le dashboard vendeur**.

`PostAsJsonAsync`/`PutAsJsonAsync` ne lèvent **pas** sur 4xx/5xx. Dans `HttpSellerApi.cs` et
`HttpSellerApi.Extra.cs`, une trentaine d'écritures renvoient le `Task` brut sans vérifier
le statut. Les plus dangereuses :

- **`ChangePasswordAsync`** (`Extra.cs:138`) → `Profil.razor:232` affiche « mot de passe
  changé » même si l'ancien mot de passe est faux.
- **`MfaConfirmAsync`** (`Extra.cs:148`) → « MFA activée » même sur code TOTP invalide. **Le
  vendeur croit sa 2FA active alors qu'elle ne l'est pas.**
- **`SetPayoutAccountAsync`** (`.cs:105`) → le RIB refusé est cru enregistré.
- **`SetPriceAsync`**, **`ShipAsync`**, **`RefundReturnAsync`** → prix, expéditions,
  remboursements silencieusement perdus.

**Correctif :** porter le helper `Send()` du dashboard admin — il existe déjà, il suffit de
le recopier et d'en envelopper toutes les écritures vendeur.

---

## 🟠 IMPORTANT — avant d'ouvrir au public

### A7 — Suppression de compte in-app manquante (app client)
L'app cliente offre l'**inscription** in-app mais aucun moyen de **supprimer son compte**
(`account_screen.dart:142` : déconnexion seule). Apple l'exige dès lors qu'il y a inscription
(Guideline 5.1.1(v)). L'app vendeur n'a pas d'inscription in-app → probablement hors champ.

### A8 — Rate limiting absent puis inefficace
Deux problèmes. **D'abord**, l'API monolithe n'appelle jamais `UseRateLimiter()` — or
`/api/identity/auth/login` y est public : brute-force viable. **Ensuite**, le limiteur des
BFF partitionne par `RemoteIpAddress` **sans `UseForwardedHeaders`** : derrière Traefik,
c'est l'IP du proxy pour *tout le monde*. Les 10 logins/min deviennent donc 10 pour la
plateforme entière — un déni de service trivial, et zéro isolement par attaquant.

### A9 — Outbox sans filet
`OutboxProcessor.cs` : un message dont le handler échoue en boucle reste non traité
**indéfiniment**, re-sélectionné à chaque tour. Pas de compteur de tentatives, pas de
dead-letter. Si plus de 50 messages sont empoisonnés, ils **affament** les événements
légitimes. Et le dispatcher rejoue *tous* les handlers d'un message quand un seul échoue —
ce qui amplifie tout défaut d'idempotence (cf. A3).

### A10 — L'e-mail de vérification ne part jamais
`EmailVerificationRequestedIntegrationEvent` est publié à l'inscription mais **n'a aucun
consommateur**. Le seul chemin qui marche est la validation **manuelle par un admin**. Le
compte self-service reste bloqué en `PendingVerification`, sans e-mail.

### A11 — Refresh tokens exfiltrables (deux dashboards)
Admin et vendeur (hôte Web WASM) stockent le **refresh token en `localStorage`**, lisible par
tout JavaScript. Une faille XSS le vole et le rejoue. Pour une **console d'administration**,
c'est particulièrement sensible. Recommandé : cookie `HttpOnly`/`Secure` posé par le BFF.
*(Les apps mobiles, elles, utilisent bien le stockage chiffré — RAS.)*

> **Note liée :** l'`IsAdmin` du dashboard admin est **fail-open** (`AuthState.cs:56` : pas
> de rôle décodé → `true`). Sans danger **tant que** le BFF Admin refuse les non-admins sur
> chaque route — ce qui rejoint A1 : à garantir côté serveur, jamais côté client seul.

### A12 — Les builds pointent sur le staging
Valeur par défaut = staging **et figée dans la CI** : `codemagic.yaml:59` construit l'IPA
TestFlight du client vers le staging ; `MauiProgram.cs:19` code l'URL staging **en dur** dans
le binaire vendeur. Une app livrée taperait le mauvais environnement.

### A13 — Secrets à roter
`deploy/.env` (présent sur disque, **ignoré par git** — donc pas fuité publiquement, mais à
traiter comme exposé) contient des clés réelles : R2, Cloudinary, FedaPay sandbox, Redis,
Grafana. **Et** `appsettings.json` (celui-là **versionné**) porte une **clé de signature JWT
en clair**. Roter, et sortir les secrets des fichiers versionnés.

### A14 — Push muet sur Android récent
Les deux apps intègrent FCM mais **aucun manifeste ne déclare `POST_NOTIFICATIONS`**. Sur
Android 13+, sans elle, l'app ne peut pas demander l'autorisation : le push que nous venons
de câbler ne s'affichera pas. Une ligne à ajouter dans chaque manifeste.

---

## 🟡 MINEUR — dette à planifier

- **A15** — 16 endpoints BFF en `501 Not Implemented`. Au moins deux comptent : le **reset
  de mot de passe web** (le backend le supporte pourtant) et la **liste des catégories web**
  (vitrine incomplète). Au moins ce sont des 501 francs, pas de faux succès.
- **A16** — **Zéro test sur les flux argent.** Les tests de saga couvrent
  capture/annulation, mais **rien** sur le règlement, le portefeuille, le payout, le
  retrait, ni la contre-passation (A3). Pour du code qui déplace de l'argent, c'est le trou
  le plus inquiétant à moyen terme.
- **A17** — CORS ouvert à toute origine par défaut si `Cors:Origins` est vide ;
  `InitiatePayment` ne vérifie pas que la commande appartient à l'appelant.

---

## Ce qui a été vérifié et jugé SAIN

Pour que l'inquiétude soit calibrée — tout n'est pas cassé, loin de là :

- **IDOR côté BFF** : les helpers `Owns…Async` sont bien appliqués à chaque mutation vendeur ;
  l'acheteur ne peut pas lire la commande d'un autre. Le trou est côté API, pas côté BFF.
- **Webhooks** : signature HMAC vérifiée, comparaison en temps constant, **fail-closed** en
  prod. Correct.
- **Stockage des jetons mobiles** : `flutter_secure_storage` (Keychain/Keystore), jamais en
  clair. Correct.
- **En-têtes de sécurité, HTTPS, HSTS** : gérés à l'edge Traefik. Corrects.
- **Dashboard admin** : gestion d'erreurs exemplaire, aucun mock câblé (`UseMock=false`),
  tape bien le BFF et non l'API.
- **Migrations EF** : toutes présentes, y compris les trois récentes (remboursement,
  attestation e-mail admin, frais provider).
- **Structure des 25 modules** : homogène, aucun squelette vide.

---

## L'ordre dans lequel je m'y prendrais

**Cette semaine (rien ne doit être public avant) :**
1. **A1** — fermer l'API publique (ou `FallbackPolicy` + rôles). *La faille exploitable.*
2. **A2** — refuser le démarrage en prod si un PSP est simulé.
3. **A3** — rendre la contre-passation idempotente **+ son test**.

**Avant d'accepter un vrai paiement :**
4. **A6** — porter le helper `Send()` au dashboard vendeur.
5. **A8, A13** — activer/réparer le rate limiting ; roter les secrets.

**Avant de soumettre aux stores :**
6. **A4, A5, A7, A14** — signature release, retirer le cleartext, suppression de compte,
   `POST_NOTIFICATIONS`.
7. **A12** — basculer les builds sur la production.

**À planifier :**
8. **A9, A11** — filet outbox, refresh tokens hors localStorage.
9. **A10, A15, A16, A17** — e-mail de vérification, endpoints 501, tests argent, CORS.

> Une dernière franchise : **aucune ligne de ce plan n'a été compilée ni exécutée** —
> l'environnement d'audit n'a ni `dotnet`, ni `flutter`, ni `npm`. Les constats viennent de
> la lecture du code et sont, pour les plus graves, recoupés ligne à ligne. Mais la
> confirmation finale passe par `dotnet build && dotnet test`, un `flutter analyze`, et un
> vrai déploiement de staging.
