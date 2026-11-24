# Cadrage fonctionnel du MVP — AAAS

> Rédigé par l'expert fonctionnel. Document de référence pour l'agent `architecte` (choix techniques) et l'agent `testeur` (les scénarios Gherkin servent de base aux tests).
> Statut : **proposition, version 6**, mise à jour avec les décisions du porteur de projet (cahier des charges v1.5). Les valeurs par défaut marquées *(défaut)* sont à confirmer avec l'association (voir § 5).

### Décisions du porteur de projet (version 2)
- L'association est en **Tunisie**. L'application est **en français uniquement**.
- **Réservation en deux temps** : l'utilisateur fait une demande (« En attente »), il contacte l'administrateur par téléphone ou WhatsApp, puis l'administrateur **confirme** ou **refuse** la demande dans l'application.
- Une demande « En attente » **bloque une place**. Elle **expire** automatiquement si l'administrateur ne l'a pas traitée à temps.
- Les événements peuvent être **payants**, avec un **paiement hors ligne uniquement** (en main propre) : prix optionnel en dinars (TND), réservation marquée « payée » par l'administrateur, aucun remboursement géré dans l'application.
- Heure de référence : **heure de Tunis** (Africa/Tunis, UTC+1, pas de changement d'heure).
- Protection des données : **loi organique tunisienne n° 2004-63 du 27 juillet 2004** (autorité de contrôle : **INPDP**), dans une version minimale.

### Décisions du porteur de projet (version 3, cahier des charges v1.1)
- **Données du compte** : on conserve aussi la **date de création** du compte et la **date de dernière connexion** (nécessaires à US-F3 et à R25).
- **Téléphone** : seuls les numéros tunisiens **+216 suivi de 8 chiffres** sont acceptés. Pas de saisie locale sans préfixe, pas de préfixe 00, pas de numéro international.
- **Mineurs** : aucune case à cocher ni contrôle dédié ; le site est accessible à tout le monde. La mention « inscription réservée aux majeurs » est abandonnée.
- **Consentement** : on conserve la **version de la politique de confidentialité acceptée**, avec la date d'acceptation.

### Décisions du porteur de projet (version 4, cahier des charges v1.2 — questions de l'étape 2)
- **Événement déjà commencé** : un admin **peut** le modifier (pas un événement terminé ou annulé). Voir R28, US-E2.
- **Événement terminé** : son **détail reste accessible** par son adresse (historique conservé) ; il disparaît de la liste. Voir R19, US-C2.
- **Limites de saisie** validées : capacité de 1 à 10 000, prix jusqu'à 99 999,999 TND, titre et lieu 200 caractères, description 4 000 caractères. Voir R11, R12, R29, US-E2.
- **Numéro fixe** : un numéro fixe tunisien est accepté (« +216 » + 8 chiffres, mobile ou fixe), y compris comme numéro de contact de l'association. Voir R20, Q17.
- **Numéro de contact** : un admin **peut le retirer**. Voir R8, US-E8.
- **Brouillon** : un admin **peut le supprimer** dès l'étape 2, **uniquement s'il n'a jamais été publié**. Voir R15, US-E2.
- Les autres questions de l'étape 2 gardent la **valeur par défaut** proposée par l'architecte (événement en cours listé, format du prix, changement de lieu sans avertissement, avertissement « numéro non configuré » non bloquant, 20 événements par page). Voir Q18 à Q27.

### Décisions du porteur de projet (version 5, cahier des charges v1.4 — tranche « Réservations »)
Le porteur **accepte toutes les valeurs par défaut** du plan `technique/plan-reservations.md` : questions **QF-1 à QF-10** et décisions **P-R1 à P-R8**.
- **Administrateur et réservation** : un admin peut demander une place pour lui-même, comme un membre (QF-1, R31).
- **Échéance** : elle suit un changement de date de l'événement ; le délai de 24 h d'annulation d'une réservation confirmée se compte depuis le début **actuel** (QF-2, R30, R6, R9).
- **Après un refus, une expiration ou une annulation** : le détail de l'événement affiche une information sur la demande précédente, et le bouton « Demander une place » si l'événement est réservable (QF-3, US-C2). Pas de limite du nombre de nouvelles demandes après un refus (QF-10, R33).
- **Mention « Payée »** : posable même après la fin de l'événement ; retirable même sur une réservation annulée (QF-4, QF-5, R13, US-E7).
- **Inscrits** : confirmées triées par nom puis prénom (QF-6, US-E4).
- **Historique** : une réservation confirmée d'un événement terminé va dans « Historique » avec la mention « Terminé » (QF-7, US-D2).
- **Libellés** validés (QF-8) : voir `libelles-ecrans.md` (version 1.1).
- **Politique de confidentialité** : elle mentionne que les administrateurs voient les coordonnées des inscrits (QF-9, R32) ; texte en version 1.1-provisoire.
- **Décisions P-R1 à P-R8** (plan) ayant un effet fonctionnel : délais 48 h et 24 h en configuration, non modifiables par l'admin depuis l'interface (P-R4) ; un double clic sur « Demander une place » ne crée qu'une demande et ne montre pas d'erreur (P-R5, US-D1) ; l'application **n'enregistre pas** quel admin a confirmé ou refusé une demande, seulement celui qui a marqué « Payée » (P-R6, US-E6, US-E7) ; la liste des inscrits n'est pas paginée (P-R7, US-E4). P-R1 à P-R3 et P-R8 sont techniques (validation des ADR, tâche d'expiration en arrière-plan toutes les 5 minutes, ordre des tâches) : voir l'architecte.

### Décisions du porteur de projet (version 6, cahier des charges v1.5 — tranche « Actualités »)
Le porteur **accepte toutes les valeurs par défaut** du plan `technique/plan-actualites.md` : questions **QN-1 à QN-9** et décisions **P-N1 à P-N7** (ADR 0022 et 0023). Voir R18, US-B1, US-B2, US-E1 et `libelles-ecrans.md` (version 1.3).
- **Texte brut**, 8 000 caractères au plus (titre : 200) ; **pas d'image**, pas de mise en forme, **pas de lien cliquable** ; une balise saisie (par exemple `<script>`) s'affiche comme du texte (QN-1, QN-5).
- Une actualité **republiée garde sa date d'origine** ; **pas de « Mise à jour le »** ; **pas d'auteur affiché** (QN-2, QN-7, QN-4).
- **Extrait automatique** de 200 caractères dans la liste (QN-3).
- **Suppression directe** d'une actualité publiée, après une confirmation qui le rappelle (QN-6).
- **Liste admin** triée par dernière modification, avec filtre par statut ; **aperçu** dans le formulaire (QN-9, P-N6).
- **Page d'accueil** « / » = actualités ; lien « Actualités » en premier dans les menus (QN-8).
- **Pas d'épinglage** (actualité mise en tête) ni de publication programmée.
- P-N1 à P-N5 et P-N7 sont techniques (validation des ADR, nom de la classe `NewsArticle`, pagination par numéros de page, publication / dépublication / suppression sans numéro de version, remaniement de la pagination, **ordre : actualités après l'annulation d'un événement**, comme le prévoit le cahier § 8) : voir l'architecte. P-N4 a un effet visible : seul l'**enregistrement d'une modification** détecte qu'un autre admin est passé avant (conflit de modification) ; publier, dépublier et supprimer ne comparent pas de version, mais refusent un état incohérent (déjà publiée, pas publiée, déjà supprimée).

### Correspondance du vocabulaire (cahier des charges / ce document)

| Cahier des charges | Ce document | Dans le code (indication de l'architecte) |
|---|---|---|
| Membre | Utilisateur | rôle `User` |
| Annulée par le membre | Annulée par l'utilisateur | statut `CancelledByUser` |
| Administrateur | Administrateur | rôle `Admin` |
| RG-xx (règles obligatoires, § 5.2 du cahier) | R-xx (règles détaillées, § 4 ci-dessous) | — |

Correspondance des règles : RG-01 = R2, RG-02 = R5 et R7, RG-03 = R4, R31 et R33, RG-04 = R6 et R30, RG-05 = R9, RG-06 = R11, RG-07 = R14 et R15, RG-08 = R12 et R13, RG-09 = R22, RG-10 = R8, RG-11 = R20, RG-12 = R28 (et R30 pour le recalcul), RG-13 = R11, R12 et R29, RG-14 = R19, RG-15 = R32, RG-16 = R18 (et R26 pour la date). En cas de contradiction, le cahier des charges fait foi.

---

## 1. Le besoin en une phrase

Permettre à l'association de **publier ses actualités et ses événements**, et à ses membres de **demander une place en ligne** que l'administrateur confirme après un contact téléphonique, avec un outil simple à gérer par des bénévoles.

### Rôles

| Rôle | Qui | Ce qu'il peut faire au MVP |
|---|---|---|
| **Visiteur** | Toute personne non connectée | Lire les actualités publiées, voir les événements à venir (prix, places restantes), créer un compte, se connecter. |
| **Utilisateur** | Personne ayant un compte et connectée | Tout ce que fait le visiteur + demander une place, voir le numéro de l'administrateur à contacter, suivre ses réservations, annuler, supprimer son compte. |
| **Administrateur** | Bénévole désigné par l'association | Gérer les actualités et les événements ; confirmer ou refuser les demandes ; marquer une réservation comme payée ; voir les inscrits **et leurs coordonnées** (R32) ; annuler une réservation ou un événement ; configurer (ou retirer) le numéro de contact ; **et, comme un utilisateur, demander une place pour lui-même (R31)**. |
| **Application** (traitement automatique) | — | Faire passer en « Expirée » les demandes non traitées dans le délai. |

L'**assistant RAG** (questions sur les documents de l'association) est prévu **plus tard** (semaine 4) et n'est pas cadré ici.

---

## 2. Cycle de vie d'une réservation

Une réservation est **active** quand elle est **En attente** (échéance non dépassée) ou **Confirmée**. Seules les réservations actives occupent une place.

**Places restantes = capacité − (nombre de réservations En attente + Confirmées).**

| Statut | Signification | Occupe une place ? | Statut final ? |
|---|---|---|---|
| **En attente** | Demande faite par l'utilisateur, pas encore traitée par l'admin | Oui | Non |
| **Confirmée** | Validée par l'admin | Oui | Non |
| **Refusée** | Refusée par l'admin | Non | Oui |
| **Expirée** | Pas traitée dans le délai | Non | Oui |
| **Annulée par l'utilisateur** | L'utilisateur s'est désisté | Non | Oui |
| **Annulée par l'association** | L'admin l'a annulée ou l'événement a été annulé | Non | Oui |

Une demande En attente **dont l'échéance est passée est expirée**, même si la mise à jour automatique n'est pas encore passée : tous les écrans l'affichent « Expirée » et elle ne compte plus dans les places occupées (R6).

### Transitions autorisées

| De | Vers | Qui | Condition |
|---|---|---|---|
| *(aucun)* | En attente | Utilisateur (ou administrateur pour lui-même, R31) | L'événement est réservable (R2) et l'utilisateur n'a pas déjà de réservation active pour cet événement. |
| En attente | Confirmée | Admin | Avant l'échéance d'expiration et avant le début de l'événement. |
| En attente | Refusée | Admin | Avant l'échéance (donc avant le début de l'événement). Un motif est facultatif *(défaut)*. |
| En attente | Expirée | Application | L'échéance est atteinte : **48 h après la demande, ou au début de l'événement si c'est plus tôt** ; l'échéance est **recalculée** si la date de l'événement change (R30). |
| En attente | Annulée par l'utilisateur | Utilisateur | Avant le début de l'événement (pas de délai). |
| En attente | Annulée par l'association | Admin / annulation de l'événement | Avant la fin de l'événement. |
| Confirmée | Annulée par l'utilisateur | Utilisateur | Jusqu'à **24 h avant le début actuel** de l'événement (limite comprise) *(défaut)*. |
| Confirmée | Annulée par l'association | Admin / annulation de l'événement | Avant la fin de l'événement. |

Toute autre transition est **interdite**. En particulier, un statut final ne change plus : une demande refusée ou expirée ne peut pas être confirmée ; l'utilisateur doit faire une nouvelle demande.

**« Payée »** n'est pas un statut. C'est une **mention** que l'admin ajoute à une réservation **Confirmée** d'un événement **payant** (règle R13), **à tout moment, même après la fin de l'événement**. Elle reste visible si la réservation est ensuite annulée, pour que l'association puisse gérer un éventuel remboursement en dehors de l'application, et l'admin peut la **retirer** (erreur de saisie), même sur une réservation annulée.

---

## 3. User stories et critères d'acceptation

Les scénarios sont écrits en Gherkin français. Toutes les heures sont en heure de Tunis. « Événement réservable » signifie : publié, non annulé, pas encore commencé et avec au moins une place restante (règle R2).

### Épique A — Comptes et authentification

#### US-A1 — Créer un compte *(modifiée : téléphone tunisien uniquement, date de création)*
> En tant que **visiteur**, je veux **créer un compte avec mon e-mail, mon téléphone et un mot de passe** afin de **pouvoir demander des places**.

```gherkin
Fonctionnalité: Création de compte

  Scénario: Création réussie
    Étant donné que je suis un visiteur
    Quand je saisis mon prénom, mon nom, un e-mail valide, un numéro de téléphone valide et un mot de passe conforme
    Et que je coche la case d'acceptation de la politique de confidentialité
    Et que je valide
    Alors mon compte est créé avec le rôle "Utilisateur"
    Et la date de création du compte est enregistrée
    Et je suis connecté

  Scénario: E-mail déjà utilisé
    Étant donné qu'un compte existe déjà avec l'e-mail "salma@exemple.tn"
    Quand je crée un compte avec l'e-mail "Salma@Exemple.tn"
    Alors le compte n'est pas créé
    Et un message m'indique qu'un compte existe déjà avec cet e-mail

  Scénario: Numéro tunisien valide (mobile ou fixe)
    Quand je crée un compte avec le téléphone "+216 20 123 456", "+21620123456" ou "+216 71 123 456" (numéro fixe)
    Alors le compte est créé

  Scénario: Numéro de téléphone invalide
    Quand je crée un compte avec le téléphone "12 34"
    Alors le compte n'est pas créé
    Et un message m'indique le format attendu, par exemple "+216 20 123 456"

  Scénario: Formats de numéro refusés
    Quand je crée un compte avec l'un de ces téléphones :
      | téléphone         | raison                                 |
      | 20 123 456        | saisie locale sans le préfixe +216     |
      | 0021620123456     | préfixe 00 non accepté                 |
      | +33 6 12 34 56 78 | numéro international non tunisien      |
      | +216 20 123 45    | moins de 8 chiffres après +216         |
      | +216 20 123 4567  | plus de 8 chiffres après +216          |
    Alors le compte n'est pas créé
    Et un message m'indique le format attendu, par exemple "+216 20 123 456"

  Scénario: Mot de passe trop faible
    Quand je crée un compte avec le mot de passe "abc"
    Alors le compte n'est pas créé
    Et un message m'explique les exigences du mot de passe

  Scénario: Consentement non donné
    Quand je valide le formulaire sans cocher la case de la politique de confidentialité
    Alors le compte n'est pas créé
    Et un message m'indique que l'acceptation est obligatoire

  Scénario: Champ obligatoire manquant ou e-mail invalide
    Quand je valide le formulaire avec un e-mail "salma@" ou sans nom
    Alors le compte n'est pas créé
    Et le champ en erreur est signalé
```

#### US-A2 — Se connecter *(modifiée : date de dernière connexion)*
> En tant qu'**utilisateur ou administrateur**, je veux **me connecter avec mon e-mail et mon mot de passe** afin d'**accéder à mon espace**.

```gherkin
Fonctionnalité: Connexion

  Scénario: Connexion réussie
    Étant donné que j'ai un compte avec l'e-mail "salma@exemple.tn"
    Quand je me connecte avec cet e-mail et le bon mot de passe
    Alors je suis connecté
    Et j'accède aux fonctions de mon rôle
    Et la date de ma dernière connexion est mise à jour

  Scénario: Identifiants incorrects
    Quand je me connecte avec un mauvais mot de passe ou un e-mail inconnu
    Alors je ne suis pas connecté
    Et le message est le même dans les deux cas : "E-mail ou mot de passe incorrect"
    Et la date de dernière connexion n'est pas modifiée

  Scénario: Trop de tentatives
    Étant donné que j'ai échoué 5 fois de suite à me connecter
    Quand je tente une 6e connexion
    Alors la connexion est bloquée temporairement (15 minutes)
    Et un message me l'indique
```

#### US-A3 — Se déconnecter
> En tant qu'**utilisateur connecté**, je veux **me déconnecter** afin que **personne d'autre n'utilise mon compte sur cet appareil**.

```gherkin
Fonctionnalité: Déconnexion

  Scénario: Déconnexion
    Étant donné que je suis connecté
    Quand je me déconnecte
    Alors je redeviens visiteur
    Et les pages réservées aux utilisateurs ne me sont plus accessibles

  Scénario: Session expirée
    Étant donné que ma session a expiré
    Quand j'essaie de demander une place
    Alors on me demande de me reconnecter
    Et aucune demande n'est créée
```

### Épique B — Actualités

Règles : R18 (et RG-16 du cahier des charges). Les actualités sont la **page d'accueil** du site. La date de publication s'affiche **sans l'heure**, au jour de Tunis. Le texte d'une actualité est toujours affiché **comme du texte** (aucune balise n'est interprétée).

#### US-B1 — Consulter la liste des actualités *(modifiée v6 : extrait, date, ordre, accueil)*
> En tant que **visiteur**, je veux **voir les actualités publiées, les plus récentes en premier** afin de **suivre la vie de l'association**.

```gherkin
Fonctionnalité: Liste des actualités

  Scénario: Affichage des actualités publiées
    Étant donné que 3 actualités sont publiées et 1 est en brouillon
    Quand j'ouvre la page des actualités
    Alors je vois les 3 actualités publiées, de la plus récente à la plus ancienne
    Et je ne vois pas l'actualité en brouillon
    Et chaque actualité affiche son titre, sa date de publication et un extrait

  Scénario: Plus de 10 actualités
    Étant donné que 25 actualités sont publiées
    Quand j'ouvre la page des actualités
    Alors je vois les 10 plus récentes
    Et je peux passer à la page suivante

  Scénario: Dernière page
    Étant donné que 25 actualités sont publiées
    Quand j'ouvre la page 3 des actualités
    Alors je vois les 5 plus anciennes
    Et la page indique "Page 3 sur 3"

  Scénario: Aucune actualité
    Étant donné qu'aucune actualité n'est publiée
    Quand j'ouvre la page des actualités
    Alors un message indique "Aucune actualité pour le moment"

  Scénario: Deux actualités publiées au même moment
    Étant donné que 12 actualités ont exactement la même date de publication
    Quand je consulte la page 1 puis la page 2, et que je recommence
    Alors l'ordre est toujours le même
    Et chaque actualité apparaît une seule fois sur l'ensemble des deux pages

  Scénario: Extrait d'un texte court
    Étant donné une actualité dont le texte fait 150 caractères
    Quand j'ouvre la page des actualités
    Alors l'extrait est le texte entier, sans "…"

  Scénario: Extrait d'un texte long
    Étant donné une actualité dont le texte dépasse 200 caractères
    Quand j'ouvre la page des actualités
    Alors l'extrait fait 200 caractères au plus
    Et il est coupé à la fin d'un mot
    Et il se termine par "…"

  Scénario: Extrait d'un texte de exactement 200 caractères
    Étant donné une actualité dont le texte fait exactement 200 caractères
    Quand j'ouvre la page des actualités
    Alors l'extrait est le texte entier, sans "…"

  Scénario: Extrait d'un texte sans espace
    Étant donné une actualité dont le texte est un seul mot de 300 caractères
    Quand j'ouvre la page des actualités
    Alors l'extrait est coupé net à 200 caractères et se termine par "…"

  Scénario: Extrait d'un texte en plusieurs paragraphes
    Étant donné une actualité dont le texte contient des sauts de ligne et des lignes vides
    Quand j'ouvre la page des actualités
    Alors l'extrait tient sur un seul paragraphe, les sauts de ligne étant remplacés par une espace

  Scénario: Contenu contenant des balises
    Étant donné une actualité dont le titre ou le texte contient "<b>gras</b>" ou "<script>alert(1)</script>"
    Quand j'ouvre la page des actualités
    Alors ces caractères s'affichent tels quels, comme du texte
    Et aucun code n'est exécuté

  Scénario: Date affichée à l'heure de Tunis
    Étant donné une actualité publiée le 5 décembre 2026 à 23 h 30 UTC (soit le 6 décembre à 0 h 30 à Tunis)
    Quand j'ouvre la page des actualités
    Alors sa date affichée est "Publiée le 6 décembre 2026"
    Et aucune heure n'est affichée

  Scénario: Une actualité dépubliée ou supprimée disparaît aussitôt
    Étant donné une actualité publiée visible dans la liste
    Quand un administrateur la dépublie ou la supprime
    Alors elle n'apparaît plus dans la liste dès l'actualisation de la page

  Scénario: Une actualité republiée garde sa place
    Étant donné une actualité publiée le 1er novembre, dépubliée, puis republiée le 20 novembre
    Quand j'ouvre la page des actualités
    Alors elle est classée selon sa date d'origine, le 1er novembre
    Et elle n'est pas remontée en tête de liste

  Scénario: Page d'accueil
    Quand j'ouvre l'adresse principale du site
    Alors la page des actualités s'affiche
    Et le lien "Actualités" est le premier du menu
    Et un clic sur le logo ou le nom du site me ramène à cette page

  Scénario: Accessible sans compte, même avec une session expirée
    Étant donné que je suis visiteur, ou que ma session a expiré
    Quand j'ouvre la page des actualités
    Alors les actualités s'affichent sans me demander de me connecter
```

#### US-B2 — Lire une actualité *(modifiée v6 : contenu, date, adresse mal formée)*
> En tant que **visiteur**, je veux **lire une actualité en entier** afin d'**avoir toute l'information**.

```gherkin
Fonctionnalité: Détail d'une actualité

  Scénario: Lecture d'une actualité publiée
    Quand j'ouvre une actualité publiée
    Alors je vois son titre, sa date de publication et son texte complet

  Scénario: Actualité dépubliée ou inexistante
    Étant donné qu'une actualité a été dépubliée
    Quand j'ouvre son adresse directement
    Alors une page "Contenu introuvable" s'affiche

  Scénario: Actualité en brouillon
    Étant donné une actualité en brouillon qui n'a jamais été publiée
    Quand j'ouvre son adresse directement
    Alors une page "Contenu introuvable" s'affiche

  Scénario: Adresse mal formée
    Quand j'ouvre l'adresse d'une actualité dont l'identifiant n'a pas la forme attendue (par exemple "/actualites/abc")
    Alors une page "Contenu introuvable" s'affiche
    Et aucune erreur technique ne m'est montrée

  Scénario: Le même message pour tous les cas d'absence
    Quand j'ouvre l'adresse d'une actualité en brouillon, dépubliée, supprimée, inexistante ou mal formée
    Alors la page "Contenu introuvable" est identique dans tous les cas
    Et rien ne me permet de savoir si l'actualité existe ou a existé
    Et un lien "Retour à l'accueil" me ramène à la page des actualités

  Scénario: Un administrateur n'a pas de passe-droit sur l'adresse publique
    Étant donné que je suis connecté en tant qu'administrateur
    Et une actualité en brouillon
    Quand j'ouvre son adresse publique
    Alors une page "Contenu introuvable" s'affiche
    Et je dois utiliser l'aperçu du formulaire d'administration pour la voir

  Scénario: Contenu contenant du code
    Étant donné une actualité dont le texte contient "<script>alert(1)</script><img src=x onerror=alert(1)>"
    Quand je l'ouvre
    Alors ce texte s'affiche tel quel, balises visibles
    Et aucun code n'est exécuté et aucune image n'est chargée

  Scénario: Paragraphes
    Étant donné une actualité dont le texte contient deux blocs séparés par une ligne vide
    Quand je l'ouvre
    Alors le texte est présenté en deux paragraphes

  Scénario: Ce qui n'est pas affiché
    Quand j'ouvre une actualité publiée
    Alors aucun auteur n'est affiché
    Et aucune mention "Mise à jour le" n'est affichée
    Et aucune image n'est affichée
    Et une adresse web écrite dans le texte n'est pas cliquable

  Scénario: Modification d'une actualité déjà publiée
    Étant donné qu'un administrateur modifie le texte d'une actualité publiée
    Quand j'ouvre cette actualité après l'enregistrement
    Alors je vois le nouveau texte
    Et la date de publication affichée est inchangée

  Scénario: Actualité republiée
    Étant donné une actualité publiée le 1er novembre, dépubliée, puis republiée le 20 novembre
    Quand je l'ouvre
    Alors sa date affichée est "Publiée le 1er novembre 2026"

  Scénario: Date affichée à l'heure de Tunis
    Étant donné une actualité publiée le 5 décembre 2026 à 23 h 30 UTC
    Quand je l'ouvre
    Alors sa date affichée est "Publiée le 6 décembre 2026", sans heure

  Scénario: Texte de la taille maximale
    Étant donné une actualité dont le texte fait 8 000 caractères
    Quand je l'ouvre
    Alors le texte complet est affiché, sans coupure

  Scénario: Retour à la liste
    Quand je lis une actualité
    Alors un lien "Retour aux actualités" me ramène à la liste
```

### Épique C — Événements

#### US-C1 — Consulter les événements à venir *(modifiée : prix, places restantes ; v4 : événement en cours, 20 par page, format du prix)*
> En tant que **visiteur**, je veux **voir la liste des événements à venir** afin de **choisir ceux qui m'intéressent**.

```gherkin
Fonctionnalité: Liste des événements

  Scénario: Affichage des événements à venir
    Étant donné des événements publiés à venir, un événement passé et un événement en brouillon
    Quand j'ouvre la page des événements
    Alors je vois uniquement les événements publiés à venir, du plus proche au plus lointain
    Et chacun affiche son titre, sa date, son heure, son lieu, son prix et le nombre de places restantes

  Scénario: Événement en cours
    Étant donné un événement publié qui a commencé il y a 1 heure et qui se termine dans 2 heures
    Quand j'ouvre la page des événements
    Alors l'événement est encore dans la liste
    Et il est affiché avec la mention "Réservations closes"
    Et il n'est pas réservable

  Scénario: Événement terminé
    Étant donné un événement publié dont l'heure de fin est dépassée
    Quand j'ouvre la page des événements
    Alors l'événement n'est plus dans la liste

  Scénario: Places restantes comptant les demandes en attente
    Étant donné un événement de 20 places avec 8 réservations confirmées, 4 en attente et 3 refusées ou expirées
    Quand j'ouvre la page des événements
    Alors l'événement affiche "8 places restantes"

  Scénario: Événement gratuit ou payant
    Étant donné un événement sans prix et un événement à 15 TND
    Quand j'ouvre la page des événements
    Alors le premier affiche "Gratuit"
    Et le second affiche "15 TND, à régler sur place"

  Scénario: Format du prix
    Étant donné un événement à 15 TND et un événement à 12,5 TND
    Quand j'ouvre la page des événements
    Alors le premier affiche "15 TND, à régler sur place" (sans décimales)
    Et le second affiche "12,500 TND, à régler sur place" (3 décimales)

  Scénario: Événement complet
    Étant donné un événement de 20 places avec 15 réservations confirmées et 5 en attente
    Quand j'ouvre la page des événements
    Alors l'événement est affiché avec la mention "Complet"

  Scénario: Événement annulé
    Étant donné un événement à venir annulé par l'association
    Quand j'ouvre la page des événements
    Alors l'événement est affiché avec la mention "Annulé"
    Et il n'est pas réservable

  Scénario: Plus de 20 événements
    Étant donné que 45 événements publiés sont à venir
    Quand j'ouvre la page des événements
    Alors je vois les 20 premiers, du plus proche au plus lointain
    Et je peux passer à la page suivante

  Scénario: Aucun événement à venir
    Quand aucun événement publié n'est à venir
    Alors un message indique "Aucun événement prévu pour le moment"
```

#### US-C2 — Voir le détail d'un événement *(modifiée : prix, statut de ma demande ; v4 : événement terminé ; v5 : dernière demande finale, événement non réservable)*
> En tant que **visiteur**, je veux **voir le détail d'un événement** afin de **savoir s'il me convient avant de demander une place**.

```gherkin
Fonctionnalité: Détail d'un événement

  Scénario: Visiteur non connecté
    Quand j'ouvre un événement réservable sans être connecté
    Alors je vois sa description, sa date, son heure de début et de fin, son lieu, son prix et les places restantes
    Et le bouton "Demander une place" m'invite à me connecter ou à créer un compte

  Scénario: Utilisateur ayant une demande en attente
    Étant donné que je suis connecté et que j'ai une demande en attente pour cet événement
    Quand j'ouvre l'événement
    Alors je vois "Demande en attente de confirmation" avec la date limite de traitement
    Et je vois le numéro de l'administrateur à contacter
    Et je peux annuler ma demande

  Scénario: Utilisateur ayant une réservation confirmée
    Étant donné que je suis connecté et que ma réservation est confirmée
    Quand j'ouvre l'événement
    Alors je vois "Réservation confirmée"
    Et je peux annuler si le délai d'annulation n'est pas dépassé

  Scénario: Dernière demande refusée, expirée ou annulée, événement réservable
    Étant donné que je suis connecté et que ma dernière demande pour cet événement est refusée (avec le motif "Réservé aux adhérents"), expirée ou annulée
    Et que l'événement est réservable
    Quand j'ouvre l'événement
    Alors je vois une information sur ma demande précédente ("Votre demande précédente a été refusée." suivie du motif s'il y en a un, ou "Votre demande précédente a expiré sans être confirmée.", ou "Vous avez annulé votre demande précédente.")
    Et le bouton "Demander une place" est proposé

  Scénario: Dernière demande finale, événement non réservable
    Étant donné que je suis connecté et que ma dernière demande est refusée ou expirée
    Et que l'événement est complet, déjà commencé ou annulé
    Quand j'ouvre l'événement
    Alors je vois l'information sur ma demande précédente
    Et aucun bouton "Demander une place" n'est proposé
    Et la raison est indiquée ("Cet événement est complet.", "Les réservations sont closes." ou "Cet événement a été annulé.")

  Scénario: Événement terminé (historique conservé)
    Étant donné un événement publié dont l'heure de fin est dépassée
    Quand j'ouvre son adresse directement (par exemple depuis "Mes réservations")
    Alors je vois son détail
    Et il est indiqué que l'événement est terminé
    Et aucune demande de place n'est possible

  Scénario: Événement non publié ou inexistant
    Quand j'ouvre l'adresse d'un événement en brouillon ou inexistant
    Alors une page "Contenu introuvable" s'affiche
```

### Épique D — Réservations

#### US-D1 — Demander une place *(modifiée : réservation en deux temps ; v5 : alignée sur le plan des réservations)*
> En tant qu'**utilisateur**, je veux **demander une place pour un événement** afin que **l'association me la réserve**.

```gherkin
Fonctionnalité: Demande de réservation

  Contexte:
    Étant donné que je suis connecté en tant qu'utilisateur

  Scénario: Demande réussie
    Étant donné un événement réservable de 20 places avec 5 réservations actives
    Quand je demande une place le 10 mars à 10 h
    Alors ma réservation est créée avec le statut "En attente"
    Et il reste 14 places
    Et un message m'indique que ma demande doit être confirmée par l'association avant le 12 mars à 10 h
    Et le numéro de l'administrateur à contacter m'est affiché

  Scénario: Événement complet
    Étant donné un événement de 20 places avec 20 réservations actives (en attente ou confirmées)
    Quand je tente de demander une place
    Alors la demande est refusée avec le message "Événement complet"
    Et aucune réservation n'est créée

  Scénario: Dernière place demandée par deux personnes en même temps
    Étant donné un événement avec 1 seule place restante
    Quand deux utilisateurs demandent une place au même instant
    Alors une seule demande est créée
    Et l'autre utilisateur reçoit le message "Événement complet"
    Et le nombre de réservations actives ne dépasse jamais la capacité

  Scénario: Une demande dont l'échéance est dépassée ne bloque pas la dernière place
    Étant donné un événement avec 1 place, occupée par une demande en attente dont l'échéance est dépassée (même si la mise à jour automatique n'a pas encore eu lieu)
    Quand je demande une place
    Alors ma demande est acceptée avec le statut "En attente"

  Scénario: Déjà une demande ou une réservation active
    Étant donné que j'ai une réservation en attente ou confirmée pour cet événement
    Quand je tente de demander une place à nouveau
    Alors la demande est refusée avec le message "Vous avez déjà une demande en cours ou une réservation pour cet événement"
    Et aucune seconde réservation n'est créée

  Scénario: Double clic sur "Demander une place"
    Quand je clique deux fois de suite, très vite, sur "Demander une place"
    Alors une seule demande est créée
    Et aucun message d'erreur ne s'affiche
    Et l'écran montre ma demande en attente

  Scénario: Nouvelle demande après un refus, une expiration ou une annulation
    Étant donné que ma précédente réservation pour un événement réservable est refusée, expirée ou annulée
    Quand je demande une place à nouveau
    Alors une nouvelle réservation "En attente" est créée

  Scénario: Nouvelle demande après plusieurs refus
    Étant donné que l'administrateur a déjà refusé deux de mes demandes pour cet événement réservable
    Quand je demande une place une troisième fois
    Alors une nouvelle réservation "En attente" est créée
    Et aucune limite du nombre de demandes ne m'est opposée

  Scénario: Événement déjà commencé ou passé
    Étant donné un événement dont l'heure de début est dépassée
    Quand je tente de demander une place
    Alors la demande est refusée avec le message "Les réservations sont closes"

  Scénario: Événement en brouillon ou inexistant
    Quand je tente de demander une place pour un événement en brouillon ou inexistant
    Alors la demande est refusée comme pour un contenu introuvable
    Et aucune place n'est occupée

  Scénario: Événement annulé
    Quand je tente de demander une place pour un événement annulé
    Alors la demande est refusée avec le message "Cet événement a été annulé."
    Et aucune place n'est occupée

  Scénario: Un administrateur demande une place pour lui-même
    Étant donné que je suis connecté en tant qu'administrateur
    Et un événement réservable
    Quand je demande une place
    Alors ma réservation est créée avec le statut "En attente", comme pour un utilisateur
    Et elle apparaît dans la liste des demandes à traiter comme les autres
```

#### US-D2 — Suivre mes réservations *(modifiée : nouveaux statuts ; v5 : historique, événement indisponible)*
> En tant qu'**utilisateur**, je veux **voir mes demandes et réservations avec leur statut** afin de **savoir où j'en suis**.

```gherkin
Fonctionnalité: Mes réservations

  Scénario: Liste de mes réservations
    Étant donné que j'ai 1 demande en attente, 1 réservation confirmée à venir, 1 passée et 1 refusée
    Quand j'ouvre "Mes réservations"
    Alors je vois d'abord la section "À venir" avec les réservations en attente et confirmées, de la plus proche à la plus lointaine
    Et je vois ensuite la section "Historique" avec les réservations passées, refusées, expirées et annulées
    Et chaque ligne affiche son statut en clair ("En attente de confirmation", "Confirmée", "Refusée", "Expirée", "Annulée par vous", "Annulée par l'association")

  Scénario: Réservation confirmée d'un événement terminé
    Étant donné une réservation confirmée pour un événement dont l'heure de fin est dépassée
    Quand j'ouvre "Mes réservations"
    Alors elle apparaît dans la section "Historique" avec le statut "Confirmée" et la mention "Terminé"

  Scénario: Demande en attente
    Quand j'ouvre une demande en attente dans "Mes réservations"
    Alors je vois sa date limite de traitement et le numéro de l'administrateur à contacter

  Scénario: Demande dont l'échéance est dépassée
    Étant donné une demande en attente dont l'échéance est dépassée, même si la mise à jour automatique n'a pas encore eu lieu
    Quand j'ouvre "Mes réservations"
    Alors elle apparaît "Expirée" dans la section "Historique"

  Scénario: Demande refusée avec un motif
    Étant donné que l'admin a refusé ma demande avec le motif "Réservé aux adhérents"
    Quand j'ouvre "Mes réservations"
    Alors ma réservation apparaît "Refusée" avec ce motif

  Scénario: Événement payant
    Étant donné une réservation confirmée pour un événement à 15 TND
    Quand j'ouvre "Mes réservations"
    Alors je vois "15 TND, à régler sur place" ou "Payée" selon ce que l'admin a enregistré

  Scénario: Événement annulé par l'association
    Étant donné qu'un événement auquel j'étais inscrit a été annulé
    Quand j'ouvre "Mes réservations"
    Alors ma réservation apparaît avec le statut "Annulée par l'association"

  Scénario: Événement repassé en brouillon
    Étant donné une réservation finale (refusée, expirée ou annulée) pour un événement que l'admin a remis en brouillon
    Quand j'ouvre "Mes réservations"
    Alors la réservation reste dans mon historique
    Et le titre de l'événement n'est pas un lien et indique "Événement indisponible"

  Scénario: Je ne vois que mes propres réservations
    Étant donné qu'un autre utilisateur a des réservations
    Quand j'ouvre "Mes réservations"
    Alors je ne vois aucune réservation d'un autre utilisateur

  Scénario: Aucune réservation
    Quand je n'ai aucune réservation
    Alors un message m'invite à consulter les événements à venir
```

#### US-D3 — Annuler ma demande ou ma réservation *(modifiée ; v5 : début actuel, statut changé entre-temps)*
> En tant qu'**utilisateur**, je veux **annuler ma demande ou ma réservation** afin de **libérer la place si je ne peux pas venir**.

```gherkin
Fonctionnalité: Annulation par l'utilisateur

  Scénario: Annuler une demande en attente
    Étant donné que j'ai une demande en attente pour un événement qui commence dans 5 heures
    Quand j'annule ma demande
    Alors son statut devient "Annulée par l'utilisateur"
    Et la place est de nouveau disponible

  Scénario: Annuler une réservation confirmée dans le délai
    Étant donné que j'ai une réservation confirmée pour un événement dans 3 jours
    Quand j'annule ma réservation
    Alors son statut devient "Annulée par l'utilisateur"
    Et la place est de nouveau disponible

  Scénario: Délai d'annulation dépassé pour une réservation confirmée
    Étant donné que j'ai une réservation confirmée pour un événement qui commence dans 10 heures
    Quand je tente d'annuler
    Alors l'annulation est refusée avec le message "Le délai d'annulation est dépassé : contactez l'association"
    Et le numéro de l'administrateur m'est affiché pour le prévenir

  Scénario: Limite exacte du délai
    Étant donné une réservation confirmée pour un événement qui commence dans exactement 24 heures
    Quand j'annule ma réservation
    Alors l'annulation est acceptée

  Scénario: Délai compté depuis le début actuel après un changement de date
    Étant donné une réservation confirmée pour un événement prévu dans 3 jours
    Et que l'admin avance l'événement pour qu'il commence dans 10 heures
    Quand je tente d'annuler
    Alors l'annulation est refusée car le délai de 24 heures se compte depuis le nouveau début
    Et si l'admin repousse ensuite l'événement à dans 3 jours, l'annulation redevient possible

  Scénario: Réservation déjà payée
    Étant donné une réservation confirmée et marquée "Payée"
    Quand je l'annule dans le délai
    Alors l'annulation est acceptée
    Et un message m'indique que le remboursement éventuel se règle directement avec l'association

  Scénario: Statut final
    Étant donné que ma réservation est refusée, expirée ou déjà annulée
    Quand je tente de l'annuler
    Alors rien ne change et aucune place n'est libérée une deuxième fois
    Et un message m'indique que la demande a déjà été traitée ou annulée, et l'écran affiche son statut actuel

  Scénario: Demande devenue expirée ou traitée entre-temps
    Étant donné que j'ai ouvert l'écran avec une demande en attente
    Et que, pendant ce temps, elle a expiré ou a été confirmée ou refusée par l'admin
    Quand je clique sur "Annuler ma demande"
    Alors l'annulation n'est pas appliquée à la place du nouveau statut
    Et un message m'indique que la demande a déjà été traitée ou annulée
    Et l'écran affiche le statut actuel

  Scénario: Réservation d'un autre utilisateur
    Quand je tente d'annuler la réservation d'un autre utilisateur
    Alors l'action est refusée comme pour un contenu introuvable (je ne sais pas si elle existe)
```

#### US-D4 — Voir le numéro de l'administrateur à contacter *(nouvelle ; v5 : lu à chaque affichage)*
> En tant qu'**utilisateur ayant fait une demande**, je veux **voir le numéro de téléphone ou WhatsApp de l'administrateur** afin de **le contacter pour faire confirmer ma place**.

```gherkin
Fonctionnalité: Contact de l'administrateur

  Scénario: Affichage après une demande
    Étant donné que le numéro de contact configuré est "+216 20 123 456", joignable par téléphone et WhatsApp
    Quand je viens de demander une place
    Alors je vois le numéro "+216 20 123 456"
    Et je peux appeler ce numéro ou ouvrir une conversation WhatsApp en un geste depuis mon téléphone

  Scénario: Numéro affiché sur le détail de l'événement et dans "Mes réservations"
    Étant donné que j'ai une demande en attente
    Quand j'ouvre le détail de l'événement ou "Mes réservations"
    Alors le numéro de contact est affiché dans les deux écrans
    Et c'est toujours le numéro configuré à cet instant (jamais une copie ancienne)

  Scénario: Numéro uniquement téléphonique
    Étant donné que le numéro de contact est configuré sans WhatsApp
    Quand je consulte ma demande en attente
    Alors seul le lien d'appel est proposé

  Scénario: Numéro réservé aux utilisateurs concernés
    Étant donné que je suis visiteur non connecté
    Quand je consulte un événement
    Alors le numéro de l'administrateur ne m'est pas affiché

  Scénario: Numéro retiré par l'administrateur
    Étant donné qu'aucun numéro de contact n'est configuré (jamais enregistré ou retiré)
    Quand je consulte ma demande en attente
    Alors aucun numéro ni lien d'appel n'est affiché
    Et un message m'indique que l'association n'a pas communiqué de numéro pour le moment
```

#### US-D5 — Expiration automatique des demandes *(nouvelle ; v5 : recalcul de l'échéance, statut juste avant la mise à jour)*
> En tant qu'**utilisateur qui cherche une place**, je veux **que les demandes non traitées expirent** afin que **les places ne restent pas bloquées indéfiniment**.

```gherkin
Fonctionnalité: Expiration des demandes

  Scénario: Expiration après 48 heures
    Étant donné une demande en attente créée le 10 mars à 10 h pour un événement le 20 mars
    Et que l'admin ne l'a pas traitée
    Quand on arrive au 12 mars à 10 h
    Alors la demande passe au statut "Expirée"
    Et la place est de nouveau disponible

  Scénario: Échéance ramenée au début de l'événement
    Étant donné une demande en attente créée le 10 mars à 10 h pour un événement qui commence le 11 mars à 9 h
    Quand on arrive au 11 mars à 9 h sans validation
    Alors la demande passe au statut "Expirée"

  Scénario: Confirmation juste avant l'échéance
    Étant donné une demande en attente dont l'échéance est le 12 mars à 10 h
    Quand l'admin la confirme le 12 mars à 9 h 59
    Alors elle est "Confirmée" et n'expire pas

  Scénario: Une demande expirée ne se confirme plus
    Étant donné une demande expirée
    Quand l'admin tente de la confirmer
    Alors l'action est refusée
    Et un message indique que l'utilisateur doit faire une nouvelle demande

  Scénario: Les places affichées sont toujours justes
    Étant donné une demande dont l'échéance est dépassée
    Quand un visiteur consulte l'événement
    Alors cette demande n'est plus comptée dans les places occupées

  Scénario: Échéance ramenée quand l'admin avance la date de l'événement
    Étant donné une demande en attente créée le 10 mars à 10 h (échéance le 12 mars à 10 h) pour un événement le 20 mars
    Quand l'admin avance l'événement au 11 mars à 9 h
    Alors l'échéance de la demande devient le 11 mars à 9 h

  Scénario: Échéance allongée quand l'admin repousse la date, sans dépasser 48 h après la demande
    Étant donné une demande en attente créée le 10 mars à 10 h, dont l'échéance est le 11 mars à 9 h parce que l'événement commence à ce moment-là
    Quand l'admin repousse l'événement au 20 mars
    Alors l'échéance devient le 12 mars à 10 h (48 h après la demande, jamais plus)

  Scénario: Une demande déjà échue n'est pas ranimée par un changement de date
    Étant donné une demande dont l'échéance est dépassée
    Quand l'admin repousse la date de l'événement
    Alors la demande reste "Expirée"
    Et l'utilisateur doit faire une nouvelle demande

  Scénario: Redemander sans attendre la mise à jour automatique
    Étant donné que ma demande en attente est échue mais pas encore marquée "Expirée" en base
    Quand je demande une place à nouveau
    Alors ma nouvelle demande est acceptée si une place est libre
    Et mon ancienne demande apparaît "Expirée"
```

### Épique E — Administration

Toutes les actions de cette épique sont **réservées aux administrateurs**. Scénario commun :

```gherkin
  Scénario: Accès refusé à un non-administrateur
    Étant donné que je suis visiteur ou simple utilisateur
    Quand je tente d'accéder à une fonction d'administration
    Alors l'accès est refusé
    Et aucune donnée n'est modifiée
```

#### US-E1 — Gérer les actualités *(modifiée v6 : limites, aperçu, date conservée, suppression d'une publiée, conflits)*
> En tant qu'**administrateur**, je veux **créer, modifier, publier, dépublier et supprimer une actualité** afin de **tenir le public informé**.

```gherkin
Fonctionnalité: Gestion des actualités

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Création en brouillon
    Quand je crée une actualité avec un titre et un texte
    Alors elle est enregistrée en "Brouillon"
    Et elle n'est pas visible des visiteurs
    Et je reste sur sa fiche, prête à être modifiée ou publiée

  Scénario: Publication
    Étant donné une actualité en brouillon
    Quand je la publie
    Alors elle devient visible des visiteurs
    Et sa date de publication est la date du jour

  Scénario: Modification d'une actualité publiée
    Quand je modifie le texte d'une actualité publiée
    Alors la nouvelle version est visible immédiatement
    Et sa date de publication d'origine est conservée

  Scénario: Modification d'un brouillon
    Étant donné une actualité en brouillon
    Quand je modifie son titre et son texte
    Alors les modifications sont enregistrées
    Et elle reste en "Brouillon", sans date de publication

  Scénario: Dépublication
    Quand je dépublie une actualité
    Alors elle repasse en "Brouillon" et n'est plus visible des visiteurs

  Scénario: Republication
    Étant donné une actualité publiée le 1er novembre puis dépubliée
    Quand je la publie de nouveau le 20 novembre
    Alors elle redevient visible des visiteurs
    Et sa date de publication reste le 1er novembre
    Et elle n'est pas remontée en tête de la liste publique

  Scénario: Suppression
    Quand je supprime une actualité après confirmation
    Alors elle disparaît définitivement

  Scénario: Suppression d'un brouillon
    Étant donné une actualité en brouillon
    Quand je la supprime après confirmation
    Alors elle disparaît définitivement

  Scénario: Suppression directe d'une actualité publiée
    Étant donné une actualité publiée
    Quand je clique sur "Supprimer"
    Alors une confirmation me rappelle qu'elle est définitive et qu'elle n'est plus visible du public dès maintenant
    Et après confirmation, elle disparaît de la liste publique et de la liste d'administration, sans avoir à la dépublier d'abord
    Et son adresse publique affiche "Contenu introuvable"

  Scénario: Suppression sans confirmation
    Quand je clique sur "Supprimer" puis sur "Annuler" dans la confirmation
    Alors l'actualité n'est pas supprimée

  Scénario: Suppression d'une actualité déjà supprimée par un autre administrateur
    Étant donné une actualité que l'administrateur Y vient de supprimer, alors que ma page est restée ouverte
    Quand je la supprime à mon tour
    Alors un message m'indique "Cette actualité a déjà été supprimée."
    Et je suis ramené à la liste

  Scénario: Champs obligatoires
    Quand j'enregistre une actualité sans titre ou sans texte
    Alors elle n'est pas enregistrée et le champ manquant est signalé

  Scénario: Texte ne contenant que des espaces
    Quand j'enregistre une actualité dont le texte ne contient que des espaces ou des sauts de ligne
    Alors elle n'est pas enregistrée et le texte est signalé comme obligatoire

  Plan du Scénario: Limites de saisie
    Quand j'enregistre une actualité avec <champ> égal à <valeur>
    Alors la saisie est <résultat>

    Exemples:
      | champ | valeur                                            | résultat                              |
      | titre | 200 caractères                                    | acceptée                              |
      | titre | 201 caractères                                    | refusée (200 caractères au plus)      |
      | texte | 8 000 caractères                                  | acceptée                              |
      | texte | 8 001 caractères                                  | refusée (8 000 caractères au plus)    |
      | texte | 8 000 caractères dont des sauts de ligne          | acceptée (un saut de ligne compte pour 1 caractère) |
      | texte | 8 000 caractères entourés d'espaces               | acceptée (les espaces de début et de fin ne comptent pas) |

  Scénario: Compteur de caractères
    Quand je saisis le texte d'une actualité
    Alors un compteur m'indique le nombre de caractères saisis sur 8 000

  Scénario: Contenu contenant du code
    Quand j'enregistre une actualité dont le texte contient "<script>alert(1)</script>"
    Alors elle est enregistrée telle quelle, sans modification
    Et l'aperçu et la page publique affichent ces caractères comme du texte, sans rien exécuter

  Scénario: Aperçu
    Étant donné que je rédige une actualité dans le formulaire
    Quand je passe en mode "Aperçu"
    Alors je vois le rendu public : titre, date et texte en paragraphes
    Et l'aperçu suit ce que je tape, sans enregistrer
    Et, tant que l'actualité n'a jamais été publiée, la mention "Brouillon, non publié" remplace la date

  Scénario: Liste d'administration
    Étant donné 3 actualités dont 2 publiées et 1 brouillon, modifiées à des moments différents
    Quand j'ouvre la liste d'administration
    Alors je vois les 3 actualités, la dernière modifiée en premier
    Et chaque ligne affiche son titre, son statut ("Brouillon" ou "Publiée"), sa date de première publication ou "—", et sa dernière modification

  Scénario: Filtre par statut
    Étant donné des actualités en brouillon et des actualités publiées
    Quand je choisis le filtre "Brouillons" puis "Publiées" puis "Tous"
    Alors la liste ne montre que les actualités du statut choisi, ou toutes avec "Tous"

  Scénario: Plus de 20 actualités en administration
    Étant donné 45 actualités
    Quand j'ouvre la liste d'administration
    Alors je vois les 20 dernières modifiées
    Et je peux passer à la page suivante

  Scénario: Aucune actualité pour le filtre choisi
    Étant donné qu'aucune actualité n'est en brouillon
    Quand je choisis le filtre "Brouillons"
    Alors un message indique "Aucune actualité ne correspond à ce filtre."

  Scénario: Enregistrer avant de publier
    Étant donné que j'ai modifié le formulaire sans enregistrer
    Alors les boutons "Publier", "Dépublier" et "Supprimer" sont désactivés
    Et une aide m'invite à enregistrer d'abord mes modifications

  Scénario: Quitter un formulaire modifié
    Étant donné que j'ai modifié le formulaire sans enregistrer
    Quand je quitte la page
    Alors une confirmation "Quitter sans enregistrer ?" me laisse rester ou quitter

  Scénario: Double clic sur un bouton
    Quand je clique deux fois de suite, très vite, sur "Publier" (ou "Enregistrer", "Supprimer")
    Alors l'action n'est faite qu'une fois
    Et aucun message d'erreur ne s'affiche

  Scénario: Conflit de modification entre deux administrateurs
    Étant donné qu'une actualité est ouverte chez deux administrateurs X et Y
    Et que X enregistre une modification
    Quand Y enregistre à son tour sa modification sans avoir rechargé
    Alors l'enregistrement de Y est refusé
    Et un message explique que l'actualité vient d'être modifiée par un autre administrateur, qu'il faut recharger la page et que ses modifications seront perdues
    Et Y peut fermer le message pour recopier sa saisie
    Et la version de X n'est pas écrasée

  Scénario: Enregistrements simultanés
    Quand deux administrateurs enregistrent en même temps une modification de la même actualité, à partir de la même version
    Alors une seule modification est enregistrée
    Et l'autre administrateur reçoit le message de conflit

  Scénario: Publier une actualité déjà publiée
    Étant donné une actualité que l'administrateur Y vient de publier, alors que ma page affiche encore un brouillon
    Quand je clique sur "Publier"
    Alors un message m'indique que l'actualité est déjà publiée
    Et la fiche affiche son état actuel
    Et sa date de publication n'est pas modifiée

  Scénario: Dépublier une actualité qui n'est pas publiée
    Étant donné une actualité que l'administrateur Y vient de dépublier, alors que ma page l'affiche encore publiée
    Quand je clique sur "Dépublier"
    Alors un message m'indique que l'actualité n'est pas publiée
    Et la fiche affiche son état actuel

  Scénario: Actualité introuvable en administration
    Quand j'ouvre l'adresse d'administration d'une actualité qui n'existe pas
    Alors un message indique "Actualité introuvable." avec un lien de retour à la liste

  Scénario: Accès refusé aux actualités d'administration
    Étant donné que je suis visiteur ou simple utilisateur
    Quand j'ouvre une page ou une action d'administration des actualités (liste, fiche, création, publication, suppression)
    Alors l'accès est refusé (connexion demandée pour un visiteur, "Accès réservé" pour un utilisateur)
    Et aucune actualité n'est modifiée
    Et un brouillon ne m'est jamais montré
```

#### US-E2 — Gérer les événements *(modifiée : prix, capacité ; v4 : événement commencé, limites de saisie, lieu, suppression d'un brouillon ; v5 : recalcul des échéances)*
> En tant qu'**administrateur**, je veux **créer, modifier, publier, dépublier et supprimer un brouillon d'événement** afin de **proposer des activités aux membres**.

```gherkin
Fonctionnalité: Gestion des événements

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Création en brouillon
    Quand je crée un événement avec titre, description, lieu, date et heure de début, heure de fin et capacité de 30 places
    Alors il est enregistré en "Brouillon" et n'est pas visible des visiteurs

  Scénario: Événement payant
    Quand je crée un événement avec un prix de 12,500 TND
    Alors le prix est enregistré et affiché "12,500 TND, à régler sur place"

  Scénario: Événement gratuit
    Quand je crée un événement sans prix ou avec un prix de 0
    Alors l'événement est affiché "Gratuit"

  Scénario: Publication
    Quand je publie un événement en brouillon
    Alors il apparaît dans la liste des événements à venir et devient réservable

  Scénario: Données invalides
    Quand j'enregistre un événement avec une capacité de 0, un prix négatif, une date de début dans le passé ou une fin avant le début
    Alors il n'est pas enregistré et l'erreur est expliquée

  Plan du Scénario: Limites de saisie
    Quand j'enregistre un événement avec <champ> égal à <valeur>
    Alors la saisie est <résultat>

    Exemples:
      | champ       | valeur                  | résultat                                   |
      | capacité    | 1                       | acceptée                                   |
      | capacité    | 10 000                  | acceptée                                   |
      | capacité    | 10 001                  | refusée (entre 1 et 10 000 places)         |
      | prix        | 99 999,999 TND          | acceptée                                   |
      | prix        | 100 000 TND             | refusée (99 999,999 TND au plus)           |
      | prix        | 12,5001 TND             | refusée (3 décimales au plus)              |
      | titre       | 200 caractères          | acceptée                                   |
      | titre       | 201 caractères          | refusée                                    |
      | lieu        | 200 caractères          | acceptée                                   |
      | lieu        | 201 caractères          | refusée                                    |
      | description | 4 000 caractères        | acceptée                                   |
      | description | 4 001 caractères        | refusée                                    |

  Scénario: Augmenter la capacité
    Étant donné un événement de 20 places complet
    Quand je passe la capacité à 25
    Alors 5 places redeviennent disponibles

  Scénario: Réduire la capacité sous le nombre de réservations actives
    Étant donné un événement de 20 places avec 10 réservations confirmées et 5 en attente
    Quand je passe la capacité à 12
    Alors la modification est refusée
    Et un message indique que 15 places sont déjà occupées (10 confirmées, 5 en attente)

  Scénario: Réduire la capacité au nombre de réservations actives
    Étant donné un événement de 20 places avec 15 réservations actives
    Quand je passe la capacité à 15
    Alors la modification est acceptée et l'événement devient complet

  Scénario: Changer la date ou le prix d'un événement avec des inscrits
    Étant donné un événement avec des réservations actives
    Quand je modifie sa date, son heure ou son prix
    Alors un avertissement me rappelle de prévenir les inscrits
    Et après confirmation, la modification est enregistrée et les réservations sont conservées
    Et, si la date de début change, l'échéance des demandes en attente est recalculée (R30, voir US-D5)

  Scénario: Changer uniquement le lieu d'un événement avec des inscrits
    Étant donné un événement avec des réservations actives
    Quand je ne modifie que son lieu (ni date, ni heure, ni prix)
    Alors la modification est enregistrée sans avertissement
    Et les réservations sont conservées

  Scénario: Modifier un événement déjà commencé
    Étant donné un événement publié qui a commencé il y a 1 heure et qui se termine dans 2 heures
    Quand je modifie sa description
    Alors la modification est enregistrée

  Scénario: Déplacer le début d'un événement commencé dans le passé
    Étant donné un événement publié qui a déjà commencé
    Quand je change sa date de début pour une autre date passée
    Alors la modification est refusée
    Et l'erreur sur la date de début est expliquée

  Scénario: Événement terminé ou annulé
    Étant donné un événement terminé, ou un événement annulé
    Quand j'essaie de le modifier
    Alors la modification est refusée
    Et un message explique qu'un événement terminé ou annulé ne peut plus être modifié

  Scénario: Dépublier un événement sans réservation active
    Étant donné un événement publié sans réservation en attente ni confirmée
    Quand je le dépublie
    Alors il repasse en "Brouillon"

  Scénario: Dépublier un événement avec des réservations actives
    Étant donné un événement publié avec des réservations en attente ou confirmées
    Quand je tente de le dépublier
    Alors l'action est refusée
    Et on me propose d'annuler l'événement à la place

  Scénario: Supprimer un brouillon jamais publié
    Étant donné un événement en "Brouillon" qui n'a jamais été publié
    Quand je le supprime après confirmation
    Alors il disparaît définitivement

  Scénario: Brouillon qui a déjà été publié
    Étant donné un événement publié puis dépublié, revenu en "Brouillon"
    Quand je tente de le supprimer
    Alors la suppression est refusée
    Et un message explique qu'un événement déjà publié ne peut pas être supprimé
```

La suppression d'un événement **publié** n'est pas possible avant l'étape 7 ; à partir de l'étape 7, la règle R15 (aucune réservation, jamais) s'applique (voir US-E3 et Q29).

#### US-E3 — Annuler ou supprimer un événement *(modifiée : demandes en attente)*
> En tant qu'**administrateur**, je veux **annuler un événement qui n'aura pas lieu** afin que **les inscrits le sachent et que plus personne ne réserve**.

```gherkin
Fonctionnalité: Annulation et suppression d'un événement

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Annuler un événement avec des inscrits
    Étant donné un événement publié avec 10 réservations confirmées, dont 4 payées, et 2 demandes en attente
    Quand j'annule l'événement après confirmation
    Alors son statut devient "Annulé"
    Et les 12 réservations passent au statut "Annulée par l'association"
    Et l'événement reste visible avec la mention "Annulé"
    Et je peux consulter la liste des 12 personnes à prévenir avec leur téléphone
    Et les 4 réservations payées sont signalées pour un remboursement éventuel hors application

  Scénario: Supprimer un événement sans aucune réservation
    Étant donné un événement qui n'a jamais eu de réservation
    Quand je le supprime après confirmation
    Alors il disparaît définitivement

  Scénario: Supprimer un événement ayant des réservations
    Étant donné un événement qui a ou a eu des réservations, quel que soit leur statut
    Quand je tente de le supprimer
    Alors la suppression est refusée
    Et on me propose de l'annuler à la place

  Scénario: Un événement annulé ne se réactive pas
    Étant donné un événement annulé
    Quand je consulte ses actions possibles
    Alors je ne peux ni le republier ni le rendre réservable
```

Note v4 : un brouillon jamais publié est déjà supprimable depuis l'étape 2 (US-E2). Le scénario « Supprimer un événement sans aucune réservation » ci-dessus (étape 7) étend la suppression aux événements publiés qui n'ont jamais eu de réservation.

#### US-E4 — Voir les inscrits et les demandes d'un événement *(modifiée ; v5 : tri, coordonnées, compte supprimé)*
> En tant qu'**administrateur**, je veux **voir les demandes et les inscrits d'un événement** afin de **traiter les demandes, préparer l'accueil et prévenir les participants**.

```gherkin
Fonctionnalité: Liste des inscrits

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Liste par statut
    Étant donné un événement de 30 places avec 3 demandes en attente, 12 réservations confirmées et 2 refusées
    Quand j'ouvre la liste des inscrits
    Alors je vois d'abord les 3 demandes en attente, de la plus ancienne à la plus récente, avec leur échéance
    Et je vois ensuite les 12 confirmées avec la mention "Payée" ou "Non payée" si l'événement est payant
    Et chaque ligne affiche le prénom, le nom, le téléphone, l'e-mail et la date de la demande
    Et je vois le total "15 / 30 places occupées (12 confirmées, 3 en attente)"
    Et les réservations refusées, expirées et annulées sont affichées à part

  Scénario: Ordre des confirmées et des autres réservations
    Étant donné des réservations confirmées au nom de "Ben Salah Amine", "Ben Ali Zied" et "Ben Ali Amel"
    Et 3 réservations refusées, expirées ou annulées à des dates différentes
    Quand j'ouvre la liste des inscrits
    Alors les confirmées sont triées par nom puis prénom : "Ben Ali Amel", "Ben Ali Zied", "Ben Salah Amine"
    Et les autres réservations sont affichées à part, la plus récente d'abord

  Scénario: Demande dont l'échéance est dépassée
    Étant donné une demande en attente dont l'échéance est dépassée, même si la mise à jour automatique n'a pas encore eu lieu
    Quand j'ouvre la liste des inscrits
    Alors elle apparaît "Expirée" dans les autres réservations
    Et elle n'est pas comptée dans les places occupées
    Et je ne peux plus la confirmer

  Scénario: Compte supprimé
    Étant donné une réservation dont l'utilisateur a supprimé son compte
    Quand j'ouvre la liste des inscrits
    Alors la ligne affiche "Compte supprimé" sans prénom, nom, téléphone ni e-mail

  Scénario: Aucun inscrit
    Quand j'ouvre la liste des inscrits d'un événement sans réservation
    Alors un message indique "Aucune demande pour le moment"

  Scénario: Coordonnées réservées aux administrateurs
    Étant donné que je suis visiteur ou simple utilisateur
    Quand je tente d'ouvrir la liste des inscrits d'un événement
    Alors l'accès est refusé
    Et aucun nom, téléphone ni e-mail d'un autre utilisateur n'est affiché nulle part pour moi
```

La liste n'est pas paginée au MVP (P-R7) : le volume d'une petite association le permet.

#### US-E5 — Annuler la réservation d'un utilisateur *(modifiée ; v5 : statut changé entre-temps)*
> En tant qu'**administrateur**, je veux **annuler la demande ou la réservation d'un utilisateur** afin de **traiter un désistement reçu par téléphone, même après le délai**.

```gherkin
Fonctionnalité: Annulation par un administrateur

  Scénario: Annulation à tout moment avant la fin de l'événement
    Étant donné qu'un utilisateur a une réservation confirmée pour un événement qui commence dans 2 heures
    Quand j'annule sa réservation depuis la liste des inscrits
    Alors son statut devient "Annulée par l'association"
    Et la place est libérée

  Scénario: Annulation d'une demande en attente
    Étant donné une demande en attente
    Quand je l'annule depuis la liste des inscrits
    Alors son statut devient "Annulée par l'association"
    Et la place est libérée

  Scénario: Annulation d'une réservation payée
    Étant donné une réservation confirmée et marquée "Payée"
    Quand je l'annule
    Alors elle reste marquée "Payée" pour le suivi d'un remboursement hors application

  Scénario: Réservation qui a changé de statut entre-temps
    Étant donné une réservation que l'utilisateur vient d'annuler lui-même, ou qui vient d'expirer
    Quand je tente de l'annuler depuis une liste affichée avant ce changement
    Alors l'action est refusée avec le message "Cette demande a déjà été traitée ou annulée."
    Et la liste se met à jour avec le statut actuel
    Et aucune place n'est libérée une deuxième fois

  Scénario: Événement terminé
    Étant donné un événement terminé
    Quand je consulte sa liste des inscrits
    Alors je ne peux plus annuler de réservation
    Et un message indique que l'événement est terminé
```

#### US-E6 — Confirmer ou refuser une demande *(nouvelle ; v5 : motif limité, deux admins en même temps)*
> En tant qu'**administrateur**, je veux **confirmer ou refuser une demande en attente** afin de **valider les places après avoir échangé avec la personne**.

```gherkin
Fonctionnalité: Traitement des demandes

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Confirmer une demande
    Étant donné une demande en attente dont l'échéance n'est pas atteinte
    Quand je la confirme
    Alors son statut devient "Confirmée"
    Et le nombre de places restantes ne change pas
    Et l'utilisateur voit "Confirmée" dans "Mes réservations"

  Scénario: Refuser une demande avec un motif
    Étant donné une demande en attente
    Quand je la refuse avec le motif "Réservé aux adhérents"
    Alors son statut devient "Refusée"
    Et la place est libérée
    Et l'utilisateur voit le motif dans "Mes réservations"

  Scénario: Refuser sans motif
    Quand je refuse une demande sans saisir de motif
    Alors le refus est accepté

  Scénario: Motif trop long
    Quand je refuse une demande avec un motif de 501 caractères
    Alors le refus n'est pas enregistré et l'erreur est expliquée (500 caractères au plus)
    Et un motif de 500 caractères est accepté

  Scénario: Demande qui n'est plus en attente
    Étant donné une demande expirée, annulée par l'utilisateur ou déjà traitée par un autre admin
    Quand je tente de la confirmer ou de la refuser
    Alors l'action est refusée
    Et la liste se met à jour avec le statut actuel
    Et le message est "Cette demande a déjà été traitée ou annulée." (ou, si elle a expiré, "Cette demande a expiré : le membre doit faire une nouvelle demande.")

  Scénario: Deux administrateurs traitent la même demande en même temps
    Étant donné une demande en attente affichée chez deux administrateurs
    Quand l'un la confirme et l'autre la refuse au même instant
    Alors une seule des deux actions est appliquée
    Et l'autre administrateur reçoit le message "Cette demande a déjà été traitée ou annulée." avec le statut actuel

  Scénario: Événement commencé
    Étant donné un événement dont l'heure de début est dépassée
    Quand je tente de confirmer une demande en attente
    Alors l'action est refusée

  Scénario: Liste des demandes à traiter
    Quand j'ouvre l'espace d'administration
    Alors je vois le nombre total de demandes en attente, tous événements confondus
    Et celles dont l'échéance est la plus proche sont en tête
    Et chaque ligne affiche le prénom, le nom, le téléphone, l'e-mail, l'événement et l'échéance

  Scénario: Aucune demande à traiter
    Étant donné qu'aucune demande n'est en attente
    Quand j'ouvre la liste des demandes à traiter
    Alors un message indique "Aucune demande à traiter."
```

L'application **n'enregistre pas** quel administrateur a confirmé ou refusé une demande (P-R6, minimisation des données) ; seule la mention « Payée » garde l'administrateur qui l'a posée (US-E7).

#### US-E7 — Marquer une réservation comme payée *(nouvelle ; v5 : après la fin, annulée, gratuit)*
> En tant qu'**administrateur**, je veux **indiquer qu'une personne a payé en main propre** afin de **savoir qui reste à encaisser**.

```gherkin
Fonctionnalité: Suivi du paiement

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Marquer comme payée
    Étant donné une réservation confirmée pour un événement à 15 TND
    Quand je la marque "Payée"
    Alors elle affiche "Payée" avec la date et le nom de l'admin qui l'a enregistré
    Et l'utilisateur voit "Payée" dans "Mes réservations"

  Scénario: Marquer comme payée après la fin de l'événement
    Étant donné une réservation confirmée pour un événement payant dont l'heure de fin est dépassée
    Quand je la marque "Payée"
    Alors la mention est enregistrée, sans limite de date

  Scénario: Corriger une erreur
    Étant donné une réservation marquée "Payée" par erreur
    Quand je retire la mention
    Alors elle redevient "Non payée"

  Scénario: Retirer la mention sur une réservation annulée
    Étant donné une réservation annulée qui avait été marquée "Payée" par erreur
    Quand je retire la mention
    Alors elle redevient "Non payée"

  Scénario: Réservation non confirmée
    Étant donné une demande en attente
    Quand je tente de la marquer "Payée"
    Alors l'action est refusée
    Et un message m'invite à confirmer d'abord la demande ("Confirmez d'abord la demande avant de la marquer payée")

  Scénario: Réservation annulée, refusée ou expirée
    Étant donné une réservation refusée, expirée ou annulée qui n'est pas marquée "Payée"
    Quand je tente de la marquer "Payée"
    Alors l'action est refusée
    Et seule une réservation confirmée peut recevoir la mention

  Scénario: Événement gratuit
    Étant donné une réservation confirmée pour un événement gratuit
    Quand j'affiche la réservation
    Alors l'action "Marquer comme payée" n'est pas proposée
    Et si l'action est tentée quand même, elle est refusée avec le message "Cet événement est gratuit : aucun paiement à enregistrer"

  Scénario: Aucun paiement en ligne
    Quand un utilisateur consulte un événement payant
    Alors aucun paiement en ligne ne lui est proposé
    Et il est indiqué que le paiement se fait sur place
```

#### US-E8 — Configurer le numéro de contact *(nouvelle ; v4 : numéro fixe, retrait du numéro)*
> En tant qu'**administrateur**, je veux **définir ou retirer le numéro de téléphone à contacter pour les réservations** afin que **les utilisateurs joignent la bonne personne**.

```gherkin
Fonctionnalité: Numéro de contact

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Enregistrer le numéro
    Quand j'enregistre le numéro "+216 20 123 456" en indiquant qu'il est joignable sur WhatsApp
    Alors ce numéro est affiché aux utilisateurs après chaque demande

  Scénario: Numéro fixe accepté
    Quand j'enregistre le numéro "+216 71 123 456" (téléphone fixe)
    Alors il est enregistré

  Scénario: Changer de numéro
    Étant donné que le numéro de contact a été changé
    Quand un utilisateur consulte une demande en attente faite avant le changement
    Alors il voit le nouveau numéro

  Scénario: Numéro invalide
    Quand j'enregistre le numéro "abc"
    Alors il n'est pas enregistré et le format attendu est expliqué

  Scénario: Numéro international refusé
    Quand j'enregistre le numéro "+33 6 12 34 56 78"
    Alors il n'est pas enregistré et le format attendu est expliqué

  Scénario: Retirer le numéro de contact
    Étant donné qu'un numéro de contact est enregistré
    Quand je le retire après confirmation
    Alors plus aucun numéro n'est configuré
    Et les utilisateurs ne voient plus de numéro (voir US-D4)
    Et l'option WhatsApp est désactivée

  Scénario: Numéro non configuré
    Étant donné qu'aucun numéro de contact n'est enregistré (jamais enregistré ou retiré)
    Quand j'essaie de publier un événement
    Alors un avertissement m'indique que les utilisateurs ne sauront pas qui contacter
    Et la publication est quand même effectuée (l'avertissement ne bloque pas)
```

### Épique F — Protection des données (loi n° 2004-63)

Version minimale, sans analyse juridique : consentement, données minimales, suppression du compte. Autorité de contrôle : **INPDP** (Instance nationale de protection des données personnelles).

#### US-F1 — Être informé et consentir *(modifiée : version de la politique, pas de contrôle d'âge ; v5 : visibilité par les admins)*
> En tant que **visiteur**, je veux **savoir quelles données sont collectées et pourquoi** afin de **donner mon accord en connaissance de cause**.

```gherkin
Fonctionnalité: Information et consentement

  Scénario: Politique de confidentialité accessible
    Quand je suis sur n'importe quelle page
    Alors un lien "Politique de confidentialité" est accessible en pied de page
    Et elle indique les données collectées, leur usage, leur durée de conservation, le contact de l'association
    Et elle mentionne la loi organique n° 2004-63 et la possibilité de saisir l'INPDP

  Scénario: Visibilité des coordonnées par les administrateurs
    Quand je lis la politique de confidentialité
    Alors elle indique que les administrateurs voient mon nom, mon prénom, mon téléphone et mon e-mail dès que je fais une demande de place
    Et elle explique pourquoi (valider la demande, me joindre, préparer l'accueil, me prévenir d'un changement)
    Et elle précise que les autres membres et les visiteurs ne voient jamais mes données

  Scénario: Consentement enregistré
    Quand je crée un compte en acceptant la politique
    Alors la date de mon acceptation est conservée
    Et la version de la politique que j'ai acceptée est conservée avec cette date

  Scénario: Aucun contrôle d'âge
    Quand je crée un compte
    Alors aucune case ni question sur mon âge ne m'est posée
    Et le site reste accessible à tout le monde
```

La « version » de la politique est un simple repère (par exemple « 1.0 » ou sa date de publication) qui change chaque fois que le texte de la politique change. Redemander l'accord aux membres déjà inscrits quand la politique change n'est **pas** au MVP (voir Q16).

#### US-F2 — Supprimer mon compte *(modifiée : nouveaux statuts, nouvelles données)*
> En tant qu'**utilisateur**, je veux **supprimer mon compte** afin que **mes données personnelles soient effacées**.

```gherkin
Fonctionnalité: Suppression de compte

  Scénario: Suppression avec des réservations à venir
    Étant donné que je suis connecté et que j'ai 1 demande en attente et 1 réservation confirmée à venir
    Quand je supprime mon compte et que je confirme
    Alors ces 2 réservations passent à "Annulée par l'utilisateur" et les places sont libérées
    Et mes données personnelles (prénom, nom, e-mail, téléphone, dates de création et de dernière connexion, consentement) sont effacées
    Et je suis déconnecté
    Et je ne peux plus me connecter avec cet e-mail

  Scénario: Réservation à venir déjà payée
    Étant donné que j'ai une réservation confirmée et payée
    Quand je demande la suppression de mon compte
    Alors un message m'indique que le remboursement éventuel se règle directement avec l'association
    Et la suppression reste possible après confirmation

  Scénario: Historique
    Étant donné que j'avais des réservations passées, refusées ou expirées
    Quand mon compte est supprimé
    Alors ces réservations sont conservées de façon anonyme (pour les statistiques de fréquentation)

  Scénario: Réutilisation de l'e-mail
    Étant donné qu'un compte a été supprimé
    Quand une personne crée un compte avec le même e-mail
    Alors un nouveau compte vierge est créé

  Scénario: Dernier administrateur
    Étant donné que je suis le seul administrateur
    Quand je tente de supprimer mon compte
    Alors la suppression est refusée
    Et un message indique qu'il faut d'abord désigner un autre administrateur

  Scénario: Confirmation obligatoire
    Quand je demande la suppression sans confirmer
    Alors mon compte n'est pas supprimé
```

#### US-F3 — Voir mes données *(modifiée : dates et version de la politique)*
> En tant qu'**utilisateur**, je veux **voir les informations que l'association détient sur moi** afin d'**exercer mon droit d'accès**.

```gherkin
Fonctionnalité: Mon profil

  Scénario: Consultation du profil
    Quand j'ouvre "Mon compte"
    Alors je vois mon prénom, mon nom, mon e-mail, mon téléphone, la date de création du compte, la date de mon consentement et la version de la politique que j'ai acceptée
    Et j'accède à mes réservations
```

---

## 4. Règles métier retenues

| # | Règle | Valeur retenue |
|---|---|---|
| R1 | Places par réservation | **1 place par réservation**, pas d'accompagnant *(défaut)*. |
| R2 | Événement réservable | Publié, **non annulé**, **pas encore commencé**, et réservations actives (En attente + Confirmées) < capacité. |
| R3 | Clôture des demandes | À l'**heure de début** de l'événement *(défaut)*. |
| R4 | Unicité | **Une seule réservation active** (En attente ou Confirmée) par utilisateur et par événement. Après un refus, une expiration ou une annulation, on peut refaire une demande (sans limite, R33). |
| R5 | Réservation en deux temps | La demande de l'utilisateur crée une réservation **En attente** qui **occupe une place**. Seul un admin la passe en **Confirmée** ou **Refusée** (motif facultatif, 500 caractères au plus). Cycle de vie complet : § 2. |
| R6 | Expiration | Une demande En attente expire à l'échéance : **48 h après la demande, ou au début de l'événement si c'est plus tôt**. Elle passe en **Expirée** et la place est libérée. Une demande expirée ne peut plus être confirmée. Une demande dont l'échéance est passée est **traitée comme expirée partout** (statut affiché, places, redemande possible) sans attendre la mise à jour automatique. L'échéance est recalculée si la date de l'événement change (R30). |
| R7 | Places restantes | **Capacité − (En attente + Confirmées)**, visible de tous *(défaut)*. Les demandes En attente dont l'échéance est passée ne sont **pas** comptées. |
| R8 | Contact de l'admin | **Un seul numéro de contact** pour toute l'association, configuré par un admin, avec l'indication « joignable sur WhatsApp » ou non *(défaut)*. **Un admin peut aussi le retirer** (plus aucun numéro n'est alors configuré et l'option WhatsApp est désactivée). Il est affiché à l'utilisateur après sa demande, sur ses demandes en attente et quand il ne peut plus annuler lui-même. Quand aucun numéro n'est configuré, un message indique à l'utilisateur que l'association n'a pas communiqué de numéro pour le moment *(défaut)*. Il n'est pas affiché aux visiteurs. |
| R9 | Annulation par l'utilisateur | Demande **En attente** : possible jusqu'au début de l'événement. Réservation **Confirmée** : possible jusqu'à **24 h avant le début actuel** (limite comprise ; le début actuel tient compte d'un éventuel changement de date, R30) *(défaut J-1)*. La place est libérée immédiatement. |
| R10 | Annulation par l'admin | Possible à tout moment **avant la fin** de l'événement, sur une réservation En attente ou Confirmée. Statut « Annulée par l'association ». |
| R11 | Capacité | Nombre entier **de 1 à 10 000**. L'admin peut l'augmenter librement (dans cette limite). Il **ne peut pas la réduire sous le nombre de réservations actives** (En attente + Confirmées) : la modification est refusée avec un message *(défaut)*. |
| R12 | Prix | **Facultatif**, en **dinars tunisiens (TND)**, de 0 à **99 999,999 TND**, jusqu'à 3 décimales (millimes). Vide ou 0 = **Gratuit**. Affiché « X TND, à régler sur place » : **sans décimales** si le prix est entier (« 15 TND »), **avec 3 décimales** sinon (« 12,500 TND »). Modifier la date, l'heure ou le prix d'un événement qui a des inscrits est permis après un avertissement ; les réservations sont conservées et l'association prévient elle-même les inscrits. **Changer uniquement le lieu ne déclenche pas d'avertissement.** |
| R13 | Paiement | **Hors ligne uniquement**, aucun paiement en ligne. L'admin peut ajouter la mention **« Payée »** sur une réservation **Confirmée** d'un événement **payant**, **à tout moment, y compris après la fin de l'événement**. Il peut la **retirer** en cas d'erreur, **même sur une réservation annulée**. La date et l'admin qui l'a posée sont enregistrés. **Aucun remboursement n'est géré dans l'application** : la mention « Payée » reste visible sur une réservation annulée, pour le suivi. Elle ne peut pas être posée sur une réservation non confirmée (en attente, refusée, expirée, annulée) ni sur un événement gratuit. |
| R14 | Statuts d'un événement | Brouillon → Publié → (Annulé). On ne peut dépublier que s'il n'y a **aucune réservation active**. Un événement annulé est définitif, et toutes ses réservations actives passent en « Annulée par l'association ». |
| R15 | Suppression d'un événement | Seulement s'il **n'a jamais eu de réservation**, quel qu'en soit le statut ; sinon on l'annule *(défaut)*. **Dès l'étape 2**, un admin peut supprimer (après confirmation) un **brouillon qui n'a jamais été publié** ; un événement qui a été publié, même dépublié depuis, ne peut pas être supprimé avant l'étape 7. |
| R16 | Liste d'attente | Aucune au MVP *(défaut)*. |
| R17 | Notifications | Aucun e-mail ni SMS automatique au MVP : les statuts sont visibles dans l'application et le contact se fait par téléphone ou WhatsApp *(défaut)*. |
| R18 | Actualités *(précisée en v6, cahier RG-16)* | **Statuts** : Brouillon / Publiée. **Contenu** : **texte simple** ; **titre** 200 caractères au plus, **texte** 8 000 caractères au plus (espaces de début et de fin non comptés ; un saut de ligne compte pour 1), tous deux obligatoires ; **ni image, ni mise en forme, ni lien cliquable** ; le texte est **affiché tel quel** (une balise saisie s'affiche comme du texte, jamais exécutée) ; une ligne vide sépare deux paragraphes. **Date** : la date de publication est celle de la **première** publication ; elle est **conservée** à la dépublication et à la republication (l'actualité ne remonte pas en tête) et **n'est pas modifiée** par une modification ; pas de « Mise à jour le » ; affichée **sans l'heure**, au jour de Tunis (R26) ; **aucun auteur** affiché ni enregistré. **Liste publique** : seulement les publiées, **la plus récente d'abord**, à date égale dans un ordre stable, **10 par page** *(défaut)*, avec titre, date et **extrait automatique de 200 caractères au plus** (blancs et sauts de ligne ramenés à une espace, coupé sur un mot, suivi de « … » ; texte de 200 caractères ou moins : entier, sans « … »). **Visibilité** : brouillon, dépubliée, supprimée, inexistante ou adresse mal formée donnent la **même page « Contenu introuvable »**, y compris pour un admin sur l'adresse publique (il utilise l'aperçu). **Accueil** : « / » affiche les actualités. **Administration** : créer (en brouillon), modifier (brouillon ou publiée), prévisualiser, publier, dépublier, **supprimer** (depuis les deux statuts, après confirmation, définitif, sans dépublier d'abord). Liste admin : toutes les actualités, **dernière modification d'abord**, filtre « Tous / Brouillons / Publiées », 20 par page. **Deux admins** : l'enregistrement d'une modification est refusé si l'actualité a été modifiée entre-temps (conflit) ; publier une publiée ou dépublier un brouillon est refusé avec un message ; supprimer une actualité déjà supprimée donne « Cette actualité a déjà été supprimée. ». Ni image, ni épinglage, ni publication programmée au MVP *(défaut)*. |
| R19 | Affichage public des événements | La **liste** montre les événements **publiés ou annulés qui ne sont pas terminés** (un événement en cours reste listé jusqu'à sa fin, avec « Réservations closes »), avec leur prix et leurs places restantes, triés du plus proche au plus lointain, **20 par page** *(défaut)*. Pas d'archive des événements passés dans la liste au MVP *(défaut)*. Le **détail** d'un événement publié ou annulé **reste accessible par son adresse même une fois terminé** (historique conservé, utile à « Mes réservations »). Un brouillon n'est jamais visible. |
| R20 | Données d'un compte | **Prénom, nom, e-mail, téléphone, mot de passe** (conservé uniquement sous forme hachée), plus : la **date de création du compte** (affichée à l'utilisateur, US-F3) ; la **date de dernière connexion** (pour repérer les comptes inactifs depuis 3 ans, R25) ; le **consentement** (date d'acceptation et **version de la politique de confidentialité acceptée**, R24). Aucune autre donnée : en particulier ni date de naissance ni information d'âge. Le téléphone est **obligatoire** car il sert au contact avant la confirmation *(à confirmer en Q8)*. Format accepté : **uniquement « +216 » suivi de exactement 8 chiffres, numéro mobile ou fixe** (exemples : +216 20 123 456, +216 71 123 456). Refusés : saisie locale sans préfixe, préfixe « 00 », numéros internationaux. Les espaces de présentation sont tolérés à la saisie *(défaut)*. L'e-mail est unique, sans tenir compte des majuscules. La même règle de format s'applique au numéro de contact de l'association (R8, Q17). |
| R21 | Mot de passe et connexion | Au moins **8 caractères**, dont une lettre et un chiffre *(défaut)*. Blocage **15 min après 5 échecs** *(défaut)*. Pas de vérification de l'e-mail au MVP *(défaut)*. |
| R22 | Administrateurs | Pas d'inscription en tant qu'admin : le **premier admin est créé à l'installation**, les suivants sont désignés manuellement au MVP. Il doit toujours rester **au moins un admin**. |
| R23 | Suppression de compte | Immédiate et définitive. Les réservations à venir (En attente ou Confirmées) sont **annulées** et leurs places libérées. Les autres réservations sont **rendues anonymes**. |
| R24 | Protection des données | Cadre : **loi organique tunisienne n° 2004-63 du 27 juillet 2004**, autorité **INPDP**. Minimum retenu : consentement explicite à l'inscription, avec conservation de la date et de la **version de la politique acceptée** ; aucun contrôle d'âge dédié (le site est accessible à tout le monde) ; données minimales (R20) ; suppression du compte (R23) ; consultation de ses données (US-F3) ; information sur la visibilité des coordonnées par les administrateurs (R32). |
| R25 | Conservation des données | Un compte inactif depuis **3 ans** (mesuré avec la date de dernière connexion, ou à défaut la date de création) est à supprimer *(défaut ; traitement manuel ou plus tard)*. |
| R26 | Heure de référence | Toutes les dates et heures sont en **heure de Tunis (Africa/Tunis, UTC+1, sans changement d'heure)**. |
| R27 | Langue | Application **en français uniquement**. |
| R28 | Modification d'un événement | Un admin peut modifier un événement (brouillon ou publié) tant qu'il n'est **ni terminé ni annulé**, **y compris s'il a déjà commencé**. Sur un événement commencé, la date de début **ne peut pas être déplacée dans le passé** (une date de début inchangée reste acceptée). Un événement **terminé** (heure de fin dépassée) ou **annulé** ne peut plus être modifié. La date de fin d'un événement commencé ne peut pas être placée dans le passé *(défaut, voir Q28)*. |
| R29 | Limites de saisie des textes | **Titre** et **lieu** : 200 caractères au plus. **Description** : 4 000 caractères au plus. Titre, lieu et description sont obligatoires. Les limites de capacité et de prix sont en R11 et R12. (Pour une actualité : R18.) |
| R30 | Recalcul de l'échéance *(v5, QF-2)* | Quand un admin **change la date de début** d'un événement, l'échéance de chaque demande En attente **non échue** est recalculée avec la formule de R6 à partir du **nouveau début** : **ramenée** au nouveau début s'il est plus tôt ; **allongée** s'il est plus tard, **sans jamais dépasser 48 h après la demande**. Une demande **déjà échue n'est jamais ranimée**. Le délai d'annulation de 24 h (R9) se compte depuis le début **actuel**. Les réservations sont conservées (R12). |
| R31 | Administrateur et réservation *(v5, QF-1)* | Un administrateur a aussi les droits d'un utilisateur : il peut **demander une place pour lui-même**, selon le même parcours (demande, confirmation par un admin, expiration, annulation). Aucun traitement spécial : sa demande apparaît dans les demandes à traiter comme les autres. |
| R32 | Visibilité des coordonnées *(v5, QF-9)* | Les administrateurs voient **prénom, nom, téléphone et e-mail** des personnes ayant fait une demande, dans la liste des demandes à traiter et dans la liste des inscrits, pour traiter les demandes, les joindre, préparer l'accueil et prévenir en cas d'annulation. Ces données ne sont visibles ni des autres utilisateurs ni des visiteurs. Si le compte a été supprimé, la ligne affiche « Compte supprimé ». La politique de confidentialité le mentionne (version 1.1). |
| R33 | Nouvelles demandes après refus *(v5, QF-10)* | **Aucune limite** du nombre de nouvelles demandes d'un utilisateur après un refus, une expiration ou une annulation : seul le fait d'avoir déjà une réservation active (R4) bloque. L'admin peut refuser à nouveau. |

### Points à signaler à l'agent `architecte` (décisions techniques, hors de mon rôle)
- Garantir qu'on ne dépasse **jamais** la capacité quand deux personnes demandent la dernière place en même temps (US-D1).
- **Expiration automatique** (R6) : comment la déclencher (traitement périodique ou calcul au moment de la lecture). L'important est que les places affichées et les contrôles de capacité ne comptent jamais une demande dont l'échéance est passée.
- Deux admins qui traitent la même demande en même temps (US-E6).
- Durée de session et expiration du jeton, stockage sécurisé du mot de passe, blocage après échecs (R21).
- Dates stockées en UTC (consigne du projet), saisies et affichées en heure de Tunis (R26) ; format des montants en TND à 3 décimales (R12).
- Lien d'appel et lien WhatsApp à partir du numéro de contact (US-D4), et stockage de ce paramètre (US-E8).
- Anonymisation des réservations lors de la suppression d'un compte (R23).
- Création du premier administrateur à l'installation (R22).
- Stockage de `CreatedAt`, `LastLoginAt` et de la version de la politique acceptée (R20) ; mise à jour de `LastLoginAt` à la connexion (US-A2) ; règle de validation du téléphone « +216 + 8 chiffres » (R20).
- **(v4) Retrait du numéro de contact** (R8, US-E8) : prévoir l'opération « retirer » (le plan d'étape 2 n'enregistre qu'un numéro valide) et la remise de l'option WhatsApp à « non ».
- **(v4) Suppression d'un brouillon jamais publié** (R15, US-E2) : il faut pouvoir savoir qu'un événement **a déjà été publié**, même après dépublication (la date de publication est aujourd'hui remise à vide à la dépublication) ; prévoir aussi le point d'entrée de suppression.
- **(v4) Événement terminé** : le détail public doit permettre d'indiquer « terminé » (la liste publique du plan ne l'expose pas, voir le récapitulatif).
- **(v5) Codes d'erreur** : le plan des réservations (§ 7) emploie `event_cancelled` et `event_ended`, absents de la liste des codes du § 4 du plan ; les libellés correspondants sont proposés dans `libelles-ecrans.md` § 11. Le plan prévoit aussi un message côté membre pour `reservation_state_changed` qui n'existe pas dans sa liste de messages (proposition en § 11 des libellés).
- **(v5) Réservation « courante » d'un membre** : « À venir » = demande en attente ou confirmée dont l'événement **n'est pas terminé** (un événement en cours reste donc « À venir » jusqu'à sa fin) ; le plan (QF-7) ne le précise que pour les événements terminés. À confirmer avec la définition de `IsCurrentForMember`.
- **(v6) Messages de `news_state_changed`** : le plan des actualités ne donne pas le texte affiché (il dit seulement « message puis relecture de la fiche »). `libelles-ecrans.md` § 14 propose « Cette actualité est déjà publiée. » et « Cette actualité n'est pas publiée. » : à aligner avec les messages du Domain (`AlreadyPublishedMessage`, `NotPublishedMessage`) ou à remplacer par les leurs.
- **(v6) `concurrent_update` sur publier, dépublier, supprimer** : ces actions n'envoient pas de version (P-N4) mais l'API peut quand même répondre 409 `concurrent_update` ; le plan ne prévoit pas de libellé : le même dialogue que pour l'enregistrement est proposé (`libelles-ecrans.md` § 14).
- **(v6) Page au-delà de la dernière** : l'API renvoie 200 avec une liste vide (plan § 9.3) et le plan dit « même traitement que la liste des événements », mais aucun critère n'existe pour les événements : le comportement visible (message ? retour à la première page ?) reste à préciser avant les écrans.
- **(v6) Page « Contenu introuvable » commune** : le plan (§ 7.2) change le lien de `not-found` et `forbidden` en « Retour à l'accueil » ; les libellés des événements (« Retour aux événements ») et d'« Accès réservé » ont été alignés (`libelles-ecrans.md` § 3, § 4). Cela rend le lien « Retour aux événements » inutilisé.
- **(v6) Date de première publication d'un brouillon dépublié** : la liste admin affiche la date conservée même pour un brouillon (colonne « Première publication » pour ne pas laisser croire qu'il est visible) ; le plan parle seulement de « date de publication ».
- **(v6) Conflit de nom et limite du texte** : la limite de 8 000 caractères est vérifiée après suppression des espaces de début et de fin et normalisation des sauts de ligne (plan § 4) ; l'écran affiche le compteur brut : prévoir que le compteur et la validation ne se contredisent pas (le compteur peut afficher plus que la limite sans erreur à cause des espaces de fin).

---

## 5. Questions ouvertes à poser à l'association

| # | Question | Valeur par défaut en attendant |
|---|---|---|
| Q1 | Une personne peut-elle réserver **plusieurs places** (conjoint, enfants, invités) ? | Non, 1 place par compte. |
| Q2 | Jusqu'à quand peut-on **annuler** soi-même une réservation confirmée ? | Jusqu'à 24 h avant le début. |
| Q3 | Jusqu'à quand peut-on **faire une demande** ? | Jusqu'au début de l'événement. |
| Q4 | Certains événements sont-ils **réservés aux adhérents** ? | Non : ouverts à tout inscrit ; l'admin peut refuser une demande au cas par cas. |
| Q5 | Faut-il une **liste d'attente** quand c'est complet ? | Non au MVP. |
| Q6 | Faut-il des **notifications automatiques** (e-mail ou SMS) quand une demande est confirmée, refusée ou expirée ? | Non au MVP. |
| Q7 | Si un événement **change de date**, garde-t-on les inscrits ou les désinscrit-on ? | On garde les inscrits, l'association les prévient. |
| Q8 | Le **téléphone** de l'utilisateur doit-il être **obligatoire** ou facultatif ? Faut-il d'autres informations ? Des **mineurs** peuvent-ils s'inscrire ? | Téléphone obligatoire, au format +216 + 8 chiffres uniquement ; autres données : seulement les dates de création et de dernière connexion et le consentement (R20). **Mineurs : tranché par le porteur, aucun contrôle ni case dédiés, le site est accessible à tout le monde** (la mention « réservé aux majeurs » est abandonnée). |
| Q9 | Le **numéro de contact** est-il le même pour tous les événements, ou faut-il un numéro par événement ou par responsable ? | Un seul numéro pour l'association. |
| Q10 | Le **nombre de places restantes** peut-il être affiché publiquement ? | Oui. |
| Q11 | **Combien d'administrateurs**, et qui ? | 1 à 3 bénévoles désignés par le bureau. |
| Q12 | Une **déclaration à l'INPDP** est-elle nécessaire pour ce traitement ? Qui s'en charge ? | À vérifier par l'association avant la mise en ligne. |
| Q13 | Qui rédige la **politique de confidentialité**, et quel est le **contact** pour les questions sur les données ? | Un modèle simple fourni avec l'application ; contact : l'e-mail de l'association. |
| Q14 | Combien de temps **garder** les comptes inactifs et l'historique des réservations ? | 3 ans d'inactivité ; historique rendu anonyme et conservé. |
| Q15 | Les actualités ont-elles besoin d'**images** ou de pièces jointes ? Faut-il **archiver** les événements passés ? | Non au MVP (pas d'archive dans la liste ; le détail d'un événement terminé reste accessible, voir Q19). Images : confirmé « non » en v6 (QN-4 et plan des actualités). |
| Q16 | Quand la **politique de confidentialité change**, faut-il redemander l'accord aux membres déjà inscrits ? | Non au MVP : on conserve seulement la version acceptée à l'inscription ; à revoir plus tard. |
| Q17 | Le **numéro de contact de l'association** (US-E8) doit-il suivre la même règle que le téléphone des membres (+216 + 8 chiffres) ? | **Résolue (v4)** : oui, même règle ; un numéro **fixe** tunisien est accepté (« +216 » + 8 chiffres, mobile ou fixe). (Plan d'étape 2 : E2-12.) |

### Questions de l'étape 2 (issues du plan d'architecte, E2-1 à E2-13) — toutes résolues en v4

Réponses du porteur ou valeur par défaut du plan d'architecte retenue. La colonne « Réf. plan » renvoie au plan d'étape 2.

| # | Réf. plan | Question | Décision | Origine |
|---|---|---|---|---|
| Q18 | E2-1 | Un événement **en cours** (commencé, pas terminé) figure-t-il dans la liste ? | Oui, listé jusqu'à sa fin, avec « Réservations closes » (R19, US-C1). | Défaut du plan |
| Q19 | E2-2 | Le détail d'un événement **terminé** reste-t-il accessible ? | **Oui** : accessible par son adresse (historique conservé), absent de la liste (R19, US-C2). | **Porteur** (confirme le défaut) |
| Q20 | E2-3 | Format d'affichage du prix ? | Entier sans décimales (« 15 TND »), sinon 3 décimales (« 12,500 TND ») (R12, US-C1). | Défaut du plan |
| Q21 | E2-4 | Un changement de **lieu** déclenche-t-il l'avertissement aux inscrits ? | Non : seuls date, heure et prix (R12, US-E2). | Défaut du plan |
| Q22 | E2-5 | **Limites de saisie** ? | Capacité 1 à 10 000 ; prix jusqu'à 99 999,999 TND ; titre et lieu 200 caractères ; description 4 000 (R11, R12, R29, US-E2). | **Porteur** (valeurs logiques validées) |
| Q23 | E2-6 | Peut-on modifier un événement **commencé** ou **annulé** ? | **Commencé : oui** (début non déplaçable dans le passé) ; **terminé ou annulé : non** (R28, US-E2). | **Porteur** (commencé) ; défaut du plan (terminé, annulé) |
| Q24 | E2-7 | Un admin peut-il **retirer** le numéro de contact ? | **Oui** (R8, US-E8, US-D4). Le plan prévoyait « non » : voir récapitulatif. | **Porteur** (écart avec le défaut) |
| Q25 | E2-8 | L'avertissement « numéro non configuré » à la publication est-il bloquant ? | Non : la publication est effectuée avec l'avertissement (US-E8). | Défaut du plan |
| Q26 | E2-9 | Peut-on **supprimer un brouillon** créé par erreur avant l'étape 7 ? | **Oui**, uniquement s'il n'a **jamais été publié** (R15, US-E2). Le plan prévoyait « non » : voir récapitulatif. | **Porteur** (écart avec le défaut) |
| Q27 | E2-13 | Taille de page de la liste publique des événements ? | 20 (R19, US-C1). | Défaut du plan |

Les points E2-10 (type du prix `decimal(9,3)`) et E2-11 (abandon de l'objet valeur `Money`) sont des choix **techniques** : ils relèvent de l'architecte (ADR 0010) et ne figurent pas ici. E2-12 est traitée avec Q17.

### Questions de la tranche « Réservations » (plan `technique/plan-reservations.md` § 15.1, QF-1 à QF-10) — toutes résolues en v5

Le porteur a accepté la valeur par défaut du plan pour chacune.

| # | Question | Décision | Origine | Où |
|---|---|---|---|---|
| QF-1 | Un **administrateur** peut-il demander une place pour lui-même ? | **Oui**, même parcours qu'un utilisateur. | Défaut du plan, accepté | R31, US-D1 |
| QF-2 | L'**échéance** suit-elle un changement de date de l'événement ? | **Oui** : ramenée au nouveau début s'il est plus tôt ; allongée s'il est plus tard, sans dépasser 48 h après la demande ; demande échue jamais ranimée. Le délai de 24 h d'annulation se compte depuis le début **actuel**. | Défaut du plan, accepté | R30, R6, R9, US-D3, US-D5, US-E2 |
| QF-3 | Que voit un membre dont la **dernière demande** est refusée, expirée ou annulée ? | Une ligne d'information (avec le motif en cas de refus) et le bouton « Demander une place » **si l'événement est réservable**. | Défaut du plan, accepté | US-C2 |
| QF-4 | La mention « Payée » peut-elle être posée **après la fin** de l'événement ? | **Oui**, sans limite de date. | Défaut du plan, accepté | R13, US-E7 |
| QF-5 | La mention « Payée » peut-elle être **retirée sur une réservation annulée** ? | **Oui** ; elle ne peut être **posée** que sur une Confirmée d'un événement payant. | Défaut du plan, accepté | R13, US-E7 |
| QF-6 | **Ordre** de la liste des inscrits ? | Confirmées par **nom puis prénom** ; en attente de la plus ancienne à la plus récente ; autres, la plus récente d'abord. | Défaut du plan, accepté | US-E4 |
| QF-7 | Une réservation **confirmée d'un événement terminé** est-elle « passée » ? | **Oui** : section « Historique » avec le statut « Confirmée » et la mention « Terminé ». | Défaut du plan, accepté | US-D2 |
| QF-8 | **Libellés** à valider (statuts, messages d'erreur, titres des écrans) ? | Validés tels que proposés par le plan ; rédigés dans `libelles-ecrans.md` (v1.1). | Défaut du plan, accepté | `libelles-ecrans.md` |
| QF-9 | La **politique de confidentialité** doit-elle dire que les admins voient les coordonnées des inscrits ? | **Oui** ; version portée à 1.1-provisoire. | Défaut du plan, accepté | R32, US-F1, `politique-confidentialite.md` |
| QF-10 | Faut-il **limiter les redemandes** après un refus ? | **Non** (RG-03 l'autorise) ; l'admin refuse à nouveau. | Défaut du plan, accepté | R33, US-D1 |

Les décisions **P-R1 à P-R8** du plan sont acceptées : voir l'encadré « version 5 » en haut de ce document pour celles qui ont un effet fonctionnel.

### Questions de la tranche « Actualités » (plan `technique/plan-actualites.md` § 14.1, QN-1 à QN-9) — toutes résolues en v6

Le porteur a accepté la valeur par défaut du plan pour chacune.

| # | Question | Décision | Origine | Où |
|---|---|---|---|---|
| QN-1 | **Longueur maximale** du texte d'une actualité ? | **8 000 caractères** (titre : 200, comme un événement). Texte brut. | Défaut du plan, accepté | R18, US-E1 |
| QN-2 | Une actualité **dépubliée puis republiée** garde-t-elle sa date d'origine ? | **Oui** : elle ne remonte pas en tête de liste. | Défaut du plan, accepté | R18, US-B1, US-B2, US-E1 |
| QN-3 | **Extrait** de la liste : automatique ou saisi ? Quelle longueur ? | **Automatique**, 200 caractères au plus, coupé sur un mot, suivi de « … ». | Défaut du plan, accepté | R18, US-B1 |
| QN-4 | Afficher l'**auteur** ? | **Non** : aucune donnée personnelle publiée. | Défaut du plan, accepté | R18, US-B2 |
| QN-5 | Les **adresses web** du texte doivent-elles être cliquables ? | **Non** au MVP (texte brut) ; peut venir plus tard. | Défaut du plan, accepté | R18, US-B2 |
| QN-6 | Peut-on **supprimer directement** une actualité publiée ? | **Oui**, après une confirmation qui rappelle qu'elle disparaît du public. | Défaut du plan, accepté | R18, US-E1 |
| QN-7 | Afficher « **Mise à jour le …** » ? | **Non** : seule la date de publication est affichée. | Défaut du plan, accepté | R18, US-B2 |
| QN-8 | **Libellés** des écrans Actualités et place dans les menus ? | Validés tels que proposés par le plan ; « Actualités » en premier dans le menu public et dans le sous-menu Administration ; accueil « / » = actualités ; rédigés dans `libelles-ecrans.md` (v1.3). | Défaut du plan, accepté | `libelles-ecrans.md` § 2, 12, 13, 14 |
| QN-9 | **Ordre de la liste admin** ? | Dernière modification d'abord ; filtre « Tous / Brouillons / Publiées ». | Défaut du plan, accepté | R18, US-E1 |

Les décisions **P-N1 à P-N7** du plan sont acceptées : voir l'encadré « version 6 » en haut de ce document pour celles qui ont un effet fonctionnel.

Questions restées ouvertes à la suite des décisions v4 :

| # | Question | Valeur par défaut en attendant |
|---|---|---|
| Q28 | Sur un événement **déjà commencé**, peut-on placer la **date de fin dans le passé** (ce qui le rendrait immédiatement « terminé ») ? | Non : la fin doit rester dans le futur (R28). Le plan d'étape 2 ne le contrôle pas encore. |
| Q29 | À partir de l'étape 7, un **événement publié qui n'a jamais eu de réservation** peut-il être supprimé (R15 d'origine), alors que la décision v4 ne vise que le brouillon jamais publié ? | Oui : R15 d'origine s'applique à partir de l'étape 7 (US-E3), la décision v4 est une anticipation limitée aux brouillons. |

---

## 6. Priorisation

### MVP (semaines 1 et 2)

| Épique | Stories MVP |
|---|---|
| A — Comptes et authentification | US-A1, US-A2, US-A3 |
| B — Actualités | US-B1, US-B2 |
| C — Événements | US-C1, US-C2 |
| D — Réservations | US-D1, US-D2, US-D3, US-D4, US-D5 |
| E — Administration | US-E1, US-E2, US-E3, US-E4, US-E5, US-E6, US-E7, US-E8 |
| F — Protection des données | US-F1, US-F2, US-F3 |

**23 stories au MVP.** La réservation en deux temps (US-D1, D4, D5, E6) est désormais indispensable : sans elle, aucune place n'est jamais confirmée.

Si le temps manque, ces éléments peuvent passer en « plus tard » sans casser le cœur :
- US-E5 (annulation par l'admin) ;
- US-E7 (mention « Payée » : l'association peut suivre les paiements sur papier) ;
- US-F3 (voir mes données) ;
- la pagination des actualités, le motif de refus, le compteur global des demandes et le blocage après échecs de connexion.

### Plus tard
- **Assistant RAG** : questions sur les documents de l'association (semaine 4, cadrage séparé).
- Mot de passe oublié et réinitialisation, vérification de l'adresse e-mail.
- Notifications automatiques (e-mail, SMS ou WhatsApp) : confirmation, refus, expiration, rappel, annulation d'événement.
- Plusieurs places par réservation, liste d'attente.
- Gestion des administrateurs depuis l'interface (promouvoir ou retirer un admin).
- Modification du profil, export de mes données.
- Numéro de contact par événement, images dans les actualités, archive des événements passés (page dédiée dans la liste ; le détail d'un événement terminé reste déjà accessible par son adresse).
- **Actualités (v6)** : épinglage d'une actualité en tête, affichage de l'auteur, mention « Mise à jour le », adresses web cliquables et mise en forme du texte, publication programmée.
- Suivi des remboursements, récapitulatif des encaissements par événement.
- Purge automatique des comptes inactifs, statistiques de fréquentation.
- Événements réservés aux adhérents.
- Redemander l'accord quand la politique de confidentialité change (Q16).
- Délais de 48 h et de 24 h modifiables depuis l'interface (aujourd'hui en configuration, P-R4) ; pagination de la liste des inscrits (P-R7).

### Ordre de réalisation conseillé

L'idée : construire d'abord le **cœur métier testable** (la réservation, son cycle de vie et ses règles), puis l'habillage.

1. **Comptes** : US-A1, US-A2 (+ création du premier admin, R22). Tout le reste en dépend.
2. **Événements côté admin** : US-E2 (création, prix, publication, capacité, modification, suppression d'un brouillon jamais publié) et US-E8 (numéro de contact, y compris son retrait).
3. **Consultation des événements** : US-C1, US-C2.
4. **Demande de réservation** : US-D1 et US-D4. C'est le cœur : complet, doublon, dernière place.
5. **Traitement des demandes** : US-E6 (confirmer / refuser) puis US-D5 (expiration). On boucle ainsi le cycle de vie du § 2.
6. **Suivi et annulation** : US-D2, US-D3.
7. **Gestion fine côté admin** : US-E4, US-E7, US-E5, US-E3.
8. **Actualités** : US-E1, puis US-B1 et US-B2 (simple, peu de règles).
9. **Protection des données** : US-F1, US-F2, US-F3, et US-A3.

Les étapes 1 à 9 côté API correspondent à la semaine 1, avec les tests tirés des scénarios ci-dessus (le tableau des transitions du § 2 donne une liste de cas à tester). La semaine 2 reprend le même ordre pour les écrans Angular et l'espace admin.

Note v5 : le plan `technique/plan-reservations.md` regroupe ces étapes en tranches : **A** (demander, suivre, annuler : US-D1 à D4, partie membre de US-C2), **B** (traiter : US-E4 à E7, US-D5 « expirée ne se confirme plus »), **C** (expiration automatique en arrière-plan, US-D5). Les scénarios de recalcul de l'échéance (US-D5) relèvent de la tranche A.

Note v6 : la tranche « Actualités » (`technique/plan-actualites.md`) reste à l'étape 8 : **après** l'annulation d'un événement (US-E3) et les inscrits, conformément au cahier § 8 (P-N7). Elle n'a aucun lien avec les réservations. Deux moitiés : API (US-E1, US-B1, US-B2), puis écrans.
