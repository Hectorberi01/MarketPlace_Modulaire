# Conditions générales de vente — proposition de rédaction

Pour l'adresse `legal_sales_customer`, version **1.0** dans la console.

## D'où vient ce texte

Il **fusionne les deux rédactions existantes**, qui traitaient toutes deux du même objet
sans se ressembler :

- `terms_customer.json` (version `2026-07-12`) — celle que les acheteurs acceptent
  aujourd'hui. Solide sur le **droit** : Code du numérique, information précontractuelle,
  rétractation, garanties légales, responsabilité de plein droit de la plateforme.
- La page `/legal/conditions` de la vitrine — solide sur le **fonctionnement réel** :
  commande multi-vendeurs, francs CFA sans centimes, frais par commune, adresse
  incomplète refusée, engagement sur le total affiché, point de repère, retour sous sept
  jours, cagnotte, annulation, juridiction.

Chacune disait ce que l'autre taisait. Les garder séparées, c'était garantir qu'elles
divergent — c'est déjà le cas, voir ci-dessous.

---

## TROIS POINTS À TRANCHER AVANT DE PUBLIER

### 1. Une contradiction entre les deux textes, sur le remboursement

Le texte **accepté par vos acheteurs** dit :

> « Tout remboursement vous est versé SANS FRAIS, par le MÊME moyen de paiement que celui
> que vous avez utilisé — sauf accord exprès de votre part pour un autre moyen. »

La page de la vitrine dit :

> « Le remboursement est crédité sur votre **cagnotte**. »

Ce n'est pas une nuance de rédaction : ce sont deux engagements incompatibles, et le
second est celui que le code applique par défaut. `CustomerRefundChannel` a bien deux
valeurs — `Cash` (versement Mobile Money) et `Wallet` (cagnotte) — mais c'est la cagnotte
qui est le chemin normal.

Or le premier texte énonce la règle du Code du numérique qu'il cite lui-même : le
remboursement se fait par le même moyen de paiement, sauf accord **exprès** du
consommateur. Créditer une cagnotte sans cet accord exprès est fragile, et la rédaction
ci-dessous en tient compte — **elle décrit le choix, elle ne l'impose pas**. Si vous
préférez conserver la cagnotte par défaut, il faut recueillir cet accord au moment du
remboursement, pas dans les conditions générales.

Je ne suis pas juriste et ce point mérite un avis. Mais la divergence entre les deux
textes, elle, est un fait vérifiable.

### 2. Les frais de service de 2 % ne figurent dans aucun des deux textes

`Order.PlatformFee` ajoute **2 %** au total de l'acheteur (`appsettings.json`,
`PlatformFeeRate: 0.02`). L'assiette est le total des articles remises déduites, hors
frais de livraison, et le taux est figé à la commande. Le commentaire du domaine est
explicite : « L'acheteur paie les articles, la livraison, PLUS ce frais. »

**Aucune des deux rédactions ne le mentionne.** Un frais facturé à l'acheteur et absent
des conditions générales est le genre d'omission qui se paie cher. Il est écrit dans la
section 4 ci-dessous ; vérifiez que le montant et l'assiette correspondent bien à ce que
vous facturez.

### 3. La TVA

La cartouche **À COMPLÉTER** de l'ancienne page est reprise telle quelle. La question
n'est pas le taux : l'attestation IFU range l'exploitant au régime des micro et petites
entreprises, dans lequel une entreprise n'est en principe pas redevable de la TVA. À
faire confirmer avant de publier — la mention « toutes taxes comprises » en dépend.

---

# Les sections, prêtes à coller

---

## Section 1 — Titre

```
1. Objet et parties
```

## Section 1 — Contenu

```
HBA Express est une place de marché exploitée par **HBA TECH ET TRADE**, entreprise individuelle de droit béninois immatriculée au RCCM de Cotonou sous le numéro RB/ABC/26 A 142835 (voir les [mentions légales](/legal/mentions)).

Le contrat de vente est conclu **entre l'acheteur et le vendeur** de l'article. L'exploitant du site n'est pas le vendeur : il fournit la plateforme, encaisse le paiement pour le compte du vendeur, et organise le règlement après livraison.

Une même commande peut réunir les articles de plusieurs vendeurs. Elle est alors payée en une fois, mais exécutée par chacun d'eux pour ce qui le concerne.

Les présentes conditions sont soumises au Code du numérique en République du Bénin (loi n° 2017-20 du 20 avril 2018, modifiée par la loi n° 2020-35 du 6 janvier 2021).
```

---

## Section 2 — Titre

```
2. Votre compte
```

## Section 2 — Contenu

```
Commander exige un compte : le panier, les adresses et l'historique y sont rattachés. Vous devez être majeur et fournir des informations exactes.

L'adresse électronique doit être confirmée par un code à six chiffres avant la première commande. Un numéro de téléphone joignable est obligatoire — c'est par lui que le livreur vous appellera.

Vous êtes responsable de la confidentialité de votre mot de passe. Signalez sans délai toute utilisation que vous n'auriez pas autorisée.
```

---

## Section 3 — Titre

```
3. Information avant l'achat
```

## Section 3 — Contenu

```
Avant de commander, vous disposez de l'identité du vendeur, des caractéristiques essentielles du produit, de son état (neuf ou occasion), de son prix, des frais de livraison, du délai de préparation annoncé et des moyens de paiement acceptés.

Vous êtes également informé des conséquences d'une inexécution du vendeur, de la marche à suivre pour réclamer, des procédures de règlement des litiges, de l'existence et des conditions du droit de rétractation, et des modalités de retour.

Le récapitulatif de votre commande vous est présenté avant validation, et vous pouvez corriger toute erreur de saisie avant de confirmer.
```

---

## Section 4 — Titre

```
4. Prix et frais
```

## Section 4 — Contenu

```
Les prix sont affichés en **francs CFA (XOF)**. Le franc CFA n'ayant pas de sous-unité, aucun montant n'est exprimé en centimes.

À COMPLÉTER — Mention de TVA à trancher. La question n'est pas le taux applicable : l'attestation IFU range l'exploitant au régime des micro et petites entreprises, dans lequel une entreprise n'est en principe pas redevable de la TVA.

Au total des articles s'ajoutent deux montants, présentés sur des lignes distinctes avant validation :

- les **frais de livraison**, qui dépendent de la commune de destination et ne sont donc connus qu'une fois l'adresse choisie ;
- des **frais de service de 2 %**, calculés sur le total des articles remises déduites, hors frais de livraison. Le taux applicable à une commande est celui en vigueur au moment où elle est passée ; une évolution ultérieure ne la modifie pas.

Le total affiché avant validation est celui qui sera débité. Si le montant a changé entre l'affichage et le paiement, la commande est refusée plutôt que débitée d'un montant que vous n'avez pas vu.
```

---

## Section 5 — Titre

```
5. Commande
```

## Section 5 — Contenu

```
La commande est passée en validant le panier, une adresse de livraison et un moyen de paiement.

Une adresse incomplète — sans commune, sans point de repère ou sans téléphone valide — est refusée avant le paiement. Sans ces informations, la livraison échoue, et il vaut mieux le dire avant de prendre l'argent.

La commande n'est confirmée qu'une fois le paiement abouti et la disponibilité des articles vérifiée. Si un article devient indisponible entre-temps, la commande est annulée et aucun montant n'est retenu.
```

---

## Section 6 — Titre

```
6. Paiement — votre argent est retenu
```

## Section 6 — Contenu

```
Vous payez par **Mobile Money ou par carte bancaire**, par l'intermédiaire de notre prestataire de paiement. Vous êtes redirigé vers sa page sécurisée, où vous choisissez votre opérateur ou votre carte.

Aucun numéro de carte n'est stocké par la plateforme. Le **code de sécurité** de votre carte ne nous est jamais demandé et n'est conservé nulle part. Si vous choisissez d'enregistrer un moyen de paiement dans votre compte, seuls la marque et les quatre derniers chiffres sont conservés.

**Votre argent est retenu par la plateforme** : il n'est reversé au vendeur qu'après confirmation de la livraison. C'est ce mécanisme, et lui seul, qui vous protège — un vendeur qui n'expédie pas ne touche rien.

Si le paiement échoue, la commande reste en attente de règlement et peut être reprise depuis son détail, sans refaire le panier. Une commande jamais réglée est annulée automatiquement, et les articles réservés sont remis en vente.
```

---

## Section 7 — Titre

```
7. Livraison
```

## Section 7 — Contenu

```
La livraison est assurée dans les communes desservies. Le tarif et le délai indicatif dépendent de la zone de destination et vous sont présentés avant le paiement.

Le **point de repère** que vous renseignez est l'information principale utilisée par le livreur, davantage que le nom de la rue. Une adresse imprécise est la première cause d'échec de livraison.

Le vendeur s'engage à expédier dans le délai qu'il a annoncé et à fournir un numéro de suivi. Le risque de perte ou de détérioration vous est transféré à la remise effective du colis.

Le délai de livraison est donné à titre indicatif : il varie selon la zone, la météo, le trafic et la disponibilité du livreur. Si vous n'êtes pas disponible, le livreur peut vous contacter pour convenir d'un nouveau créneau.

Une commande non expédiée dans un délai raisonnable peut être annulée à votre demande, et intégralement remboursée.
```

---

## Section 8 — Titre

```
8. Droit de rétractation
```

## Section 8 — Contenu

```
Conformément au Code du numérique, vous disposez d'un droit de rétractation pour les achats conclus à distance. Le délai, son point de départ et ses exceptions sont ceux fixés par la loi ; ils vous sont rappelés sur la fiche produit.

Pour l'exercer, ouvrez une demande de retour depuis l'application ou depuis le détail de la commande. Le produit doit être renvoyé complet et dans son état d'origine.

Ce droit ne s'applique pas dans les cas exclus par la loi, notamment les biens sur mesure, les biens périssables, et les biens descellés ne pouvant être renvoyés pour des raisons d'hygiène.
```

---

## Section 9 — Titre

```
9. Retours et remboursements
```

## Section 9 — Contenu

```
Une demande de retour peut être ouverte depuis le détail d'une commande livrée, pendant **sept jours** à compter de la livraison. Ce délai part de la date de livraison réelle, et non de la date de commande : la lenteur d'un transport ne réduit pas votre délai.

**Accepter un retour n'est pas le rembourser : ce sont deux décisions distinctes.** Le vendeur accepte d'abord la demande, vous renvoyez l'article, sa réception est constatée, puis le remboursement et son montant sont validés.

Une fois l'article reçu, le vendeur dispose d'un délai pour en examiner l'état et confirmer le remboursement. S'il ne répond pas dans ce délai, la demande est automatiquement validée.

Les articles renvoyés doivent être dans l'état dans lequel vous les avez reçus, avec tous les accessoires et emballages. Les frais de retour sont à votre charge, sauf si le vendeur a accepté de les prendre en charge.

Certains articles ne sont pas éligibles au retour, notamment les denrées périssables, les produits d'hygiène et les articles sur mesure. Vérifiez la description avant de commander.

À COMPLÉTER — Modalité du remboursement, à trancher. Le Code du numérique impose un remboursement sans frais, par le même moyen de paiement que celui utilisé, sauf accord exprès pour un autre moyen. La plateforme crédite aujourd'hui une cagnotte, immédiatement utilisable et dont le versement sur Mobile Money peut être demandé. Soit ce versement devient le choix par défaut, soit l'accord exprès est recueilli au moment du remboursement — dans les deux cas cette section doit dire ce qui se passe réellement.
```

---

## Section 10 — Titre

```
10. Annulation
```

## Section 10 — Contenu

```
Vous pouvez annuler une commande depuis son détail tant qu'elle n'est pas confirmée, c'est-à-dire avant le règlement ou pendant le court intervalle qui suit.

Une fois la commande **confirmée**, elle n'est plus annulable : le vendeur a engagé sa préparation. Vous pouvez lui écrire par la messagerie de la commande et il peut accepter d'y renoncer, mais cet arrangement ne constitue pas un droit d'annulation ouvert après confirmation.

Le droit de rétractation prévu à la section 8 reste entier, et s'exerce après la livraison.
```

---

## Section 11 — Titre

```
11. Vos garanties légales
```

## Section 11 — Contenu

```
Le Code du numérique met trois garanties à la charge de tout vendeur en ligne. Elles vous sont dues de plein droit, et **aucune clause ne peut les écarter** :

- **Conformité** — le produit livré correspond exactement à sa description, ses photos et l'état annoncé.
- **Éviction** — le produit ne porte atteinte à aucun droit de propriété intellectuelle d'un tiers.
- **Vices cachés** — le produit est garanti contre les défauts cachés qui le rendent impropre à son usage, ou qui diminuent tellement cet usage que vous ne l'auriez pas acheté, ou en auriez donné un moindre prix, si vous les aviez connus.
```

---

## Section 12 — Titre

```
12. Notre responsabilité
```

## Section 12 — Contenu

```
La plateforme est responsable envers vous de la bonne exécution des obligations nées du contrat conclu par son intermédiaire, y compris lorsque ces obligations sont exécutées par le vendeur ou par un prestataire — transporteur, prestataire de paiement.

Elle peut s'en exonérer en prouvant que l'inexécution vous est imputable, qu'elle tient au fait imprévisible et insurmontable d'un tiers étranger à la fourniture, ou à un cas de force majeure.
```

---

## Section 13 — Titre

```
13. Avis
```

## Section 13 — Contenu

```
Seul un acheteur ayant effectivement commandé l'article peut déposer un avis.

Les avis mensongers, injurieux, ou publiés en échange d'une contrepartie sont supprimés. Un avis reste visible tant qu'il n'a pas été examiné, y compris lorsqu'il a été signalé.
```

---

## Section 14 — Titre

```
14. Réclamations et litiges
```

## Section 14 — Contenu

```
En cas de désaccord, contactez d'abord le vendeur depuis la messagerie de la commande.

Si le désaccord persiste, ouvrez un litige : le vendeur est notifié et doit répondre. Sans réponse de sa part, la décision se prend sur vos seuls éléments. L'exploitant examine le dossier et tranche sur la base des échanges et des pièces fournies.

Vous pouvez aussi écrire à support@hba-express.com, ou nous joindre par téléphone et par courrier — les coordonnées figurent dans les [mentions légales](/legal/mentions).

Le **droit béninois** est applicable. À défaut de règlement amiable, les juridictions béninoises sont compétentes, et le Tribunal de commerce de Cotonou pour les litiges relevant de sa compétence.

Un litige peut être soumis à un dispositif de médiation lorsque le vendeur en a retenu un ; le nom du médiateur et la procédure pour le saisir vous sont alors communiqués.
```

---

## Section 15 — Titre

```
15. Données personnelles
```

## Section 15 — Contenu

```
Le traitement de vos données est décrit dans la [politique de confidentialité](/legal/confidentialite).
```

---

## Section 16 — Titre

```
16. Modification
```

## Section 16 — Contenu

```
Les présentes conditions peuvent être modifiées. La version applicable à une commande est celle en vigueur au moment où elle est passée.

Toute modification substantielle vous est présentée dans l'application, et votre accord vous est de nouveau demandé.
```
