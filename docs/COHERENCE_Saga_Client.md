# Point de cohérence — la saga acheteur, de bout en bout

> 9 septembre 2026. Parcours suivi dans le code, module par module, depuis l'inscription
> jusqu'au retrait de cagnotte. Les points marqués **[vérifié]** ont été relus
> personnellement, pas seulement rapportés.

---

## Verdict

**La chaîne se casse à la livraison, et elle se casse silencieusement.**

Le coursier livre, il est payé, et l'expédition reste en préparation. Aucun événement
n'est levé, la commande ne passe jamais `Delivered`, **la séquestre du vendeur n'est
jamais libérée**. Il n'y a pas d'erreur, pas de lettre morte, pas d'alerte : le système
fonctionne exactement comme prévu à chaque étape, et le résultat est qu'un vendeur qui a
expédié n'est jamais payé.

En amont, deux étapes du parcours dépendent d'un bouton que rien n'oblige à presser. En
aval, les deux circuits de remboursement ne partagent pas de plafond.

| Segment | État |
|---|---|
| Compte et session | Fonctionne, avec deux impasses utilisateur |
| Panier → commande | Solide sur l'argent, deux valeurs manquantes |
| Paiement → confirmation | **Le meilleur du dépôt.** Rien à redire |
| Confirmation → livraison | **Rompu en deux endroits** |
| Après-vente | Trois trous d'argent |

---

## 1. La rupture principale : la boucle de livraison ne se referme pas

### 1.1 Le coursier livre, l'expédition ne bouge pas [vérifié]

`POST /api/delivery/courses/completed` — ce que le service de dispatch appelle quand un
coursier marque une course livrée — fait **une seule chose** :

```csharp
var result = await sender.Send(
    new CreditCourierDeliveryCommand(
        request.CourierId, request.OrderId, request.DeliveryJobId,
        request.Amount, string.IsNullOrWhiteSpace(request.Currency) ? "XOF" : request.Currency),
    ct);
```

Il crédite le coursier. Il ne touche ni Shipping, ni Ordering.

Or le seul producteur de `ShipmentDeliveredIntegrationEvent` est
`Shipment.MarkDelivered`, et celui-ci exige l'état `Shipped` :

```csharp
public Result MarkDelivered()
{
    if (Status != ShipmentStatus.Shipped)
    {
        return Result.Failure(Error.Conflict("shipping.invalid_transition",
            "Seule une expédition en transit peut être livrée."));
    }
```

L'expédition partie en course est en `Preparing`, pas en `Shipped`. Elle ne peut donc pas
être marquée livrée. Et pour la faire passer en `Shipped`, il faut un **transporteur et
un numéro de suivi**, tous deux obligatoires :

```csharp
if (string.IsNullOrWhiteSpace(carrier) || string.IsNullOrWhiteSpace(trackingNumber))
{
    return Result.Failure(Error.Validation("shipping.tracking_required",
        "Transporteur et numéro de suivi sont obligatoires."));
}
```

Des données qui **n'existent pas** pour une moto en interne. Le modèle d'expédition est
celui d'un transporteur tiers ; le dispatch, lui, est en propre. Les deux n'ont jamais été
raccordés.

**Conséquence en chaîne** : expédition bloquée en `Preparing` → pas de
`ShipmentDeliveredIntegrationEvent` → `MarkOrderDeliveredOnAllShipmentsDelivered` ne se
déclenche pas → la commande reste `Confirmed` → ni
`ReleaseEscrowOnOrderDeliveredHandler` ni `ReleaseEarningsOnOrderDeliveredHandler` ne
tournent → **la séquestre du vendeur n'est jamais libérée**, et `Payment.EscrowReleasedAt`
reste nul indéfiniment.

La seule sortie est qu'un administrateur appelle `/api/orders/{id}/deliver` à la main,
pour chaque commande.

**Ce qui rend ce défaut coûteux, c'est qu'il ne fait aucun bruit.** Chaque composant
répond correctement : le coursier est payé, le crédit est idempotent, l'expédition refuse
une transition invalide — mais personne n'appelle cette transition, donc personne ne voit
le refus. Un défaut qui échoue produit une lettre morte ; celui-ci produit une commande
qui a l'air normale.

### 1.2 Et en amont, le dispatch dépend d'un bouton facultatif [vérifié]

La course n'est créée que par `OrderReadyForDeliveryIntegrationEvent`, lui-même publié
uniquement par `ShipmentPreparingDomainEvent`, lui-même levé uniquement par
`MarkPreparing`. Mais `MarkShipped` accepte `Pending` comme état d'entrée :

```csharp
if (Status is not (ShipmentStatus.Pending or ShipmentStatus.Preparing))
```

Un vendeur qui appelle `/seller/shipments/{id}/ship` sans passer par `/prepare` va
directement de `Pending` à `Shipped`. `ShipmentPreparingDomainEvent` n'est jamais levé,
**aucune course n'est jamais créée**, et la commande est invisible du service de dispatch.
Rien ne le journalise. Le tableau de bord vendeur renomme même l'état
(`"Preparing"` → `"Prepared"`), ce qui se lit comme une écriture comptable optionnelle
plutôt que comme le seul déclencheur de la livraison.

**Les deux ruptures se combinent** : la 1.2 empêche la course d'exister, la 1.1 empêche la
course existante de refermer la boucle. Le parcours nominal actuel ne mène nulle part.

### 1.3 Ce qu'il faudrait, à mon sens

Ce n'est pas un correctif d'une ligne, c'est une décision de modèle : **une livraison en
propre n'est pas une expédition transporteur**. Trois options, par ordre de coût :

1. `/api/delivery/courses/completed` appelle aussi une transition d'expédition dédiée
   (`MarkDeliveredByCourier`) qui accepte `Preparing` et n'exige pas de numéro de suivi.
   Le plus court chemin, et il garde le modèle actuel.
2. Rendre `carrier`/`trackingNumber` facultatifs quand l'expédition est portée par une
   course interne, en marquant l'expédition avec l'identifiant de course.
3. Séparer franchement les deux modes de livraison dans le domaine Shipping.

Et pour 1.2 : `MarkShipped` ne devrait plus accepter `Pending`, ou bien la publication de
`OrderReadyForDelivery` doit être déplacée sur la confirmation de commande plutôt que sur
la préparation.

---

## 2. Ce qui bloque un acheteur avant même de commander

### 2.1 L'écran de connexion ne mène nulle part pour un compte non confirmé [vérifié]

```csharp
if (user.Status == UserStatus.PendingVerification)
{
    _security.LoginFailed(Method, "pending_approval", ClientType);
    return Result.Failure<LoginResponse>(Error.Forbidden(
        "identity.auth.pending_approval",
        "Votre compte est en attente de validation. Vous serez prévenu dès qu'il sera activé."));
}
```

`PendingVerification` a désormais **deux sens** — « n'a pas saisi son code » et « attend
un administrateur » — et les deux produisent le même code d'erreur et le même message. Or
le premier cas est le cas NORMAL de tout acheteur, et le message lui dit de **ne rien
faire et d'attendre une notification qui n'arrivera jamais**.

Un acheteur qui s'inscrit, perd l'e-mail, réinstalle l'app et revient sur l'écran de
connexion est définitivement bloqué : l'app n'a que `403 pending_approval`, elle ne peut
pas le router vers l'écran de saisie du code ni vers `/email/resend`.

Le commentaire au-dessus de ce bloc affirme d'ailleurs qu'un compte « n'en sort que par
l'approbation d'un administrateur ». C'est **faux depuis la correction 1.1** :
`ConfirmEmailCommandHandler` appelle `Approve()`. Le commentaire décrit un état du monde
qui a changé, et c'est probablement ce qui a masqué le problème.

**Correction** : un code d'erreur distinct — `identity.auth.email_not_verified` — que
l'app puisse router vers l'écran du code.

### 2.2 Le renvoi de code ment pendant la première minute [vérifié par lecture]

Le délai minimal d'une minute que je viens de poser est armé **à la création du compte**,
puisque l'inscription pose déjà `EmailVerificationExpiresOnUtc`. Le geste naturel —
s'inscrire, ne rien voir arriver en quinze secondes, taper « Renvoyer » — répond donc
« un nouveau lien vient d'être envoyé » et n'envoie rien.

C'est ma correction d'hier, et son effet de bord ne m'était pas apparu. Le budget d'envoi
reste juste ; il ne devrait simplement pas compter l'envoi de l'inscription comme un
premier renvoi. **Correction** : n'armer le délai qu'à partir du deuxième envoi, ou le
descendre à trente secondes.

### 2.3 Un numéro déjà pris bloque définitivement, en silence

Nouvelle adresse + numéro déjà rattaché à un autre compte (SIM réattribuée, numéro
familial, second compte) → `phone_taken` → **aucun compte n'est créé**, et l'appelant
reçoit quand même `registered = true`. Ensuite : `/email/resend` ne trouve rien et répond
« envoyé » ; `/login` répond identifiants invalides ; `/password/forgot` répond « envoyé »
sans rien envoyer.

Quatre routes qui annoncent toutes un succès, et un acheteur définitivement bloqué —
sans qu'aucun signal ne soit émis qu'un agent du support pourrait rechercher.

C'est l'arbitrage que j'avais signalé hier en corrigeant l'énumération, et le voici en
situation. La sortie propre n'est pas de rouvrir l'oracle : c'est de **journaliser** le
`phone_taken` avalé, avec l'adresse, pour que le support puisse le retrouver.

### 2.4 `password/reset` n'a aucune règle de mot de passe

Le dossier `PasswordReset/` ne contient que la commande et son handler — **pas de
validateur**, là où `RegisterUserCommandValidator` et `ChangePasswordCommandValidator`
imposent tous deux 8 à 128 caractères. Le contrôle du domaine porte sur le *hash*
(`string.IsNullOrWhiteSpace(newPasswordHash)`), jamais sur le mot de passe.

La route de réinitialisation est donc la seule de la plateforme qui accepte
`NewPassword = "a"` — et elle révoque toutes les sessions, ce qui fait de ce mot de passe
faible la seule voie d'accès restante.

### 2.5 Codes à six chiffres sans compteur de tentatives

Vérification e-mail : 6 chiffres, **48 heures**. Réinitialisation : 6 chiffres, 1 heure.
Un code faux ne décrémente rien, ne verrouille rien, ne purge pas la demande en cours. Le
seul plafond est le limiteur `auth`, 30 requêtes par minute et **par IP** — et
`AuthRateLimiter` dit lui-même que ça ne protège pas un compte précis.

Un espace de 10⁶ avec une heure de validité et aucun compteur par compte, sur une route
qui aboutit à une prise de contrôle : c'est le point le plus sérieux de ce segment.
`Sha256TokenGenerator` sait produire un jeton opaque de 256 bits ; c'est le format à
utiliser pour un **lien**, et le code à six chiffres à réserver à la saisie manuelle avec
un compteur de tentatives.

---

## 3. Ce qui se perd entre le panier et la commande

### 3.1 Le gel des valeurs sur la commande : c'est solide

Prix de base, remises, prix unitaire final, taux de commission, taux de frais prestataire,
code promo, adresse, frais de port, cagnotte appliquée — **tout est figé sur la commande**,
et Settlement lit délibérément les taux gelés de la ligne plutôt que la configuration
courante. `GrossSplit.Split` calcule le net comme un reste, donc les trois parts somment
exactement. C'est la meilleure partie du dépôt et elle mérite d'être dite.

### 3.2 La réservation de stock a une date d'expiration que personne ne lit [vérifié]

`ReserveStockCommand(..., int ExpiresInMinutes = 15)` et
`StockReservation.ExpiresAtUtc` existent. Mais :

```csharp
public int Reserved => _reservations.Sum(r => r.Quantity);
public int Available => OnHand - Reserved;
```

Aucun filtre sur `ExpiresAtUtc`. Dans tout le module Inventory, ce champ apparaît
exactement à trois endroits : l'affectation dans le constructeur, la propriété, et la
configuration EF. **Aucune requête, aucun balayeur, aucun `WHERE ExpiresAtUtc > now`.**

Le stock est donc immobilisé jusqu'à ce que `AbandonedOrderSweeper` annule la commande —
soit **six heures**, vingt-quatre fois plus que ce que la réservation annonce. Sur une
référence à faible stock, un panier abandonné rend l'article indisponible pour tout le
monde pendant une demi-journée.

### 3.3 Le total affiché et le total facturé sont deux évaluations indépendantes

`/checkout/quote` valorise le panier à l'instant T1. `/checkout/pay` le revalorise à T2, à
travers `PlaceOrderCommand`. Entre les deux, `PriceCalculator` réévalue
`promotion.IsActiveAt(now)`.

Scénario : l'acheteur ouvre le paiement à 23 h 58 avec une promo qui expire à minuit, voit
11 500, appuie sur Payer à 00 h 01. La commande est figée à 13 000, et le PSP demande
13 000 contre un écran qui affichait 11 500. **Rien ne compare le total annoncé au total
figé**, et `PayRequest` ne porte aucun champ de total attendu.

**Correction** : un `expectedTotal` dans `PayRequest`, refusé en 409 s'il diverge. C'est
le contrat standard de ce genre de tunnel, et il coûte trois lignes.

### 3.4 Deux valeurs absentes du parcours

- **La taxe n'existe nulle part.** Le module Tax est complet — `TaxRule`, `TaxResolver`,
  `ITaxModuleApi.ComputeTaxAsync` — et son contrat dit « permet à Ordering/Cart de
  calculer la taxe applicable ». `ITaxModuleApi` n'a **aucun appelant** hors de son propre
  installeur. Ni le panier, ni le tunnel, ni la commande ne mentionnent la taxe. Si de la
  TVA est attendue sur ces commandes, elle n'est ni affichée ni stockée.
- **Les frais de port sont un tableau en dur dans le BFF** : `standard 1500`,
  `express 3000`. Aucune entrée hors du code choisi par le client — ni commune de
  destination, ni poids, ni lieu d'expédition. Cotonou et Karimama, sept cents kilomètres
  d'écart, coûtent le même prix. Le module Shipping contient pourtant
  `ShippingZoneResolver`, `ShippingZone` et `ShippingRate`, avec des règles de
  départage soignées — mais **pas de `DbSet`, pas de configuration EF, pas de dépôt,
  aucun appelant**. La tarification géolocalisée est écrite et inatteignable.

### 3.5 `POST /api/orders` est une route morte qui répond 400 en permanence

`OrderingEndpoints.cs` envoie encore `new PlaceOrderCommand(buyerId)` sans adresse. Depuis
que l'invariant est passé dans le handler, cette route ne peut plus créer d'adresse
manquante — mais elle ne peut plus rien créer du tout. Sa documentation affirme toujours
qu'elle fonctionne.

---

## 4. L'argent : ce qui peut être créé, détruit ou immobilisé

### 4.1 Les deux circuits de remboursement ne partagent pas de plafond [vérifié]

Le remboursement direct (admin, en Mobile Money) plafonne sur `GrandTotal` moins la somme
des remboursements **de sa propre table**. Le commentaire le dit :

```csharp
// NE NETTE PAS les remboursements issus du flux « retour ». À suivre si les deux
// flux doivent partager un plafond commun.
```

Symétriquement, `ReturnRequest.ApproveRefund` ignore l'existence des `CustomerRefund`. Sur
une même commande, la plateforme peut donc verser `GrandTotal` en espèces **et**
`Σ LineTotal` en cagnotte — environ **deux fois la valeur de la commande**, chaque plafond
étant vérifié proprement de son côté.

### 4.2 La même ligne peut être remboursée un nombre illimité de fois [vérifié]

```csharp
public async Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, ...)
    => await _dbContext.ReturnRequests.AnyAsync(
        r => r.OrderId == orderId && r.OfferId == offerId
             && r.Status != ReturnStatus.Rejected && r.Status != ReturnStatus.Refunded, ...);
```

`Refunded` est exclu, et l'index `(OrderId, OfferId)` n'est pas unique. Une fois le
remboursement versé, un second retour sur la même ligne repart avec le même plafond.

Le commentaire au-dessus montre que le cas *en cours* a été raisonné avec soin —
`RefundPending` compte bien comme ouvert, pour empêcher exactement ce doublon. C'est le
cas *cumulé* qui n'a pas été fermé : il manque un plafond **par ligne**, pas seulement par
retour.

### 4.3 Un litige tranché promet un remboursement que personne ne verse [vérifié]

`DisputeResolvedIntegrationEvent` a **un seul consommateur** dans tout le dépôt :
`DisputeResolvedNotificationHandler`. Settlement n'en a aucun.

Et ce consommateur unique écrit à l'acheteur :

> « Votre litige est tranché : un remboursement de {amount} vous sera versé. »

Zéro franc ne bouge, et **aucune tâche n'est créée nulle part**. C'est pire qu'un trou
d'argent : c'est un engagement écrit envoyé par e-mail et par notification, sans
contrepartie et sans trace pour le support.

C'est aussi le point que j'avais laissé ouvert dans l'audit des routes — « le modérateur
fixe un montant qu'un humain paiera ensuite ». La vérification montre qu'il n'y a pas
d'humain : rien ne lui signale qu'il y a quelque chose à payer.

### 4.4 Un remboursement ne retourne jamais d'où il vient

`Order.WalletApplied` existe précisément pour ça, et son commentaire le dit : « un retour
survient des semaines plus tard, quand il faut savoir quelle part rendre en espèces et
quelle part en cagnotte ». Or `WalletApplied` n'apparaît **nulle part** dans le module
Returns.

Un acheteur qui a payé 8 000 en Mobile Money et 5 000 de cagnotte reçoit 13 000 **en
cagnotte**. Le grand livre reste équilibré — la contrepartie est prélevée sur le vendeur
et la plateforme — mais le solde retirable de l'acheteur augmente de 13 000 alors que la
plateforme n'a encaissé que 8 000 d'espèces pour cette commande. L'exposition est réelle,
simplement différée et invisible.

### 4.5 Un retour est impossible après la livraison [vérifié]

```csharp
if (!string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
{
    return Result.Failure<Guid>(Error.Conflict("returns.order_not_eligible",
        "Seule une commande confirmée peut faire l'objet d'un retour."));
}
```

La livraison fait passer la commande de `Confirmed` à `Delivered`. **La fenêtre de retour
se ferme donc à l'instant précis où l'acheteur voit la marchandise.**

`SubmitReviewCommandHandler` a exactement le même contrôle et il a été corrigé — il
accepte `Confirmed || Delivered`. Les retours ne l'ont pas été. Aujourd'hui le défaut est
masqué par §1.1, qui empêche les commandes d'atteindre `Delivered` ; réparer la livraison
sans réparer ceci supprimerait les retours du jour au lendemain.

Corollaire : il n'existe **aucune fenêtre de temps** pour un retour. Un retour peut être
ouvert sur une commande `Confirmed` de n'importe quel âge.

### 4.6 Deux approbations simultanées envoient deux virements

`WithdrawalConfiguration` n'a pas de `UsePostgresRowVersion()` — contrairement à
`WalletConfiguration`. Et dans `ApproveWithdrawalCommandHandler`, l'appel au PSP a lieu
**avant** tout `SaveChangesAsync`. Deux administrateurs, ou un double-clic, ou une requête
rejouée : les deux passent le contrôle d'état, les deux appellent FedaPay, les deux
écrivent `Processing`. Deux virements Mobile Money pour un seul débit. Le verrou du
portefeuille ne protège pas : le chemin nominal ne touche pas au portefeuille.

### 4.7 `Cancel` n'interdit pas d'annuler une commande livrée [vérifié]

```csharp
if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled or OrderStatus.Failed)
{
    return Result.Failure(Error.Conflict("ordering.not_cancellable", "La commande n'est plus annulable."));
}
```

`Delivered` est absent de la garde. Un acheteur qui appelle `/client/orders/{id}/cancel`
sur sa commande livrée déclenche toute la compensation : libération de réservations déjà
consommées (le stock enfle) et libération d'un crédit de cagnotte déjà consommé (l'argent
est rendu deux fois).

Qu'une commande *confirmée* ne soit pas annulable mais qu'une commande *livrée* le soit se
lit comme un oubli au moment où `Delivered` a été ajouté à l'énumération.

### 4.8 La réconciliation du grand livre existe et ne tourne jamais

`ReconcileWalletsQuery` fait exactement ce qu'il faut : comparaison du solde stocké à la
somme signée du grand livre, par (propriétaire, type, compte), **et** détection des sommes
orphelines sans portefeuille en face. Il répond 409 en cas de dérive, pour qu'une sonde
puisse alerter sans lire le corps.

Il a **un seul appelant** : la route admin. Le seul service de fond de Settlement
réconcilie les retraits, jamais l'invariant portefeuille/grand livre. La dérive est donc
détectée quand un humain ouvre un écran.

---

## 5. Ce qui est solide, et mérite d'être dit

- **Paiement → confirmation.** Le montant facturé est `AmountDue`, pas `GrandTotal` ; le
  cas « cagnotte couvre tout » est refusé plutôt que de créer un paiement à zéro ; le
  webhook est idempotent à trois niveaux (issue, agrégat, statut de commande) ; la
  signature est fermée par défaut. Rien à redire.
- **L'éventail sur confirmation.** Les huit consommateurs de `OrderConfirmedIntegrationEvent`
  portent chacun leur propre garde d'idempotence. C'est ce qu'exige une outbox
  « au moins une fois », et c'est fait partout.
- **L'idempotence des remboursements.** Types de référence distincts par direction, et
  `ExistsForReferenceAsync` interroge le change tracker EF avant la base — donc deux
  handlers dans une même transaction ne peuvent pas se croire premiers tous les deux.
- **L'arithmétique de la contre-passation.** `GrossSplit.Prorate` fait du net un reste :
  débits vendeur + plateforme = crédit acheteur, exactement, sans fuite d'arrondi, et
  immunisé aux changements de taux entre la vente et le retour.
- **Le plafond de retrait.** Borné au solde disponible à la création, plus une politique
  qui interdit le négatif aux clients. Un acheteur ne peut pas retirer ce qu'il n'a pas.
- **Le règlement des retraits.** Une seule méthode idempotente partagée par le webhook et
  la réconciliation ; `Unknown` ne rembourse jamais ; `Completed` seulement sur `Sent`.
- **Les avis.** Achat vérifié côté serveur, un avis par (acheteur, produit, commande),
  propriété masquée en 404.
- **L'anti-IDOR sur tout le parcours client.** Commandes, paiements, retours, litiges,
  adresses, cagnotte : l'identité vient toujours du jeton.

---

## 6. Ordre de réparation proposé

1. **§1.1 et §1.2** — la boucle de livraison. Sans elles, aucun vendeur n'est payé et le
   parcours nominal ne se termine pas. C'est une décision de modèle, pas un correctif :
   à trancher avant d'écrire.
2. **§4.3** — le litige qui promet sans payer. Le plus court des trois trous d'argent, et
   le seul qui envoie un engagement écrit au client.
3. **§4.1 et §4.2** — le plafond commun aux deux circuits, et le plafond par ligne.
4. **§4.5** — accepter `Delivered` pour les retours. **À faire en même temps que §1**,
   sinon réparer la livraison supprime les retours.
5. **§4.7** — ajouter `Delivered` à la garde de `Cancel`. Une ligne.
6. **§2.1** — un code d'erreur distinct pour « e-mail non vérifié ». Une ligne, et elle
   débloque tous les acheteurs qui confirment en retard.
7. **§2.5 et §2.4** — compteur de tentatives par compte, et validateur sur la
   réinitialisation.
8. **§3.3** — `expectedTotal` dans `PayRequest`.
9. **§3.2** — filtrer les réservations expirées dans `Reserved`.
10. **§4.6** — jeton de concurrence sur `Withdrawal`, et sauvegarde avant l'appel PSP.
11. **§4.8** — planifier `ReconcileWalletsQuery`.
12. **§2.2** — ne pas compter l'envoi d'inscription comme un renvoi.
13. **§3.4** — décider si la taxe et la tarification par zone entrent dans le lot 1. Les
    deux modules sont écrits ; c'est un branchement, pas un développement.

---

## Annexe — ce qui n'a pas été vérifié personnellement

Les points suivants viennent du balayage et n'ont pas été relus ligne à ligne : §2.3,
§2.4, §3.1, §3.3, §3.4, §4.4, §4.6, §4.8, et le détail des huit consommateurs de
`OrderConfirmedIntegrationEvent`. Ils sont cohérents avec ce que j'ai vérifié autour, mais
si l'un d'eux commande une décision coûteuse, il vaut mieux l'ouvrir d'abord.
