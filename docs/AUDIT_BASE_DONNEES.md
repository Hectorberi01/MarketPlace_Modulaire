# Audit de la base de données — HbaExpress Marketplace

**Objet** : schéma PostgreSQL de production, audité depuis le code (26 modules, 157 migrations, 92 entités, 813 colonnes, 169 index).
**Méthode** : lecture des instantanés EF (`*ModelSnapshot.cs`), des configurations (`*Configuration.cs`), des dépôts et des gestionnaires. Aucune connexion à la base, aucun fichier modifié.
**Date** : 13 septembre 2026.

---

## Ce qu'il faut savoir avant de lire

Ce schéma est **au-dessus de la moyenne**, et il faut le dire avant la liste des défauts, sans quoi celle-ci donnerait une image fausse.

Trois constats mesurés : les 1 274 horodatages sont **tous** en `timestamp with time zone`, sans exception ; 1 825 colonnes de texte sont bornées contre 28 laissées libres, et ces 28 sont des messages d'erreur techniques ; les montants sont **tous** en `numeric(18,2)`, aucun en flottant.

Surtout, les six verrous optimistes sont posés aux six bons endroits — portefeuille, retrait, commande, paiement, stock, promotion — et pour les bonnes raisons, écrites en commentaire. Le verrou sur `withdrawals` est le plus révélateur : le chemin d'approbation d'un retrait ne touche pas au portefeuille, le verrou du portefeuille ne le protégeait donc pas. Quelqu'un s'est posé la question. Le double virement Mobile Money est évité.

De même, le motif « la dernière part est un reste » est appliqué **partout** où de l'argent se divise — `GrossSplit`, `RefundSplit`, `PriceCalculator`, `CartPricer`. Aucune somme de parts ne peut différer du tout. C'est le premier endroit où une place de marché perd de l'argent, et il est fermé.

Deux de mes propres alertes initiales étaient des faux positifs, levés en allant lire : `wallet_accounts.Balance` sans devise (la devise est portée par le portefeuille parent) et l'absence de verrou sur `seller_earnings` (le crédit du portefeuille a lieu dans la même transaction, et lui est verrouillé).

Les six points GRAVE ci-dessous ont tous été vérifiés ligne à ligne.

---

# GRAVE

## 1. Supprimer son compte vendeur détruit le dossier de conformité KYB

**Le chemin, de bout en bout :**

```
app mobile vendeur
  → SellerAccountEndpoints.cs:143   sender.Send(new DeleteSellerCommand(seller.Id), ct)
  → DeleteSellerCommandHandler.cs:32   _sellerRepository.Remove(seller)
  → SellerRepository.cs:17   _dbContext.Sellers.Remove(seller)
  → KybDocumentConfiguration.cs:44   .OnDelete(DeleteBehavior.Cascade)
  → kyb_documents : RCCM, IFU, pièce d'identité — effacés
```

**Ce qui rend ce point sérieux, c'est que le code interdit par ailleurs exactement cela.** `Seller.cs:232` refuse la suppression unitaire d'une pièce vérifiée :

> « Cette pièce a été validée et fait partie du dossier de conformité : elle ne peut plus être supprimée. »

La règle est énoncée, puis annulée par la cascade deux cents lignes plus loin. Et l'écran de suppression promet le contraire à l'utilisateur (`SellerAccountEndpoints.cs:70`) :

> « Les enregistrements exigés par la loi (factures, versements) restent conservés côté serveur. »

Le KYB n'est pas dans cette liste. Il est pourtant l'archétype de l'enregistrement que la loi impose de conserver **après** la fin de la relation d'affaires (LBC/FT).

**Conséquence.** Lors d'un contrôle, il devient impossible d'établir l'identité d'un vendeur qui a encaissé des versements. Les `payouts` et `seller_earnings`, eux, survivent : ils portent `SellerId` en `Guid` nu, sans clé étrangère (`SettlementConfigurations.cs:21` et `:101`). Il reste donc des mouvements d'argent rattachés à quelqu'un dont on ne peut plus rien prouver.

**Correctif.** Effacement logique du vendeur (`DeletedAtUtc` + anonymisation de `ShopName`, `LogoUrl`, `payout_account`) plutôt que `Remove()`. À défaut, `DeleteBehavior.Restrict` sur `kyb_documents` avec archivage préalable des pièces vérifiées vers une table de rétention hors cascade.

---

## 2. La suppression de compte laisse le téléphone, l'adresse et les coordonnées GPS du domicile

`Anonymize` (`User.cs:649-652`) ne touche que l'agrégat `User` : prénom, nom, e-mail, téléphone, hachage du mot de passe. C'est bien fait — e-mail dérivé de l'identifiant pour préserver l'unicité, `PasswordHash = "DELETED"` plutôt que vide, idempotence. Mais le périmètre s'arrête là.

**Ce qui survit, vérifié table par table :**

| Table | Colonnes | Preuve |
|---|---|---|
| `addresses` | `Recipient`, `Phone`, **`Latitude`, `Longitude`** | `AddressConfiguration.cs:21,22,43,44` |
| `payment_methods` | `AccountRef`, `HolderName` | `PaymentMethodConfiguration.cs:22,25` |
| `orders` | `ShipToRecipient`, `ShipToPhone`, `ShipToLatitude`, `ShipToLongitude` | `OrderConfiguration.cs:97,98,104,105` |
| `device_tokens` | `Token` | `DeviceTokenConfiguration.cs:17` |
| `customer_refunds` | `Msisdn` (numéro Mobile Money) | `WalletConfigurations.cs:138` |
| `sellers` | `payout_account` (jsonb : opérateur, numéro, titulaire) | `SellerConfiguration.cs:48` |

Aucune cascade ne peut les emporter : `AddressConfiguration` ne déclare **aucune** relation vers `User`, seulement `HasIndex(a => a.UserId)` (l. 53). Idem pour `payment_methods`. Ces tables sont orphelines par construction.

Et **aucun événement n'est publié** à l'anonymisation — pas de `UserAnonymizedIntegrationEvent` — donc aucun module ne peut réagir, même en le voulant.

**Le mécanisme existe pourtant déjà dans le dépôt, pour les vendeurs :** `SellerDeletedIntegrationEvent` → `SellerDeletedProductPurgeHandler` (`SellerLifecycleCatalogHandlers.cs:121`) purge le catalogue. Ce qui manque pour les personnes, c'est le même motif.

**Conséquence.** Après une suppression annoncée comme définitive et irréversible — mot de passe exigé —, le numéro de téléphone, le nom du destinataire, les coordonnées GPS du domicile et le numéro Mobile Money restent lisibles en base. La promesse faite à l'utilisateur est fausse, et c'est un manquement direct au droit à l'effacement.

**Correctif.** Publier `UserAnonymizedIntegrationEvent` et le consommer dans Identity (adresses, moyens de paiement, jetons d'appareil), Ordering (les champs `ShipTo*` des commandes hors délai fiscal) et Settlement (`Msisdn`).

---

## 3. Des centimes de franc CFA, dans une devise qui n'en a pas

Le franc CFA n'a pas de sous-unité. `Money` le sait — `Money.cs:39` : `DecimalsByCurrency["XOF"] = 0`, et `Money.Round(montant, devise)` arrondit en conséquence. **Tous** les calculs monétaires du dépôt y passent : Offers, Pricing, Tax, Billing, Settlement, Stripe, PayPal, MTN.

**Sauf deux, et tous deux sur le chemin de l'argent.**

**3a — `CartPricer.cs:173,176`** arrondit à deux décimales en dur :

```csharp
: Math.Min(sellerLeft, decimal.Round(sellerDiscount * line.LineTotal / weightTotal, 2));
```

`cart.Currency` est pourtant disponible dans la portée (utilisé l. 113). La correction a d'ailleurs été faite dans `PriceCalculator` — `PriceCalculator.cs:98` : `Money.Round(discount / 2m, basePrice.Currency)` — avec le commentaire qui explique précisément le problème :

> « Le calcul arrondissait à deux décimales en dur, ce qui fabriquait en XOF des centimes qu'aucune passerelle ne sait encaisser. »

Corrigé à un endroit, pas à l'autre.

**La propagation, établie ligne à ligne :**

```
CartPricer.cs:192        SellerDiscount = line.SellerDiscount + sellerPart   (à centimes)
PlaceOrderCommandHandler.cs:54   transmis tel quel
OrderConfiguration.cs:162        persisté en numeric(18,2), aucune contrainte
AccrueEarnings…Handler.cs:66     gross = (UnitBasePrice × Qté) − SellerDiscount
GrossSplit.cs:55                 net = gross − commission − frais   ← hérite des centimes
SettlementConfigurations.cs:26   NetAmount persisté
WalletConfigurations.cs:84       crédité au portefeuille
FedaPayPayoutGateway.cs:140      amount = (long)Math.Round(instruction.Amount)
```

**3b — `FedaPayPayoutGateway.cs:140`** tronque en silence. Le contraste est net : la passerelle d'**encaissement** refuse explicitement de tronquer (`HttpPaymentGatewayBase.cs:129` : « L'encaissement est REFUSÉ plutôt que tronqué »), la passerelle de **versement** arrondit sans rien dire. Le garde-fou `ToWholeUnits` existe pourtant, dans la classe de base, ligne 127.

**Conséquence.** On ne prend jamais un centime de trop à l'acheteur, mais on peut verser un franc de moins au vendeur — et l'écart reste inscrit au grand livre. Il se recrée à chaque commande multi-lignes portant une remise de panier.

Précision utile : **la somme ne dérive pas**. La dernière ligne reçoit le reste (`CartPricer.cs:172`), l'écart n'est ni créé ni perdu. Il est seulement déplacé vers la dernière ligne du panier — donc vers le vendeur qui s'y trouve, dans un ordre qu'aucun tri ne fixe (`foreach (var item in cart.Items)`, l. 55).

**Correctif.** `Money.Round(x, cart.Currency)` dans `CartPricer` ; `ToWholeUnits(...)` dans `FedaPayPayoutGateway` ; et une contrainte de grille sur `order_lines` tant que la place est mono-devise.

---

## 4. L'index e-mail ne peut pas servir à la connexion

`UserConfiguration.cs:89` déclare l'index unique **partiel** :

```csharp
builder.HasIndex(u => u.Email).IsUnique().HasFilter("\"Status\" <> 'Deleted'");
```

Les trois requêtes qui l'interrogent ne portent aucun prédicat sur `Status` :

- `UserRepository.cs:92` — `FirstOrDefaultAsync(u => u.Email == value, ct)` — **la connexion**
- `UserRepository.cs:110` — `AnyAsync(u => u.Email == value, ct)` — l'inscription
- `UserRepository.cs:122` — `AnyAsync(u => u.PhoneNumber == value, ct)`

PostgreSQL n'utilise un index partiel que s'il peut **prouver** que le prédicat implique le filtre. `"Email" = $1` n'implique rien sur `"Status"`. L'index est écarté à la planification.

**Conséquence.** Parcours séquentiel de `users` à chaque connexion, chaque inscription, chaque vérification d'unicité. C'est le chemin le plus chaud de l'application.

**Correctif, le moins coûteux d'abord** — ajouter `&& u.Status != UserStatus.Deleted` aux trois requêtes : le planificateur accepte alors l'index partiel, et cela ne coûte rien en écriture. Sinon, deux index de lecture :

```sql
CREATE INDEX CONCURRENTLY ix_users_email_lookup ON identity.users ("Email");
CREATE INDEX CONCURRENTLY ix_users_phone_lookup ON identity.users ("PhoneNumber");
```

---

## 5. Le contrôle d'idempotence du grand livre balaye la table à chaque remboursement

`WalletRepositories.cs:255` cherche une écriture existante :

```csharp
.AnyAsync(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId, ct)
```

Le seul index commençant par `ReferenceType` est partiel **sur une constante** — `WalletConfigurations.cs:280` : `HasFilter("\"ReferenceType\" = 'refund'")`.

Or il existe **neuf** types de référence (`WalletMutations.cs:53-115`) : `refund`, `return_refund`, `checkout_reserve`, `checkout_spend`, `checkout_release`, `order_cancelled`, `courier_delivery`, `withdrawal`, `batch_payout`. Huit d'entre eux ne sont couverts par aucun index.

**Conséquence.** Parcours séquentiel de `wallet_transactions` — la table qui grossit le plus vite de toute la base — à chaque remboursement de retour et à chaque ligne de versement de lot.

Et, en défense en profondeur : pour ces huit types, l'idempotence ne repose que sur la vérification applicative, qui est une lecture précédant l'écriture. Le verrou du portefeuille la sauve aujourd'hui, mais c'est un effet de bord, pas une garantie.

**Correctif :**

```sql
CREATE INDEX CONCURRENTLY ix_wallet_transactions_reference
  ON settlement.wallet_transactions ("ReferenceType", "ReferenceId");
```

*Note : j'ai d'abord cru à un double remboursement, parce que le débit vendeur utilise `refund` et le crédit client `return_refund` pour le même `returnRequestId`. Vérification faite, c'est délibéré et correct — `WalletMutations.cs:55-64` explique que partager la référence bloquerait l'un des deux côtés du remboursement.*

---

## 6. Trois balayeurs périodiques sans index, indéfiniment

| Tâche | Cadence | Requête | Index disponible |
|---|---|---|---|
| Commandes abandonnées | 15 min (`AbandonedOrderSweeper.cs:33`) | `OrderRepository.cs:54` — `Status == AwaitingPayment && CreatedAtUtc < seuil`, trié | `orders` : PK + `(BuyerId, Status)` seulement |
| Expiration des remises | 5 min (`DiscountExpiryService.cs:19`) | `OfferRepository.cs:42` — `DiscountEndsOnUtc <= maintenant` | `offers` : `ProductId`, `SellerId`, `(SellerId, VariantSku)` |
| Règlement | à la demande | `SettlementRepositories.cs:30` — `Status == Released && ReleasedAtUtc` entre bornes | `(Status, CreatedAtUtc)` — `ReleasedAtUtc` dans aucun index |

Sur le troisième, l'index existant ne discrimine presque rien : `Released` est l'état d'arrivée de la majorité des gains.

**Correctif** — trois index partiels, qui restent minuscules :

```sql
CREATE INDEX CONCURRENTLY ix_orders_awaiting_payment
  ON ordering.orders ("CreatedAtUtc") WHERE "Status" = 'AwaitingPayment';

CREATE INDEX CONCURRENTLY ix_offers_discount_expiry
  ON offers.offers ("DiscountEndsOnUtc")
  WHERE "DiscountEndsOnUtc" IS NOT NULL AND "OriginalSellerPrice" IS NOT NULL;

CREATE INDEX CONCURRENTLY ix_seller_earnings_released
  ON settlement.seller_earnings ("ReleasedAtUtc") WHERE "Status" = 'Released';
```

---

# MOYEN

## 7. Aucune contrainte de vérification dans toute la base

`HasCheckConstraint` : **zéro occurrence** dans le dépôt. Tous les invariants financiers ne tiennent que par le C#, en mémoire, avant `SaveChanges`. Un script SQL de correction, un import, un job de reprise ou un second service écrivant dans la même base les franchit sans obstacle.

| Invariant | Garanti par | Colonne non protégée |
|---|---|---|
| Solde jamais négatif | `Wallet.cs:139` | `wallet_accounts.Balance` |
| Montant ≥ 0 | `Money.cs:84` | `payments.amount`, `withdrawals.Amount`, `seller_earnings.*` |
| Stock ≥ 0 | `InventoryItem.cs:117` | `inventory_items.OnHand` |
| Taux de commission ∈ [0,1] | `CommissionRule.cs:67` | `commission_rules.Rate` |
| Fin > début | `DateRange.cs:20` | `promotions.start_utc` / `end_utc` |

Les énumérations sont stockées **en texte** borné (61 `HasConversion<string>()`) — c'est le bon choix, il permet d'ajouter un statut sans renuméroter. Mais sans contrainte, `UPDATE withdrawals SET "Status" = 'Approuvé'` passe, et à la relecture l'agrégat devient **illisible** : le retrait disparaît des files admin et l'argent reste bloqué sans diagnostic.

## 8. L'outbox n'est jamais purgée — dans les 26 schémas

`OutboxProcessor.cs` ne contient aucun `DELETE`. Le commentaire de `OutboxConfiguration.cs:41` le dit lui-même : « Les millions de lignes TRAITÉES, qui s'accumulent dans la table sans jamais en sortir ». L'index de file étant partiel, la file ne se dégrade pas — mais le stockage et l'autovacuum, si.

Second effet, plus gênant : les charges utiles `jsonb` figent les données au moment de l'émission. L'anonymisation d'un compte (point 2) ne les rattrape pas. *À confirmer avant de corriger : cela ne compte que si les événements transportent des données personnelles et non de simples identifiants.*

## 9. Trois cascades latentes sur des données que l'on ne doit pas perdre

Aucune n'est atteignable par le code aujourd'hui — il n'existe aucun `Remove()` sur ces agrégats. Elles n'attendent qu'un `DELETE` manuel, c'est-à-dire le geste qu'on fait pour « rejouer un lot raté ».

- `settlement_batches` → `payouts` (`SettlementConfigurations.cs:84`) : efface la preuve qu'un virement est parti, référence prestataire comprise.
- `invoices` → `invoice_lines` (`BillingConfigurations.cs:78`) : efface le détail d'une facture.
- `disputes` → `dispute_messages` (`DisputeConfiguration.cs:49`) : efface les pièces contradictoires d'un litige.

`Restrict` sur les trois. Une facture s'annule, elle ne se supprime pas.

## 10. Des requêtes qui chargent tout puis filtrent en mémoire

- `SettlementRepositories.cs:91` charge **tous** les lots et **tous** les versements de la plateforme, puis `:98` garde ceux d'un vendeur — alors que `ix payouts("SellerId")` existe (`SettlementConfigurations.cs:113`).
- `SettlementCommands.cs:53` charge toute la période, puis `:58` filtre par vendeur en mémoire.
- `InventoryItemRepository.cs:74` charge **tous** les articles avec toutes leurs réservations, puis `:75` garde ceux en stock bas.
- `ConversationRepository.cs:35-43` : quatre `Include` en cascade, **aucun `Take`** — ouvrir la boîte de réception matérialise l'intégralité des messages, pièces jointes et réactions de toutes les conversations.

Ces quatre-là se corrigent en C#, sans toucher au schéma.

## 11. Recherche texte : `ILIKE '%…%'` sans index trigramme

`ProductRepository.cs:66`, `SearchDocumentRepository.cs:29`, `SellerRepository.cs:60`, `UserRepository.cs:44`. Un index btree ne sert **jamais** un joker en tête. Sur `catalog.products`, le parcours précède un `GroupBy` et un `CountAsync` : trois parcours complets par affichage d'écran.

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX CONCURRENTLY ix_products_name_trgm ON catalog.products USING gin ("Name" gin_trgm_ops);
```

Corollaire : l'index `search.documents("Name")` (`SearchDocumentConfiguration.cs:29`) est payé à chaque écriture et n'est jamais lu.

## 12. Index manquants ou mal ordonnés, ailleurs

- `notifications.notification_preferences` : **aucun index hors clé primaire**, alors que `NotificationPreferenceRepository.cs:13` cherche par `UserId` sur le chemin d'envoi de chaque notification.
- `reviews.reviews` : `SellerId` filtré trois fois, indexé zéro fois.
- `search.documents` : `SellerId` non indexé — c'est la page boutique publique.
- `settlement.withdrawals` : `ProviderRef` non indexé, sur un chemin de webhook (`WalletRepositories.cs:86`).
- `analytics.metric_snapshots` : l'index unique est `(Scope, OwnerId, PeriodKey, Granularity)` mais la requête (`MetricSnapshotRepository.cs:22`) traite `Granularity` en égalité et `PeriodKey` en plage. La colonne de plage précède celle d'égalité : l'index s'arrête à `(Scope, OwnerId)`. Réordonner suffit, l'unicité est identique.
- `*.outbox_messages` ×26 : l'index de file est `(NextAttemptAtUtc, OccurredOnUtc)` mais la requête attaque la première colonne par un `OR … IS NULL` — non utilisable — et trie sur la seconde.

## 13. Une convention de nommage à deux régimes

98 % des colonnes sont en **PascalCase** ; 24 propriétés portent un `HasColumnName` en snake_case — ce sont les objets-valeurs `Money` possédés (`amount`, `currency`, `base_price_amount`).

Conséquence pratique : toute requête écrite à la main doit quoter ses identifiants, **sauf** sur ces 24 colonnes où il ne faut pas. Le script `deploy/convert-eur-to-xof.sql` en est l'illustration : il alterne `SET base_price_amount` (l. 28, sans guillemets) et `SET "UnitBaseAmount"` (l. 34, avec). **Je l'ai vérifié colonne par colonne : il est correct.** Mais c'est un piège permanent pour l'exploitation, et le commentaire de `WalletConfigurations.cs:274` montre qu'il a déjà coûté une migration invalide.

---

# MINEUR

- **Trois index redondants** (préfixe exact d'un autre, coût d'écriture pour rien) : `offers("SellerId")`, `inventory_items("Sku")`, `customer_refunds("OrderId")`.
- **`search.documents("IsVisible")`** : booléen quasi toujours vrai, jamais choisi par le planificateur.
- **`CommissionRule.cs:165`** : `FixedFee`, `MinFee` et `MaxFee` échappent à l'arrondi devise — l'arrondi ne porte que sur `grossAmount × Rate`, le forfait est ajouté après.
- **`SellerFinanceEndpoints.cs:89`** : la somme des lignes affichées peut différer du total affiché d'une unité. Affichage seulement.
- **Aucun `HasQueryFilter`** : l'effacement logique (`users.DeletedOnUtc`, `messages.DeletedAtUtc`) n'est pas garanti par l'ORM. `UserStatus.Deleted` n'est testé qu'à deux endroits. Risque de justesse des chiffres, pas de fuite — l'anonymisation ayant déjà remplacé les données.
- **Messagerie** : « supprimer pour tout le monde » conserve le corps du message en base. C'est documenté et la lecture le respecte (`ConversationQueries.cs:39`), mais aucune purge différée n'existe.

---

# Vérifié et sain — ne pas y toucher

1. **Les six verrous optimistes** sont aux bons endroits, avec leurs justifications écrites. `withdrawals` en particulier.
2. **L'idempotence des contre-passations est garantie par la BASE** — index unique partiel `ux_wallet_transactions_refund_reversal`. Le raisonnement de `WalletConfigurations.cs:254` est exact : une vérification applicative ne ferme jamais la fenêtre, seul un index unique le peut.
3. **Le motif « la dernière part est un reste »**, appliqué partout. Aucune somme de parts ne dérive.
4. **Les taux sont figés sur la ligne de commande** (`OrderLine.cs:20`) et relus de là, pas de la configuration courante. Une contre-passation applique le barème de la vente, pas celui d'aujourd'hui.
5. **`WalletPolicy.cs:11`** : un solde ne peut passer sous zéro que s'il représente une dette envers la plateforme. La cagnotte client ne peut pas devenir négative.
6. **Les passerelles d'encaissement refusent de tronquer** plutôt que de produire une commande payée pour une somme jamais perçue.
7. **Unicité des soldes** : `(WalletId, Account)` unique. Deux lignes « disponible » sur un même portefeuille sont impossibles.
8. **Mots de passe et jetons hachés**, jamais en clair. Tentatives bornées à cinq.
9. **Un seul SQL brut** dans tout le dépôt, paramétré (`OutboxProcessor.cs:237`). Aucun `ExecuteUpdate`/`ExecuteDelete` qui contournerait les invariants.
10. **Aucune table d'audit ne duplique de données personnelles** — cherché explicitement. `analytics` ne porte que des identifiants et des compteurs.
11. **Les index filtrés de `notifications` et `delivery`** sont, eux, correctement utilisables : le filtre porte sur la colonne interrogée (`x = $1` implique `x IS NOT NULL`). C'est l'inverse exact du point 4.

---

# Par quoi commencer

**Cette semaine, sans migration.** Le point 4 (ajouter le prédicat `Status` aux trois requêtes) et le point 10 (quatre requêtes qui chargent tout) se corrigent en C#, sans toucher au schéma, et libèrent immédiatement le chemin de connexion et les écrans les plus lourds.

**Ensuite, une migration d'index.** Points 5, 6, 11 et 12, tous en `CREATE INDEX CONCURRENTLY`, donc sans verrou de table.

**Puis les deux points de conformité.** Le 1 (KYB) et le 2 (droit à l'effacement) demandent une décision produit avant du code : que conserve-t-on, combien de temps, et que promet-on à l'écran. Le mécanisme technique existe déjà dans le dépôt pour les vendeurs.

**Le point 3 quand vous voudrez** : deux lignes à changer, mais elles touchent le calcul des prix — à faire avec un test qui compare un panier multi-lignes remisé avant et après.
