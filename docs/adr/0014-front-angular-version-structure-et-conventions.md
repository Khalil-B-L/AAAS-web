# ADR 0014 — Front Angular : version, structure et conventions

- **Statut** : Accepté (porteur, 23/11/2026) — valeurs par défaut validées (Angular 22, Material, zoneless, pas d'e2e outillé, ESLint)
- **Date** : 20/11/2026 ; mise en œuvre le 23/11/2026 (Angular 22.3.0, Material 22.3, TypeScript 6.0, Vitest 5.1, angular-eslint 22.8)
- **Mise en œuvre** : semaine 2 (front), tâche T0 du plan [`plan-front-angular.md`](../technique/plan-front-angular.md)
- **Références** : cahier § 6.1, § 7 (front Angular récent, standalone, signals) ; architecture § 6 ; ADR 0003, 0011 ; ADR 0015 à 0017

## Contexte

Le cahier impose un « Angular récent » avec composants standalone et signals. Le porteur a presque oublié Angular : chaque notion doit être expliquée, et les choix doivent correspondre à ce qu'on attend en entretien en 2026-2027. L'API expose 14 endpoints (étapes 1 et 2) ; le front des étapes suivantes (réservations, actualités) viendra s'ajouter au même squelette.

État d'Angular au 20/11/2026 (page officielle des versions) : **v22 active** (sortie le 03/06/2026, support actif jusqu'en 06/2027, LTS jusqu'en 06/2028) ; v21 en LTS ; la v23 est prévue en juin 2027 (une version majeure par an désormais). Depuis la v21, un nouveau projet est **zoneless** et teste avec **Vitest** par défaut ; la v22 rend `OnPush` par défaut, stabilise `resource`/`rxResource`/`httpResource` et les **Signal Forms**, utilise `fetch` dans `HttpClient` par défaut, exige TypeScript 6 et abandonne Node 20.

## Décision

1. **Angular 22.x** (dernière mineure au moment du `ng new`), **Node 24 LTS** (déjà installé : v24.15), versions épinglées dans `package-lock.json` (toujours `npm ci`, jamais `npm install` en CI ou dans Docker). Fichier `front/.nvmrc` = `24`.
2. **Projet** créé dans `front/` par `ng new` : sans SSR (site statique servi par nginx puis Static Web Apps), routing, feuilles de style SCSS, zoneless et Vitest (valeurs par défaut de la v22). Conventions du guide de style actuel : fichiers sans suffixe (`event-list.ts`, classe `EventList`), un composant = `.ts` + `.html` + `.scss`.
3. **Composants standalone uniquement**, `ChangeDetectionStrategy.OnPush` (défaut v22), état local en **signals** (`signal`, `computed`, `input`, `output`, `model` si besoin), injection par `inject()`, nouveau contrôle de flux des templates (`@if`, `@for` avec `track`, `@switch`). Aucun `NgModule`, aucun `zone.js`.
4. **Données serveur** : les services d'API renvoient des `Observable` (`HttpClient`, la forme la plus répandue en entreprise) ; les composants **lisent** avec `rxResource({ params, stream })`, qui expose `value()`, `isLoading()`, `error()`, `reload()` en signals et relance la requête quand un paramètre change (page, filtre, identifiant) ; ils **écrivent** (POST/PUT/DELETE) par un appel explicite dans le gestionnaire d'événement. Pas de NgRx ni de store global : l'état partagé se limite à la session (`AuthService`, ADR 0015).
5. **Routing** : routes en français (architecture § 6), composants de page chargés à la demande (`loadComponent`), espace admin dans un fichier `admin.routes.ts` chargé par `loadChildren` et protégé par `canMatch` (le code admin n'est même pas téléchargé par un membre). `withComponentInputBinding()` : les paramètres de route et de requête arrivent dans les `input()` du composant.
6. **Organisation** : `core/` (singletons : API, modèles, session, erreurs, temps), `shared/` (pipes, validateurs, petits composants réutilisables), `features/<domaine>/` (pages), `layout/` (coquille, en-tête, pied de page, page introuvable). Détail dans le plan, § 4.
7. **Client API écrit à la main** : interfaces TypeScript recopiées des DTO de `docs/technique/api.md`, un service par contrôleur (`EventsApi`, `AdminEventsApi`, `AuthApi`, `AccountApi`, `SettingsApi`), URL construites à partir d'une seule constante `environment.apiBaseUrl` (ADR 0017).
8. **Bibliothèque de composants** : **Angular Material** (thème Material 3), limitée à ce qui évite d'écrire du code d'accessibilité soi-même : champs de formulaire, boutons, case à cocher, liste déroulante, dialogue de confirmation, *snackbar*, barre d'outils, indicateur de chargement. Mise en page responsive en CSS (grille/flex), sans bibliothèque supplémentaire. *Choix soumis au porteur (question UX-1 du plan).*
9. **Langue** : `<html lang="fr">`, `LOCALE_ID = 'fr'` avec `registerLocaleData(localeFr)` ; aucun outil d'i18n (français uniquement, R27). Les textes sont écrits directement dans les templates.
10. **Tests** : Vitest via `ng test` (builder `@angular/build:unit-test`, environnement jsdom), `TestBed` pour les composants, `HttpTestingController` pour les services d'API. Pas de test de bout en bout navigateur au MVP (question au porteur). Détail dans le plan, § 17.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Angular 21 (LTS) | Plus stable en apparence, mais sort du support LTS en 06/2027, avant la fin probable du projet ; v22 apporte `OnPush` par défaut et les API de ressources stables, utiles à réviser. |
| Signal Forms (stables en v22) au lieu des Reactive Forms | Plus cohérents avec les signals, mais la stack du projet impose les reactive forms, et l'immense majorité des projets existants (donc des entretiens) les utilisent. À présenter dans la fiche de révision comme « l'étape suivante ». ADR 0016. |
| `httpResource` partout (sans services `Observable`) | Moins de code, mais cache `HttpClient` et les `Observable`, que les entretiens interrogent encore ; `rxResource` fait le pont entre les deux mondes et se montre en une ligne. |
| Client généré depuis `/openapi/v1.json` (openapi-typescript, ng-openapi-gen) | Évite la dérive des types, mais ajoute un outil et une étape de génération pour 14 endpoints ; le code généré n'apprend rien. Réévaluable en S3 (génération des seuls types dans la CI). |
| NgRx / store global | Sur-ingénierie pour un état partagé réduit à la session. |
| SSR / prérendu | Static Web Apps gratuit sert des fichiers statiques ; aucun besoin de référencement fort pour une association. |
| CSS maison sans bibliothèque | Possible et plus léger, mais dialogues, focus clavier et messages d'erreur accessibles seraient à écrire ; Material est aussi un sujet d'entretien fréquent. Reste l'alternative si le porteur préfère un rendu plus personnel. |
| Karma / Jasmine | Abandonnés par le CLI au profit de Vitest depuis la v21. |

## Conséquences

- Une seule façon de faire par sujet : les tâches suivantes (réservations, actualités) recopient les patrons des écrans de S2.
- Zoneless + `OnPush` : un template ne se met à jour que si un **signal** lu par le template change (ou un événement du template, ou un `async`). Une donnée stockée dans un simple champ de classe et modifiée dans un `subscribe` **ne s'affiche pas** : piège à documenter dans la fiche de révision.
- Les interfaces TypeScript peuvent dériver des DTO C# : tout changement de `api.md` doit être reporté dans `core/models/` dans la même tâche (règle de documentation du projet).
- Material ajoute environ 100 à 200 Ko au bundle initial ; acceptable (objectif « < 2 s », cahier § 6.3), à mesurer avec `ng build` (budgets du CLI).
- Montée de version : `ng update` une fois par an (v23 en juin 2027).

## Validation et mise en œuvre

- Le porteur a validé les valeurs par défaut le 23/11/2026 : Material (UX-1), pas de suite e2e outillée au MVP (Q-T1), ESLint (Q-T2).
- Écarts assumés lors de la réalisation (aucun ne change la décision) :
  - **Bundle initial** : les jetons et services Material lourds restent hors de `app.config.ts`. `MAT_FORM_FIELD_DEFAULT_OPTIONS` y faisait entrer form-field et `@angular/forms` (+115 Ko) : il est fourni par chaque écran à formulaire (`shared/forms/form-field-defaults.ts`). `MatSnackBar` (couche overlay) est remplacé par un petit `NotificationService` à base de signal ; `mat-toolbar` par un `<header>` en CSS. Résultat : 423 Ko bruts / 107 Ko transférés (budget 500 Ko).
  - **Modèles** : un seul fichier `core/models/api-models.ts` au lieu d'un fichier par DTO (lecture plus simple pour 15 interfaces).
  - **Polices** : polices du système (`system-ui`…) plutôt qu'une police hébergée : aucune requête externe, CSP respectée, rien à télécharger.
  - **Lecture d'une fiche modifiable** (formulaire admin, numéro de contact) : chargement explicite (`load()`) plutôt que `rxResource`, car le formulaire est l'état et doit être remplacé après chaque réponse ; les listes et détails utilisent `rxResource`.
  - **Contrôle navigateur** : `front/e2e/parcours-navigateur.mjs` (CDP, sans dépendance) pour vérifier les parcours sur une pile jetable ; ce n'est pas une suite e2e outillée.
