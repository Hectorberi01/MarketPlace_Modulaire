# Les trois textes de la vitrine, à reprendre dans la console

Les pages `/legal/conditions`, `/legal/confidentialite` et `/legal/mentions` ne portent
plus aucun texte : leur contenu vient désormais du back-office. **Cette rédaction est la
seule qui existe** — elle a été extraite des pages avant qu'elles ne soient vidées.

Tant qu'elle n'est pas saisie dans la console, les trois pages affichent un message
d'indisponibilité et restent hors de l'index des moteurs.

## Comment l'extraction a été vérifiée

Le convertisseur JSX ne devine rien : il reconnaît les balises que ces trois fichiers
emploient et laisse tomber le reste de façon visible. Les comptes ont été comparés à la
source — titres, sous-titres, éléments de liste, cartouches « À COMPLÉTER » — et
contrôlés contre toute fuite de balise, d'accolade JSX ou d'entité HTML.

| Page | Sections | Blocs | Titres | Listes | Fuites |
|---|---|---|---|---|---|
| Conditions générales de vente | 12 | 33 | 12/12 | 2/2 | 0 |
| Politique de confidentialité | 8 | 14 | 8/8 | 11/11 | 0 |
| Mentions légales | 6 | 14 | 6/6 | — | 1, corrigée |

La fuite des mentions légales était l'adresse du site, construite en JSX. Elle est
devenue un **À COMPLÉTER** plutôt qu'une valeur inventée.

## Ce qui reste à trancher

Les cartouches **À COMPLÉTER** sont reprises telles quelles. Ce sont les valeurs qui ne
se lisent nulle part dans le code et qu'inventer reviendrait à publier une déclaration
fausse. Ne publiez aucune de ces trois pages sans les avoir remplies.


---

# Conditions générales de vente

Adresse : `legal_sales_customer`  ·  12 sections


## Section 1 — Titre

```
1. Objet et parties
```


## Section 1 — Contenu

```
HBA Express est une place de marché exploitée par **HBA TECH ET TRADE**, entreprise individuelle de droit béninois immatriculée au RCCM de Cotonou sous le numéro RB/ABC/26 A 142835 (voir les [mentions légales](/legal/mentions)). Le contrat de vente est conclu **entre l'acheteur et le vendeur** de l'article ; l'exploitant du site n'est pas le vendeur. Il fournit la plateforme, encaisse le paiement pour le compte du vendeur, et organise le règlement après livraison.

Une même commande peut réunir les articles de plusieurs vendeurs. Elle est alors payée en une fois, mais exécutée par chacun d'eux pour ce qui le concerne.
```


## Section 2 — Titre

```
2. Compte
```


## Section 2 — Contenu

```
Commander exige un compte : le panier, les adresses et l'historique y sont rattachés. L'adresse e-mail doit être confirmée par un code à six chiffres avant la première commande. Un numéro de téléphone joignable est obligatoire — c'est par lui que le livreur vous appellera.

Vous êtes responsable de la confidentialité de votre mot de passe. Signalez sans délai toute utilisation que vous n'auriez pas autorisée.
```


## Section 3 — Titre

```
3. Prix
```


## Section 3 — Contenu

```
Les prix sont affichés en **francs CFA (XOF), toutes taxes comprises**. Le franc CFA n'ayant pas de sous-unité, aucun montant n'est exprimé en centimes.

À COMPLÉTER — Mention de TVA à trancher, et la question n'est PAS « quel taux appliquer ». L'attestation IFU range l'exploitant au régime des **micro et petites entreprises** — un régime dans lequel une entreprise n'est, en principe, pas redevable de la TVA et ne la facture donc pas. Si c'est le cas, cette page ne doit pas annoncer un taux mais porter la mention d'exonération, et le moteur de taxe de la plateforme doit rester à zéro. Faites confirmer votre situation par le centre des impôts (CIPE 2 Abomey-Calavi) avant d'écrire quoi que ce soit ici : annoncer une TVA qu'on ne reverse pas, ou l'omettre alors qu'on la doit, sont deux fautes de nature différente et toutes deux coûteuses.

Les frais de livraison s'ajoutent au sous-total. Ils dépendent de la **commune de destination** et ne sont donc connus qu'une fois l'adresse choisie. Le total affiché avant validation est celui qui sera débité : si le montant a changé entre l'affichage et la validation, la commande est **refusée plutôt que passée au nouveau prix**, et le nouveau total vous est présenté.
```


## Section 4 — Titre

```
4. Commande
```


## Section 4 — Contenu

```
La commande est passée en validant le panier, une adresse de livraison et un moyen de paiement. Une adresse incomplète — sans commune, sans point de repère ou sans téléphone valide — est refusée avant le paiement : sans ces informations, le colis ne peut pas être livré.

La commande n'est confirmée qu'une fois le paiement abouti et la disponibilité des articles vérifiée. Si un article devient indisponible entre-temps, la commande est annulée et aucun montant n'est retenu.
```


## Section 5 — Titre

```
5. Paiement
```


## Section 5 — Contenu

```
Les moyens acceptés sont :

- **Mobile Money ou carte bancaire**, par l'intermédiaire de notre prestataire de paiement. Vous êtes redirigé vers sa page sécurisée, où vous choisissez votre opérateur — MTN MoMo, Moov Money — ou votre carte, et saisissez vos informations. Pour un règlement Mobile Money, une demande de confirmation part ensuite sur votre téléphone : la commande n'est réglée qu'après votre validation.
- **Cagnotte** — le solde issu de vos remboursements peut couvrir tout ou partie d'une commande. Il n'est jamais utilisé sans que vous l'ayez demandé.

Le **code de sécurité** de votre carte ne nous est jamais demandé et n'est conservé nulle part. Le numéro de carte est saisi sur la page du prestataire ; si vous choisissez d'enregistrer un moyen de paiement dans votre compte, seuls la marque, les quatre derniers chiffres, la date d'expiration et le nom du titulaire sont conservés — jamais le numéro complet. Pour un compte Mobile Money enregistré, le numéro de téléphone est conservé en entier, puisqu'il est nécessaire au prélèvement.

Si le paiement échoue, la commande reste en attente de règlement et peut être reprise depuis son détail, sans refaire le panier. Une commande jamais réglée est annulée automatiquement après un délai, et les articles réservés sont remis en vente.
```


## Section 6 — Titre

```
6. Livraison
```


## Section 6 — Contenu

```
La livraison est assurée dans les communes desservies. Le tarif et le délai indicatif dépendent de la zone de destination et vous sont présentés avant le paiement.

Le **point de repère** que vous renseignez est l'information principale utilisée par le livreur — davantage que le nom de la rue. Une adresse imprécise est la première cause d'échec de livraison.

Si vous n'êtes pas disponible lors de la livraison, le livreur peut tenter de vous contacter pour convenir d'un nouveau créneau.

Le délai de livraison peut varier selon la zone, la météo, le trafic et la disponibilité du livreur. Il est donné à titre indicatif et ne constitue pas un engagement contractuel. Le vendeur n'est pas responsable des retards de livraison imputables au transporteur. Le client est invité à suivre l'avancement de sa commande via le suivi en ligne ou à contacter le service client pour toute question relative à la livraison.

En cas de retard de livraison, vous pouvez contacter le service client pour obtenir des informations sur l'état de votre commande.
```


## Section 7 — Titre

```
7. Retours et remboursements
```


## Section 7 — Contenu

```
Une demande de retour peut être ouverte depuis le détail d'une commande livrée, pendant **sept jours** à compter de la livraison. Ce délai part de la date de livraison réelle, et non de la date de commande : la lenteur d'un transport ne réduit pas votre droit de retour.

Accepter un retour n'est pas le rembourser : ce sont deux décisions distinctes. Le vendeur accepte d'abord la demande, vous renvoyez l'article, sa réception est constatée, puis le remboursement et son montant sont validés. C'est cette dernière validation qui crédite votre cagnotte, et le montant retenu peut être inférieur au prix payé si l'article revient incomplet ou abîmé.

Le remboursement est crédité sur votre **cagnotte**, immédiatement utilisable pour une prochaine commande. Vous pouvez également demander le versement de votre cagnotte sur un compte Mobile Money : cette demande retient les fonds et reste soumise à la validation de la plateforme.

Les articles renvoyés doivent être dans l'état dans lequel vous les avez reçus, avec tous les accessoires et emballages. Les frais de retour sont à votre charge, sauf si le vendeur a accepté de les prendre en charge.

Certains articles ne sont pas éligibles au retour, notamment les denrées périssables, les produits d'hygiène et les articles sur-mesure. Vérifiez la description de l'article avant de passer commande.

Une fois le retour accepté et l'article reçu, le vendeur dispose d'un délai pour examiner l'état de l'article et confirmer le remboursement. Si le vendeur ne répond pas dans ce délai, la demande de remboursement est automatiquement validée.
```


## Section 8 — Titre

```
8. Annulation
```


## Section 8 — Contenu

```
Une commande peut être annulée par vous-même, depuis son détail, tant qu'elle n'est pas confirmée — c'est-à-dire avant le règlement, ou pendant le court intervalle qui suit. Une fois la commande **confirmée**, elle n'est plus annulable : la voie de sortie est alors la **demande de retour**, ouverte après la livraison, dans le délai indiqué à l'article 7.

Vous pouvez écrire au vendeur par la messagerie de la commande, et il peut accepter d'y renoncer de son côté ; mais cet arrangement ne constitue pas un droit d'annulation ouvert après confirmation.
```


## Section 9 — Titre

```
9. Avis
```


## Section 9 — Contenu

```
Les avis ne peuvent être déposés que par un acheteur ayant effectivement commandé l'article. Un avis reste visible tant qu'il n'a pas été examiné, y compris lorsqu'il a été porté à l'attention de l'exploitant. Les avis manifestement injurieux ou sans rapport avec le produit sont retirés.
```


## Section 10 — Titre

```
10. Litiges
```


## Section 10 — Contenu

```
En cas de désaccord, contactez d'abord le vendeur depuis la messagerie de la commande. Si le désaccord persiste, ouvrez un litige : l'exploitant examine alors le dossier et tranche sur la base des échanges et des pièces fournies.

Les présentes conditions sont soumises au **droit béninois**. L'exploitant étant immatriculé au registre du commerce de Cotonou, les juridictions béninoises sont compétentes, et le **Tribunal de commerce de Cotonou** pour les litiges relevant de sa compétence. Les règles protectrices que la loi réserve au consommateur restent applicables quelle que soit cette clause.

Les litiges peuvent être soumis à un dispositif de médiation, si le vendeur en a retenu un. Le nom du médiateur ou de l'organisme et la procédure pour le saisir sont communiqués dans ce cas.

Si aucun dispositif de médiation n'est retenu, aucune promesse de médiation ne peut être faite. Annoncer une médiation qui n'existe pas est pire que de n'en proposer aucune.
```


## Section 11 — Titre

```
11. Données personnelles
```


## Section 11 — Contenu

```
Le traitement de vos données est décrit dans la [ politique de confidentialité ](/legal/confidentialite) .
```


## Section 12 — Titre

```
12. Modification
```


## Section 12 — Contenu

```
Les présentes conditions peuvent être modifiées. La version applicable à une commande est celle en vigueur au moment où elle est passée.
```


---

# Politique de confidentialité

Adresse : `legal_privacy_customer`  ·  8 sections


## Section 1 — Titre

```
Responsable du traitement
```


## Section 1 — Contenu

```
Le responsable du traitement est l'entité qui détermine les finalités et les moyens du traitement des données personnelles.

Vous pouvez contacter le responsable du traitement à l'adresse e-mail suivante : support@hba-express.com.
```


## Section 2 — Titre

```
Données collectées
```


## Section 2 — Contenu

```
- **Compte** : Prénom, nom, adresse e-mail, numéro de téléphone, mot de passe (conservé sous forme condensée, jamais en clair).
- **Adresses de livraison** : Destinataire, téléphone, commune, quartier, point de repère, complément. Une position GPS peut être enregistrée, mais elle est **facultative** : une adresse sans coordonnées est parfaitement livrable.
- **Commandes et paiements** : Articles, montants, adresse figée sur la commande, statut de livraison, mouvements de cagnotte. Le paiement lui-même se fait sur la page du prestataire : nous ne voyons ni le numéro de carte que vous y saisissez, ni son code de sécurité, qui ne nous est jamais demandé et n'est conservé nulle part.
- **Moyens de paiement enregistrés** : Si vous choisissez d'enregistrer une carte dans votre compte, son numéro nous est transmis pour en extraire les quatre derniers chiffres, puis il est **abandonné sans être conservé**. Seuls demeurent la marque, ces **quatre derniers chiffres**, la date d'expiration et le **nom du titulaire**. Pour un compte Mobile Money enregistré, le numéro de téléphone est conservé **en entier** : il est nécessaire au prélèvement.
- **Messages et avis** : Contenu des échanges avec les vendeurs, pièces jointes, avis publiés et réponses des vendeurs.
- **Notifications** : Jeton technique de votre appareil, si vous activez les notifications, afin de vous envoyer le suivi de vos commandes. Il est supprimé lorsque vous les désactivez.
- **Journaux techniques** : Adresse IP et informations de connexion, conservées pour la sécurité et la limitation des abus. Un cas particulier est journalisé nommément : lorsqu'une inscription est bloquée parce que le ** numéro de téléphone est déjà rattaché à un autre compte ** , l'adresse et le numéro saisis sont enregistrés. C'est la seule façon pour l'assistance de débloquer la situation — sans cette trace, la personne concernée ne pourrait ni s'inscrire, ni se connecter, ni recevoir de code, et personne ne saurait pourquoi.
```


## Section 3 — Titre

```
Ce que nous ne faisons pas
```


## Section 3 — Contenu

```
Le site ne charge ** aucun traceur publicitaire ni aucun outil de mesure d'audience tiers ** . Aucune donnée de navigation n'est transmise à une régie. Vos données ne sont ni vendues ni louées.

Le seul cookie déposé est celui de votre session : il est chiffré, inaccessible au JavaScript de la page, et nécessaire au fonctionnement du panier et du compte. Il n'est pas utilisé à des fins de suivi.
```


## Section 4 — Titre

```
Destinataires
```


## Section 4 — Contenu

```
- **Les vendeurs** concernés par vos commandes, pour ce qui leur est nécessaire : articles, adresse de livraison, téléphone du destinataire. Ils n'ont pas accès à vos moyens de paiement.
- **Les livreurs**, pour l'adresse et le téléphone du destinataire uniquement.
- **Les prestataires de paiement**, pour le traitement des transactions.
- **L'hébergeur** de la plateforme.
```


## Section 5 — Titre

```
Durée de conservation
```


## Section 5 — Contenu

```
Les données sont conservées le temps nécessaire à la gestion de votre compte et de vos commandes, puis archivées pour respecter les obligations légales. Les données de facturation sont conservées au moins 10 ans.

Les données techniques et les journaux sont conservés pendant 12 mois.
```


## Section 6 — Titre

```
Vos droits
```


## Section 6 — Contenu

```
Vous pouvez accéder à vos données, les rectifier, en demander l'effacement, vous opposer à certains traitements et demander la portabilité de vos informations. Une partie de ces actions se fait directement depuis votre compte : modifier votre profil, gérer vos adresses, désactiver les notifications, supprimer votre compte.

La suppression du compte n'efface pas les pièces que la loi impose de conserver, notamment les données de facturation des commandes déjà livrées.

Pour toute autre demande, vous pouvez nous écrire depuis la [page de contact](/aide/contact).

À COMPLÉTER — Adresse à laquelle adresser une demande d'exercice de droits, et délai de réponse annoncé.
```


## Section 7 — Titre

```
Sécurité
```


## Section 7 — Contenu

```
Les échanges avec le site sont chiffrés. Les jetons d'authentification ne sont jamais remis au navigateur : ils restent sur le serveur, dans un cookie chiffré que le JavaScript de la page ne peut pas lire. Les mots de passe sont conservés sous forme condensée et non réversible.
```


## Section 8 — Titre

```
Contact
```


## Section 8 — Contenu

```
Pour toute question relative à vos données, écrivez-nous depuis la [page de contact](/aide/contact). L'identité du responsable de traitement et ses coordonnées figurent dans les [mentions légales](/legal/mentions).
```


---

# Mentions légales

Adresse : `legal_notice`  ·  6 sections


## Section 1 — Titre

```
Éditeur du site
```


## Section 1 — Contenu

```
HBA Express est édité par **HBA TECH ET TRADE**, entreprise individuelle de droit béninois exploitée en direct par son propriétaire. Une entreprise individuelle n'ayant pas de capital social, aucun montant n'est mentionné à ce titre.

**Siège et établissement principal** — Îlot C/SB, parcelle C/SB, maison Anicet Chabi, quartier Yenandjro, Godomey, commune d'Abomey-Calavi, département de l'Atlantique, Bénin.

** Registre du commerce et du crédit mobilier (RCCM) ** — Cotonou n° RB/ABC/26 A 142835, immatriculée le 26 juin 2026 au greffe du Tribunal de commerce de Cotonou.

**Identifiant fiscal unique (IFU)** — 0202363674658, délivré le 29 juin 2026 par la Direction générale des impôts (centre des impôts CIPE 2 Abomey-Calavi). Régime fiscal : micro et petites entreprises.

**Représentant légal et directeur de la publication** — Hector Ayeko Beri Adjakpa, propriétaire-exploitant.

**Contact** — hector.adjakpa@hbatechettrade.com · +229 01 97 75 53 41.
```


## Section 2 — Titre

```
Hébergement
```


## Section 2 — Contenu

```
Le site est hébergé sur un serveur dédié par **OVH SAS**, société par actions simplifiée au capital de 50 000 000 €, immatriculée au RCS Lille Métropole sous le numéro 424 761 419 00045, dont le siège social est situé au 2 rue Kellermann, 59100 Roubaix, France.
```


## Section 3 — Titre

```
Nature du service
```


## Section 3 — Contenu

```
HBA Express est une **place de marché**. Les produits présentés sont proposés à la vente par des vendeurs indépendants, qui en sont responsables : description, conformité, garantie et, selon le mode d'expédition retenu, remise au transporteur.

L'éditeur intervient comme intermédiaire technique : il met en relation acheteurs et vendeurs, encaisse les paiements pour le compte de ces derniers, et reverse les sommes dues après livraison. Cette distinction n'est pas cosmétique — elle détermine à qui l'acheteur s'adresse en cas de litige. Le recours amiable se fait d'abord auprès du vendeur, via la messagerie de la commande, puis auprès de l'éditeur si le désaccord persiste.
```


## Section 4 — Titre

```
Propriété intellectuelle
```


## Section 4 — Contenu

```
La structure du site, sa charte graphique et ses textes sont la propriété de l'éditeur. Les visuels, marques et descriptions des produits restent la propriété de leurs vendeurs respectifs, qui garantissent détenir les droits nécessaires à leur publication.

Toute reproduction du catalogue à des fins commerciales, y compris par extraction automatisée, est interdite sans autorisation écrite.
```


## Section 5 — Titre

```
Signalement d'un contenu
```


## Section 5 — Contenu

```
Une annonce trompeuse, un produit illicite ou une atteinte à vos droits peuvent être signalés à l'éditeur, qui retire les contenus manifestement illicites dès qu'il en a connaissance.

**Signaler un contenu** — tout signalement se fait par e-mail à hector.adjakpa@gmail.com, en précisant l'adresse de la page concernée et la raison du signalement. Les signalements manifestement fondés sont traités dès leur prise de connaissance.
```


## Section 6 — Titre

```
Adresse du site
```


## Section 6 — Contenu

```
À COMPLÉTER — adresse du site
```
