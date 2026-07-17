# Audit de capacité — API marketplace à 1 M d'utilisateurs/jour

*Établi le 13 juillet 2026, à partir de `deploy/docker-compose.prod.yml` et du code
source — pas d'une estimation à vue de nez.*

---

## Réponse courte

**≈ 16 vCPU et 24–32 Go de RAM** pour la couche applicative, **et le disque n'est
pas le sujet** (160 Go suffisent largement ; 90 Go passeraient).

Mais ce chiffre est le moins utile de ce document, pour trois raisons développées
plus bas :

1. **Un trou de cache** multiplie ce besoin par 2 à 3. Corrigé, la facture chute.
2. **Postgres n'est pas sur le VPS** (Neon managé). Le vrai mur de coût est là, pas
   dans la RAM du serveur.
3. **À 1 M d'utilisateurs/jour, un VPS unique est une faute d'architecture**, quelle
   que soit sa taille. Un redémarrage = panne totale, devant 1 M de personnes.

---

## 1. Ce que déploie réellement la production

Lu dans `deploy/docker-compose.prod.yml` :

| Service | Rôle | Limite actuelle |
|---|---|---|
| `api` | Bootstrap, 25 modules | 1 vCPU / 640 Mo |
| `bff-mobile` | + SignalR (WebSockets) | 1 vCPU / 640 Mo |
| `bff-web` | | 1 vCPU / 640 Mo |
| `bff-seller` | | 1 vCPU / 640 Mo |
| `bff-admin` | | 1 vCPU / 640 Mo |
| `redis` | cache + backplane SignalR | non plafonné |
| `traefik` | TLS, routage | non plafonné |

**Postgres est ABSENT du compose** : la base est externe (Neon). C'est un choix
structurant — il sort la charge la plus lourde du VPS, et déplace la question du
dimensionnement vers votre facture Neon.

**Aucun réplica n'est déclaré.** Une instance de chaque hôte, point.

---

## 2. Le calcul, avec ses hypothèses affichées

Une application marketplace consomme **20 à 50 appels d'API par utilisateur et par
jour** (accueil, catégories, recherche, fiches produit, panier, commandes). Je
retiens **30** comme valeur centrale.

```
1 000 000 utilisateurs × 30 appels        =  30 M requêtes/jour
30 M / 86 400 s                           ≈ 347 req/s en MOYENNE
```

La moyenne ne dimensionne rien. Le Bénin est sur **un seul fuseau horaire** : le
trafic se concentre le soir. Un facteur de pointe de **5×** est réaliste :

```
347 × 5                                   ≈ 1 700 req/s en POINTE
```

Et une notification push envoyée à tous les utilisateurs produit un pic bien plus
violent — **5 000 à 10 000 req/s pendant deux minutes**. C'est ce pic-là qui casse
les serveurs, pas la moyenne.

### Combien de cœurs cela représente

Le calcul est direct : `vCPU nécessaires = req/s × coût CPU par requête`.

| Chemin | Coût CPU/req | À 1 700 req/s |
|---|---|---|
| Lecture **avec** cache Redis | ~4 ms | **≈ 7 vCPU** |
| Lecture **sans** cache (EF + Neon) | ~12–15 ms | **≈ 20–25 vCPU** |

**Le cache ne fait pas gagner 20 % — il divise le besoin par trois.** D'où la
section suivante.

---

## 3. Le trou : le catalogue n'est pas caché — **CORRIGÉ**

> **Mise à jour du 13 juillet 2026.** Les trous décrits ci-dessous ont été bouchés.
> Le détail de ce qui a été fait est en section 8. La section qui suit est conservée
> telle quelle : c'est le diagnostic, et il explique *pourquoi* la correction porte
> là où elle porte.

`ICacheService` (Redis) est correctement câblé. Il est utilisé dans **le module
Cart** — et **nulle part ailleurs**.

Vérifié :

```
grep -rn "ICacheService" src/Modules/Catalog/   →  aucun résultat
```

Or le catalogue — liste de produits, fiche produit, recherche — c'est **plus de
80 % du trafic** d'une marketplace. Chaque affichage d'accueil part aujourd'hui
frapper Neon, pour renvoyer à des milliers d'utilisateurs **exactement la même
liste de produits**.

C'est le poste n° 1, et de loin. Un cache-aside de 60 secondes sur les lectures
catalogue :

- fait tomber la charge Neon d'environ **3 000–5 000 requêtes/s à ~400** ;
- ramène le besoin applicatif de ~22 vCPU à ~7 ;
- coûte quelques dizaines de lignes, le service existant étant déjà injectable.

**Tant que ce trou n'est pas bouché, dimensionner le VPS revient à acheter des
cœurs pour recalculer sans cesse la même réponse.**

---

## 4. Le poste qu'on oublie : SignalR

`bff-mobile` porte les WebSockets (SignalR + backplane Redis). Une connexion
WebSocket ouverte **occupe de la mémoire en permanence**, qu'elle transporte quelque
chose ou non — de l'ordre de 30 à 60 Ko par connexion.

À 1 M d'utilisateurs/jour, une pointe de **30 000 à 50 000 connexions simultanées**
est plausible :

```
50 000 × 50 Ko  ≈  2,5 Go  — pour bff-mobile SEUL
```

Il est aujourd'hui plafonné à **640 Mo**. Il serait tué par l'OOM bien avant
d'atteindre cette charge, et la limite ne préviendrait personne : les mobiles se
reconnecteraient en boucle, aggravant la panne.

C'est le composant à surveiller en premier, et celui qui justifie de sortir
`bff-mobile` sur sa propre machine le moment venu.

---

## 5. Dimensionnement recommandé (cache corrigé)

| Poste | vCPU | RAM | Justification |
|---|---|---|---|
| `bff-mobile` × 4 réplicas | 4 | 8 Go | le chemin chaud + WebSockets |
| `bff-web` × 2 | 2 | 1,5 Go | |
| `api` × 2 | 2 | 1,5 Go | commandes, sagas |
| `bff-seller` / `bff-admin` × 1 | 2 | 1,5 Go | trafic faible |
| `redis` | 2 | 4–6 Go | paniers actifs + cache catalogue |
| `traefik` | 2 | 1 Go | terminaison TLS à 1 700 req/s |
| OS, Docker, journaux | 1 | 2 Go | |
| **Marge** (déploiement, GC, pics) | **+30 %** | **+30 %** | un déploiement bleu-vert **double** brièvement la mémoire applicative |
| **TOTAL** | **≈ 16 vCPU** | **≈ 24–32 Go** | |

### Le disque n'est pas le problème

- Images Docker : ~2 Go.
- Journaux : **plafonnés** (`max-size: 10m`, `max-file: 3` = 30 Mo/conteneur). Bien vu.
- Redis AOF : quelques Go.

**160 Go suffisent.** Le seul scénario qui change cela est l'installation de Loki /
Prometheus sur la même machine : à 30 M de requêtes/jour, les journaux d'accès
représentent plusieurs Go **par jour**. Dans ce cas, prévoyez 400 Go — ou, mieux,
sortez l'observabilité sur une autre machine.

---

## 6. L'objection de fond : pas un VPS, mais deux

**À 1 M d'utilisateurs/jour, un serveur unique est indéfendable.** Un `apt upgrade`,
un noyau à recharger, un disque qui lâche — et le service disparaît entièrement,
devant un million de personnes. Aucune quantité de RAM ne corrige cela.

Plutôt qu'**une** machine à 16 vCPU / 32 Go, prenez **deux** machines à 8 vCPU /
16 Go derrière un répartiteur de charge. Même prix, même puissance — mais l'une
peut redémarrer pendant que l'autre sert. C'est la seule différence qui compte
vraiment à cette échelle.

Neon doit alors passer sur une offre avec réplica de lecture.

---

## 7. Ce que je vous conseille réellement

Il faut le dire franchement : **1 M d'utilisateurs par jour au Bénin, c'est autour
d'un quart de tous les internautes du pays, chaque jour.** C'est une superbe étoile
polaire. Ce n'est pas une hypothèse de dimensionnement pour cette année.

Acheter aujourd'hui 32 Go pour un trafic que vous atteindrez dans trois ans, c'est
payer trente-six mois de capacité inutilisée — et l'architecture aura changé
d'ici là. La dépense n'achète pas de la sécurité, elle achète de l'attente.

Ce qui compte n'est pas la taille du serveur, c'est que **rien dans le code
n'empêche de grandir**. Concrètement, dans cet ordre :

1. **Cacher les lectures catalogue.** C'est le seul point qui divise le besoin par
   trois, et il est valable dès aujourd'hui — vos utilisateurs actuels en verront
   la différence.
2. **Plafonner la taille de page** (`ProductRepository` fait un `.Take(take)` non
   borné — un client peut demander 10 000 produits d'un coup ; c'est aussi un
   vecteur de déni de service).
3. **Étendre la limitation de débit** : elle ne couvre aujourd'hui que les groupes
   `/auth`. Le catalogue et la recherche sont ouverts.
4. **Ajouter des réplicas** dans le compose — c'est ce qui rend la montée en charge
   possible sans rien réécrire.

Ces quatre points faits, vous tenez confortablement **50 000 à 100 000 utilisateurs
par jour sur 4 vCPU / 8 Go**. Et le jour où le trafic double, vous ajoutez un
réplica au lieu de refaire l'architecture dans l'urgence.

---

## 8. Ce qui a été corrigé (13 juillet 2026)

### La fiche produit, avant et après

C'est l'endpoint le plus consulté de l'application (`GET /mobile/products/{id}`,
anonyme). Voici ce qu'il coûtait en requêtes SQL, à **chaque affichage** :

| Appel | Avant | Après (cache chaud) |
|---|---|---|
| Fiche produit (`ICatalogModuleApi`) | 1 requête | 0 |
| Offres du produit (prix) | 1 requête | 0 |
| Note agrégée (moyenne sur tous les avis) | 1 **agrégation** | 0 |
| Nom de boutique de chaque vendeur | **N requêtes** (une par vendeur, en boucle — et chacune chargeait les documents KYB) | 0 |
| **Total, produit à 3 vendeurs** | **6 requêtes** | **0** |

Le N+1 des vendeurs était le plus coûteux, et le plus invisible : il ne figurait
dans aucun endpoint, il se cachait dans une boucle `foreach`.

### Ce qui est désormais en cache

**Catalogue** — fiche produit (5 min), catégories et marques (30 min, ce sont des
données de référence lues à chaque ouverture de l'app), produits par boutique (2 min).
**Vendeurs** — résumé par vendeur et par utilisateur (10 min). `IsActiveSellerAsync`
ne fait plus de requête du tout : il se déduit du résumé déjà en cache.
**Offres** — 60 secondes seulement. **Ce module porte les prix** : un prix périmé
n'est pas une lenteur, c'est une promesse non tenue. Ne pas allonger ce TTL.
**Avis** — note agrégée et liste d'avis (10 min).

**Volontairement PAS en cache** : la liste admin de tous les produits. Une poignée
de personnes qui viennent vérifier l'état réel du catalogue, souvent juste après
l'avoir modifié. Leur servir une vue vieille de cinq minutes leur ferait douter de
leur propre action. Toutes les mises en cache ne sont pas des gains.

### Comment l'invalidation ne peut pas être oubliée

Le premier réflexe serait d'invalider dans chaque handler de commande. Le module
Catalog en compte **seize**. Il aurait suffi qu'un seul oublie — ou qu'un dix-septième
soit écrit demain sans y penser — pour servir indéfiniment une donnée périmée. Ce
bug-là est invisible en développement (le cache y est vide) et permanent en
production, sans erreur ni trace.

L'invalidation est donc faite dans le **`SaveChangesAsync` du DbContext**, en lisant
le `ChangeTracker` : toute écriture y passe forcément, et le tracker sait exactement
ce qui a changé — y compris quand seule une image ou une variante a bougé, sans que
le produit lui-même soit modifié.

Deux propriétés importantes :

- **L'éviction a lieu APRÈS le commit.** L'inverse ouvrirait à une lecture
  concurrente la possibilité de recharger l'ancienne valeur juste avant que la
  nouvelle ne soit validée — le cache resterait faux jusqu'à son TTL.
- **Le cache échoue OUVERT.** Si Redis tombe, on retombe sur la base : la
  marketplace ralentit, elle ne s'arrête pas. Un cache qui ferait tomber le site en
  tombant aurait *réduit* la disponibilité. (À l'inverse du CAPTCHA du site, qui doit
  échouer fermé : un cache et un contrôle de sécurité n'ont pas le même mode de
  défaillance.)

### Le cache négatif — la protection qu'on oublie toujours

Les fiches produit sont **anonymes**. Sans mémorisation des absences, il suffit de
boucler sur des identifiants au hasard (`/mobile/products/{guid}`) pour traverser le
cache à tous les coups et frapper Neon à chaque requête. Le cache devient inutile
exactement au moment où l'on en a besoin. C'est une attaque connue, et elle ne
demande qu'une boucle. Les absences sont donc mémorisées 30 secondes.

### Ce qui reste ouvert

- **La taille de page n'est toujours pas bornée** (`ProductRepository.ListAllAsync`,
  `.Take(take)`). Un client peut demander 10 000 produits d'un coup.
- **La limitation de débit ne couvre que `/auth`.** Le catalogue et la recherche
  sont ouverts.
- **Le module Search n'est pas caché.** Les requêtes de recherche ont une longue
  traîne (chaque utilisateur tape autre chose) : un cache y rapporte beaucoup moins
  qu'un index correct. À traiter séparément, et pas de la même façon.

### ⚠️ Ce code n'a pas été compilé

Le SDK .NET n'est pas disponible dans l'environnement où ces modifications ont été
écrites. La cohérence a été vérifiée à la main — signatures, appelants, factories
design-time, enregistrements DI des cinq hôtes, et un contrôle automatique
confirmant que **chaque clé mise en cache possède bien un point d'invalidation**.
Mais aucun compilateur n'est passé dessus.

**À faire avant de déployer :**

```bash
dotnet build
dotnet test
```
