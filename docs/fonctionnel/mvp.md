# Cadrage fonctionnel du MVP — AAAS

> Rédigé par l'expert fonctionnel. Document de référence pour l'agent `architecte` (choix techniques) et l'agent `testeur` (les scénarios Gherkin servent de base aux tests).
> Statut : **proposition, version 4**, mise à jour avec les décisions du porteur de projet (cahier des charges v1.2). Les valeurs par défaut marquées *(défaut)* sont à confirmer avec l'association (voir § 5).

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

### Correspondance du vocabulaire (cahier des charges / ce document)

| Cahier des charges | Ce document | Dans le code (indication de l'architecte) |
|---|---|---|
| Membre | Utilisateur | rôle `User` |
| Annulée par le membre | Annulée par l'utilisateur | statut `CancelledByUser` |
| Administrateur | Administrateur | rôle `Admin` |
| RG-xx (règles obligatoires, § 5.2 du cahier) | R-xx (règles détaillées, § 4 ci-dessous) | — |

Correspondance des règles : RG-01 = R2, RG-02 = R5 et R7, RG-03 = R4, RG-04 = R6, RG-05 = R9, RG-06 = R11, RG-07 = R14 et R15, RG-08 = R12 et R13, RG-09 = R22, RG-10 = R8, RG-11 = R20, RG-12 = R28, RG-13 = R11, R12 et R29, RG-14 = R19. En cas de contradiction, le cahier des charges fait foi.

---

## 1. Le besoin en une phrase

Permettre à l'association de **publier ses actualités et ses événements**, et à ses membres de **demander une place en ligne** que l'administrateur confirme après un contact téléphonique, avec un outil simple à gérer par des bénévoles.

### Rôles

| Rôle | Qui | Ce qu'il peut faire au MVP |
|---|---|---|
| **Visiteur** | Toute personne non connectée | Lire les actualités publiées, voir les événements à venir (prix, places restantes), créer un compte, se connecter. |
| **Utilisateur** | Personne ayant un compte et connectée | Tout ce que fait le visiteur + demander une place, voir le numéro de l'administrateur à contacter, suivre ses réservations, annuler, supprimer son compte. |
| **Administrateur** | Bénévole désigné par l'association | Gérer les actualités et les événements ; confirmer ou refuser les demandes ; marquer une réservation comme payée ; voir les inscrits ; annuler une réservation ou un événement ; configurer (ou retirer) le numéro de contact. |
| **Application** (traitement automatique) | — | Faire passer en « Expirée » les demandes non traitées dans le délai. |

L'**assistant RAG** (questions sur les documents de l'association) est prévu **plus tard** (semaine 4) et n'est pas cadré ici.

---

## 2. Cycle de vie d'une réservation

Une réservation est **active** quand elle est **En attente** ou **Confirmée**. Seules les réservations actives occupent une place.

**Places restantes = capacité − (nombre de réservations En attente + Confirmées).**

| Statut | Signification | Occupe une place ? | Statut final ? |
|---|---|---|---|
| **En attente** | Demande faite par l'utilisateur, pas encore traitée par l'admin | Oui | Non |
| **Confirmée** | Validée par l'admin | Oui | Non |
| **Refusée** | Refusée par l'admin | Non | Oui |
| **Expirée** | Pas traitée dans le délai | Non | Oui |
| **Annulée par l'utilisateur** | L'utilisateur s'est désisté | Non | Oui |
| **Annulée par l'association** | L'admin l'a annulée ou l'événement a été annulé | Non | Oui |

### Transitions autorisées

| De | Vers | Qui | Condition |
|---|---|---|---|
| *(aucun)* | En attente | Utilisateur | L'événement est réservable (R2) et l'utilisateur n'a pas déjà de réservation active pour cet événement. |
| En attente | Confirmée | Admin | Avant l'échéance d'expiration et avant le début de l'événement. |
| En attente | Refusée | Admin | Avant le début de l'événement. Un motif est facultatif *(défaut)*. |
| En attente | Expirée | Application | L'échéance est atteinte : **48 h après la demande, ou au début de l'événement si c'est plus tôt**. |
| En attente | Annulée par l'utilisateur | Utilisateur | Avant le début de l'événement (pas de délai). |
| En attente | Annulée par l'association | Admin / annulation de l'événement | Avant la fin de l'événement. |
| Confirmée | Annulée par l'utilisateur | Utilisateur | Jusqu'à **24 h avant le début** (limite comprise) *(défaut)*. |
| Confirmée | Annulée par l'association | Admin / annulation de l'événement | Avant la fin de l'événement. |

Toute autre transition est **interdite**. En particulier, un statut final ne change plus : une demande refusée ou expirée ne peut pas être confirmée ; l'utilisateur doit faire une nouvelle demande.

**« Payée »** n'est pas un statut. C'est une **mention** que l'admin ajoute à une réservation **Confirmée** d'un événement **payant** (règle R13). Elle reste visible si la réservation est ensuite annulée, pour que l'association puisse gérer un éventuel remboursement en dehors de l'application.

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

#### US-B1 — Consulter la liste des actualités
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

  Scénario: Aucune actualité
    Étant donné qu'aucune actualité n'est publiée
    Quand j'ouvre la page des actualités
    Alors un message indique "Aucune actualité pour le moment"
```

#### US-B2 — Lire une actualité
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

#### US-C2 — Voir le détail d'un événement *(modifiée : prix, statut de ma demande ; v4 : événement terminé)*
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

#### US-D1 — Demander une place *(modifiée : réservation en deux temps)*
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

  Scénario: Dernière place demandée par deux personnes en même temps
    Étant donné un événement avec 1 seule place restante
    Quand deux utilisateurs demandent une place au même instant
    Alors une seule demande est créée
    Et l'autre utilisateur reçoit le message "Événement complet"
    Et le nombre de réservations actives ne dépasse jamais la capacité

  Scénario: Déjà une demande ou une réservation active
    Étant donné que j'ai une réservation en attente ou confirmée pour cet événement
    Quand je tente de demander une place à nouveau
    Alors la demande est refusée avec le message "Vous avez déjà une demande en cours ou une réservation pour cet événement"

  Scénario: Nouvelle demande après un refus, une expiration ou une annulation
    Étant donné que ma précédente réservation pour un événement réservable est refusée, expirée ou annulée
    Quand je demande une place à nouveau
    Alors une nouvelle réservation "En attente" est créée

  Scénario: Événement déjà commencé ou passé
    Étant donné un événement dont l'heure de début est dépassée
    Quand je tente de demander une place
    Alors la demande est refusée avec le message "Les réservations sont closes"

  Scénario: Événement non publié ou annulé
    Quand je tente de demander une place pour un événement en brouillon ou annulé
    Alors la demande est refusée
    Et aucune place n'est occupée
```

#### US-D2 — Suivre mes réservations *(modifiée : nouveaux statuts)*
> En tant qu'**utilisateur**, je veux **voir mes demandes et réservations avec leur statut** afin de **savoir où j'en suis**.

```gherkin
Fonctionnalité: Mes réservations

  Scénario: Liste de mes réservations
    Étant donné que j'ai 1 demande en attente, 1 réservation confirmée à venir, 1 passée et 1 refusée
    Quand j'ouvre "Mes réservations"
    Alors je vois d'abord les réservations à venir (en attente et confirmées), de la plus proche à la plus lointaine
    Et je vois ensuite les réservations passées, refusées, expirées et annulées
    Et chaque ligne affiche son statut en clair

  Scénario: Demande en attente
    Quand j'ouvre une demande en attente dans "Mes réservations"
    Alors je vois sa date limite de traitement et le numéro de l'administrateur à contacter

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

  Scénario: Je ne vois que mes propres réservations
    Étant donné qu'un autre utilisateur a des réservations
    Quand j'ouvre "Mes réservations"
    Alors je ne vois aucune réservation d'un autre utilisateur

  Scénario: Aucune réservation
    Quand je n'ai aucune réservation
    Alors un message m'invite à consulter les événements à venir
```

#### US-D3 — Annuler ma demande ou ma réservation *(modifiée)*
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
    Alors l'annulation est refusée
    Et le numéro de l'administrateur m'est affiché pour le prévenir

  Scénario: Limite exacte du délai
    Étant donné une réservation confirmée pour un événement qui commence dans exactement 24 heures
    Quand j'annule ma réservation
    Alors l'annulation est acceptée

  Scénario: Réservation déjà payée
    Étant donné une réservation confirmée et marquée "Payée"
    Quand je l'annule dans le délai
    Alors l'annulation est acceptée
    Et un message m'indique que le remboursement éventuel se règle directement avec l'association

  Scénario: Statut final
    Étant donné que ma réservation est refusée, expirée ou déjà annulée
    Quand je tente de l'annuler
    Alors rien ne change et aucune place n'est libérée une deuxième fois

  Scénario: Réservation d'un autre utilisateur
    Quand je tente d'annuler la réservation d'un autre utilisateur
    Alors l'action est refusée
```

#### US-D4 — Voir le numéro de l'administrateur à contacter *(nouvelle)*
> En tant qu'**utilisateur ayant fait une demande**, je veux **voir le numéro de téléphone ou WhatsApp de l'administrateur** afin de **le contacter pour faire confirmer ma place**.

```gherkin
Fonctionnalité: Contact de l'administrateur

  Scénario: Affichage après une demande
    Étant donné que le numéro de contact configuré est "+216 20 123 456", joignable par téléphone et WhatsApp
    Quand je viens de demander une place
    Alors je vois le numéro "+216 20 123 456"
    Et je peux appeler ce numéro ou ouvrir une conversation WhatsApp en un geste depuis mon téléphone

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

#### US-D5 — Expiration automatique des demandes *(nouvelle)*
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

#### US-E1 — Gérer les actualités
> En tant qu'**administrateur**, je veux **créer, modifier, publier, dépublier et supprimer une actualité** afin de **tenir le public informé**.

```gherkin
Fonctionnalité: Gestion des actualités

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Création en brouillon
    Quand je crée une actualité avec un titre et un texte
    Alors elle est enregistrée en "Brouillon"
    Et elle n'est pas visible des visiteurs

  Scénario: Publication
    Étant donné une actualité en brouillon
    Quand je la publie
    Alors elle devient visible des visiteurs
    Et sa date de publication est la date du jour

  Scénario: Modification d'une actualité publiée
    Quand je modifie le texte d'une actualité publiée
    Alors la nouvelle version est visible immédiatement
    Et sa date de publication d'origine est conservée

  Scénario: Dépublication
    Quand je dépublie une actualité
    Alors elle repasse en "Brouillon" et n'est plus visible des visiteurs

  Scénario: Suppression
    Quand je supprime une actualité après confirmation
    Alors elle disparaît définitivement

  Scénario: Champs obligatoires
    Quand j'enregistre une actualité sans titre ou sans texte
    Alors elle n'est pas enregistrée et le champ manquant est signalé
```

#### US-E2 — Gérer les événements *(modifiée : prix, capacité ; v4 : événement commencé, limites de saisie, lieu, suppression d'un brouillon)*
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

#### US-E4 — Voir les inscrits et les demandes d'un événement *(modifiée)*
> En tant qu'**administrateur**, je veux **voir les demandes et les inscrits d'un événement** afin de **traiter les demandes, préparer l'accueil et prévenir les participants**.

```gherkin
Fonctionnalité: Liste des inscrits

  Scénario: Liste par statut
    Étant donné un événement de 30 places avec 3 demandes en attente, 12 réservations confirmées et 2 refusées
    Quand j'ouvre la liste des inscrits
    Alors je vois d'abord les 3 demandes en attente, de la plus ancienne à la plus récente, avec leur échéance
    Et je vois ensuite les 12 confirmées avec la mention "Payée" ou "Non payée" si l'événement est payant
    Et chaque ligne affiche le prénom, le nom, le téléphone, l'e-mail et la date de la demande
    Et je vois le total "15 / 30 places occupées (12 confirmées, 3 en attente)"
    Et les réservations refusées, expirées et annulées sont affichées à part

  Scénario: Aucun inscrit
    Quand j'ouvre la liste des inscrits d'un événement sans réservation
    Alors un message indique "Aucune demande pour le moment"
```

#### US-E5 — Annuler la réservation d'un utilisateur *(modifiée)*
> En tant qu'**administrateur**, je veux **annuler la demande ou la réservation d'un utilisateur** afin de **traiter un désistement reçu par téléphone, même après le délai**.

```gherkin
Fonctionnalité: Annulation par un administrateur

  Scénario: Annulation à tout moment avant la fin de l'événement
    Étant donné qu'un utilisateur a une réservation confirmée pour un événement qui commence dans 2 heures
    Quand j'annule sa réservation depuis la liste des inscrits
    Alors son statut devient "Annulée par l'association"
    Et la place est libérée

  Scénario: Annulation d'une réservation payée
    Étant donné une réservation confirmée et marquée "Payée"
    Quand je l'annule
    Alors elle reste marquée "Payée" pour le suivi d'un remboursement hors application

  Scénario: Événement terminé
    Étant donné un événement terminé
    Quand je consulte sa liste des inscrits
    Alors je ne peux plus annuler de réservation
```

#### US-E6 — Confirmer ou refuser une demande *(nouvelle)*
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

  Scénario: Demande qui n'est plus en attente
    Étant donné une demande expirée, annulée par l'utilisateur ou déjà traitée par un autre admin
    Quand je tente de la confirmer ou de la refuser
    Alors l'action est refusée
    Et la liste se met à jour avec le statut actuel

  Scénario: Événement commencé
    Étant donné un événement dont l'heure de début est dépassée
    Quand je tente de confirmer une demande en attente
    Alors l'action est refusée

  Scénario: Liste des demandes à traiter
    Quand j'ouvre l'espace d'administration
    Alors je vois le nombre total de demandes en attente, tous événements confondus
    Et celles dont l'échéance est la plus proche sont en tête
```

#### US-E7 — Marquer une réservation comme payée *(nouvelle)*
> En tant qu'**administrateur**, je veux **indiquer qu'une personne a payé en main propre** afin de **savoir qui reste à encaisser**.

```gherkin
Fonctionnalité: Suivi du paiement

  Contexte:
    Étant donné que je suis connecté en tant qu'administrateur

  Scénario: Marquer comme payée
    Étant donné une réservation confirmée pour un événement à 15 TND
    Quand je la marque "Payée"
    Alors elle affiche "Payée" avec la date et le nom de l'admin qui l'a enregistré

  Scénario: Corriger une erreur
    Étant donné une réservation marquée "Payée" par erreur
    Quand je retire la mention
    Alors elle redevient "Non payée"

  Scénario: Réservation non confirmée
    Étant donné une demande en attente
    Quand je tente de la marquer "Payée"
    Alors l'action est refusée
    Et un message m'invite à confirmer d'abord la demande

  Scénario: Événement gratuit
    Étant donné une réservation confirmée pour un événement gratuit
    Quand j'affiche la réservation
    Alors l'action "Marquer comme payée" n'est pas proposée

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

#### US-F1 — Être informé et consentir *(modifiée : version de la politique, pas de contrôle d'âge)*
> En tant que **visiteur**, je veux **savoir quelles données sont collectées et pourquoi** afin de **donner mon accord en connaissance de cause**.

```gherkin
Fonctionnalité: Information et consentement

  Scénario: Politique de confidentialité accessible
    Quand je suis sur n'importe quelle page
    Alors un lien "Politique de confidentialité" est accessible en pied de page
    Et elle indique les données collectées, leur usage, leur durée de conservation, le contact de l'association
    Et elle mentionne la loi organique n° 2004-63 et la possibilité de saisir l'INPDP

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
| R4 | Unicité | **Une seule réservation active** (En attente ou Confirmée) par utilisateur et par événement. Après un refus, une expiration ou une annulation, on peut refaire une demande. |
| R5 | Réservation en deux temps | La demande de l'utilisateur crée une réservation **En attente** qui **occupe une place**. Seul un admin la passe en **Confirmée** ou **Refusée** (motif facultatif). Cycle de vie complet : § 2. |
| R6 | Expiration | Une demande En attente expire à l'échéance : **48 h après la demande, ou au début de l'événement si c'est plus tôt**. Elle passe en **Expirée** et la place est libérée. Une demande expirée ne peut plus être confirmée. |
| R7 | Places restantes | **Capacité − (En attente + Confirmées)**, visible de tous *(défaut)*. |
| R8 | Contact de l'admin | **Un seul numéro de contact** pour toute l'association, configuré par un admin, avec l'indication « joignable sur WhatsApp » ou non *(défaut)*. **Un admin peut aussi le retirer** (plus aucun numéro n'est alors configuré et l'option WhatsApp est désactivée). Il est affiché à l'utilisateur après sa demande, sur ses demandes en attente et quand il ne peut plus annuler lui-même. Quand aucun numéro n'est configuré, un message indique à l'utilisateur que l'association n'a pas communiqué de numéro pour le moment *(défaut)*. Il n'est pas affiché aux visiteurs. |
| R9 | Annulation par l'utilisateur | Demande **En attente** : possible jusqu'au début de l'événement. Réservation **Confirmée** : possible jusqu'à **24 h avant le début** (limite comprise) *(défaut J-1)*. La place est libérée immédiatement. |
| R10 | Annulation par l'admin | Possible à tout moment **avant la fin** de l'événement, sur une réservation En attente ou Confirmée. Statut « Annulée par l'association ». |
| R11 | Capacité | Nombre entier **de 1 à 10 000**. L'admin peut l'augmenter librement (dans cette limite). Il **ne peut pas la réduire sous le nombre de réservations actives** (En attente + Confirmées) : la modification est refusée avec un message *(défaut)*. |
| R12 | Prix | **Facultatif**, en **dinars tunisiens (TND)**, de 0 à **99 999,999 TND**, jusqu'à 3 décimales (millimes). Vide ou 0 = **Gratuit**. Affiché « X TND, à régler sur place » : **sans décimales** si le prix est entier (« 15 TND »), **avec 3 décimales** sinon (« 12,500 TND »). Modifier la date, l'heure ou le prix d'un événement qui a des inscrits est permis après un avertissement ; les réservations sont conservées et l'association prévient elle-même les inscrits. **Changer uniquement le lieu ne déclenche pas d'avertissement.** |
| R13 | Paiement | **Hors ligne uniquement**, aucun paiement en ligne. L'admin peut ajouter (et retirer en cas d'erreur) la mention **« Payée »** sur une réservation **Confirmée** d'un événement **payant**. La date et l'admin sont enregistrés. **Aucun remboursement n'est géré dans l'application** : la mention « Payée » reste visible sur une réservation annulée, pour le suivi. |
| R14 | Statuts d'un événement | Brouillon → Publié → (Annulé). On ne peut dépublier que s'il n'y a **aucune réservation active**. Un événement annulé est définitif, et toutes ses réservations actives passent en « Annulée par l'association ». |
| R15 | Suppression d'un événement | Seulement s'il **n'a jamais eu de réservation**, quel qu'en soit le statut ; sinon on l'annule *(défaut)*. **Dès l'étape 2**, un admin peut supprimer (après confirmation) un **brouillon qui n'a jamais été publié** ; un événement qui a été publié, même dépublié depuis, ne peut pas être supprimé avant l'étape 7. |
| R16 | Liste d'attente | Aucune au MVP *(défaut)*. |
| R17 | Notifications | Aucun e-mail ni SMS automatique au MVP : les statuts sont visibles dans l'application et le contact se fait par téléphone ou WhatsApp *(défaut)*. |
| R18 | Actualités | Brouillon / Publiée. Triées par date de publication décroissante, **10 par page** *(défaut)*. Suppression définitive autorisée. Texte simple, sans image au MVP *(défaut)*. |
| R19 | Affichage public des événements | La **liste** montre les événements **publiés ou annulés qui ne sont pas terminés** (un événement en cours reste listé jusqu'à sa fin, avec « Réservations closes »), avec leur prix et leurs places restantes, triés du plus proche au plus lointain, **20 par page** *(défaut)*. Pas d'archive des événements passés dans la liste au MVP *(défaut)*. Le **détail** d'un événement publié ou annulé **reste accessible par son adresse même une fois terminé** (historique conservé, utile à « Mes réservations »). Un brouillon n'est jamais visible. |
| R20 | Données d'un compte | **Prénom, nom, e-mail, téléphone, mot de passe** (conservé uniquement sous forme hachée), plus : la **date de création du compte** (affichée à l'utilisateur, US-F3) ; la **date de dernière connexion** (pour repérer les comptes inactifs depuis 3 ans, R25) ; le **consentement** (date d'acceptation et **version de la politique de confidentialité acceptée**, R24). Aucune autre donnée : en particulier ni date de naissance ni information d'âge. Le téléphone est **obligatoire** car il sert au contact avant la confirmation *(à confirmer en Q8)*. Format accepté : **uniquement « +216 » suivi de exactement 8 chiffres, numéro mobile ou fixe** (exemples : +216 20 123 456, +216 71 123 456). Refusés : saisie locale sans préfixe, préfixe « 00 », numéros internationaux. Les espaces de présentation sont tolérés à la saisie *(défaut)*. L'e-mail est unique, sans tenir compte des majuscules. La même règle de format s'applique au numéro de contact de l'association (R8, Q17). |
| R21 | Mot de passe et connexion | Au moins **8 caractères**, dont une lettre et un chiffre *(défaut)*. Blocage **15 min après 5 échecs** *(défaut)*. Pas de vérification de l'e-mail au MVP *(défaut)*. |
| R22 | Administrateurs | Pas d'inscription en tant qu'admin : le **premier admin est créé à l'installation**, les suivants sont désignés manuellement au MVP. Il doit toujours rester **au moins un admin**. |
| R23 | Suppression de compte | Immédiate et définitive. Les réservations à venir (En attente ou Confirmées) sont **annulées** et leurs places libérées. Les autres réservations sont **rendues anonymes**. |
| R24 | Protection des données | Cadre : **loi organique tunisienne n° 2004-63 du 27 juillet 2004**, autorité **INPDP**. Minimum retenu : consentement explicite à l'inscription, avec conservation de la date et de la **version de la politique acceptée** ; aucun contrôle d'âge dédié (le site est accessible à tout le monde) ; données minimales (R20) ; suppression du compte (R23) ; consultation de ses données (US-F3). |
| R25 | Conservation des données | Un compte inactif depuis **3 ans** (mesuré avec la date de dernière connexion, ou à défaut la date de création) est à supprimer *(défaut ; traitement manuel ou plus tard)*. |
| R26 | Heure de référence | Toutes les dates et heures sont en **heure de Tunis (Africa/Tunis, UTC+1, sans changement d'heure)**. |
| R27 | Langue | Application **en français uniquement**. |
| R28 | Modification d'un événement | Un admin peut modifier un événement (brouillon ou publié) tant qu'il n'est **ni terminé ni annulé**, **y compris s'il a déjà commencé**. Sur un événement commencé, la date de début **ne peut pas être déplacée dans le passé** (une date de début inchangée reste acceptée). Un événement **terminé** (heure de fin dépassée) ou **annulé** ne peut plus être modifié. La date de fin d'un événement commencé ne peut pas être placée dans le passé *(défaut, voir Q28)*. |
| R29 | Limites de saisie des textes | **Titre** et **lieu** : 200 caractères au plus. **Description** : 4 000 caractères au plus. Titre, lieu et description sont obligatoires. Les limites de capacité et de prix sont en R11 et R12. |

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
| Q15 | Les actualités ont-elles besoin d'**images** ou de pièces jointes ? Faut-il **archiver** les événements passés ? | Non au MVP (pas d'archive dans la liste ; le détail d'un événement terminé reste accessible, voir Q19). |
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
- Suivi des remboursements, récapitulatif des encaissements par événement.
- Purge automatique des comptes inactifs, statistiques de fréquentation.
- Événements réservés aux adhérents.
- Redemander l'accord quand la politique de confidentialité change (Q16).

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
