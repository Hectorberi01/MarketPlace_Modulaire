# Publier les textes contractuels dans le CMS

## Pourquoi

Les quatre documents légaux de la plateforme ne sont écrits qu'à un endroit :
`hba-express-mobile/lib/features/legal/{seller,customer}/legal_content.dart`. Le CMS,
lui, n'en a aucun.

La conséquence n'est pas cosmétique. `GET /seller/legal/terms` répond 404 — le
comportement prévu quand rien n'est publié — et les portes de consentement du **web**
n'embarquent aucune copie de repli : dans `consent-gate.tsx`, `doc` est nul, donc
`besoin` est faux, donc **la porte ne s'affiche jamais**. Idem pour la vitrine. Un
vendeur inscrit depuis le navigateur n'accepte donc aucune condition, là où celui qui
s'inscrit depuis le téléphone en accepte — les applications mobiles, elles, portent bien
leur copie embarquée.

Publier ces quatre blocs ferme cet écart, et fait taire les 404 du même geste.

## Avant de lancer quoi que ce soit

`legal_content.dart` porte une consigne qui n'est pas encore honorée :

> TROIS modifications ont été apportées aux textes ci-dessous SANS toucher à cette
> version […] À FAIRE, dans cet ordre : reporter les TROIS corrections sur le site,
> PUIS changer `version` et `lastUpdated` des deux côtés le même jour.

Le troisième point — la police téléchargée à l'exécution depuis un serveur hors du
Bénin — attend un arbitrage : la déclarer, ou embarquer la police et supprimer le
transfert. Publier avant de trancher, c'est mettre en ligne un texte qu'on retirera.

## Les deux étapes

### 1. Extraire

```sh
python3 extraire_textes_legaux.py
```

Lit le Dart, écrit `documents/*.json`. **Ne publie rien.** Le lecteur refuse plutôt que
de deviner : interpolation inconnue, échappement illisible, section sans paragraphe — il
s'arrête et n'écrit rien. Un contrat amputé se lit parfaitement et dit autre chose ;
c'est le mode de panne contre lequel tout ce fichier est écrit.

**Relisez `documents/*.json`.** C'est ce qui partira, pas la source : vos corrections
éventuelles sur ces fichiers sont publiées telles quelles.

### 2. Publier

```sh
export API_BASE_URL=http://api:8080              # depuis le VPS, réseau Docker
export ADMIN_TOKEN=<jeton JWT d'un administrateur>

python3 publier_textes_legaux.py                # SIMULATION, n'écrit rien
python3 publier_textes_legaux.py --publier      # publie ce qui manque
```

L'adresse est celle de l'**hôte applicatif**, pas de la console : la passerelle web
s'authentifie par cookie de session et ignorerait le jeton porteur.

Sans `--publier`, le script simule. Ce qui part devient le texte opposable aux vendeurs
et aux acheteurs — un script qui écrit au premier lancement serait une façon de publier
un contrat sans l'avoir décidé.

Relancé, il ne refait rien : un emplacement déjà publié dans la même version est laissé
tel quel. Si une **autre** version y est publiée, il refuse et le dit ; `--remplacer`
archive l'ancien bloc avant de publier le nouveau. Écraser un texte que des utilisateurs
ont accepté n'est pas une opération de routine.

## Après

Les 404 de `/seller/legal/terms` et `/seller/legal/privacy` disparaissent, et la porte
de consentement s'affiche à la prochaine ouverture des consoles web. Les comptes créés
avant restent à `acceptedTermsVersion` nul : la porte les prendra au prochain passage,
puisqu'elle compare cette valeur à la version publiée.
