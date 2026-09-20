# Politique de confidentialité vendeurs — proposition de rédaction

Pour l'adresse `legal_privacy_seller`, version **1.0** dans la console.

## D'où vient ce texte

Il reprend la rédaction déjà embarquée dans l'application vendeur
(`privacy_seller.json`, version `2026-07-12`), **corrigée et complétée d'après le code**.
Ce n'est pas un modèle générique : chaque élément énuméré correspond à un champ, une
route ou une table qui existe.

Trois raisons de ne pas publier `privacy_seller.json` tel quel :

1. **Sa section 5 est corrompue.** L'extraction depuis le Dart y a laissé tomber un bloc
   de commentaire du code source — « PRESTATAIRE OMIS, ET CE N'ÉTAIT PAS UN DÉTAIL… » —
   au milieu du texte contractuel, suivi de **74 paragraphes vides**. Publié tel quel,
   un vendeur lirait un commentaire de développeur dans sa politique de confidentialité.
2. **Les coordonnées de reversement y sont sous-décrites** et les pièces de vérification
   ne sont pas nommées.
3. **Il manque la section la plus importante pour une place de marché** : les données
   d'acheteurs que le vendeur reçoit, et ce qu'il a le droit d'en faire.

## Trois décisions qui vous reviennent

Elles sont marquées **À COMPLÉTER** dans le texte. Ne publiez pas sans les avoir tranchées :
une politique qui annonce une durée de conservation inventée est pire qu'une politique
muette, parce qu'elle vous engage.

- la **durée de conservation des pièces de vérification** après clôture d'un compte ;
- l'**accès des administrateurs** à ces pièces : le dire ou non, et dans quels termes ;
- le **prestataire de polices de caractères** : la rédaction actuelle le déclare, mais
  `google_fonts` est présent dans le `pubspec` sans aucune section `fonts:` en regard.
  Si aucune police n'est réellement téléchargée, ce paragraphe déclare un transfert qui
  n'a pas lieu et doit être retiré. À vérifier avant publication.

---

# Les sections, prêtes à coller

Chaque bloc ci-dessous est **une section** : le titre va dans le champ « Titre de la
section », le corps dans « Contenu ».

---

## Section 1 — Titre

```
1. Qui est responsable de vos données
```

## Section 1 — Contenu

```
HBA TECH ET TRADE est responsable des traitements de données réalisés au moyen de la Plateforme HBA Express. Ces traitements relèvent du Livre V du Code du numérique et du contrôle de l'Autorité de protection des données à caractère personnel (APDP) du Bénin.

Pour toute question relative à vos données : support@hba-express.com.
```

---

## Section 2 — Titre

```
2. Ce que nous collectons
```

## Section 2 — Contenu

```
### Votre compte

- nom et prénom ;
- adresse électronique ;
- numéro de téléphone ;
- mot de passe, conservé sous forme d'empreinte et **jamais en clair** ;
- secret d'authentification à deux facteurs, si vous activez cette protection ;
- photo de profil, facultative.

Si vous activez le déverrouillage biométrique, une copie de vos identifiants est conservée **sur votre appareil**, dans son coffre-fort sécurisé, et n'en sort qu'après une authentification réussie. Elle ne nous est jamais transmise.

### Votre boutique

- nom de la boutique ;
- logo et description, facultatifs ;
- votre catalogue : produits, photos, prix, stocks.

### Vos informations d'entreprise

Déclaratives et facultatives à l'inscription, elles constituent votre dossier :

- raison sociale ;
- numéro RCCM ;
- identifiant fiscal unique (IFU) ;
- adresse et commune de la boutique ;
- secteur d'activité ;
- nom du gérant ou représentant légal ;
- numéro de téléphone de la boutique.

### Vos pièces de vérification

- une pièce d'identité ;
- un extrait du registre du commerce ;
- un justificatif d'identifiant fiscal ;
- un justificatif d'adresse.

### Vos coordonnées de reversement

- le canal choisi : MTN MoMo, Moov Money, Wave, Celtis ou compte bancaire ;
- le numéro du compte de réception ;
- le nom du titulaire du compte.

### Votre activité

- commandes reçues, expéditions, retours et litiges ;
- vos échanges avec les acheteurs et avec l'assistance ;
- l'historique de vos versements ;
- si vous les avez acceptées, un identifiant d'appareil pour les notifications ;
- des données techniques d'utilisation, de sécurité et de prévention de la fraude.

Certaines de ces informations sont obligatoires pour activer les fonctionnalités de vente ou pour procéder aux reversements.
```

---

## Section 3 — Titre

```
3. Pourquoi nous les collectons
```

## Section 3 — Contenu

```
- **Exécution du contrat** : afficher vos produits, traiter les commandes, vous verser vos gains, vous permettre de répondre aux acheteurs, arbitrer les litiges.
- **Obligation légale** : vérifier l'existence de votre entreprise, conserver les pièces comptables et fiscales.
- **Intérêt légitime** : prévenir la fraude et sécuriser la Plateforme.
- **Consentement** : notifications, et toute prospection commerciale. Retirable à tout moment.

Nous ne vendons pas vos données et ne faisons pas de publicité ciblée.
```

---

## Section 4 — Titre

```
4. Ce que les acheteurs voient de vous
```

## Section 4 — Contenu

```
Sont publics : le nom de votre boutique, votre logo, votre description, vos produits, vos prix, votre note et vos réponses publiques aux avis.

Ne sont **jamais** exposés aux acheteurs : votre adresse électronique, votre numéro de téléphone personnel, vos pièces de vérification, vos coordonnées de reversement et votre chiffre d'affaires.
```

---

## Section 5 — Titre

```
5. Les données d'acheteurs que vous recevez
```

## Section 5 — Contenu

```
Pour exécuter une commande, la Plateforme vous transmet les informations nécessaires à la livraison :

- le nom du destinataire ;
- son numéro de téléphone ;
- sa commune, son quartier, son adresse et le point de repère indiqué ;
- lorsque l'acheteur l'a fournie, la position géographique de l'adresse, destinée au lien de navigation que vous transmettez au coursier.

S'y ajoutent le contenu de vos échanges avec l'acheteur et l'historique des commandes qui vous concernent.

**Ces données ne vous appartiennent pas.** Vous ne pouvez les utiliser que pour exécuter la commande, répondre à une réclamation ou satisfaire une obligation légale. Il vous est notamment interdit de les conserver au-delà de ce qui est nécessaire, de les transmettre à un tiers, ou de vous en servir à des fins de prospection.

Vous répondez de la sécurité des données d'acheteurs que vous détenez, y compris lorsque vous les exportez ou les recopiez hors de la Plateforme.
```

---

## Section 6 — Titre

```
6. Nos prestataires
```

## Section 6 — Contenu

```
- **Paiements et reversements Mobile Money** : notre prestataire de paiement. Nous ne stockons aucun numéro de carte.
- **Photos de produits** : notre prestataire de traitement d'images, qui les détoure sur fond blanc puis les héberge. N'y faites figurer aucune donnée personnelle.
- **Notifications** : notre prestataire de messagerie push, qui reçoit l'identifiant de votre appareil.
- **Hébergement** : nos serveurs et le stockage de vos pièces justificatives.

Certains de ces prestataires traitent des données hors du Bénin. Ces transferts sont encadrés par le Livre V du Code du numérique.
```

> **À COMPLÉTER** — le paragraphe sur les polices de caractères a été retiré de cette
> proposition : à rétablir si, et seulement si, l'application télécharge réellement ses
> polices depuis un service tiers. Voir la note en tête de ce document.

---

## Section 7 — Titre

```
7. Sécurité
```

## Section 7 — Contenu

```
Vos identifiants de session sont conservés dans le coffre-fort sécurisé de votre téléphone (Keychain sur iOS, Keystore sur Android), jamais en clair. Les échanges avec nos serveurs sont chiffrés.

Votre session expire automatiquement et peut être révoquée à tout moment par déconnexion.

En cas de violation de données susceptible de créer un risque pour vos droits, nous en informons l'APDP et, lorsque la loi l'exige, vous-même.
```

---

## Section 8 — Titre

```
8. Combien de temps nous les conservons
```

## Section 8 — Contenu

```
- **Compte et boutique** : tant que votre boutique est active.
- **Commandes, facturation et reversements** : au-delà de la clôture, pour la durée imposée par les obligations comptables et fiscales. Ces données ne peuvent pas être effacées sur simple demande — c'est la loi qui l'impose, pas nous.
- **Pièces de vérification** : À COMPLÉTER — durée de conservation après clôture du compte.
- **Échanges avec les acheteurs** : conservés avec le dossier de la commande à laquelle ils se rattachent.

À la suppression de votre compte, vos données d'identification sont anonymisées ; les écritures comptables, elles, sont conservées.
```

---

## Section 9 — Titre

```
9. Vos droits
```

## Section 9 — Contenu

```
Vous disposez d'un droit d'accès, de rectification, d'opposition, de suppression et de portabilité de vos données, dans les limites de conservation énoncées ci-dessus. La plupart des informations se corrigent directement depuis votre espace vendeur.

Écrivez à support@hba-express.com depuis l'adresse électronique de votre compte ; nous répondons sous trente jours.

Si vous estimez vos droits méconnus, vous pouvez saisir l'APDP.
```

> **À COMPLÉTER** — dire ici, ou en section 7, qui accède à vos pièces de vérification
> au sein de la plateforme. C'est une question que les vendeurs posent, et y répondre
> par avance vaut mieux que d'avoir à s'en expliquer après coup.

---

## Section 10 — Titre

```
10. Modifications
```

## Section 10 — Contenu

```
Cette politique peut être modifiée. Chaque version porte un numéro, et la version applicable est celle en vigueur au moment où vous en prenez connaissance.

Lorsqu'une modification touche vos droits ou la nature des données collectées, nous vous invitons à en prendre connaissance à votre prochaine connexion.
```
