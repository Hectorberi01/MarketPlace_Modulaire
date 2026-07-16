# 18 — Le canal e-mail, et la faille critique qu'il cachait

> **Date :** 14 juillet 2026. Point de départ : **A10**, classé « important » dans le premier
> audit — *« `EmailVerificationRequestedIntegrationEvent` n'a aucun consommateur, les e-mails
> de vérification ne partent jamais »*.
>
> En cherchant pourquoi, j'ai trouvé la vraie cause. Et la vraie cause avait produit une
> **prise de contrôle totale de la plateforme, en deux requêtes, sans compte.**

---

## 🚨 La faille : n'importe qui pouvait prendre n'importe quel compte

`POST /mobile/auth/password/forgot` — endpoint **anonyme** — contenait ceci :

```csharp
// TODO production : envoyer result.Value par e-mail/SMS et ne pas l'exposer ici.
return Results.Ok(new { sent = true, token = result.Value });
```

`result.Value`, c'était le **jeton de réinitialisation en clair**. Renvoyé dans la réponse
HTTP. Pour **n'importe quelle adresse e-mail saisie**.

**L'attaque, en deux requêtes :**

```
POST /mobile/auth/password/forgot   { "email": "admin@hbaexpress.com" }
    → 200 { "sent": true, "token": "a3f9…" }          ← le jeton de l'admin

POST /mobile/auth/password/reset    { "email": "admin@hbaexpress.com",
                                      "token": "a3f9…",
                                      "newPassword": "…" }
    → 200 { "reset": true }                            ← le compte est pris
```

Les cinq hôtes partagent la **même base Identity** : un jeton obtenu par le BFF Mobile
ouvre la console d'administration. Catalogue, KYB, portefeuilles, versements — tout.

Aucune authentification requise. Aucune trace anormale. Le limiteur de débit (30 req/min)
ne gênait même pas : deux requêtes suffisent.

---

## La cause : il n'existait **aucun** canal e-mail

`grep -rn "IEmailSender\|SendEmail\|Smtp\|Resend\|SendGrid"` sur tout `src/` → **0 résultat**.

Le module Notifications savait faire du push (FCM) et de l'in-app. Rien d'autre. Et de ce
vide découlait **toute** la chaîne de pannes :

| Symptôme | Ce qui se passait réellement |
|---|---|
| A10 — e-mails de vérification | `EmailVerificationRequestedIntegrationEvent` était publié à chaque inscription… et consommé par **personne**. L'événement partait dans l'outbox, était marqué traité, et disparaissait. |
| Route de confirmation | `ConfirmEmailCommand` existait, testée — et **aucun endpoint ne l'appelait**. Même si l'e-mail était parti, le lien n'aurait mené nulle part. |
| Réinitialisation (Web) | Les deux routes renvoyaient `501` avec un diagnostic **faux** : « capacité Identity non implémentée ». La capacité existait entièrement. C'est le canal qui manquait. |
| Réinitialisation (Mobile) | Le jeton n'avait nulle part où aller — **alors il est sorti par la réponse HTTP.** |

> **La leçon, et elle vaut au-delà de ce dépôt.** Une capacité manquante finit toujours par
> être contournée. Et le contournement est rarement sûr. Ici, le trou fonctionnel a
> *engendré* le trou de sécurité — pas par négligence, mais par nécessité : il fallait bien
> que le jeton aille quelque part.
>
> Le `TODO production` disait la vérité. Personne ne l'a lu.

---

## Le correctif

### 1. La fuite est rendue **impossible à réécrire**

`RequestPasswordResetCommand` ne renvoie plus rien : `ICommand<string?>` → **`ICommand`**.

Ce n'est pas un changement de signature, c'est un changement de **garantie**. Un endpoint
ne peut pas divulguer ce qu'il ne reçoit pas. La protection est portée par le **type**, pas
par la vigilance du prochain développeur qui touchera au BFF.

`tests/Identity.UnitTests/PasswordResetTokenLeakTests.cs` garde cette propriété par
réflexion : si quelqu'un redonne une valeur de retour à la commande, le test rouge le dit.

### 2. Le jeton a désormais un chemin légitime

```
RequestPasswordResetCommand
   └─ user.BeginPasswordReset(hash)          ← seul le HACHÉ va en base
   └─ publish PasswordResetRequestedIntegrationEvent{ ResetToken = clair }
        │  (même transaction que l'écriture du haché — voir ModuleDbContext)
        ▼
      outbox
        ▼
   SendPasswordResetEmailHandler  (module Notifications)
        └─ IEmailSender.SendAsync(...)  → boîte mail du propriétaire. Et de personne d'autre.
```

Idem pour `EmailVerificationRequestedIntegrationEvent`, qui a enfin un consommateur :
`SendEmailVerificationHandler`.

### 3. Le canal e-mail

| Élément | Rôle |
|---|---|
| `IEmailSender` (Application) | Le port. `EmailMessage(To, Subject, HtmlBody, TextBody)` — la partie **texte est obligatoire** : un e-mail sans elle part en spam, et un e-mail de reset en spam, c'est un utilisateur enfermé dehors. |
| `ResendEmailSender` (Infrastructure) | **Resend**, comme le site vitrine : un seul domaine à authentifier (SPF/DKIM), une seule facture, pas de SMTP à maintenir. **Vérifie le code HTTP** — `PostAsJsonAsync` ne lève pas sur 4xx/5xx, et un 403 « domaine non vérifié » passerait sinon pour un succès. |
| `DevelopmentEmailSender` | Écrit l'e-mail (jeton compris) dans la console. Sans lui, un développeur local ne peut ni vérifier un compte ni réinitialiser un mot de passe — c'est-à-dire exactement le vide qui a créé la faille. |
| `AccountLinkBuilder` | Fabrique les liens. Ils pointent vers le **front**, jamais vers une URL à effet de bord (voir plus bas). |

### 4. Refus de démarrer en production sans e-mail

```csharp
else if (IsProduction(configuration))
{
    throw new InvalidOperationException("Notifications:Email n'est pas configuré …");
}
```

Volontairement brutal. Sans e-mail en production, **aucun mot de passe oublié ne peut être
récupéré** : les utilisateurs sont enfermés dehors, définitivement, et l'exploitant ne
l'apprend que par les plaintes. Un adaptateur silencieux ferait « tourner » la plateforme
dans cet état. Un démarrage refusé se remarque tout de suite, et se corrige en une variable.

Même patron que la garde des passerelles de paiement (A2) et celle de la clé JWT (A13).

### 5. Les liens sont des **POST**, jamais des GET

Les clients de messagerie et les passerelles antivirus **pré-chargent** les liens des
e-mails pour les analyser. Une confirmation d'adresse sur `GET` serait déclenchée par le
scanner avant même que l'utilisateur ne clique ; une réinitialisation sur `GET` serait pire
encore.

Le lien mène donc à une **page** du front, qui affiche un formulaire, qui appelle ensuite
l'endpoint. La lecture est sûre ; seule l'écriture agit.

---

## Configuration à renseigner (vault)

```bash
RESEND_API_KEY=re_…                                   # clé Resend
EMAIL_FROM="HBA Express <no-reply@hbaexpress.com>"    # domaine VÉRIFIÉ chez Resend (SPF+DKIM)
EMAIL_APP_BASE_URL=https://hbaexpress.com             # base des liens cliquables
```

⚠️ **Le domaine de `EMAIL_FROM` doit être vérifié chez Resend.** Sinon chaque envoi est
refusé en 403 — et comme l'échec se produit dans l'outbox, il serait rejoué toutes les
5 secondes sans jamais aboutir.

---

## Reste à faire côté front

Le backend est complet. Il manque **deux pages** dans le front web, vers lesquelles pointent
les liens :

| Page | Reçoit | Appelle |
|---|---|---|
| `/verifier-email?uid=…&token=…` | `uid`, `token` | `POST /web/auth/email/confirm` |
| `/reinitialiser-mot-de-passe?email=…&token=…` | `email`, `token` + saisie du nouveau mot de passe | `POST /web/auth/password/reset` |

---

## Deux événements restent sans consommateur — et c'est acceptable

`UserRegisteredIntegrationEvent` et `UserEmailConfirmedIntegrationEvent` n'ont toujours
aucun handler. **Ce n'est pas un bug** : rien n'en dépend aujourd'hui (contrairement à la
vérification, qui bloquait l'usage du compte, et au reset, qui a produit une faille). Ils
restent disponibles pour un futur e-mail de bienvenue ou une métrique d'activation.

Je le note plutôt que de fabriquer des consommateurs dont personne n'a besoin.
