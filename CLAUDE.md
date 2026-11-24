# AAAS — consignes du projet

Application web pour une **association en Tunisie** : actualités, comptes, demandes de réservation d'événements validées par un administrateur, publication par les administrateurs, puis un assistant RAG. **Le document de référence est le cahier des charges `docs/cahier-des-charges.md` : toute tâche doit le respecter** (périmètre, règles RG-xx, contraintes techniques, définition de « terminé » § 10). Tout ajout ou changement de périmètre se fait d'abord dans ce document (avec une ligne dans son historique), jamais directement dans le code. Le détail des user stories est dans `docs/fonctionnel/mvp.md`.

**Le projet sert d'abord à réviser** ASP.NET Core, EF Core, SQL Server, xUnit, Angular, Docker, GitHub Actions et Azure, en vue d'entretiens. Docker et Angular sont presque oubliés : il faut les expliquer. .NET et SQL Server sont maîtrisés.

## Contexte métier à ne pas oublier
- Fuseau `Africa/Tunis` (UTC+1, sans changement d'heure). Les dates sont stockées en UTC.
- Interface en français uniquement.
- Aucun paiement en ligne : prix optionnel en TND, payé en main propre, marqué « payé » par l'admin.
- Réservation en deux temps : demande « En attente » (bloque une place, expire après un délai), l'utilisateur contacte le numéro de l'admin affiché, l'admin confirme ou refuse.
- Protection des données : loi organique tunisienne n° 2004-63 (INPDP), pas le RGPD.

## Façon de travailler
1. `expert-fonctionnel` : user stories et critères d'acceptation.
2. `architecte` : valide et conçoit le plan, rédige les ADR.
3. `expert-technique` : code en expliquant, tient les fiches de révision.
4. `testeur` : tests unitaires et d'intégration.
5. `code-reviewer` : relecture avant commit.
6. `documentaliste` : guide utilisateur et documentation de la logique.

Ne jamais committer avec l'identité Ouidou : ce dépôt utilise l'identité git locale `Khalil-B-L`. L'utilisateur fait lui-même ses commits et ses push.

## Documentation : à tenir à jour au fil de l'eau
Toute modification qui change un comportement visible ou une règle doit mettre à jour la doc concernée **dans la même tâche**.

| Dossier / fichier | Contenu | Rédigé par |
|---|---|---|
| `docs/fonctionnel/` | Besoin, user stories, critères d'acceptation, questions ouvertes | expert-fonctionnel |
| `docs/adr/` | Décisions d'architecture (une par fichier `NNNN-titre.md`) | architecte |
| `docs/technique/architecture.md` | Vue d'ensemble : couches, schéma de données, flux, déploiement | architecte |
| `docs/technique/regles-metier.md` | Toutes les règles métier et le cycle de vie des statuts, avec l'endroit du code qui les applique | expert-technique |
| `docs/technique/api.md` | Endpoints : rôle requis, entrées, sorties, codes d'erreur | expert-technique |
| `docs/technique/exploitation.md` | Lancer en local (Docker), configuration, secrets, CI/CD, déploiement Azure | expert-technique |
| `docs/revision/` | Fiches de révision par techno | expert-technique |
| `docs/guide-utilisateur/` | Guides pour les visiteurs/membres et pour les administrateurs | documentaliste |

Le documentaliste relit l'ensemble et assemble la documentation finale ; il ne doit pas avoir à deviner la logique à partir du code.

---

# REPRENDRE LE PROJET (état au 24/11/2026)

**Ordre de lecture pour une nouvelle session** : ce fichier, puis `docs/technique/etat-avancement.md` (détail par tâche, décisions T-xx, questions ouvertes D1-D9), `docs/cahier-des-charges.md` (v1.5), `docs/technique/architecture.md`, puis le plan ou l'ADR de la tâche (`docs/technique/plan-*.md`, `docs/adr/`).

## Qui est l'utilisateur et comment lui parler
Khalil fait AAAS pour un proche (association « A.A.A.S — Association d'amitié et d'action sociale », Tunisie, Cap Bon/Nabeul). C'est aussi une révision : **expliquer Docker, Angular et la liaison complète** (Angular ↔ API ↔ SQL Server ↔ Docker ↔ CI/CD ↔ Azure) ; .NET et SQL Server sont maîtrisés. Il répond souvent « Vazy » = « vas-y, avec les valeurs par défaut recommandées ». Il commite et pousse lui-même (identité `Khalil-B-L`, jamais Ouidou ; l'e-mail du commit est personnel, le dépôt `github.com/Khalil-B-L/AAAS-web` peut être public : lui proposer l'adresse noreply GitHub si besoin). Ne rien committer à sa place.

## Ce qui est FAIT (tout est dans l'arbre de travail ; vérifier `git status` / `git log` pour savoir ce que l'utilisateur a déjà committé : seuls « Etape 1 » et un instantané du front sont certains)
- **Étape 1** : comptes, JWT, rôles, premier admin (ADR 0008), santé `/health`, Docker (compose API + SQL Server + front).
- **Étape 2** : événements (admin + public), numéro de contact de l'association, brouillon supprimable, rowversion/UPDLOCK.
- **Front Angular 22** (zoneless, signals, Material, Vitest) : inscription, connexion, événements publics et admin, numéro de contact, **thème et logo de l'association** (`front/src/theme/_brand.scss`, `front/src/app/core/config/app-info.ts`).
- **Réservations A, B, C** : demande de place, mes réservations, annulation membre, traitement admin (confirmer, refuser, annuler, payé, inscrits), expiration automatique (BackgroundService, ADR 0020). Relues par le `code-reviewer` (OK).
- **Actualités N1-N8** : code, docs et tests livrés (ADR 0022-0023 acceptés), tests complétés par le `testeur` (concurrence sur fiche supprimée, émojis/8 000 caractères, pagination, XSS, identifiants mal formés, journaux). **Pas encore relues par le `code-reviewer`** : c'est la première chose à faire à la reprise. Deux comportements notés par le testeur, cohérents avec le code : un PUT avec titre/texte vide sur une fiche supprimée renvoie 400 (validation du DTO avant le service), pas 404 ; tout 404 public porte `no-store` (posé par `ExceptionHandlerMiddleware`).
- Dernier état de tests rapporté par le `testeur` (actualités comprises) : Domain 244, Application 133, Infrastructure 78, intégration 382, front 396 (27 fichiers, stable sur 3 passages) ; lint OK ; bundle front ≈ 429 Ko (budget 500). Parcours `front/e2e/pile-e2e.sh` (news, reservations, parcours) OK sur pile jetable avant les derniers tests (non rejoué depuis). **Relancer avant de committer** (commandes plus bas). Non couverts : `NoStoreMiddleware` sur un 5xx, charge de concurrence, `DatePipe` hors fuseau Tunis.

## Ce qui RESTE (ordre conseillé)
1. **Finir les actualités** : relancer `testeur` puis `code-reviewer`, corriger, relancer tous les tests.
2. **Annulation d'un événement** (US-E3, Q29 : statut `Cancelled`, suppression d'un publié sans réservation) : petit.
3. **Suppression de compte** (US-F2, ADR 0007, RG-09 dernier admin protégé, réservations anonymisées) : petit/moyen.
4. **Guides utilisateur** (`documentaliste`, `docs/guide-utilisateur/`, à partir de `regles-metier.md` § 6 et `libelles-ecrans.md`) : après 2 et 3.
5. **CI GitHub Actions** : **écrite mais jamais exécutée** (`.github/workflows/ci.yml` : jobs backend avec `-warnaserror` + tests projet par projet + artefacts TRX, front lint/test/build, `docker build` des 2 images sans push ; `.github/dependabot.yml`). Validée par `actionlint` et en local (build `-c Release -warnaserror` : 0 avertissement). **À valider au premier push** : cache de couches `type=gha`, durée de l'intégration (image SQL Server ≈ 1,5 Go), version de npm vs `packageManager`, filtre `dotnet/*` de Dependabot. Fiche : `docs/revision/github-actions.md` ; guide : `exploitation.md` § 10. Reste à faire : `cd.yml` (livraison/Azure).
6. **Azure / déploiement (S3)** : décisions du porteur nécessaires (App Service F1 vs conteneur personnalisé, Static Web Apps, Azure SQL serverless, Key Vault, OIDC GitHub) ; avant production : `UseForwardedHeaders`, HTTPS/HSTS, rate limiting sur `/api/auth/*`, EF jamais en Debug, instance unique si `PromoteExistingAccount`. Voir `architecture.md` § 9-11.
7. **Assistant RAG (S4)** : `POST /api/assistant/questions`, embeddings, index vectoriel ; ADR à écrire ; nécessite clés/comptes de service.
8. Décisions en attente (`etat-avancement.md` § 4) : D3 politique de confidentialité à compléter (nom officiel, adresse, e-mail, date, déclaration INPDP ; texte `docs/fonctionnel/politique-confidentialite.md` v1.1-provisoire), D6 ADR 0001-0003 et 0007-0013 toujours « Proposé » (vérifier le statut en tête de chaque fichier), D8 erreurs 500 avec message SQL brut (option B recommandée : ne journaliser que type, numéro SQL, méthode, chemin), nom officiel et couleurs exactes à confirmer avec l'association, T-N8 (`/admin` mène aux événements alors que « Actualités » est premier du sous-menu).

## Lancer en local (poste Windows, Git Bash/PowerShell)
- **Docker = Rancher Desktop** : s'il est arrêté, `docker` ne répond pas ; le lancer d'abord (`C:\Program Files\Rancher Desktop\Rancher Desktop.exe`).
- Copier `.env.example` en `.env` (non versionné, ignoré par `*.env`) et remplir : `MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY` (≥ 32 octets), `BOOTSTRAP_ADMIN_EMAIL` (adresse non publiée), `BOOTSTRAP_ADMIN_PASSWORD` (jamais la valeur publique de l'exemple). Générer : `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`. Le mot de passe SA est figé à la création du volume (`docker compose down -v` pour le changer). Ne jamais mettre ces valeurs dans Git ni dans une doc.
- `docker compose up -d --build` puis `docker compose ps` (3 services healthy). Site : **http://127.0.0.1:4200** (pas `localhost` si un `ng serve` d'un autre projet occupe `[::1]:4200`) ; API : http://127.0.0.1:8080 (`/health`, `/health/ready`). La migration s'applique au démarrage. Détails : `docs/technique/exploitation.md`.
- Tests : `dotnet build Aaas.slnx` (0 avertissement exigé) ; `dotnet test tests/<projet> -m:1 -v q` **un projet à la fois** (la solution entière a déjà été tuée par manque de mémoire) ; l'intégration (Testcontainers, ~3 min) **seule, sans autre agent qui utilise Docker** (sinon délais dépassés) ; front : `cd front && npm ci && npm run test:ci && npm run lint && npm run build` ; parcours bout en bout sur pile jetable (ne touche pas à la pile locale) : `bash front/e2e/pile-e2e.sh up|parcours|reservations|news|down`.

## Conventions établies (ne pas les redécouvrir)
- **Couches** Domain / Application / Infrastructure / Api (ADR 0001) : contrôleurs fins, pas de MediatR/CQRS/repositories, `DbContext` utilisé directement (`IAppDbContext`). Règles métier dans le Domain, erreurs via `BusinessRuleException` + `ErrorCodes` → ProblemDetails avec `code` (les décisions du front se prennent sur `code`, jamais sur le texte).
- **Concurrence** : `rowversion` + 409 `concurrent_update` ; verrou `UPDLOCK` via `IEventLock` quand une règle dépend d'un décompte (places) ; RCSI, jamais SNAPSHOT pour la demande de place ; index unique filtré en filet ; une écriture n'est jamais annulée côté front (on ignore sa réponse si l'écran a changé).
- **Routes** : pour chaque `{id:guid}`, une route de repli `{id}` sans contrainte → 404 (public) ou 401/403/404 (admin) pour éviter le 401 trompeur (leçon D1). `Cache-Control: no-store` sur `/api/me` et `/api/admin` (middleware placé avant `UseExceptionHandler`).
- **Dates** : UTC en base (`DateTimeOffset`, `TimeProvider` partout, `FakeTimeProvider` en test), affichage Africa/Tunis (`+0100`), saisie avec fuseau explicite ; jamais le fuseau du navigateur.
- **Données personnelles (R24)** : jamais d'e-mail, téléphone, nom, contenu saisi ni message SQL dans les journaux ni dans le `localStorage` (seulement `{accessToken, localDeadline}`). EF : erreurs SQL rétrogradées en Debug ; erreurs 2601/2627/2628 journalisées sans exception.
- **Front** : aucun `[innerHTML]` ni `bypassSecurityTrust` (règle ESLint) ; contenu affiché par interpolation ; libellés et messages alignés sur `docs/fonctionnel/libelles-ecrans.md` et `api.md` ; tout DTO ajouté dans `api.md` se reporte dans `front/src/app/core/models/api-models.ts`. Contraste : jamais de texte blanc sur l'orange vif `#FF7A00` (texte brun foncé `#2B1600`).
- **Téléphone** : uniquement `+216` + 8 chiffres (mobile ou fixe), stocké E.164.

## Pièges et leçons sur le travail avec les agents
- `expert-fonctionnel` et `documentaliste` n'ont pas d'outil d'édition partielle : ils **réécrivent les fichiers en entier** ; faire compter scénarios/entrées avant/après et regarder `git diff`.
- Un seul agent à la fois sur Docker/`dotnet test` ; ne pas modifier `src/` pendant qu'un `testeur` lance ses tests.
- Chaque tranche suit : fonctionnel → architecte (plan + ADR « Proposé », valeurs par défaut) → validation du porteur → expert-technique → testeur → code-reviewer → corrections → documentaliste. Mettre à jour `etat-avancement.md` à chaque jalon.
- Une session expirée (`/login`) ou une fermeture de session arrête les agents en cours ; relire `git status` pour voir ce qui est resté à moitié.
- Avant de rendre un poste d'entreprise : copier les valeurs du `.env` dans un gestionnaire de mots de passe, supprimer le `.env` et tout fichier de mots de passe, se déconnecter de GitHub.
