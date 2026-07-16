# 13 — Diagrammes de séquence des parcours principaux

> Ces diagrammes décrivent le code **tel qu'il est**, pas tel qu'on aimerait qu'il soit.
> Quand une étape est manuelle, un compromis ou une limite connue, c'est **dit**. Une
> documentation qui embellit est pire qu'une absence de documentation : elle fait perdre
> du temps à celui qui la croit.
>
> Prérequis : [12-anatomie-d-une-requete.md](12-anatomie-d-une-requete.md) explique
> MediatR, le pattern `Result` et l'outbox. Ici, on suppose ces notions acquises.

## Sommaire

1. [Inscription et connexion](#1-inscription-et-connexion)
2. [Consulter une fiche produit](#2-consulter-une-fiche-produit-le-chemin-le-plus-chaud)
3. [Du panier à la commande payée — **la saga**](#3-du-panier-à-la-commande-payée--la-saga)
4. [Le vendeur publie un produit](#4-le-vendeur-publie-un-produit)
5. [Retour et remboursement](#5-retour-et-remboursement)
6. [Retrait des gains du vendeur](#6-retrait-des-gains-du-vendeur)

---

## 1. Inscription et connexion

```mermaid
sequenceDiagram
    autonumber
    actor U as 👤 Utilisateur
    participant A as 📱 App
    participant BFF as 🌐 BFF Mobile
    participant ID as 🔐 Module Identity
    participant DB as 🐘 PostgreSQL

    rect rgba(200,230,255,0.25)
    Note over U,DB: INSCRIPTION
    U->>A: remplit le formulaire
    A->>BFF: POST /mobile/auth/register
    BFF->>ID: RegisterUserCommand
    ID->>ID: BCrypt.HashPassword(motDePasse)
    Note over ID: Le mot de passe n'est JAMAIS stocké en clair,<br/>ni chiffré (réversible) : il est HACHÉ.<br/>Même nous ne pouvons pas le relire.
    ID->>ID: Statut = PendingVerification
    Note over ID: ⚠️ Le compte existe mais NE PEUT PAS<br/>se connecter : un admin doit l'approuver.<br/>(Politique configurable par Identity:Registration)
    ID->>DB: INSERT identity.users
    BFF-->>A: 201 Created
    end

    rect rgba(255,235,200,0.3)
    Note over U,DB: CONNEXION
    U->>A: e-mail + mot de passe
    A->>BFF: POST /mobile/auth/login
    BFF->>ID: LoginCommand(email, mdp, RequiredRoles)
    ID->>DB: SELECT … WHERE email = $1
    ID->>ID: BCrypt.Verify(saisi, hachéEnBase)

    alt Mot de passe faux
        ID-->>BFF: Result.Failure(Unauthorized)
        Note over ID,BFF: Le message ne dit PAS si c'est l'e-mail ou<br/>le mot de passe qui est faux — sinon on offre<br/>un moyen d'énumérer les comptes existants.
    else Compte non approuvé
        ID-->>BFF: identity.auth.pending_approval
    else Mauvaise application
        ID-->>BFF: identity.auth.wrong_surface
        Note over ID: Un VENDEUR ne peut pas se connecter à<br/>l'app cliente avec ses identifiants vendeur.<br/>Chaque BFF exige ses propres rôles.
    else Tout est bon
        ID->>ID: génère un JWT (signé HS256)
        ID-->>BFF: AccessToken + RefreshToken
        BFF-->>A: 200 OK
        A->>A: stocke les jetons (secure storage)
        A->>BFF: POST /mobile/devices (jeton FCM)
        Note over A,BFF: L'appareil s'enregistre pour le push<br/>APRÈS la connexion — jamais avant :<br/>l'appel exige une session.
    end
    end
```

**Ce qu'il faut retenir.** Le JWT est signé avec une clé **partagée par les 5 hôtes** : un
jeton émis par le BFF Mobile est valide partout. C'est pratique — et c'est aussi pourquoi
chaque BFF **vérifie les rôles** en plus de la signature. Sans cela, un vendeur pourrait
utiliser son jeton sur l'API d'administration.

---

## 2. Consulter une fiche produit (le chemin le plus chaud)

C'est l'appel le plus fréquent de toute l'application. Il agrège **quatre modules** en une
seule réponse HTTP — c'est précisément le rôle d'un BFF.

```mermaid
sequenceDiagram
    autonumber
    participant A as 📱 App
    participant BFF as 🌐 BFF Mobile
    participant R as ⚡ Redis
    participant CAT as 📦 Catalog
    participant OFF as 💰 Offers
    participant SEL as 🏪 Sellers
    participant REV as ⭐ Reviews
    participant DB as 🐘 PostgreSQL

    A->>BFF: GET /mobile/products/{id}

    BFF->>CAT: catalog.GetProductAsync(id)
    CAT->>R: catalog:product:{id} ?
    alt cache chaud
        R-->>CAT: ProductSummary (≈1 ms)
    else cache froid
        CAT->>DB: SELECT catalog.products + variants + media
        CAT->>R: mémorise (TTL 5 min)
    end

    BFF->>OFF: offers.GetOffersByProductAsync(id)
    OFF->>R: offers:by-product:{id} ?
    Note over OFF,R: TTL 60 s SEULEMENT — ce module porte les PRIX.<br/>Un prix périmé n'est pas une lenteur,<br/>c'est une promesse qu'on ne tient pas.

    loop pour chaque vendeur du produit
        BFF->>SEL: sellers.GetSellerAsync(sellerId)
        SEL->>R: sellers:seller:{id} ?
        Note over BFF,SEL: ⚠️ Boucle N+1 — mais mise en cache (10 min).<br/>Sans le cache, c'était UNE REQUÊTE SQL PAR VENDEUR,<br/>et chacune chargeait ses documents KYB<br/>pour n'en garder que le nom de boutique.
    end

    BFF->>REV: GetProductRatingQuery(id)
    REV->>R: reviews:rating:{id} ?
    Note over REV: C'est une AGRÉGATION (moyenne sur tous les avis).<br/>Son coût grandit avec le succès du produit :<br/>les fiches les plus vues sont les plus chères à calculer.

    BFF-->>A: 200 OK — produit + offres + vendeurs + note
    Note over BFF,A: Un seul aller-retour réseau.<br/>Cache chaud : ZÉRO requête SQL.
```

> **Le sens du BFF.** Sans lui, l'application mobile ferait 4 à 6 appels HTTP et devrait
> savoir recoller les morceaux. Sur une connexion mobile béninoise, chaque aller-retour se
> paie. Le BFF fait le travail **une fois**, au plus près des données.

---

## 3. Du panier à la commande payée — **la saga**

C'est le parcours central, et le plus complexe. Il traverse **sept modules**, sans qu'aucun
ne connaisse les autres. Le lien entre eux, ce sont les **événements**.

```mermaid
sequenceDiagram
    autonumber
    actor U as 👤 Acheteur
    participant A as 📱 App
    participant BFF as 🌐 BFF Mobile
    participant CART as 🛒 Cart
    participant ORD as 📋 Ordering
    participant INV as 📊 Inventory
    participant PAY as 💳 Payments
    participant FP as 🏦 FedaPay
    participant OB as 📦 Outbox
    participant SET as 💰 Settlement
    participant NOT as 🔔 Notifications

    U->>A: « Payer »
    A->>BFF: POST /mobile/checkout/pay

    BFF->>CART: GetActiveCartQuery
    BFF->>ORD: PlaceOrderCommand(lignes)

    activate ORD
    ORD->>ORD: Order.Place() → statut Pending
    ORD->>INV: réserve le stock
    Note over INV: Le stock est RÉSERVÉ, pas décrémenté.<br/>Si le paiement échoue, il est relâché.
    ORD->>OB: OrderPlacedIntegrationEvent
    Note over ORD,OB: Commande + événement : MÊME TRANSACTION.
    deactivate ORD

    BFF->>PAY: InitiatePaymentCommand(orderId, montant)
    PAY->>FP: POST /v1/transactions (crée la page de paiement)
    FP-->>PAY: URL de redirection
    PAY-->>BFF: RedirectUrl
    BFF-->>A: { redirectUrl }

    A->>FP: ouvre la page FedaPay (WebView)
    U->>FP: saisit son Mobile Money
    Note over U,FP: 🔑 L'APPLICATION NE VOIT JAMAIS<br/>les identifiants de paiement.<br/>C'est FedaPay qui les collecte — et c'est<br/>ce qui nous dispense de la conformité PCI.

    FP->>PAY: POST /api/payments/webhooks/fedapay
    Note over FP,PAY: Endpoint ANONYME — mais la SIGNATURE est vérifiée.<br/>Sans signature valide : rejet. Sinon, n'importe qui<br/>pourrait déclarer une commande payée.

    activate PAY
    PAY->>PAY: Payment.Capture()
    PAY->>OB: PaymentCapturedIntegrationEvent
    deactivate PAY

    Note over OB: ⏱️ L'OutboxProcessor relit la table<br/>toutes les 5 s, par lots de 50.<br/>Propagation GARANTIE, mais pas instantanée.

    OB->>ORD: PaymentCaptured
    activate ORD
    ORD->>ORD: Order.Pay() puis Order.Confirm()
    ORD->>INV: solde le stock réservé
    ORD->>OB: OrderConfirmedIntegrationEvent<br/>(+ répartition PAR VENDEUR)
    deactivate ORD

    par Éventail des conséquences
        OB->>SET: crédite le « solde à venir » de chaque vendeur
        Note over SET: Net vendeur = brut − commission − frais.<br/>Pas encore retirable : libéré à la LIVRAISON.
    and
        OB->>NOT: notifie l'ACHETEUR (« commande confirmée »)
    and
        OB->>NOT: notifie CHAQUE VENDEUR (« nouvelle commande »)
        Note over NOT: Chacun ne reçoit QUE SA PART.<br/>Lui envoyer le total de la commande lui ferait<br/>croire qu'il a vendu le chiffre d'un concurrent.
    end
```

### Et si le paiement échoue ?

```mermaid
sequenceDiagram
    autonumber
    participant FP as 🏦 FedaPay
    participant PAY as 💳 Payments
    participant OB as 📦 Outbox
    participant ORD as 📋 Ordering
    participant INV as 📊 Inventory
    participant NOT as 🔔 Notifications

    FP->>PAY: webhook « échec »
    PAY->>OB: PaymentFailedIntegrationEvent
    OB->>ORD: PaymentFailed
    ORD->>ORD: CancelOrderCommand
    ORD->>INV: LIBÈRE le stock réservé
    Note over INV: Le stock revient en vente.<br/>Sans cela, un panier abandonné<br/>bloquerait la marchandise pour toujours.
    ORD->>OB: OrderCancelledIntegrationEvent
    OB->>NOT: notifie l'acheteur (« commande annulée »)

    rect rgba(255,220,220,0.35)
    Note over NOT: ⚠️ TROU CONNU — l'acheteur est prévenu de<br/>l'ANNULATION, mais pas de l'ÉCHEC DE PAIEMENT<br/>lui-même (PaymentFailed n'a aucun handler de<br/>notification). Le message reçu est donc<br/>« commande annulée », sans en dire la raison.
    end
```

---

## 4. Le vendeur publie un produit

```mermaid
sequenceDiagram
    autonumber
    actor V as 🏪 Vendeur
    participant A as 📱 App vendeur
    participant BFF as 🌐 BFF Seller
    participant CAT as 📦 Catalog
    participant CLD as 🎨 Cloudinary
    participant R2 as ☁️ Cloudflare R2
    participant DB as 🐘 PostgreSQL
    participant RD as ⚡ Redis

    V->>A: photo + description + prix
    A->>BFF: POST /seller/products
    BFF->>BFF: vérifie le rôle « Seller » (JWT)

    BFF->>CAT: CreateProductCommand

    activate CAT
    CAT->>CLD: détourage IA + fond blanc
    Note over CLD: Optionnel : si Cloudinary n'est pas configuré,<br/>l'image passe telle quelle (NullImageProcessor).<br/>La fonctionnalité DÉGRADE, elle ne casse pas.
    CLD-->>CAT: image traitée
    CAT->>R2: stocke l'image définitive
    R2-->>CAT: URL publique

    CAT->>CAT: Product.Create() → génère le SLUG
    Note over CAT: Le slug retire les accents SANS String.Normalize<br/>(inopérant : InvariantGlobalization=true).<br/>Une table de correspondance explicite s'en charge.

    CAT->>DB: INSERT catalog.products (+ outbox)
    CAT->>RD: ÉVINCE catalog:products:seller:{id}
    Note over CAT,RD: L'invalidation part du SaveChanges du DbContext.<br/>Le vendeur relit donc SES PROPRES écritures :<br/>son produit apparaît immédiatement dans sa liste.
    deactivate CAT

    BFF-->>A: 201 Created
```

---

## 5. Retour et remboursement

> **Lisez la note en rouge.** C'est le seul parcours de l'application où un **geste humain
> est indispensable**, et ce n'est pas un choix de conception : FedaPay **n'expose aucune
> API de remboursement**.

```mermaid
sequenceDiagram
    autonumber
    actor B as 👤 Acheteur
    actor V as 🏪 Vendeur
    actor AD as 🛡️ Admin
    participant RET as ↩️ Returns
    participant SET as 💰 Settlement
    participant NOT as 🔔 Notifications
    participant FP as 🏦 FedaPay (tableau de bord)

    B->>RET: RequestReturnCommand
    RET->>RET: statut = Requested

    V->>RET: ApproveReturnCommand → Approved
    B->>RET: renvoie le colis (suivi)
    V->>RET: MarkReceivedCommand → Received

    rect rgba(255,240,200,0.5)
    Note over V,RET: ÉTAPE 1 — LE VENDEUR VALIDE
    V->>RET: ApproveReturnRefundCommand(montant)
    RET->>RET: statut = RefundPending
    Note over RET: ⚠️ « VALIDÉ », PAS « REMBOURSÉ ».<br/>Aucun franc n'a bougé. La distinction est TOUT :<br/>avant, ce bouton écrivait « Remboursé » alors<br/>que le client ne recevait jamais rien.
    RET->>NOT: notifie l'acheteur (« accepté, versement en cours »)
    Note over NOT: Ce message évite le litige pendant l'attente.<br/>Le silence coûte plus cher qu'un remboursement.
    end

    rect rgba(255,215,215,0.55)
    Note over AD,FP: ÉTAPE 2 — L'HUMAIN VERSE (INCONTOURNABLE)
    AD->>RET: GET /admin/refunds/pending
    Note over AD,RET: La file de travail. Elle doit être VIDE le soir :<br/>chaque ligne est un litige en préparation.
    AD->>FP: effectue le versement à la main
    Note over FP: ❗ FedaPay n'a AUCUN endpoint de remboursement.<br/>Leur doc est formelle : tableau de bord uniquement,<br/>et SEULEMENT en MTN Mobile Money.<br/>Aucun code ne changera cela.
    FP-->>AD: référence de l'opération
    AD->>RET: POST /admin/refunds/{id}/confirm { référence }
    end

    activate RET
    RET->>RET: statut = Refunded + référence
    Note over RET: SEULE porte vers « Remboursé », et elle EXIGE une preuve.<br/>Le jour où un client jure n'avoir rien reçu,<br/>c'est cette référence qui tranche.
    RET->>SET: ReturnRefundedIntegrationEvent
    deactivate RET

    SET->>SET: CONTRE-PASSATION
    Note over SET: Reprend le net du vendeur (solde négatif autorisé)<br/>et restitue la commission de la plateforme.<br/>Sans cela, un retour coûtait DEUX FOIS :<br/>on remboursait le client ET on payait le vendeur.

    par
        SET->>NOT: notifie l'acheteur (« remboursement effectué »)
    and
        SET->>NOT: notifie le vendeur (« déduit de votre solde »)
    end
```

> **Une limite à connaître avant qu'un client ne la découvre :** FedaPay ne rembourse
> **qu'en MTN Mobile Money**. Un acheteur ayant payé par Moov n'est pas remboursable par ce
> canal — il faudra un autre moyen, et une décision.

---

## 6. Retrait des gains du vendeur

```mermaid
sequenceDiagram
    autonumber
    actor V as 🏪 Vendeur
    actor AD as 🛡️ Admin
    participant SET as 💰 Settlement
    participant FP as 🏦 FedaPay (Payouts)

    Note over SET: RAPPEL — deux soldes :<br/>• « à venir » : crédité à la CONFIRMATION<br/>• « principal » : libéré à la LIVRAISON, retirable

    V->>SET: RequestWithdrawalCommand(montant)
    SET->>SET: Wallet.Withdraw() — refuse si solde insuffisant
    SET->>SET: statut = Pending

    AD->>SET: GET /admin/wallet/withdrawals/pending
    AD->>SET: approuve
    SET->>FP: API Payouts (virement sortant)
    Note over SET,FP: Ici, l'API existe VRAIMENT — contrairement<br/>au remboursement. Les payouts sont automatisables.

    alt Virement réussi
        FP-->>SET: PayoutPaid
        Note over SET: ⚠️ Trou connu : le vendeur n'est PAS notifié<br/>qu'il a été payé (aucun handler sur PayoutPaid).<br/>Il découvre son virement en consultant son solde.
    else Virement échoué
        FP-->>SET: échec
        SET->>SET: RECRÉDITE le solde du vendeur
        Note over SET: Sans ce recrédit, l'argent disparaîtrait :<br/>débité du portefeuille, jamais arrivé en banque.
    end
```

---

## Trous connus, rassemblés ici

Un développeur qui découvre le projet doit les connaître **avant** de les redécouvrir en
production :

| Événement | Ce qui manque | Conséquence concrète |
|---|---|---|
| `PaymentFailed` | Pas de **notification** (la commande est bien annulée) | Le client voit « commande annulée » sans en connaître la raison |
| `MessageSent` | Pas de **push** — la messagerie ne fait que du SignalR | Un message reçu app fermée n'alerte personne : la vente est perdue |
| `PayoutPaid` | Aucun handler | Le vendeur ne sait pas qu'il a été payé |
| `SellerActivated` | Aucun handler | Le vendeur ignore que sa boutique est validée ; il relance le support |
| `StockDepleted` | Aucun handler | Le vendeur ne sait pas qu'il est en rupture, et perd des ventes |
| `DisputeOpened` / `Resolved` | Aucun handler | Les litiges sont muets des deux côtés |

L'infrastructure de notification est **complète** : il ne manque que les handlers.
