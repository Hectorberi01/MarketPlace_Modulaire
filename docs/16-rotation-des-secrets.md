# 16 — Rotation des secrets : procédure

> **Ce document est une checklist opérationnelle.** Il suppose que vous avez accès au
> VPS et aux consoles des prestataires. Chaque secret a une conséquence propre : certains
> se rotent sans que personne ne s'en aperçoive, un seul **déconnecte tous vos
> utilisateurs**. L'ordre compte.
>
> **Aucune de ces actions ne peut être faite par un outil.** Générer une clé, c'est
> facile ; décider quand la remplacer sur un système vivant, non.

---

## Ce que l'audit a réellement trouvé — et ce qu'il avait surestimé

Il faut commencer par corriger l'alarme, parce qu'une alarme exagérée fait perdre autant
de temps qu'une alarme absente.

| Constat initial | Vérification | Verdict |
|---|---|---|
| « Mots de passe de base de données commités » | Les chaînes de connexion versionnées pointent toutes sur `Host=localhost` | ✅ **Faux positif.** Ce sont des mots de passe de base locale. Ils ne donnent accès à rien. |
| « Clés PSP / R2 / Cloudinary commitées » | Toutes les clés des `appsettings.json` versionnés sont **vides** | ✅ **Faux positif.** |
| « Clé de signature JWT commitée » | **Vrai.** Présente dans les 5 `appsettings.json`, donc dans l'historique Git | 🔴 **Confirmé. C'est le seul vrai problème.** |
| « Secrets réels dans `deploy/.env` » | Le fichier existe sur disque, mais **n'est pas suivi par Git** | 🟠 **Pas fuité publiquement**, mais en clair sur la machine. À traiter. |

**Conclusion : un seul secret a réellement fuité — la clé JWT.** Les autres sont à
protéger, pas à considérer comme compromis.

---

## 1. 🔴 La clé de signature JWT — la seule urgence

### Pourquoi c'est grave

Cette clé signe **tous** les jetons d'authentification. Qui la détient peut **forger** un
jeton disant « je suis administrateur » — sans mot de passe, sans compte, sans laisser de
trace de connexion. Le contrôle total de la plateforme, en une ligne de commande.

Elle est dans l'historique Git. Un `git rm` n'y change **rien** : on ne retire pas un
secret d'un historique, on le **remplace**.

### Ce qui a déjà été fait dans le code

- La clé committée est remplacée par une clé de développement **explicitement publique** :
  `DEV-ONLY-PUBLIC-KEY-NOT-A-SECRET-DO-NOT-USE-IN-PRODUCTION`. Le dépôt reste utilisable
  après un simple clone.
- **`JwtKeyGuard` refuse de démarrer** si un hôte tourne en `Production` avec cette clé, ou
  avec une clé de moins de 32 octets. Le `docker-compose` imposait déjà l'override
  (`${JWT_SIGNING_KEY:?…}`) ; c'est désormais une seconde barrière, qui couvre les autres
  chemins de déploiement (systemd, recette montée à la main, futur pipeline).
- Les 5 hôtes partagent enfin la **même** clé en développement. Ils en avaient deux
  différentes : un jeton du BFF Mobile n'était pas valide sur l'API en local, alors qu'il
  l'est en production. Un piège de débogage silencieux.

### Ce que vous devez faire

```bash
# 1. Générer une nouvelle clé (48 octets ≫ le minimum de 32).
openssl rand -base64 48
```

```bash
# 2. La placer dans deploy/.env (et dans votre coffre-fort, PAS dans Git).
JWT_SIGNING_KEY=<la valeur générée>
```

```bash
# 3. Redéployer les 5 hôtes ENSEMBLE.
docker compose -f deploy/docker-compose.prod.yml up -d --force-recreate \
  api bff-mobile bff-web bff-seller bff-admin
```

> ### ⚠️ CETTE ROTATION DÉCONNECTE TOUT LE MONDE.
>
> Tous les jetons signés avec l'ancienne clé deviennent invalides **instantanément** :
> acheteurs, vendeurs, administrateurs. Chacun devra se reconnecter.
>
> Ce n'est pas un bug, c'est le but. Mais **faites-le à une heure creuse**, et prévenez
> vos vendeurs — sinon leur premier réflexe sera de croire à une panne, et votre support
> passera la journée à expliquer.
>
> **Redéployez les 5 hôtes en même temps.** Si l'API tourne avec la nouvelle clé et le BFF
> Mobile avec l'ancienne, les jetons émis par l'un seront rejetés par l'autre : plus rien
> ne fonctionne, et le diagnostic est déroutant.

---

## 2. 🟠 Les secrets de `deploy/.env` — à traiter, sans panique

Ils **ne sont pas dans Git** (vérifié). Le risque est celui d'un fichier en clair sur une
machine : sauvegarde, accès SSH partagé, copie oubliée sur un poste.

Traitez-les comme « potentiellement lus », pas comme « publiés ».

| Secret | Où le regénérer | Conséquence de la rotation |
|---|---|---|
| `FEDAPAY_API_KEY` | Console FedaPay → API | Aucune interruption. Les paiements en cours ne sont pas affectés. |
| `FEDAPAY_WEBHOOK_SECRET` | Console FedaPay → Webhooks | ⚠️ **Actuellement VIDE** — voir §4. |
| `R2_ACCESS_KEY_ID` / `R2_SECRET_ACCESS_KEY` | Cloudflare → R2 → jetons d'API | Créez la nouvelle clé **avant** de supprimer l'ancienne, sinon les images produit deviennent inaccessibles entre les deux. |
| `CLOUDINARY_API_SECRET` | Console Cloudinary | Dégradation seule : sans lui, les images passent sans détourage. Rien ne casse. |
| `REDIS_PASSWORD` | Vous le choisissez | Redéployez Redis **et** les 5 hôtes ensemble. Le cache est vidé — sans gravité (il se reconstruit), mais les paniers en cache sont relus depuis la base. |
| `GRAFANA_PASSWORD` | Vous le choisissez | Aucun impact applicatif. |
| `MARKETPLACE_CONNECTION` (Neon) | Console Neon → Roles → reset password | ⚠️ **Coupe la base.** Redéployez les 5 hôtes immédiatement après. À faire en dernier, et à une heure creuse. |

### Ordre recommandé

1. Cloudinary (aucun risque)
2. Grafana (aucun risque)
3. FedaPay (aucune interruption)
4. R2 (créer la nouvelle **avant** de révoquer l'ancienne)
5. Redis (redéploiement simultané)
6. **JWT** (§1 — déconnecte tout le monde)
7. Neon (coupe la base — en dernier, heure creuse)

---

## 3. Éviter la récidive

Le vrai correctif n'est pas de nettoyer : c'est de faire en sorte que ça ne puisse plus
arriver.

```bash
# Détecte un secret AVANT qu'il n'entre dans un commit.
brew install gitleaks          # ou: apt install gitleaks
gitleaks protect --staged
```

À brancher en hook `pre-commit`. Cinq secondes par commit, contre une clé dans
l'historique pour toujours.

**Ce qui est déjà en place :** `deploy/.env`, `deploy/monitoring/.env`, `key.properties`
et `*.jks` sont ignorés par Git. Vérifié.

---

## 4. ⚠️ Deux choses à régler AVANT la mise en production

Ce ne sont pas des rotations, mais elles se découvrent en lisant ce fichier — autant les
traiter ici.

### `FEDAPAY_WEBHOOK_SECRET` est vide

La vérification de signature des webhooks est **fail-closed** en production : c'est le bon
comportement. Mais avec un secret vide, **tous les webhooks FedaPay seront rejetés**.

Conséquence : les paiements ne se confirmeront **jamais** automatiquement. Vos clients
paieront, et leur commande restera « en attente de paiement ».

→ Renseignez ce secret depuis la console FedaPay.

### `ROOT_DOMAIN` vaut `example.com`

La variable n'est renseignée nulle part. Les routeurs Traefik construisent les domaines
avec (`api.${ROOT_DOMAIN}`, `m.${ROOT_DOMAIN}`…). Sans elle, rien n'est joignable.

→ Renseignez votre domaine réel dans `deploy/.env`.

---

## Après la rotation — vérifier, ne pas supposer

```bash
# Les 5 hôtes sont-ils repartis ?
docker compose -f deploy/docker-compose.prod.yml ps

# Le garde-fou JWT n'a-t-il pas bloqué le démarrage ?
docker compose -f deploy/docker-compose.prod.yml logs api | grep -i "JWT\|SIGNATURE"

# Une connexion fonctionne-t-elle avec la nouvelle clé ?
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://m.$ROOT_DOMAIN/mobile/auth/login \
  -H 'content-type: application/json' \
  -d '{"email":"…","password":"…"}'
# attendu : 200
```

Si un hôte refuse de démarrer avec un message parlant de clé de développement : le garde-fou
a fait exactement son travail. La variable `JWT_SIGNING_KEY` n'a pas été prise en compte
par ce conteneur.
