# Cahier des charges — AAAS

| | |
|---|---|
| **Projet** | AAAS — application web de l'association |
| **Version** | 1.0 — 10/11/2026 |
| **Porteur** | Khalil-B-L |
| **Statut** | Validé pour démarrage — toute modification passe par l'historique en fin de document |

Ce document est la **référence** du projet : tous les agents et toutes les tâches doivent le respecter. Le détail des user stories et des critères d'acceptation est dans [`fonctionnel/mvp.md`](fonctionnel/mvp.md) ; en cas de contradiction, ce cahier des charges fait foi jusqu'à sa prochaine mise à jour.

---

## 1. Contexte

Une **association en Tunisie**, animée par des bénévoles, organise des événements et diffuse des actualités. Aujourd'hui les inscriptions se font de manière informelle (téléphone, bouche-à-oreille), sans vue claire sur les places restantes.

Le projet a un **double objectif** :
1. **Pour l'association** : un outil simple pour publier les actualités et les événements et gérer les inscriptions.
2. **Pour le porteur du projet** : réviser de bout en bout une stack .NET / Angular / Docker / CI-CD / Azure / IA, sur un projet réel, présentable en entretien.

## 2. Objectifs

| # | Objectif | Indicateur de réussite |
|---|---|---|
| O1 | Les membres consultent actualités et événements en ligne | Pages publiques accessibles sans compte |
| O2 | Les membres demandent une place en quelques clics | Demande de réservation en moins de 1 minute |
| O3 | Les administrateurs gardent le contrôle des inscriptions | Chaque demande est confirmée ou refusée par un admin ; jamais de dépassement de capacité |
| O4 | L'association n'a pas de coût de fonctionnement significatif | Hébergement sur offres gratuites ou très bas coût |
| O5 | Le porteur maîtrise chaque brique technique | Fiches de révision rédigées pour chaque techno (§ 9) |

## 3. Utilisateurs

| Rôle | Description |
|---|---|
| **Visiteur** | Personne non connectée : consulte les actualités et les événements. |
| **Membre** (utilisateur) | A un compte : demande des réservations, les suit, les annule, gère son compte. |
| **Administrateur** | Bénévole de l'association : publie le contenu, traite les demandes de réservation, suit les paiements. |

## 4. Périmètre

### 4.1 Dans le périmètre (MVP)
- Comptes : inscription, connexion, déconnexion, suppression du compte.
- Actualités : consultation publique ; création, modification, publication et dépublication par les admins.
- Événements : consultation publique avec places restantes ; gestion complète par les admins (brouillon, publication, annulation).
- Réservations : demande, validation par l'admin, expiration automatique, annulation, suivi.
- Paiement **hors ligne** : prix affiché, marquage « payé » par l'admin.
- Protection des données minimale (§ 6.4).

### 4.2 Hors périmètre (MVP)
- **Paiement en ligne** (non disponible pour l'association en Tunisie).
- Envoi d'e-mails ou de SMS automatiques.
- Liste d'attente.
- Plusieurs places par réservation (accompagnants).
- Application mobile native (le site doit en revanche être utilisable sur mobile).
- Langue arabe (français uniquement).

### 4.3 Phase 2 (semaine 4)
- **Assistant** qui répond aux questions des membres à partir des documents de l'association (statuts, règlement, FAQ, descriptions d'événements), en citant ses sources (RAG avec Azure OpenAI).

## 5. Exigences fonctionnelles

Le détail (user stories, critères d'acceptation Gherkin) est dans [`fonctionnel/mvp.md`](fonctionnel/mvp.md). Les exigences ci-dessous sont **obligatoires**.

### 5.1 Réservation — processus de référence

1. Le membre connecté fait une **demande** sur un événement réservable → statut **En attente**. Une place est bloquée.
2. L'application lui affiche le **numéro de téléphone (ou WhatsApp) de l'administrateur** à contacter pour valider la demande (et régler le prix s'il y en a un).
3. L'administrateur **confirme** ou **refuse** la demande dans l'application.
4. Sans action de l'admin dans le délai prévu, la demande **expire** automatiquement et la place est libérée.

| Statut | Signification | Compte dans les places prises ? |
|---|---|---|
| En attente | Demande faite, en attente de validation | Oui |
| Confirmée | Validée par l'admin | Oui |
| Refusée | Refusée par l'admin | Non |
| Expirée | Pas validée dans le délai | Non |
| Annulée par le membre | Annulée par le membre | Non |
| Annulée par l'association | Événement annulé ou annulation par l'admin | Non |

### 5.2 Règles métier obligatoires

| Réf. | Règle |
|---|---|
| RG-01 | Un événement n'est réservable que s'il est **publié**, **non annulé**, **pas encore commencé** et **non complet**. |
| RG-02 | Places prises = demandes **En attente** + **Confirmées**. La capacité ne doit **jamais** être dépassée, y compris quand deux membres réservent la dernière place en même temps. |
| RG-03 | Une seule réservation active (En attente ou Confirmée) par membre et par événement. Une nouvelle demande est possible après refus, expiration ou annulation. |
| RG-04 | Une demande En attente expire après **48 h** sans validation, et au plus tard au début de l'événement. |
| RG-05 | Le membre peut annuler une demande **En attente** jusqu'au début de l'événement, et une réservation **Confirmée** jusqu'à **24 h** avant le début ; la place est libérée immédiatement. |
| RG-06 | L'admin ne peut pas réduire la capacité sous le nombre de places prises. |
| RG-07 | Un événement ayant eu des réservations ne peut pas être supprimé, seulement **annulé** ; ses réservations actives passent à « Annulée par l'association ». |
| RG-08 | Prix optionnel en **dinars tunisiens (TND)**, jusqu'à 3 décimales (millimes) ; vide ou 0 = gratuit. L'admin ajoute la mention **payée** à une réservation Confirmée (ce n'est pas un statut, elle peut être retirée en cas d'erreur) ; aucun remboursement n'est géré par l'application. |
| RG-09 | Il existe toujours au moins un administrateur. |
| RG-10 | Le numéro de contact de l'association (avec option WhatsApp) est configurable par les admins, sans modifier le code. Il n'est affiché qu'aux membres connectés, jamais aux visiteurs. |
| RG-11 | Le téléphone du membre est obligatoire à l'inscription (+216 suivi de 8 chiffres, ou numéro international), pour que l'admin puisse le joindre. |

Les valeurs par défaut (48 h, 24 h) sont paramétrables et peuvent évoluer selon les réponses de l'association (questions ouvertes dans `fonctionnel/mvp.md`).

## 6. Exigences non fonctionnelles

### 6.1 Ergonomie
- Interface en **français**, compréhensible par des personnes peu à l'aise avec l'informatique.
- Site **responsive** : utilisable confortablement sur smartphone.
- Heures affichées à l'heure de **Tunis** (`Africa/Tunis`, UTC+1, sans changement d'heure) ; stockage en UTC.

### 6.2 Sécurité
- Mots de passe hachés (ASP.NET Core Identity), authentification par **JWT**.
- Autorisation vérifiée **côté API** pour chaque action ; un membre n'accède jamais aux réservations d'un autre.
- **Aucun secret** dans le dépôt : user-secrets en local, variables d'environnement dans Docker, **Azure Key Vault** en production via Managed Identity.
- HTTPS en production ; CORS limité au front.

### 6.3 Performance et disponibilité
- Pages principales affichées en moins de 2 secondes dans des conditions normales.
- Pas d'exigence de haute disponibilité (outil associatif) ; une coupure courte est acceptable.

### 6.4 Protection des données personnelles
- Cadre légal : **loi organique tunisienne n° 2004-63 du 27 juillet 2004** (autorité de contrôle : INPDP), et non le RGPD.
- Données collectées limitées au nécessaire (prénom, nom, e-mail, téléphone, mot de passe haché, date de l'accord) ; accord explicite à l'inscription.
- Le membre peut **supprimer son compte** : réservations à venir annulées, historique passé anonymisé.
- La nécessité d'une déclaration auprès de l'INPDP est à vérifier par l'association.

### 6.5 Coûts
- Hébergement Azure sur offres **gratuites ou à très bas coût** (crédits gratuits pendant le développement).
- Région Azure : **France Central** (la plus proche de la Tunisie).

## 7. Contraintes techniques

| Domaine | Choix imposé |
|---|---|
| API | ASP.NET Core (.NET LTS récent), architecture en couches Domain / Application / Infrastructure / Api |
| Données | SQL Server, EF Core (migrations), Azure SQL en production |
| Tests | xUnit ; tests d'intégration avec WebApplicationFactory et Testcontainers SQL Server |
| Front | Angular récent : composants standalone, signals |
| Conteneurs | Dockerfile pour l'API et le front ; `docker compose` lance API + SQL Server + front en local |
| CI/CD | GitHub Actions : build, tests, image Docker, déploiement |
| Cloud | Azure App Service (API), Static Web Apps (front), Azure SQL, Key Vault |
| IA (phase 2) | Azure OpenAI + recherche vectorielle (Azure AI Search ou Azure SQL) |
| Code source | Dépôt GitHub public `Khalil-B-L/AAAS-web` |

Toute dérogation à ces choix fait l'objet d'un ADR dans `docs/adr/`.

## 8. Planning

| Semaine | Lot | Livrable |
|---|---|---|
| **S1** | API et données | Solution .NET, modèle et migrations, auth JWT et rôles, événements, réservations et toutes les règles RG, expiration automatique, tests xUnit et d'intégration, `docker compose` API + SQL Server |
| **S2** | Front Angular | Pages publiques, compte, réservation et suivi, espace admin, front ajouté au `docker compose` |
| **S3** | Industrialisation | Pipeline GitHub Actions, déploiement Azure (App Service, Static Web Apps, Azure SQL, Key Vault) |
| **S4** | Assistant et finitions | Assistant RAG, documentation finale (guides utilisateur, logique de l'application), démo |

Ordre de réalisation au sein du MVP : comptes et premier admin → gestion des événements → consultation → demande de réservation → validation par l'admin et expiration → mes réservations et annulation → annulation d'événement et inscrits → actualités → protection des données.

## 9. Livrables

| Livrable | Emplacement |
|---|---|
| Code source (API, front, tests) | Dépôt GitHub |
| Fichiers Docker et pipeline CI/CD | Dépôt GitHub |
| Application déployée | Azure |
| Cahier des charges (ce document) | `docs/cahier-des-charges.md` |
| Cadrage fonctionnel | `docs/fonctionnel/` |
| Décisions d'architecture | `docs/adr/`, `docs/technique/architecture.md` |
| Documentation technique (règles, API, exploitation) | `docs/technique/` |
| Guide utilisateur membre et administrateur | `docs/guide-utilisateur/` |
| Fiches de révision (Docker, Angular, Azure, CI/CD, RAG…) | `docs/revision/` |

## 10. Définition de « terminé »

Une fonctionnalité est **terminée** quand :
1. ses critères d'acceptation (`fonctionnel/mvp.md`) sont tous satisfaits ;
2. les règles métier concernées sont couvertes par des tests qui passent ;
3. le build et les tests passent (localement, puis dans la CI à partir de S3) ;
4. elle a été relue par l'agent `code-reviewer` sans remarque bloquante ;
5. la documentation concernée est à jour (`docs/technique/`, et `docs/guide-utilisateur/` si le comportement visible change) ;
6. la notion technique nouvelle a sa fiche dans `docs/revision/` ;
7. elle fonctionne avec `docker compose up`.

## 11. Risques

| Risque | Impact | Parade |
|---|---|---|
| Périmètre qui grossit | Retard sur les 4 semaines | Tout ajout passe par ce document ; hors périmètre par défaut |
| Réponses de l'association tardives | Règles incertaines | Valeurs par défaut paramétrables, ajustées ensuite |
| Coûts Azure après les crédits | Facture pour l'association | Offres gratuites ou serverless, budget et alertes Azure |
| Dépassement de capacité en cas d'accès simultané | Perte de confiance | Gestion explicite de la concurrence + test dédié (RG-02) |
| Charge de validation pour les admins | Demandes qui expirent | Expiration automatique (RG-04), liste des demandes en attente bien visible |

## 12. Historique des modifications

| Version | Date | Modification |
|---|---|---|
| 1.0 | 10/11/2026 | Création |
