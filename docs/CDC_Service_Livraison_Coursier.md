# Cahier des charges — Service de livraison par coursiers (« 2ᵉ mode »)

> **Statut** : spécification (v1, 22 juillet 2026) — **amendée le 8 septembre 2026**, voir §0.
> **Nature** : projet **autonome**, hébergé et déployé **séparément** de la plateforme
> HBA Express, pour ne pas alourdir le backend marketplace. Il communique avec la
> plateforme uniquement par des **contrats d'intégration** (événements + API), jamais
> par accès direct à ses bases.

---

## 0. Amendement du 8 septembre 2026 — où vit l'argent du coursier

La question laissée ouverte au §12 — *« Rémunération coursier : réutiliser
Wallet/Settlement plateforme **ou** portefeuille propre au service ? »* — est **tranchée :
le portefeuille du coursier reste dans Settlement**, avec ceux du vendeur, du client et
de la plateforme.

**Pourquoi.** Les raisons qui justifient un service séparé — dispatch temps réel,
géolocalisation continue, montée en charge indépendante — ne s'appliquent pas à l'argent.
Un portefeuille propre au service supposerait un second grand livre, une seconde
réconciliation comptable, un second cycle de retrait et une seconde intégration FedaPay.
La marketplace en possède déjà un, conçu dès l'origine pour quatre acteurs
(`WalletOwnerType.Courier` existe depuis le premier jour du chantier portefeuille).

**Ce que cela change concrètement.** Un module `Delivery` **mince** existe désormais dans
le monolithe. Il porte l'identité du coursier **côté marketplace** — de quoi le payer et
de quoi refuser de le payer :

| Dans le monolithe (`Delivery` + `Settlement`) | Dans le service coursier (ce document) |
|---|---|
| Compte utilisateur, nom, téléphone | Véhicule, KYC, zone, disponibilité |
| Compte de versement Mobile Money | Position, itinéraires, temps réel |
| Statut : Pending / Active / Suspended | Note, taux d'acceptation, taux d'annulation |
| Portefeuille, retraits, grand livre | Courses, offres, dispatch, cascade 3PL |

La frontière est tenue par un test d'architecture
(`DeliveryBoundaryTests.Le_module_reste_MINCE_le_dispatch_n_entre_pas_ici`) qui échoue si
un type nommé `DeliveryJob`, `DispatchOffer`, `PickupPoint`, `CourierPosition` ou
`CourierZone` apparaît dans le domaine du module. **Le reste du document reste valide** :
le service coursier demeure autonome, avec sa base, son temps réel et son app.

**Contrats effectivement en place**

*Entrant (service → marketplace)* — `POST /api/delivery/courses/completed`, signé
HMAC-SHA256 sur `horodatage.corps` (`X-Delivery-Signature` + `X-Signature-Timestamp`,
secret `Delivery:WebhookSecret`), **fermé par défaut** : hors développement, pas de
secret configuré = appel rejeté. L'horodatage est en secondes Unix et entre dans la
chaîne signée ; au-delà de cinq minutes d'écart, dans un sens ou dans l'autre, l'appel
est refusé — sans lui, un corps signé capturé une fois restait payable indéfiniment.
Corps : `{ courierId, orderId, deliveryJobId, amount, currency }`. `deliveryJobId` est la
clé d'idempotence — un rejeu ne paie pas deux fois.

*Sortant (marketplace → service)* — `CourierActivatedIntegrationEvent`,
`CourierSuspendedIntegrationEvent` (module Delivery) et
`CourierEarningCreditedIntegrationEvent` (module Settlement). Ils sont publiés dans
l'outbox ; **leur acheminement vers le service coursier reste à construire**.

**Ce qui n'est toujours pas décidé et qui bloque la mise en production**

- **Aucun plafond** sur la rémunération d'une course : l'appelant de l'endpoint décide
  combien sort du portefeuille de la plateforme. La signature protège d'un inconnu, pas
  d'un service compromis ni d'un bug de tarification chez lui.
- **Qui fixe le prix de la course** (§12, toujours ouvert) : le montant vient aujourd'hui
  du service, la marketplace ne le recalcule pas. Payer un coursier **débite le compte
  « frais de livraison » de la plateforme** ; si ce compte passe en négatif, c'est le
  signal que les forfaits facturés aux acheteurs ne couvrent pas les courses.
- **Le transport des événements sortants** vers le service (bus, webhooks signés ?).

---

## 1. Contexte et objectif

La marketplace HBA Express fonctionne aujourd'hui en **FBS** (*Fulfillment By Seller*) :
chaque vendeur expédie sa part de commande via un **transporteur** du catalogue
(module `Shipping`, conservé tel quel). Ce document décrit un **2ᵉ mode de livraison**
complémentaire : une **place de marché de coursiers** type Uber/Gozem, pour le
**dernier kilomètre local**.

**Problème résolu** : dans une commande **multi-vendeur** d'une même ville, plusieurs
vendeurs doivent converger vers un acheteur. Confier ça à des coursiers gig (moto)
qui **ramassent chez plusieurs vendeurs puis livrent l'acheteur** est plus rapide et
moins cher qu'un transporteur classique.

**Pourquoi un projet séparé**
- Garder le backend marketplace **léger** (le dispatch temps réel, la géolocalisation
  continue et l'app coursier ont des besoins d'infra très différents).
- **Découplage** : le service peut évoluer/scaler indépendamment (le temps réel et la
  géo sont gourmands).
- **Réutilisabilité** : le service coursier pourrait servir d'autres verticales.

**Objectif du MVP** : quand **tous les vendeurs** d'une commande ont marqué leur part
« préparée », créer une **course** et l'**offrir aux coursiers éligibles** ; **le premier
qui accepte** prend la livraison, ramasse chez les vendeurs, et livre l'acheteur.

---

## 2. Périmètre

**Dans le périmètre**
- Catalogue et onboarding des **coursiers** (profil, véhicule, KYC, zone, disponibilité).
- Cycle de vie d'une **course** (DeliveryJob) de la création à la livraison.
- **Dispatch** temps réel aux coursiers + **acceptation « premier arrivé »**.
- Suivi de **position** du coursier et **suivi** pour vendeur/acheteur.
- **Tarification** de la course et **rémunération** du coursier.
- **App coursier** (client mobile dédié).
- **Contrats d'intégration** avec la plateforme (entrants/sortants).

**Hors périmètre**
- Le mode **FBS** existant (transporteurs) : inchangé, géré par la plateforme.
- Le paiement de la commande par l'acheteur (reste sur la plateforme).
- La logistique inter-villes / longue distance.

---

## 3. Acteurs

| Acteur | Rôle |
|---|---|
| **Coursier (livreur)** | Reçoit les offres de course, accepte, ramasse, livre. |
| **Vendeur** | Prépare sa part ; remet le colis au coursier au ramassage. |
| **Acheteur** | Reçoit le colis ; suit le coursier ; confirme la réception. |
| **Admin plateforme** | Gère les zones, les règles de tarif, modère les coursiers. |
| **Plateforme marketplace** | Émet « commande prête », reçoit les mises à jour de livraison. |
| **Service coursier** (ce projet) | Orchestre le dispatch et le cycle de la course. |

---

## 4. Domaine métier

### 4.1 Agrégats et entités

**`Courier`** (coursier)
- Identité, nom, téléphone, **véhicule** (moto, vélo, voiture), plaque.
- **Statut de compte** : `Pending` (KYC en cours) → `Active` → `Suspended`.
- **Disponibilité** : `Online` / `Offline` (ne reçoit d'offres qu'en ligne).
- **Position** courante (lat/lng, horodatée) — mise à jour périodiquement en ligne.
- **Zone(s)** de service.
- **Note moyenne**, nombre de courses, taux d'acceptation, taux d'annulation.
- Compte de **versement** (rémunération).

**`DeliveryJob`** (course) — agrégat racine
- Référence **commande** plateforme (`orderId`), **acheteur**.
- **Points de ramassage** (`PickupPoint[]`) : un par vendeur/lieu d'expédition
  (adresse, contact vendeur, colis attendus, statut ramassé/non).
- **Point de dépôt** (`Dropoff`) : adresse acheteur, contact, instructions.
- **Statut** (machine à états, §4.2).
- **Source de fulfillment** : `Internal` (coursier plateforme) / `External:{prestataire}`
  (3PL) / `Fbs` (transporteur classique). Détermine le canal de suivi (position live si
  interne, statuts via API sinon). Voir le **repli en cascade** (§5.7).
- **Coursier assigné** (null tant qu'offerte ; null aussi si source externe/FBS).
- **Référence prestataire** (id de livraison chez le 3PL, si source externe).
- **Tarif** de la course (montant, devise), **part coursier**, **commission plateforme**.
- Horodatages : créée, offerte, acceptée, ramassée, livrée, annulée/expirée.
- **Historique de dispatch** (à qui offerte, timeouts, ré-offres).
- **Preuve de livraison** (photo, code, signature — au choix).

**`DispatchOffer`** (offre de course à un coursier) — entité de dispatch
- `deliveryJobId`, `courierId`, `offeredAtUtc`, `expiresAtUtc`, statut
  (`Sent` / `Accepted` / `Declined` / `Expired`).

### 4.2 Cycle de vie d'une course (machine à états)

```
                 ┌─────────────┐  (aucun coursier / timeout global)
                 │             ▼
Created ──▶ Offered ──▶ Accepted ──▶ PickingUp ──▶ InTransit ──▶ Delivered
   │           │  ▲         │             │            │
   │           │  └─ ré-offre│             │            │
   ▼           ▼            ▼             ▼            ▼
Cancelled   Expired     Cancelled     Cancelled    Failed
```

- **Created** : course construite à partir de la commande prête (points de ramassage +
  dépôt + tarif calculé).
- **Offered** : diffusée aux coursiers éligibles ; en attente d'acceptation.
- **Accepted** : **un** coursier a pris la course (verrou « premier arrivé »).
- **PickingUp** : le coursier ramasse chez les vendeurs (chaque `PickupPoint` passe
  « ramassé »).
- **InTransit** : tous les ramassages faits, en route vers l'acheteur.
- **Delivered** : remis à l'acheteur (avec preuve).
- **Expired** : personne n'a accepté dans le délai global → repli (§5.4).
- **Cancelled** : annulée (par l'admin, ou coursier avant ramassage sous conditions).
- **Failed** : échec en cours de route (acheteur absent, colis refusé) → procédure.

### 4.3 Invariants

- Une course est liée à **exactement une** commande plateforme.
- Un `DeliveryJob` n'a **au plus un** coursier assigné ; l'assignation est **irréversible**
  sans passer par une annulation/ré-offre explicite.
- Le passage à `InTransit` exige que **tous** les `PickupPoint` soient « ramassés ».
- La transition `Offered → Accepted` est **atomique** et **exclusive** (un seul gagnant).

---

## 5. Règles métier clés

### 5.1 Déclencheur : « commande prête »
La course n'est créée que lorsque **toutes** les expéditions (parts vendeurs) d'une
commande sont marquées « préparées ». Côté plateforme, c'est détecté quand la **dernière**
part bascule en `Preparing/Prepared` → émission de l'événement `OrderReadyForDelivery`
(contrat §6). Le service coursier consomme cet événement et crée le `DeliveryJob`.

### 5.2 Dispatch (matching)
Sélection des coursiers **éligibles** : `Active` **et** `Online` **et** dans la **zone**
**et** proches (rayon initial, ex. 3 km du 1ᵉʳ ramassage) **et** capacité véhicule
suffisante. Ordre de priorité indicatif : distance/ETA, note, taux d'acceptation.

### 5.3 Acceptation « premier arrivé » (point critique)
L'offre est envoyée à N coursiers simultanément. **Le premier qui accepte gagne.** Il faut
un **claim atomique** : une seule transaction fait passer la course `Offered → Accepted`
avec **concurrence optimiste** (ex. `UPDATE ... WHERE status = 'Offered'` ; 0 ligne
affectée = trop tard, l'app affiche « course déjà prise »). Aucune double-assignation
possible.

### 5.4 Timeout et ré-offre
- Chaque offre a un **délai** (ex. 20–30 s). Sans acceptation, on **élargit le rayon**
  et on ré-offre.
- **Délai global** (ex. 5 min) : si aucun coursier interne n'accepte → on ne s'arrête pas
  là, on **enclenche le repli en cascade** (§5.7) au lieu d'échouer sèchement.

### 5.5 Multi-ramassage
- Les `PickupPoint` sont **ordonnés** (séquence de collecte optimisée : proximité).
- Un vendeur **non prêt** au ramassage : marquer le point en incident, poursuivre les
  autres, procédure de rattrapage.

### 5.6 Cycle d'échec
- Coursier qui **annule après acceptation** : pénalité (note/taux), ré-offre immédiate.
- **Acheteur absent** : règles de contact/attente/retour.
- **Colis manquant** chez un vendeur : le point reste non ramassé, notification.

### 5.7 Repli en cascade & prestataires externes
Quand aucun coursier interne n'accepte (§5.4), on ne bascule **pas** directement en échec :
on descend une **cascade** de sources de fulfillment, dans l'ordre, jusqu'à ce qu'une
prenne la course. La course garde le même cycle de vie ; seule sa **source** change.

```
1. Coursiers INTERNES  ──(expiration/aucun dispo)──▶
2. Prestataire EXTERNE (3PL)  ──(refus/indisponible/pas de couverture)──▶
3. Repli FBS (transporteur classique) ou traitement MANUEL + alerte admin
```

**Prestataire externe (3PL).** On confie la livraison à une société tierce via **son API** :
le service crée une livraison chez elle, puis suit son statut (pull ou webhooks). Le
prestataire **devient le « coursier »** : pas de dispatch interne, pas de position live
(sauf si le prestataire l'expose).

**Abstraction — port `IExternalDeliveryProvider`** (pour ne coupler le domaine à aucun
fournisseur précis, et pouvoir en brancher plusieurs) :
- `Quote(job)` → coût/délai estimé (pour décider et comparer au frais figé).
- `CreateDelivery(job)` → crée la livraison, renvoie la **référence prestataire**.
- `GetStatus(ref)` → statut normalisé vers la machine à états interne.
- `Cancel(ref)` → annulation.
- `HandleWebhook(payload)` → maj de statut poussée par le prestataire (idempotente).

Chaque prestataire concret (ex. un agrégateur local, une société de coursiers) implémente
ce port ; on peut définir une **priorité/rotation** entre eux, filtrée par **couverture de
zone** et **devis**.

**Points de vigilance :**
- **Écart de prix** : le devis du 3PL peut dépasser le frais de livraison figé au checkout
  → règle d'absorption (marge plateforme, seuil de refus, ou re-devis à l'acheteur).
- **Suivi** : mapper leur statut/tracking vers `PickingUp/InTransit/Delivered` ; webhooks
  **idempotents** (id d'événement).
- **Responsabilité/SLA** en cas d'échec du prestataire (retour au niveau suivant de la cascade).
- **Disponibilité réelle d'une API** exploitable dans la zone cible (beaucoup d'acteurs
  n'exposent pas d'API — à vérifier avant de s'engager).

---

## 6. Intégration avec la plateforme (contrats)

Le service **ne touche pas** aux bases de la marketplace. Échanges via **bus de messages**
ou **webhooks** signés, avec une **couche anti-corruption** de chaque côté.

**Événements ENTRANTS (plateforme → service coursier)**
- `OrderReadyForDelivery` : `orderId`, acheteur (adresse dépôt, contact), liste des
  points de ramassage (lieu vendeur, contact, articles), tarif de livraison figé.
- `OrderCancelled` : annuler la course associée si non encore livrée.

**Événements SORTANTS (service coursier → plateforme)**
- `DeliveryAccepted` : `orderId`, coursier (nom, téléphone, position), ETA.
- `DeliveryPickedUp` : ramassages effectués.
- `DeliveryInTransit` / `DeliveryDelivered` (avec preuve) : la plateforme met à jour la
  commande et déclenche l'encaissement/versement.
- `DeliveryFailed` / `DeliveryExpired` : la plateforme applique son repli (FBS, remboursement).

**API** (exposée par le service, appelée par la plateforme/admin)
- Lecture de l'état d'une course par `orderId`.
- Gestion des zones et règles de tarif (ou l'admin plateforme les pousse).

> **Idempotence & fiabilité** : chaque événement porte un id ; consommation idempotente
> des deux côtés (outbox/inbox), pour survivre aux ré-émissions.

---

## 7. Tarification et rémunération

- **Tarif de la course** : calculé à partir de la **distance totale** (ramassages + dépôt),
  du **nombre de points de ramassage**, éventuellement de la **zone**/heure. Peut être
  **figé par la plateforme** au checkout (le service reçoit le montant) OU **calculé par
  le service** (à décider — voir §12).
- **Répartition** : part **coursier** + **commission plateforme**.
- **Versement coursier** : soit via le **Wallet/Settlement** existant de la plateforme
  (le service émet « à payer »), soit un portefeuille propre au service. À trancher (§12).

---

## 8. Géolocalisation et suivi temps réel

- **Position coursier** : envoyée périodiquement quand en ligne / en course (fréquence
  adaptative : plus rapide en course). Compromis **batterie / précision / vie privée**.
- **Suivi** : l'acheteur et le vendeur voient la position du coursier et l'ETA une fois
  la course acceptée.
- **Canal temps réel** : WebSocket/SignalR (offres de course, changements d'état,
  position). Gérer reconnexion, hors-ligne, et **notifications push** en secours quand
  l'app coursier est en arrière-plan.

---

## 9. App coursier (client dédié)

Parcours principaux :
1. **Onboarding/KYC** : inscription, pièces, véhicule → validation admin.
2. **Se mettre en ligne / hors ligne** ; partage de position.
3. **Recevoir une offre** (montant, distance, nb de ramassages, ETA) → **Accepter** /
   Refuser, avec **compte à rebours**.
4. **Course** : itinéraire vers chaque ramassage → confirmer chaque collecte → itinéraire
   dépôt → **preuve de livraison** (photo/code) → clôture.
5. **Historique & gains** ; note reçue.

Techniquement : Flutter (comme les autres apps), géoloc en arrière-plan, push, temps réel.

---

## 10. Exigences non fonctionnelles

- **Temps réel** : offre reçue en < 1 s ; changements d'état propagés en quasi-temps réel.
- **Concurrence** : garantie stricte « un seul gagnant » sur l'acceptation.
- **Résilience** : reprise après coupure réseau (coursier) ; ré-offre automatique.
- **Scalabilité** : le dispatch et la géo doivent tenir la charge indépendamment de la
  marketplace (raison d'être du projet séparé).
- **Sécurité / vie privée** : position = donnée sensible (minimisation, durée de rétention,
  accès restreint) ; contacts vendeur/acheteur partagés au coursier **seulement pendant**
  la course.
- **Disponibilité** : une panne du service coursier **ne doit pas bloquer** la marketplace
  (repli FBS).
- **Auditabilité** : historique complet des offres, acceptations, positions clés.

---

## 11. Architecture technique proposée

- **Service autonome** (API + base propre) — pas d'accès aux schémas marketplace.
- **Domaine** : agrégats `Courier`, `DeliveryJob` (+ `PickupPoint`, `DispatchOffer`).
  Le `Courier` du service porte l'opérationnel (véhicule, position, zone, disponibilité,
  note) ; son identité de BÉNÉFICIAIRE (nom, téléphone, compte de versement, droit d'être
  payé) vit dans le module `Delivery` du monolithe — voir §0. Les deux sont reliés par
  `Courier.ExternalRef`.
- **Temps réel** : hub WebSocket/SignalR pour offres, états, positions.
- **Intégration** : bus de messages (ou webhooks signés) + **inbox/outbox** idempotents ;
  couche anti-corruption traduisant les contrats plateforme ↔ domaine coursier.
- **Prestataires externes** : port `IExternalDeliveryProvider` (§5.7) + **un adaptateur par
  3PL** ; sélection par couverture de zone/devis, orchestrée par le **repli en cascade**.
- **Notifications push** (secours arrière-plan).
- **App coursier** Flutter dédiée.
- **Observabilité** : métriques dispatch (temps d'acceptation, taux d'expiration), traces.

---

## 12. Décisions à trancher (avant Phase 1)

- **Qui fixe le prix de la course ?** Plateforme au checkout (figé) **ou** service (dynamique
  à la distance) ? → impacte le contrat `OrderReadyForDelivery`.
- ~~**Rémunération coursier** : réutiliser Wallet/Settlement plateforme **ou** portefeuille
  propre au service ?~~ → **TRANCHÉ le 08/09/2026 : Wallet/Settlement de la plateforme.**
  Voir §0.
- **Zones cibles** du MVP (quelle(s) ville(s) ?) et **densité de coursiers** minimale.
- **Preuve de livraison** : photo, code à 4 chiffres, ou signature ?
- **FBS vs coursier** : critère de bascule (par zone ? au choix de l'acheteur ? automatique
  si tous les vendeurs sont dans la même ville ?).
- **Prestataire(s) externe(s)** : quel(s) 3PL, et **exposent-ils une API** exploitable dans
  la zone cible ? (bloquant pour l'étape 2 de la cascade — §5.7).
- **Écart de prix 3PL vs frais figé** : qui l'absorbe (plateforme, seuil, re-devis acheteur) ?
- **Ordre de la cascade** : interne → externe → FBS/manuel est le défaut ; à confirmer selon
  coûts et disponibilité par zone.

---

## 13. Découpage par phases

1. **Phase 1 — Socle métier (backend, testable sans temps réel)**
   Domaine `Courier` + `DeliveryJob`, consommation de `OrderReadyForDelivery`, création de
   course, **acceptation atomique « premier arrivé »** via endpoint, machine à états,
   émission des événements sortants. **Verrouille le modèle.**
2. **Phase 2 — Dispatch temps réel**
   Hub WebSocket, offres avec timeout/ré-offre, statut en ligne, matching par zone/distance.
3. **Phase 3 — App coursier + géolocalisation**
   Client Flutter, position, itinéraires, preuve de livraison, suivi acheteur/vendeur.
4. **Phase 4 — Rémunération & notation**
   Tarif/répartition, versements coursier, notes et réputation.

---

## 14. Risques

- **Densité de coursiers insuffisante** → courses non prises (expérience dégradée). Prévoir
  le repli FBS dès le départ.
- **Fiabilité du temps réel** sur réseaux mobiles instables.
- **Concurrence sur l'acceptation** mal gérée = double-assignation (à tester durement).
- **Vie privée / réglementaire** des livreurs (statut, assurance) — au-delà du technique.
- **Coût d'un nouveau client** (app coursier) et de son maintien.

---

## 15. Glossaire

- **Course / DeliveryJob** : mission de livraison d'une commande (ramassages + dépôt).
- **Dispatch** : diffusion d'une course aux coursiers éligibles.
- **Premier arrivé** : le premier coursier à accepter remporte la course.
- **FBS** : *Fulfillment By Seller*, mode transporteur existant (module `Shipping`).
- **Point de ramassage** : lieu d'un vendeur d'où le coursier collecte un colis.
- **Repli en cascade** : descente ordonnée des sources de fulfillment (interne → externe →
  FBS/manuel) jusqu'à ce qu'une prenne la course.
- **3PL / prestataire externe** : société de livraison tierce à qui l'on confie la course
  via son API (2ᵉ niveau de la cascade).
- **Source de fulfillment** : qui exécute la course — `Internal`, `External:{3PL}` ou `Fbs`.
