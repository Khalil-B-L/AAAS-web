# État d'avancement et reprise — AAAS

> Tenu par l'agent `expert-technique`. **But : reprendre le projet depuis un clone Git neuf, sans contexte de session.** Mis à jour le 23/11/2026, à la fin de la semaine 2 (front Angular).
> Document de référence du périmètre : [`docs/cahier-des-charges.md`](../cahier-des-charges.md) ; user stories : [`docs/fonctionnel/mvp.md`](../fonctionnel/mvp.md) (v4) ; consignes de travail : [`CLAUDE.md`](../../CLAUDE.md).

## 1. Où en est-on (résumé)

| Bloc | État | Détail |
|---|---|---|
| API étape 1 — comptes, premier admin | fait | inscription, connexion, `GET /api/account/me`, rôles, sondes `/health` |
| API étape 2 (+3) — événements admin, consultation publique, numéro de contact | fait (19/11) | ADR 0009 à 0013, `plan-etape-2.md` ; tests d'intégration complets et revue : à finir (voir `architecture.md` § 0) |
| **Front S2 — écrans des étapes 1 et 2** | **fait (23/11), à relire** | `front/` ; plan [`plan-front-angular.md`](plan-front-angular.md) T0 à T10 ; ADR 0014 à 0017 **acceptés** |
| Réservations, actualités, suppression de compte, annulation d'événement | à faire | § 3 |
| S3 — CI/CD GitHub Actions, Azure | à faire | § 3 |
| S4 — assistant RAG | à faire | § 3 |

### 1.1 Front livré (semaine 2)

| Écran | Route | User story |
|---|---|---|
| Coquille : en-tête (logo, menu selon le rôle, « Bonjour {prénom} », menu mobile), pied de page (logo, confidentialité) ; thème aux couleurs de l'association (24/11, § 1.4) | — | — |
| Inscription (téléphone +216, case de consentement, connexion automatique) | `/inscription` | US-A1 |
| Connexion (retour à la page demandée, message de session expirée), déconnexion | `/connexion` | US-A2, US-A3 |
| Mon compte (lecture) | `/mon-compte` | US-F3 (lecture) |
| Événements à venir (20 par page, mentions, prix, dates de Tunis) | `/evenements?page=` | US-C1 |
| Détail d'un événement (« Demander une place » pour un visiteur → connexion) | `/evenements/:id` | US-C2 (partie visiteur) |
| Politique de confidentialité (texte provisoire 1.0) | `/confidentialite` | US-F1 |
| Admin : liste (filtres statut et période) | `/admin/evenements` | US-E2 |
| Admin : créer, modifier, publier, dépublier, supprimer un brouillon ; flux 409 | `/admin/evenements/nouveau`, `/admin/evenements/:id` | US-E2 |
| Admin : numéro de contact (enregistrer, retirer, WhatsApp) | `/admin/parametres` | US-E8 |
| Contenu introuvable, accès réservé | `**`, `/acces-refuse` | US-B2, US-C2 |

Image Docker du front (Node → nginx non-root), service `front` dans `docker-compose.yml` (`http://localhost:4200`).

### 1.2 Vérifications faites le 23/11/2026

| Vérification | Résultat |
|---|---|
| `npm ci` (depuis un `node_modules` vide) | OK |
| `npm run lint` (ESLint, TypeScript + templates + accessibilité) | OK, 0 erreur |
| `npm run test:ci` (Vitest + couverture) | **240 tests, 20 fichiers, tous verts trois fois de suite** (24/11, après le commit de suivi de revue) — couverture : voir § 1.3. Plus aucune attente à durée fixe : animations Material désactivées dans les specs du dialogue et du `mat-select` (`MATERIAL_ANIMATIONS`), attentes sur condition (`vi.waitFor`) |
| `npm run build` (production) | OK, aucun avertissement ; bundle initial 426 Ko bruts / 108 Ko transférés (budget 500 Ko) |
| `docker build -t aaas-front:local front` | OK, image de 61 Mo, utilisateur `nginx` (UID 101) |
| `docker compose config` | OK (3 services) ; **la pile locale n'a pas été relancée** |
| nginx (conteneur jetable) | repli SPA, `/healthz`, cache long des fichiers à empreinte, gzip, CSP, 502 sans API |
| Bout en bout HTTP via nginx sur une pile **isolée** (`front/e2e/pile-e2e.sh`) | inscription (400 RG-11, 201, 409), connexion (401, 200 admin), 403 membre, création, brouillon invisible (404), publication avec avertissement `contact_not_configured`, 409 `concurrent_update` avec l'ancienne version, 200 avec la nouvelle, liste publique, contact 401 visiteur / PUT / DELETE avec `{ version }`, suppression d'un brouillon (204), F5 sur route profonde (200) |
| Parcours dans un vrai navigateur (Edge sans interface, CDP) | 12 contrôles OK sur deux passages : connexion admin et retour, menu admin, `localStorage` minimal, fiche préremplie à l'heure de Tunis, « Dépublier » grisé pendant la saisie, enregistrement, page contact, F5, déconnexion, menu mobile, **aucune violation CSP** |

### 1.3 Couverture des tests du front

`npm run test:ci` → rapport HTML dans `front/coverage/front/index.html` (non versionné). Instructions couvertes, 23/11/2026 :

| Dossier (`front/src/app/`) | Couverture | Commentaire |
|---|---|---|
| `core/time`, `shared/events`, `shared/pipes`, `shared/ui`, `core/notifications` | 100 % | fonctions pures (dates de Tunis, mentions, prix), dialogue de confirmation réel, message « flash » |
| `core/errors`, `core/auth`, `shared/forms` | 97 à 99 % | ProblemDetails, session (déconnexion, autre onglet), intercepteur, guards, validateurs et messages |
| `features/account` | 99 % | connexion, inscription, mon compte |
| `features/admin` | liste 99 %, formulaire 89 %, contact 81 % | flux 409, version, double clic, garde de sortie, course entre deux fiches, id non-GUID, réponse d'une écriture arrivée après destruction ou changement de fiche (24/11 ; `admin-event-form.ts` seul : 86 %, les cas « écran changé » de Publier / Dépublier ne sont pas testés) |
| `features/public` | 87 à 88 % | liste et détail |
| `app.ts` (coquille) | 96 % | lien « Aller au contenu », focus après navigation |
| `layout` | 72 % | `header.ts` / `.html` (51-62 %) désormais chargés par le test de la coquille mais non testés (menu mobile, déconnexion) ; le reste à 100 % |
| `core/api`, `core/config` | 69 %, 70 % | services HTTP fins non testés un par un ; `AppTitleStrategy` |
| `app.routes.ts` | 10 % | `loadComponent` non exécutés en test unitaire (vérifiés par le parcours navigateur) |
| **Total** | **90,9 %** (lignes 92,1 %, branches 89,2 %) | 240 tests (24/11) ; 90,4 % le 23/11 avec 236 tests |

### 1.4 Identité visuelle A.A.A.S (24/11/2026)

| Élément | Où | Détail |
|---|---|---|
| Nom, nom officiel, logo | `front/src/app/core/config/app-info.ts` (seul endroit) + `<title>` de `index.html` | « A.A.A.S », « Association d'amitié et d'action sociale » (texte du logo, **à confirmer avec les statuts**) ; adresse, e-mail et date de la politique restent entre crochets (D3) |
| Logo | `front/public/logo-aaas.jpg` (466 × 429, 22 Ko) | composant `shared/ui/brand-logo.ts` : en-tête (48 px de haut), pied de page (40 px dans un rond blanc de 64 px), page de connexion (120 px) ; `width`/`height` fixés (pas de décalage), `alt` = nom complet |
| Favicon | `front/public/favicon.ico` (16, 32, 48 px), `apple-touch-icon.png` (180 px) | générés à partir du logo, recadrés sur les deux mains (le texte serait illisible à 16 px) |
| Couleurs | `front/src/theme/_brand.scss` (seule source) | vert `#0F5C2E`, orange `#FF7A00`, texte sur orange `#2B1600`, brun-orange `#B45309` ; valeurs **approximatives** lues sur la maquette |
| Thème Material | `front/src/styles.scss`, palettes `front/src/theme/_theme-colors.scss` (générées par `ng generate @angular/material:theme-color`) | primaire vert, tertiaire orange ; surcharges exactes (`mat.theme-overrides`), bouton plein orange (`mat.button-overrides`) ; variables `--aaas-*` pour nos styles |
| CSP nginx | inchangée | logo et icônes servis par le même site (`img-src 'self'`) |

Contrastes WCAG mesurés (formule de luminance relative WCAG 2.x) :

| Texte / fond | Ratio | Usage | AA |
|---|---|---|---|
| blanc / vert `#0F5C2E` | 8,11:1 | pied de page | oui (AAA) |
| vert / blanc | 8,11:1 | titres, liens, menu, contour des boutons secondaires | oui (AAA) |
| brun-orange `#B45309` / blanc | 5,02:1 | mots mis en avant (`.highlight`), souligné de l'onglet actif | oui |
| `#2B1600` / orange `#FF7A00` | 6,60:1 | texte des boutons principaux | oui |
| blanc / orange `#FF7A00` | **2,61:1** | **interdit**, même en gros texte gras (seuil 3:1) | non |
| orange / blanc | **2,61:1** | **jamais en couleur de texte** | non |
| vert / orange | 3,10:1 | non utilisé (insuffisant en petit corps) | non |
| texte courant `#191D19` / blanc | 17,06:1 | corps de texte (Material `on-surface`) | oui |

Vérifié le 24/11 : `npm run lint` OK ; `npm run test:ci` **243 tests, 21 fichiers, verts quatre fois de suite** (couverture 91,1 %) ; `npm run build` OK sans avertissement, bundle initial **427 Ko bruts / 109 Ko transférés**. Non vérifié : rendu dans un navigateur et dans le conteneur (à faire par le porteur : `docker compose up -d --build front`, puis http://127.0.0.1:4200).

Pistes pour le `testeur` : `Header` (menu mobile, « Se déconnecter »), services de `core/api` un par un, `AppTitleStrategy`, et l'instabilité signalée au § 1.2.

## 2. Relancer tout depuis un clone neuf

Prérequis : SDK .NET 10 (`global.json`), Docker (Desktop ou Rancher Desktop), Node 24 + npm 11 (pour le front hors Docker), Git Bash sous Windows.

```bash
git clone <url-du-dépôt> AAAS && cd AAAS

# 1. Secrets locaux (jamais versionnés) : copier le modèle puis remplir les valeurs VIDES
cp .env.example .env
#    MSSQL_SA_PASSWORD, JWT_SIGNING_KEY, BOOTSTRAP_ADMIN_EMAIL (adresse non publiée), BOOTSTRAP_ADMIN_PASSWORD
#    générer : openssl rand -base64 48   (éviter ; et $ dans MSSQL_SA_PASSWORD)

# 2. Toute l'application en conteneurs
docker compose up --build -d
docker compose ps                  # sqlserver et front « healthy », api « Up »
#    → http://localhost:4200 (site) ; se connecter avec BOOTSTRAP_ADMIN_EMAIL / BOOTSTRAP_ADMIN_PASSWORD

# 3. Tests .NET
dotnet tool restore
dotnet build
dotnet test                        # Docker requis (Testcontainers pour les tests d'intégration)

# 4. Front : vérifications locales
cd front
npm ci
npm run lint
npm run test:ci
npm run build

# 5. Développement du front contre l'API en conteneur
API_URL=http://localhost:8080 npm start     # arrêter d'abord le conteneur front : docker compose stop front
```

Contrôle de bout en bout sur une pile jetable (ne touche pas à la pile ci-dessus) : `bash front/e2e/pile-e2e.sh up`, puis `parcours`, puis `down` (`exploitation.md` § 2 bis).

**Poste actuel du porteur** : la pile `docker compose` qui tourne date du 11/11 (image d'API de l'**étape 1**, sans `/api/events`) et n'a pas de service `front`. Pour passer à l'état courant : `docker compose up -d --build` (reconstruit l'API et le front ; la migration `AddEventsAndAssociationSettings` s'applique au démarrage sur la base existante, les comptes sont conservés). Puis saisir le numéro de contact dans « Administration » → « Numéro de contact ».

## 3. Ce qui reste à faire (ordre proposé)

Recommandation du plan front (question P-1, défaut accepté par le porteur) : avancer par **tranches verticales** — pour chaque étape : API, puis ses écrans, puis tests et docs. Le cahier § 8 n'a pas encore été mis à jour en ce sens (à faire par l'`expert-fonctionnel` avec une ligne d'historique).

| # | Bloc | API | Front | Références |
|---|---|---|---|---|
| 1 | **Finir l'étape 2** | tests d'intégration manquants, revue `code-reviewer` | revue du front faite et corrigée le 23/11 (lien d'évitement, focus après navigation, chargement annulable du formulaire admin, id non-GUID, déconnexion, changement de compte dans un autre onglet, script e2e) ; relancer `pile-e2e.sh` (non rejoué depuis) | `architecture.md` § 0 |
| 2 | **Réservations (étapes 3-4)** : demande de place « En attente » qui bloque une place, numéro à contacter, mes réservations, annulation par le membre | table `Reservations`, verrou `UPDLOCK` + index unique filtré (ADR 0004), `EventOccupancyReader` à brancher | bouton « Demander une place » pour un membre (masqué aujourd'hui, UX-9), page `/mes-reservations` (`authGuard`), affichage du numéro (`GET /api/settings/contact`) | US-D1 à D4, R2, R7, ADR 0004 |
| 3 | **Traitement admin et expiration (étapes 4-5)** : demandes à traiter, confirmer / refuser, marquer « payé », inscrits ; expiration des demandes | `ExpiresAt`, tâche périodique qui journalise ses erreurs (ADR 0005) | écrans admin « Demandes », « Inscrits » ; les 409 `registrants_warning` et `capacity_below_reserved` deviennent possibles (déjà gérés par le formulaire) | US-E4 à E7, ADR 0005 |
| 4 | **Annulation d'un événement** (étape 7) et suppression d'un publié sans réservation | transition `Cancelled` | bouton « Annuler l'événement » | US-E3, Q29 |
| 5 | **Actualités** (étape 8) | entité, CRUD admin, liste publique (10 par page) | `/actualites`, `/actualites/:id`, admin ; `/` redirigera vers les actualités | US-B1, B2, E1 |
| 6 | **Suppression de compte** | suppression physique, réservations détachées, dernier admin protégé (RG-09) | bouton dans « Mon compte » + confirmation | US-F2, ADR 0007 |
| 7 | **S3 : CI/CD et Azure** | `ci.yml` (build, tests .NET avec Testcontainers, `npm ci`, `npm run lint`, `npm run test:ci`, `npm run build`, `docker build` des deux images) ; `cd.yml` (GHCR, migration bundle, App Service, Static Web Apps), OIDC, Key Vault, actions « avant production » de `architecture.md` § 9 | `environment.azure.ts` + `fileReplacements`, `staticwebapp.config.json` (repli SPA, mêmes en-têtes de sécurité, `connect-src` élargi à l'URL d'App Service), `Cors:AllowedOrigins` = URL Static Web Apps | `architecture.md` § 10-11, ADR 0017 § 2 |
| 8 | **S4 : assistant RAG** | `POST /api/assistant/questions`, embeddings, index vectoriel (type `VECTOR` d'Azure SQL ou AI Search), ADR à écrire | `features/assistant/` | `architecture.md` § 12 |
| — | **Documentation utilisateur** | — | guides membre et admin (`docs/guide-utilisateur/`) par le `documentaliste`, à partir de `regles-metier.md` § 6 et `libelles-ecrans.md` | CLAUDE.md |

## 4. Décisions et questions en attente

| # | Sujet | Pour qui | État / proposition |
|---|---|---|---|
| D1 | ~~**401 au lieu de 404** pour un visiteur sur `/api/events/<pas-un-guid>`~~ | expert-technique (API) | **résolu le 23/11/2026** : API corrigée. Route de repli `GET /api/events/{id}` sans contrainte (`EventsController.GetMalformedId`, anonyme, GET seulement) → 404 `not_found` identique à un identifiant inconnu. Routes admin : repli `AdminEventsController.MalformedId` soumis à `AdminOnly` → visiteur 401, membre 403 (avant : 404, fuite), admin 404. Tests : `tests/Aaas.Api.IntegrationTests/Events/MalformedIdTests.cs` ; `api.md` à jour |
| D2 | Nom affiché, logo, couleurs de l'association (UX-2) | porteur | **fait le 24/11/2026** (§ 1.4) : « A.A.A.S », logo officiel, palette de la maquette. Reste à confirmer : nom officiel exact (statuts), valeurs exactes des couleurs (lues approximativement sur la maquette), rendu à l'œil |
| D3 | Politique de confidentialité : nom officiel, adresse, e-mail, date de mise en ligne, déclaration INPDP | porteur / association | texte provisoire publié avec la mention « Version provisoire » ; champs entre crochets dans `app-info.ts` ; tout changement du texte = nouvelle version (`PrivacyPolicy:CurrentVersion`) |
| D4 | Message « compte bloqué » : `libelles-ecrans.md` propose un texte générique, le front affiche celui de l'API (avec la durée) | expert-fonctionnel | garder le message de l'API (plus précis) ; à confirmer |
| D5 | Passage aux tranches verticales (P-1) à reporter dans le cahier § 8 | porteur / expert-fonctionnel | défaut accepté, cahier à mettre à jour |
| D6 | ADR 0001 à 0013 toujours « Proposé » | porteur | validation formelle à faire (questions ouvertes : `architecture.md` § 13) |
| D7 | Durée de session 60 min sans « rester connecté » ; saisie perdue si la session expire pendant un long formulaire (UX-5) | porteur | défaut validé pour le MVP ; à revoir après usage réel |
| D8 | Message SQL brut dans les autres erreurs 500 (option B recommandée) ; limitation de débit avant production | porteur | ouvert (`architecture.md` § 9 et § 13) |
| D9 | Suite e2e outillée (Playwright) | porteur | non au MVP (Q-T1) ; le script CDP `front/e2e/` sert de contrôle manuel |

## 5. Pour une nouvelle session (agents)

- Lire dans l'ordre : `CLAUDE.md`, ce fichier, `docs/cahier-des-charges.md`, `docs/technique/architecture.md` (§ 0, § 6, § 13), puis le plan ou l'ADR de la tâche.
- Rôles : `expert-fonctionnel` → `architecte` → `expert-technique` → `testeur` → `code-reviewer` → `documentaliste` (CLAUDE.md).
- Le porteur fait lui-même ses commits (identité `Khalil-B-L`) ; un instantané intermédiaire du front a été committé le 23/11 (`ae6ab12`), le reste du travail de S2 est dans l'arbre de travail, non committé.
- Fiches de révision à compléter au fil de l'eau : `docs/revision/` (`angular.md`, `docker.md`, `flux-ecran-api-base.md`, `aspnet-identity-jwt.md`, `ef-core-concurrence.md`, `dotnet-erreurs-journalisation.md`).
- Toute modification d'un DTO dans `api.md` se reporte dans `front/src/app/core/models/api-models.ts` ; tout message de validation de l'API dans `front/src/app/shared/forms/validation-messages.ts`.
