# 09 — Notifications push (FCM)

Notifications **push** pour l'app **vendeur (MAUI Blazor Hybrid)** et l'app **cliente (Flutter)**, via
**Firebase Cloud Messaging (FCM)**. La chaîne est **opérationnelle de bout en bout** : enregistrement du
jeton d'appareil, envoi depuis le backend (SDK FirebaseAdmin), livraison via APNs (iOS) / FCM (Android).

> Statut : ✅ en production sur staging. Ce document décrit l'implémentation réelle, la configuration de
> déploiement, et un **guide de dépannage** issu de la mise au point.

## 1. Chaîne de bout en bout

```
(1) L'app démarre / l'utilisateur se connecte
      └─ CheckIfValidAsync (permission) ─▶ le jeton FCM arrive (via APNs sur iOS)
(2) POST du jeton au BFF ──▶ table notifications.device_tokens (UserId, Token, Platform)
(3) Un fait métier survient (commande, paiement, retrait) OU un admin envoie une notif
      └─ NotificationDispatcher.NotifyAsync(userId, …)
            ├─ crée la notification in-app (cloche)
            └─ SendPushAsync ─▶ FcmPushSender ─▶ FirebaseAdmin ─▶ FCM ─▶ APNs/Android
(4) Le téléphone reçoit la bannière ; un tap peut router vers un écran (data = { type, entityId })
```

Le point d'entrée unique côté backend est **`NotificationDispatcher`** : toute notification in-app
déclenche aussi le push (best-effort — une panne FCM n'interrompt jamais la notif in-app).

## 2. Backend — module `Notifications`

| Élément | Fichier | Rôle |
|---------|---------|------|
| `DeviceToken` | `…Domain/Devices/DeviceToken.cs` | Entité `notifications.device_tokens` : `Id, UserId, Token, Platform, CreatedAtUtc, LastSeenAtUtc`. |
| `IDeviceTokenRepository` | `…Domain/Devices/` | `GetByToken`, `Add`, `Remove`, `ListByUser`, `RemoveByTokens`. |
| `RegisterDeviceTokenCommand` / `Unregister…` | `…Application/Devices/DeviceCommands.cs` | Upsert par jeton / suppression. |
| `IPushSender` + `PushMessage` | `…Application/Abstractions/IPushSender.cs` | Port d'envoi ; renvoie les jetons invalides à purger. |
| `FcmPushSender` | `…Infrastructure/Push/FcmPushSender.cs` | Envoi réel (FirebaseAdmin, `SendEachForMulticastAsync`, lots de 500, logs par jeton). |
| `NullPushSender` | `…Infrastructure/Push/NullPushSender.cs` | No-op si FCM non configuré (aucun push, aucune erreur). |
| `FcmOptions` | `…Infrastructure/Push/FcmOptions.cs` | Résout le JSON de compte de service (inline ou fichier). `IsConfigured`. |
| `NotificationDispatcher` | `…Application/Notifications/NotificationDispatcher.cs` | Crée la notif in-app **et** envoie le push. Expose `SendPushAsync` pour les chemins qui créent la notif eux-mêmes. |
| Migration | `…Infrastructure/Migrations/20260708000000_AddDeviceTokens.cs` | Crée la table + index (unique sur `Token`, index sur `UserId`). |

### Armement au démarrage (`NotificationsModuleInstaller`)

À l'installation du module, si `FcmOptions.IsConfigured` :
`FirebaseApp.Create(GoogleCredential.FromJson(...))` → `FirebaseMessaging.DefaultInstance` (singleton) →
`FcmPushSender`. Sinon → `NullPushSender`. **Chaque hôte** (api + 4 BFF) exécute cet installeur : le BFF
qui envoie le push doit donc avoir accès au compte de service (voir §4).

Un log de démarrage indique l'état :
```
[Push] FCM configuré : FcmPushSender actif (les push seront envoyés).
[Push] FCM NON configuré : NullPushSender actif (aucun push). Path='…', JsonInline=non, FichierExiste=non.
```

### Envoi + diagnostic (`NotificationDispatcher` / `FcmPushSender`)

`SendPushAsync` liste les appareils du destinataire, envoie, purge les jetons invalides, et **loggue** :
```
Push: user=<guid> sender=FcmPushSender devices=1
FCM : lot de 1 jetons — 1 succès, 0 échecs.
Push: envoi terminé pour user=<guid> — jetons=1, invalides=0.
```
En cas d'échec, le code d'erreur FCM **par jeton** est loggué (indispensable au diagnostic — cf. §8) :
```
FCM : échec jeton #0 — code=ThirdPartyAuthError : …
```

## 3. Endpoints

| Surface | Route | Corps | Effet |
|---------|-------|-------|-------|
| Vendeur (BFF Seller) | `POST /seller/notifications/devices` | `{ token, platform }` | Enregistre le jeton pour l'utilisateur du JWT. |
| Vendeur | `POST /seller/notifications/devices/unregister` | `{ token }` | Retire le jeton. |
| Client (BFF Mobile) | `POST /mobile/devices` / `…/unregister` | `{ token, platform }` | Idem pour l'acheteur. |
| Admin (BFF Admin) | `POST /admin/sellers/{id}/test-notification` | — | Résout le `UserId` du vendeur et lui envoie une notif de **test** (in-app + push). |
| Admin | `POST /admin/notifications` | `{ recipientUserId, subject, body }` | Envoi admin — crée la notif in-app **et** envoie le push (via `SendPushAsync`). |

> Le bouton **« Tester push »** de la console admin (page Vendeurs) appelle `test-notification` : il cible
> automatiquement le compte du vendeur, sans saisir le `UserId`.

## 4. Déploiement & configuration (secret via Vault)

Le backend a besoin du **JSON de compte de service Firebase**. Il est fourni par un fichier monté :

- **Vault** (`inventories/<env>/group_vars/marketplace/vault.yml`, chiffré) : variable
  `fcm_service_account_json` (bloc littéral `|`, JSON intégral).
- **Ansible** (`roles/marketplace/tasks/main.yml`) écrit ce JSON dans
  `<app_dir>/deploy/fcm/service-account.json` (mode `0600`, `no_log`).
- **docker-compose** (`x-app-common`) monte `./fcm` en lecture seule dans **tous** les hôtes :
  ```yaml
  volumes:
    - ./fcm:/run/secrets/fcm:ro
  # et dans x-app-env :
  Notifications__Fcm__ServiceAccountPath: "/run/secrets/fcm/service-account.json"
  ```
- Le dossier `deploy/fcm/` contient un `.gitkeep` ; `deploy/fcm/*` est **gitignoré** (le JSON n'est jamais
  committé).

Alternative : coller le JSON en une ligne dans `Notifications__Fcm__ServiceAccountJson`. Le fichier monté
est prioritaire s'il existe. **Fichier absent → `NullPushSender` (aucun push, sans erreur).**

## 5. Prérequis Firebase & Apple (une fois)

1. **Projet Firebase** + app **Android** (`fr.hbamarketplace.seller`) → `google-services.json`, et app
   **iOS** (même bundle id) → `GoogleService-Info.plist`.
2. **Compte de service** : Firebase → Paramètres du projet → **Comptes de service** → *Générer une nouvelle
   clé privée* → JSON. C'est ce JSON que consomme le backend (§4). **Ne jamais le committer / le coller en clair.**
3. **Clé APNs (iOS)** — developer.apple.com → **Keys** → ➕ → cocher *Apple Push Notifications service (APNs)*.
   > ⚠️ **Critique** : lors de la création, choisir l'environnement **« Sandbox & Production »**. Une clé
   > **Sandbox seule** livre sur simulateur/dev mais **échoue en production/TestFlight** (`ThirdPartyAuthError`).
   Télécharger le `.p8` (une seule fois), noter le **Key ID**.
4. **Charger la .p8 dans Firebase** : Paramètres → Cloud Messaging → app iOS → **Clé d'authentification APNs**
   → téléverser le `.p8` + **Key ID** + **Team ID** (celui de la signature, ex. `QSVS3QW2NQ`). Une seule .p8
   suffit (elle couvre sandbox + production) — éviter les doublons.

## 6. App vendeur (MAUI Blazor Hybrid)

### 6.1 Découplage RCL / MAUI
Le RCL `HbaExpress.Shared` définit l'interface **`IPushRegistration`** (+ `NullPushRegistration` no-op pour
Web/desktop). Il référence `Plugin.Firebase 4.2.1` (qui a une cible `net9.0`), donc **`PushService`**
(l'implémentation Firebase) vit dans `HbaExpress.Shared/Services/PushService.cs` et les pages peuvent
l'appeler via l'interface. C'est l'hôte MAUI qui substitue l'implémentation réelle.

### 6.2 `HbaExpress.MobileDesktop.csproj`
```xml
<PackageReference Include="Plugin.Firebase" Version="4.2.1" />
<!-- Exclure les fichiers de config Google du glob par défaut, puis les inclure explicitement -->
<DefaultItemExcludes>$(DefaultItemExcludes);Platforms\Android\google-services.json;Platforms\iOS\GoogleService-Info.plist</DefaultItemExcludes>
```
- Android : `<GoogleServicesJson Include="Platforms\Android\google-services.json" />`.
- iOS : `<BundleResource Include="Platforms\iOS\GoogleService-Info.plist" Link="GoogleService-Info.plist" />`.

### 6.3 Entitlements iOS — **dev ≠ prod**
L'environnement APNs de l'app doit correspondre à la build :
- Debug/appareil de dev → `Platforms/iOS/Entitlements.plist` : `aps-environment = development`.
- **Release (TestFlight/App Store)** → `Platforms/iOS/Entitlements.Release.plist` : `aps-environment = production`.
```xml
<PropertyGroup Condition="…== 'ios' And '$(Configuration)' == 'Release'">
  <CodesignEntitlements>Platforms/iOS/Entitlements.Release.plist</CodesignEntitlements>
</PropertyGroup>
```
- `Info.plist` : `UIBackgroundModes` → `remote-notification`.
- **App ID Apple** : capability **Push Notifications** activée, puis **régénérer le profil de provisioning**
  (sinon `warning MT7140` et l'entitlement est ignoré).

### 6.4 Init Firebase (`MauiProgram.cs`) — Plugin.Firebase **v4**
L'init se fait via `ConfigureLifecycleEvents` (pas dans AppDelegate/MainApplication). `CrossFirebase` est
dans un namespace **par plateforme** (métapackage « Bundled »).
```csharp
#if IOS
using Plugin.Firebase.Bundled.Platforms.iOS;
#elif ANDROID
using Plugin.Firebase.Bundled.Platforms.Android;
#endif

builder.ConfigureLifecycleEvents(events =>
{
#if IOS
    events.AddiOS(ios => ios.WillFinishLaunching((_, __) =>
    {
        CrossFirebase.Initialize(CreateFirebaseSettings());                    // iOS : settings seulement
        Plugin.Firebase.CloudMessaging.FirebaseCloudMessagingImplementation.Initialize(); // OBLIGATOIRE iOS
        return false;
    }));
#elif ANDROID
    events.AddAndroid(android => android.OnCreate((activity, _) =>
        CrossFirebase.Initialize(activity, () => Platform.CurrentActivity, CreateFirebaseSettings())));
#endif
});

builder.Services.AddSellerDashboard(ApiBaseUrl);
// Substitue le no-op par l'implémentation Firebase (APRÈS AddSellerDashboard pour l'emporter).
builder.Services.AddSingleton<IPushRegistration, PushService>();

#if IOS || ANDROID
static Plugin.Firebase.Bundled.Shared.CrossFirebaseSettings CreateFirebaseSettings()
    => new(isCloudMessagingEnabled: true);
#endif
```

### 6.5 `PushService` — enregistrement du jeton
> ⚠️ **Bug corrigé (important sur appareil réel).** Sur iPhone, le jeton APNs (donc FCM) arrive de façon
> **asynchrone** : `GetTokenAsync()` appelé juste après le login renvoie souvent `null`. Il faut s'abonner
> à **`TokenChanged` AVANT** de demander le jeton, sinon le jeton émis un instant plus tard est perdu et
> l'appareil ne s'enregistre jamais. (Le simulateur, lui, renvoyait le jeton immédiatement, d'où le
> symptôme « ça marche sur simulateur mais pas sur téléphone ».)
```csharp
public async Task RegisterAsync()
{
    await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();   // permission (iOS + Android 13+)

    if (!_subscribed) // s'abonner AVANT de demander le jeton
    {
        CrossFirebaseCloudMessaging.Current.TokenChanged += OnTokenChanged;
        CrossFirebaseCloudMessaging.Current.NotificationTapped += OnNotificationTapped;
        _subscribed = true;
    }

    try
    {
        var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token)) await SendTokenAsync(token); // souvent dispo (simulateur)
    }
    catch { /* TokenChanged prendra le relais dès que le jeton est prêt */ }
}

private async void OnTokenChanged(object? s, FCMTokenChangedEventArgs e)
{
    if (!string.IsNullOrWhiteSpace(e.Token)) await SendTokenAsync(e.Token);
}

private Task SendTokenAsync(string token)
    => _api.RegisterDeviceAsync(token, OperatingSystem.IsIOS() ? "ios" : "android");
```
- `RegisterAsync()` est appelé **après login** (`Login.razor.cs`, après `SetSessionAsync`).
- `RegisterDeviceAsync/UnregisterDeviceAsync` sont dans `ISellerApi` → `HttpSellerApi` (POST vers le BFF).

## 7. App cliente (Flutter)

```yaml
# pubspec.yaml
firebase_core: ^3.*
firebase_messaging: ^15.*
```
```dart
await Firebase.initializeApp(options: DefaultFirebaseOptions.currentPlatform);
// après login :
await messaging.requestPermission();
final token = await messaging.getToken();
await dio.post('/mobile/devices', data: {'token': token, 'platform': Platform.isIOS ? 'ios' : 'android'});
messaging.onTokenRefresh.listen((t) => dio.post('/mobile/devices', data: {'token': t, 'platform': …}));
```
Mêmes prérequis APNs/entitlements iOS qu'au §5-6.

## 8. Tester & dépanner

### Test rapide
1. App installée sur **appareil réel** (build Release/TestFlight pour iOS), **connexion** (enregistre le
   jeton), autorisation notifications acceptée.
2. Vérifier le jeton en base :
   ```sql
   select "UserId","Platform","CreatedAtUtc" from notifications.device_tokens order by "CreatedAtUtc" desc limit 5;
   ```
3. Envoyer : console admin → page Vendeurs → **Tester push** (ou page Notifications → Envoyer avec le
   `RecipientUserId` du vendeur).
4. Lire les logs de l'hôte qui envoie :
   ```bash
   docker compose -f deploy/docker-compose.staging.yml --env-file deploy/.env.staging \
     logs --since 3m bff-admin | grep -iE "Push:|FCM :|\[Push\]"
   ```

### Table de dépannage (symptôme → cause → correctif)

| Observation dans les logs | Cause | Correctif |
|---------------------------|-------|-----------|
| `[Push] FCM NON configuré … FichierExiste=non` | Le JSON de compte de service n'est pas monté dans le conteneur | Renseigner `fcm_service_account_json` au vault + redéployer ; vérifier le montage `./fcm:/run/secrets/fcm:ro`. |
| Aucune ligne `Push:` après un envoi | Le chemin d'envoi ne passe pas par le dispatcher, ou la requête n'atteint pas le backend (CORS/401/404), ou console admin ancienne build | Vérifier que l'endpoint dispatche (`/admin/notifications` appelle `SendPushAsync`) ; onglet Réseau du navigateur. |
| `Push: … devices=0` | Aucun jeton pour ce `UserId` : non enregistré côté app, **ou** `RecipientUserId` ≠ `UserId` du jeton en base | Se (re)connecter sur l'appareil ; cibler exactement le `UserId` présent dans `device_tokens`. |
| `FCM : … code=ThirdPartyAuthError` | Clé APNs invalide **ou limitée à Sandbox** (échoue en production) | Créer/éditer la clé .p8 en **« Sandbox & Production »**, la recharger dans Firebase avec le bon **Team ID** ; supprimer les doublons. |
| `code=Unregistered` / `InvalidArgument` | Jeton périmé (app désinstallée) | Purge automatique ; se reconnecter pour ré-enregistrer. |
| `code=SenderIdMismatch` | Jeton d'un **autre** projet Firebase | Aligner `GoogleService-Info.plist`/`google-services.json` et le compte de service sur le même projet. |
| `warning MT7140` au build iOS | Le profil de provisioning ne contient pas `aps-environment` | Activer la capability Push sur l'App ID, **régénérer** le profil. |
| Reçu sur simulateur mais pas sur téléphone | Clé APNs **Sandbox** uniquement (le simulateur = sandbox, le téléphone = production) | Clé .p8 **Sandbox & Production** (voir ci-dessus). |

### Isoler l'app du backend
Firebase Console → Cloud Messaging → *Envoyer un message de test* avec un jeton FCM : si ça arrive,
APNs/app OK et le souci est côté backend ; sinon, c'est APNs/app (clé, permission, entitlement).

## 9. Points d'attention / limites

- **Ciblage vendeur** : les push vendeur passent aujourd'hui par l'**envoi admin** et par tout appel à
  `NotifyAsync(sellerUserId, …)`. Pour des push automatiques sur les faits métier vendeur (nouvelle
  commande, retrait validé…), ajouter des handlers d'intégration côté `Notifications` ciblant le
  `UserId` du vendeur.
- **Déconnexion** : `UnregisterAsync()` existe mais **n'est pas encore branché** aux points de logout — le
  jeton reste en base jusqu'à sa péremption (purgé alors comme `Unregistered`). À câbler si besoin.
- **Deep-link** : `OnNotificationTapped` reçoit `data = { type, entityId }` mais ne route pas encore ; y
  ajouter la navigation (`type` "Order" → `commande/{entityId}`, etc.).
- **Logs de diagnostic** : `[Push]` / `Push:` / `FCM :` sont au niveau Information/Warning ; on peut les
  passer en `Debug` une fois le système stabilisé.
- **Migration** : `AddDeviceTokens` est appliquée au runtime ; le `ModelSnapshot` EF n'a pas été régénéré
  (faire un `dotnet ef migrations add` propre avant la prochaine migration Notifications).
- **Secrets** : compte de service Firebase et clé .p8 sont sensibles → vault / fichier monté, jamais
  committés. En cas d'exposition, **régénérer** la clé.
```

