# 20 — La dérive des migrations, et les clés étrangères qui allaient devenir nullables

> **Date :** 14 juillet 2026. Ce document ne décrit pas un chantier, mais un **accident
> révélateur** : en générant les 25 migrations d'outbox (doc 19), EF a émis des opérations
> que je n'avais pas demandées. Deux dérives anciennes étaient tapies dessous. La seconde
> aurait corrompu la base.

---

## Symptôme

```
Npgsql.PostgresException : column "SellerRepliedAtUtc" of relation "reviews" already exists
```

Ma migration `AddOutboxRetryTracking` sur Reviews tentait d'ajouter une colonne… qui existait
déjà.

---

## Dérive n°1 — trois migrations écrites à la main, sans `.Designer.cs`

| Migration | Ce qu'elle a fait | Ce qu'elle n'a pas fait |
|---|---|---|
| `20250629_AddReviewSellerReply` | ajouté `SellerReply`, `SellerRepliedAtUtc` | mettre à jour le `ModelSnapshot` |
| `20260708_AddDeviceTokens` | créé la table `device_tokens` | idem |
| `20260709_AddDisputeRefundAmount` | ajouté `RefundAmount` | idem |

Le `ModelSnapshot` est **la mémoire d'EF**. Il ne compare jamais le modèle à la base : il le
compare au snapshot. Une migration écrite à la main sans son `.Designer.cs` modifie la base
et **laisse EF dans l'ignorance**.

Ces trois colonnes/tables étaient donc, pour EF, **inexistantes**. À la première migration
générée automatiquement, il les a ré-émises. La bombe était armée depuis des mois ; mon
script n'a fait que la déclencher.

**Correctif.** Les opérations parasites ont été retirées des migrations, et les snapshots —
régénérés au passage — connaissent enfin ces objets. La dérive est refermée.

> **Règle.** Une migration écrite à la main DOIT s'accompagner de son `.Designer.cs`, ou le
> snapshot doit être mis à jour dans le même commit. Sinon la prochaine migration générée
> sera fausse, et personne ne le saura avant de l'appliquer.

---

## Dérive n°2 — 8 clés étrangères allaient devenir NULLABLES

Mes migrations contenaient aussi ceci, que j'aurais pu prendre pour une correction :

```csharp
migrationBuilder.AlterColumn<Guid>(
    name: "ProductId",
    table: "product_variants",
    nullable: true,              // ← !!!
    oldClrType: typeof(Guid));
```

**Ce n'était pas une correction. C'était EF alignant la base sur un modèle fautif.**

Huit relations parent-enfant ne déclaraient pas `.IsRequired()` : Billing (`Invoice → Lines`),
Catalog (`Product → Variants` et `→ Media`), Disputes (`Dispute → Messages`), Loyalty
(`Account → Transactions`), Sellers (`Seller → KybDocuments`), Shipping (`Shipment → Items`),
Wishlist (`Wishlist → Items`). Le modèle les disait facultatives ; la base les avait en
`NOT NULL`. EF s'apprêtait à faire céder la base.

### ⚠️ Pourquoi c'est grave — et pourquoi ma première explication était FAUSSE

**Ce que j'ai écrit ici initialement, et qui est faux :** que sans `IsRequired()`, EF
« sèvrerait » les enfants (`UPDATE enfant SET FK = NULL`) et que rendre la colonne nullable
produirait des orphelins silencieux à chaque suppression de parent.

**C'est inexact.** Ces relations portent toutes `OnDelete(DeleteBehavior.Cascade)`, et en EF
Core ce réglage gouverne AUSSI le sort des orphelins : avec `Cascade`, un enfant retiré de sa
collection est **supprimé**, pas mis à `NULL`. La sévérance est le comportement de
`ClientSetNull` — le *défaut* d'une relation optionnelle, mais explicitement remplacé ici.
Les données de production l'ont confirmé (voir [doc 22](22-severance-et-orphelins.md)).

**Ce qui reste vrai, et suffit largement.** Rendre ces colonnes nullables aurait détruit la
**seule garantie qui ne dépende d'aucun réglage applicatif**. Le `NOT NULL` en base refuse un
orphelin quelle qu'en soit la provenance : un bug futur, un `OnDelete` retiré par
distraction, un script de maintenance, un import. Le laisser tomber pour faire taire un diff
de migration, c'est troquer une contrainte contre une convention.

**Correctif.** `.IsRequired()` posé sur les 8 relations, snapshots remis d'aplomb (FK ombre
`Guid?` → `Guid`, relations marquées requises). Les `AlterColumn` ont disparu d'eux-mêmes :
le modèle décrit à nouveau la base.

Les 9 relations restantes (Identity ×2, Inventory, Messaging ×4, Ordering, Settlement) ont
été traitées séparément — leur colonne était **déjà** nullable en base, donc aucun
`AlterColumn` ne les signalait. Voir [doc 22](22-severance-et-orphelins.md).

---

## La leçon

**Une migration générée n'est pas une vérité : c'est un diff entre deux croyances.** EF
compare le modèle au snapshot — deux artefacts de code. Ni l'un ni l'autre ne regarde la
base réelle.

Quand une migration propose une opération que vous n'avez pas demandée, ce n'est jamais du
bruit. C'est un endroit où le code et la base ont cessé de dire la même chose. **Lisez-la
avant de l'appliquer.** Ici, l'appliquer sans lire aurait rendu huit clés étrangères
nullables, et personne ne s'en serait aperçu.

> **Et une seconde leçon, à mes dépens.** Ma première explication de cette dérive était
> fausse : j'avais invoqué un mécanisme de sévérance qui n'opère pas ici, parce qu'une ligne
> `OnDelete(Cascade)` que j'avais lue sans la voir le neutralise. La conclusion tenait, le
> raisonnement non. **Une déduction élégante ne remplace pas un `SELECT count(*)`** — voir
> [doc 22](22-severance-et-orphelins.md).
