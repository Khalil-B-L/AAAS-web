# État d'avancement et reprise — AAAS

> Tenu par l'agent `expert-technique`. **But : reprendre le projet depuis un clone Git neuf, sans contexte de session.** Mis à jour le 24/11/2026, à la fin de la **tranche Actualités** (étape 8, § 1.11).
> Document de référence du périmètre : [`docs/cahier-des-charges.md`](../cahier-des-charges.md) ; user stories : [`docs/fonctionnel/mvp.md`](../fonctionnel/mvp.md) (v5) ; libellés : [`docs/fonctionnel/libelles-ecrans.md`](../fonctionnel/libelles-ecrans.md) (v1.1) ; consignes de travail : [`CLAUDE.md`](../../CLAUDE.md).

## 1. Où en est-on (résumé)

| Bloc | État | Détail |
|---|---|---|
| API étape 1 — comptes, premier admin | fait | inscription, connexion, `GET /api/account/me`, rôles, sondes `/health` |
| API étape 2 (+3) — événements admin, consultation publique, numéro de contact | fait (19/11) | ADR 0009 à 0013, `plan-etape-2.md` ; tests d'intégration complets et revue : à finir (voir `architecture.md` § 0) |
| **Front S2 — écrans des étapes 1 et 2** | **fait (23/11), à relire** | `front/` ; plan [`plan-front-angular.md`](plan-front-angular.md) T0 à T10 ; ADR 0014 à 0017 **acceptés** |
| **Réservations, tâche A** — demander, suivre, annuler (membre) | **fait (24/11), à relire** | § 1.5 à 1.8 ; plan [`plan-reservations.md`](plan-reservations.md) ; ADR 0004 à 0006 et 0018 à 0021 **acceptés** |
| **Réservations, tâche B** — traitement par l'admin (demandes à traiter, inscrits, confirmer, refuser, annuler, « Payée ») | **fait (24/11), à relire** | § 1.10 |
| **Réservations, tâche C** — expiration périodique (`BackgroundService`, seulement après activité de l'API) | **fait (24/11), à relire** | § 1.9 |
| **Actualités** (étape 8) — liste et détail publics, accueil, gestion admin avec aperçu | **fait (24/11), à relire** | § 1.11 ; plan [`plan-actualites.md`](plan-actualites.md) ; ADR 0022 et 0023 **acceptés** |
| Suppression de compte, annulation d'événement | à faire | § 3 |
| **S3 — CI GitHub Actions** (`ci.yml`, `dependabot.yml`) | **fait (24/11), à valider au premier push** | `exploitation.md` § 10 ; actionlint OK, jamais exécuté sur GitHub ; `-warnaserror` échoue aujourd'hui sur `xUnit2012` (`tests/Aaas.Domain.Tests/News/NewsRulesTests.cs` l. 140) |
| S3 — CD (`cd.yml`) et Azure | à faire | § 3 |
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

### 1.5 Réservations, tâche A — demander, suivre, annuler (24/11/2026)

Plan : [`plan-reservations.md`](plan-reservations.md) (accepté : valeurs par défaut P-R1 à P-R8) ; ADR 0004, 0005, 0006 et 0018 à 0021 **acceptés** (porteur, 24/11/2026). Libellés : `docs/fonctionnel/libelles-ecrans.md` v1.1 §§ 8, 9, 11. Règles : `regles-metier.md` § 3 bis ; endpoints : `api.md` « Réservations — membre ».

| Tâche du plan | Fait | Fichiers principaux |
|---|---|---|
| A1 Domain | `ReservationStatus` (1 à 6, figés), `Reservation` (`Request`, `CancelByUser`, `Confirm` **anticipé de B1**, `Expire`, `RescheduleDeadline`, `IsActive`, `IsDue`, `EffectiveStatus`, `CanBeCancelledByUser`, `CancellableUntil`), `ReservationRules` (échéance, limite d'annulation, messages, `StateChanged`), `Event.EnsureOpenForRequests` / `EnsureCanAcceptRequest`, `BusinessRuleException.Details`, nouveaux codes + `ErrorDetailKeys` | `src/Aaas.Domain/Reservations/`, `Events/Event.cs`, `Common/` |
| A2 Infrastructure | `ReservationConfiguration` (index unique filtré, `INCLUDE`, 4 `CHECK`, 3 FK `NO ACTION`), `DbSet`, **migration `AddReservations`** (script relu), options `Reservations` validées au démarrage, traduction de l'unicité dans `EventLock` (`UniqueConstraintViolationException` sans `InnerException`) | `Persistence/Configurations/ReservationConfiguration.cs`, `Persistence/Migrations/20261124113136_AddReservations.cs`, `Persistence/EventLock.cs`, `DependencyInjection.cs` |
| A3 Application | `ReservationQueries` (`IsActive`, `IsCurrentForMember`, `Not`), **`EventOccupancyReader` réel** (une requête `GROUP BY`), `ReservationExpiryService.ExpireDueForEventAsync` (`ExecuteUpdate`), `MemberReservationService` (demande sous verrou, `now` lu après le verrou ; liste ; lecture ; « ma demande » ; annulation par `rowversion`), recalcul des échéances dans `AdminEventService.UpdateAsync` | `src/Aaas.Application/Reservations/`, `Events/EventOccupancyReader.cs`, `Events/AdminEventService.cs`, `Options/ReservationOptions.cs` |
| A4 API | `MyReservationsController` (`/api/me/...`, routes de repli pour identifiant mal formé), `ApiExceptionHandler` recopie `Details` dans les extensions, `NoStoreMiddleware` (`Cache-Control: no-store` sur `/api/me` et `/api/admin`, erreurs comprises), section `Reservations` dans `appsettings.json` | `src/Aaas.Api/Controllers/MyReservationsController.cs`, `Errors/` |
| A5 Tests | voir § 1.6 | `tests/*/Reservations/`, `Support/ReservationApiTestBase.cs` |
| A6 Front | modèles, `ReservationsApi`, `ContactStore`, `ContactCard`, libellés de statut, panneau « Ma réservation » dans le détail (**UX-9 levé**), page `/mes-reservations` (`authGuard`), lien « Mes réservations » dans l'en-tête et dans « Mon compte », annulation partagée (`ReservationCancellation`) ; `pile-e2e.sh reservations` | `front/src/app/features/reservations/`, `core/contact/`, `shared/ui/contact-card.ts`, `shared/reservations/`, `core/api/reservations.api.ts` |
| A7 Docs | `api.md`, `regles-metier.md` (§ 3 bis, codes, § 6), `architecture.md` (v9), `exploitation.md` (options, SQL, curl, e2e), ce fichier ; fiches `docs/revision/sql-server-verrous-isolation.md` (nouvelle), `ef-core-concurrence.md` (§ 9-10), `angular.md` (§ 17), `dotnet-erreurs-journalisation.md` | `docs/` |

Parcours démontrable : un membre demande la dernière place, voit le numéro à contacter (liens `tel:` et WhatsApp), annule ; un second membre la reprend ; une demande non traitée cesse de compter après 48 h, sans tâche de fond (vérifié par les tests à horloge simulée).

### 1.6 Vérifications faites le 24/11/2026 (tâche A)

| Vérification | Résultat |
|---|---|
| `dotnet build` (API, donc toute la chaîne) | OK, 0 avertissement |
| `dotnet test tests/Aaas.Domain.Tests -m:1` | **215** verts (relancé le 24/11 après la revue ; 123 à la livraison, dont 28 sur les réservations et `EnsureCanAcceptRequest`) |
| `dotnet test tests/Aaas.Application.Tests -m:1` | **101** verts (relancé le 24/11 après la revue ; dont DTO de demande, requête de liste, `ReservationOptions`) |
| `dotnet test tests/Aaas.Infrastructure.Tests -m:1` | **63** verts (dont 4 : traduction 2601/2627, 547 et conflit de `rowversion` non traduits) |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1` **après la revue** | **249** tests dont 3 nouveaux (annulation rejouée si le statut n'a pas changé ; modification de date rejouée contre une annulation de membre ; `now` lu sous verrou) : **non exécutés**, moteur Docker arrêté (Testcontainers) ; à relancer |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1` (Testcontainers, RCSI) | **220** verts, 1 min 44 s, dont 28 nouveaux : **concurrence** (`[Trait("Category", "Concurrency")]`) 20 membres / 1 place et 30 / 3 places (exactement C acceptées), double clic, **verrou déterministe** (une `SqlConnection` tient `UPDLOCK` : la demande attend > 1 s puis passe en 201 après `COMMIT`), capacité 5 → 3 en parallèle, dépublication en parallèle ; **parcours membre** (201 + `Location` + `no-store`, 400, 404 brouillon / inconnu, `event_cancelled`, `reservations_closed`, échue sans tâche de fond puis redemande, `already_reserved` avant `event_full`, J−3 / J−24 h exact / J−10 h, double annulation, échue → message membre, « ma demande » 204/200/404, « À venir » avec événement en cours puis « Historique », isolation entre membres, 401 et identifiants mal formés, admin qui réserve pour lui-même, échéance ramenée au nouveau début) ; **tests différés de l'étape 2** (R11 chiffré, R12 puis confirmation, R14, R7 avec échues non comptées, **une seule requête** de décompte pour 20 événements) ; **schéma** (2601, `CHECK` 547) ; **concordance** SQL / mémoire de « active » (18 lignes) ; **journaux** sans e-mail, téléphone ni nom sur tout le parcours |
| `npm run test:ci` | à la livraison : **263** tests, 22 fichiers ; **après la revue (24/11) : 296 tests, 24 fichiers, verts deux fois de suite**, couverture 91,3 % (lignes 92,6 %, branches 90 %) ; nouveaux : un seul appel « ma demande » après une demande dans le détail, retour à la dernière page de « Mes réservations », id de carte de contact uniques, date de paiement |
| `npm run lint` | OK |
| `npm run build` | OK sans avertissement ; bundle initial **427,6 Ko bruts / 108,8 Ko transférés** (budget 500 Ko) ; `my-reservations` chargé à la demande (7,8 Ko) |
| Pile jetable `pile-e2e.sh up` + `reservations` + `parcours` + `down` (images Docker réelles, nginx, migration au démarrage) | `reservations` : 11 `OK` ; index filtré vérifié dans la base du conteneur (`filter_definition` = `([Status] IN ((1), (2))) AND [UserId] IS NOT NULL`) ; `parcours` navigateur : 12 `OK`, aucune violation CSP, la liste admin affiche le décompte réel (« 1 en attente ») ; pile supprimée. **La pile `docker compose` du porteur n'a pas été touchée** |

### 1.7 Décisions prises pendant la tâche A (hors plan, à relire)

| # | Décision | Pourquoi |
|---|---|---|
| T-A1 | `Reservation.Confirm(now)` écrit dès la tâche A (prévu en B1) | tester l'annulation d'une confirmée dans le Domain ; en intégration, la confirmation est simulée en SQL (`ConfirmInDatabaseAsync`) |
| T-A2 | `CancelByUser`, `CanBeCancelledByUser`, `CancellableUntil` reçoivent le **début** de l'événement (`DateTimeOffset`), pas l'entité `Event` (l'ADR 0018 écrit `(ev, now, …)`) | les listes ne chargent que quelques colonnes de l'événement (sans la description) ; la règle ne dépend que du début |
| T-A3 | `Event.EnsureOpenForRequests` (annulé, clos) séparé de `EnsureCanAcceptRequest` (+ complet) ; un brouillon donne `reservations_closed` dans le Domain | « déjà inscrit » se contrôle entre les deux (ordre de l'ADR 0018) ; le brouillon est un filet : Application répond 404 avant |
| T-A4 | `ReservationRules.StateChanged(current, forMember)` : message membre « Votre demande a expiré. Vous pouvez en faire une nouvelle. », message admin « Cette demande a expiré : le membre doit faire une nouvelle demande. » | libellés v1.1 § 11 |
| T-A5 | `no-store` par un **middleware** sur les préfixes `/api/me` et `/api/admin` (`Response.OnStarting`), pas par un filtre MVC | un filtre ne couvre ni les 401/403 de l'authentification ni les erreurs (le gestionnaire d'exceptions vide les en-têtes) |
| T-A6 | `ReservationOptions` sans la sous-section `ExpirationJob` (arrive avec la tâche C) ; bornes : durée de vie 1 min à 30 jours, délai d'annulation 0 à 30 jours | pas de configuration sans code qui la lit ; 1 min permet une démonstration locale |
| T-A7 | Messages repris **à l'identique** de `libelles-ecrans.md` § 11, donc sans point final pour certains (« Événement complet ») | consigne « à l'identique » ; à harmoniser par l'expert-fonctionnel s'il le souhaite |
| T-A8 | Détail d'un événement : plus de spinner pendant un **rechargement** (`isLoading() && !hasValue()`) | sinon le panneau enfant était détruit (et son message perdu) à chaque `reload()` |
| T-A9 | Lien « Mes réservations » dans l'en-tête (connecté) et dans « Mon compte » ; aide visiteur « Vous devez être connecté pour demander une place. » + lien « Créer un compte » | plan § 10.2 et libellés § 8 ; le § 2 de `libelles-ecrans.md` (menu) ne cite pas encore ce lien : **à compléter par l'expert-fonctionnel** |
| T-A10 | `pile-e2e.sh reservations` : nouvelle sous-commande (parcours HTTP via nginx) | vérifier la tranche dans les images Docker réelles sans toucher à la pile du porteur |

### 1.8 Points pour le `testeur` et le `code-reviewer`

**Testeur** (non couvert par la tâche A) :
- ~~théorie complète « 6 statuts × 7 actions » ; « payée annulée → reste payée »~~ : **faits avec la tâche B** (§ 1.10) ;
- ~~concurrence n° 6 (confirmer contre refuser, confirmer contre annulation par le membre)~~ : **faits avec la tâche B** (§ 1.10) ;
- `MemberReservationService` n'a pas de test unitaire (couvert par l'intégration) ;
- front : `ContactStore` quand l'utilisateur change de compte (les `params` contiennent `userId`), pagination de « Mes réservations », chemin `reservation_state_changed` depuis la liste, lien d'en-tête « Mes réservations » ; `my-reservations` à 80 % ;
- relancer seuls les tests de concurrence pour mesurer leur stabilité : `dotnet test tests/Aaas.Api.IntegrationTests -m:1 --filter "Category=Concurrency"` (verts à chaque passage du 24/11).

**Code-reviewer** :
- **corrigé après revue (24/11)** : `AdminEventService.UpdateAsync` lit `now` **dans** le bloc verrouillé et le renvoie pour le DTO (une demande échue pendant l'attente du verrou n'est plus ranimée, `CK_Reservations_Deadline` ne peut plus être violée) ;
- **corrigé après revue (24/11)** : annulation du membre pendant le recalcul des échéances. Côté admin, si le conflit de `rowversion` ne porte que sur des `Reservation`, le bloc verrouillé est rejoué **une fois** (plus de `concurrent_update` inexact). Côté membre, si le statut effectif relu est celui vu avant `CancelByUser` (seule l'échéance a changé), suivi EF vidé, ligne relue, règle réappliquée **une fois** ; sinon 409 `reservation_state_changed` comme avant. `IAppDbContext` expose désormais `ChangeTracker` ;
- `MemberReservationService` : projections EF avec un `record` (`ReservationEventRow`) et une classe à initialiseur (`ReservationWithEvent`) filtrée après projection ; `ReservationQueries.IsCurrentForMember` compose deux expressions par `ExpressionVisitor` (sous-requête `EXISTS` sur `Events`) ;
- `EventLock.SaveChangesTranslatingUniqueViolationAsync` est `internal static` pour être testé sans base ;
- `NoStoreMiddleware` placé **avant** `UseExceptionHandler` (ordre important) ;
- front : `ReservationPanel` (états, `mine.set()` après un 201, `reload()` après un 409 ; **clé `computed` avec `equal` par valeur** depuis la revue, pour ne pas relire « ma demande » quand le parent relit le détail), `ReservationCancellation` (`switchMap` + `catchError` → résultat typé) ; `MyReservations` revient à la dernière page existante si la page courante revient vide (`effect`) ; `pile-e2e.sh reservations` rejouable (compare aux places lues avant, rend la place en fin de parcours).

### 1.9 Réservations, tâche C — expiration automatique (24/11/2026)

Plan : `plan-reservations.md` § 11 (C1, C2) ; ADR 0005 § 5 et **0020**. Règle : `regles-metier.md` § 3 bis (« Écriture de Expirée ») ; exploitation : `exploitation.md` § 4 et **§ 8 bis**.

| Tâche | Fait | Fichiers |
|---|---|---|
| C1 Application | `ReservationExpiryService.ExpireDueAsync(now, ct)` : `ExecuteUpdateAsync` global idempotent (`WHERE Status = 1 AND ExpiresAt <= @now`), derrière l'interface `IExpireDueReservations` (doublure dans les tests) ; options `Reservations:ExpirationJob` (`Enabled` = true, `Interval` = 5 min, bornes 1 min à 1 h, `OnlyWhenActive` = true), validées au démarrage | `src/Aaas.Application/Reservations/ReservationExpiryService.cs`, `Options/ReservationOptions.cs`, `DependencyInjection.cs` |
| C1 Infrastructure | `ApiActivity` (drapeau `Volatile.Write` / `Interlocked.Exchange`) ; `ExpirePendingReservationsJob : BackgroundService` (`PeriodicTimer(interval, TimeProvider)` créé dans `StartAsync`, premier passage au démarrage, scope DI par passage, `try/catch` par passage, arrêt propre, journaux 5001 à 5005 sans donnée personnelle ni message SQL) ; toujours enregistré, `Enabled` lu dans `StartAsync` | `src/Aaas.Infrastructure/Jobs/`, `DependencyInjection.cs` |
| C1 Api | `UseApiActivityTracking` en tête du pipeline : une réponse `/api/` de code < 400 compte ; `/health`, `/openapi` et les 4xx/5xx (robot anonyme en 401/404) non (correction de revue) | `src/Aaas.Api/Jobs/ApiActivityMiddleware.cs`, `Program.cs` ; `appsettings.json` |
| C1 Tests | unitaires (`FakeTimeProvider`, doublure scoped) ; intégration (tâche réelle contre SQL Server) ; tâche **désactivée par défaut** dans `ApiFactory` | voir ci-dessous |
| C2 Docs | `exploitation.md` § 4 et § 8 bis (journaux, démonstration locale, diagnostic) ; `regles-metier.md` § 3 bis ; fiche **`docs/revision/dotnet-background-service.md`** ; `api.md` inchangé (aucun endpoint) | `docs/` |

Vérifications (24/11) :

| Vérification | Résultat |
|---|---|
| `dotnet build src/Aaas.Api` | OK |
| `dotnet test tests/Aaas.Application.Tests -m:1` | **120** verts (dont 10 `ExpirationJobOptionsTests` : défauts, intervalle 10 s / 59 s / 1 h 01 / 2 h / négatif refusés avec le nom de la clé, bornes acceptées) |
| `dotnet test tests/Aaas.Infrastructure.Tests -m:1` | **74** verts (dont 11 nouveaux : `ExpirePendingReservationsJobTests` — un passage au démarrage puis un par `Advance`, avec le `now` de l'horloge simulée ; un scope créé **et libéré** par passage ; sans activité aucun appel, puis une requête → un appel → de nouveau rien ; SQL 2627 (message avec e-mail) journalisée `Error` **sans exception ni message**, passage suivant exécuté ; exception non SQL journalisée avec sa pile ; journal « 3 demande(s) » et rien pour 0 ; désactivée → aucun passage, `ExecuteTask` nul ; arrêt pendant l'attente et **pendant un passage** sans journal d'erreur ; `ApiActivityTests`) |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1 --filter "FullyQualifiedName~ExpirationJob"` | **3** verts : demande échue → 4 en base, confirmée intacte, non échue intacte, plus aucune `Status = 1` échue, place rendue, journal « 1 demande(s) » sans e-mail / nom / téléphone / identifiant ; le même membre redemande après le passage ; `OnlyWhenActive` : `/health` ne compte pas, 48 h sans requête → ligne toujours à 1, une requête `/api/events` → ligne à 4 |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1` (suite complète) | 1er passage : 272 / 283, **11 échecs tous à ~6 min** (délais dépassés : l'agent de la tâche B lançait ses tests sur le même Docker au même moment) ; relancés : les 10 classes concernées **150 / 150**, `AdminReservation*` + `ExpirationJob*` **34 / 34**. Suite complète à rejouer seule sur un poste calme |
| Pile `docker compose` du porteur | non touchée (consigne) ; démonstration locale décrite dans `exploitation.md` § 8 bis, non rejouée |

Décisions prises pendant la tâche C (hors plan, à relire) :

| # | Décision | Pourquoi |
|---|---|---|
| T-C1 | `ApiActivity` est un **drapeau** consommé par le job (`TryConsume`), pas « l'heure de la dernière requête » (ADR 0020 § 4) | la question est « une requête depuis le passage précédent ? » ; un drapeau ne dépend d'aucune horloge (réelle ou simulée) et ne peut pas rater une requête arrivée pendant le passage |
| T-C2 | `AddHostedService` **toujours** appelé ; `Enabled` lu dans `StartAsync` (le plan disait « si `Enabled` ») | lire la configuration pendant l'enregistrement ne voit pas les valeurs ajoutées par `WebApplicationFactory` ; via `IOptions`, la valeur est validée et identique partout. Désactivée : aucun timer, aucun accès à la base, une ligne 5002 |
| T-C3 | Interface `IExpireDueReservations` (Application) implémentée par `ReservationExpiryService` | doubler le service dans les tests du job (exception SQL, comptage, scope) ; le service est `sealed` |
| T-C4 | Timer créé dans `StartAsync`, pas dans `ExecuteAsync` | depuis .NET 10, `ExecuteAsync` démarre sur le pool : sans cela, un `Advance` immédiat du test pouvait précéder la création du timer |
| T-C5 | `now` lu **avant** de consommer l'activité | un passage décide de son heure en démarrant ; le test d'activité peut ainsi avancer l'horloge dès que l'activité est consommée sans que ce passage « voie » l'heure future |
| T-C6 | Erreur : type + numéro SQL **sans exception** dès qu'une `SqlException` est dans la chaîne (l'ADR ne l'imposait que pour 2601/2627/2628) ; exception complète sinon | consigne « jamais de message SQL » ; le numéro suffit au diagnostic |
| T-C7 | Après un échec, l'activité n'est **pas** remise : le passage suivant ne réessaie que s'il y a eu une nouvelle requête | une erreur persistante ne doit pas réveiller la base toutes les 5 min ; l'exactitude ne dépend pas de la tâche |
| T-C8 | Test « exception → le job continue » en **unitaire** seulement (pas en intégration) | conforme au plan § 9.3 ; provoquer une exception SQL dans l'API réelle demanderait une doublure injectée dans `ApiFactory` |

Corrections après revue `code-reviewer` (« OK pour committer après corrections », 24/11) :

| # | Correction | Fichiers |
|---|---|---|
| R-C1 | Activité = réponse **< 400** sur `/api/` : le middleware appelle `next` puis teste le code ; un robot anonyme (401/404) ne réveille plus Azure SQL serverless | `ApiActivityMiddleware.cs`, `Program.cs` (commentaire), `exploitation.md` § 8 bis, `regles-metier.md` § 1.3 et § 3 bis, ADR 0020 § 4 |
| R-C2 | ADR 0020 : paragraphe « Mise en œuvre (24/11/2026) » (T-C1, T-C2, T-C6, T-C7, activité < 400, annulation SqlClient, deadlock 1205 théorique géré par `EnableRetryOnFailure`) ; § 5 (`TryGetSqlErrorNumber`) ; § 7 (premier passage sans effet par défaut, voulu) ; `plan-reservations.md` § 6 (`AddHostedService` toujours, `RecordRequest`/drapeau) ; `exploitation.md` § 8 bis | `docs/` |
| R-C3 | Arrêt pendant un passage : SqlClient lève une `SqlException` (« Operation cancelled by user », n° 0) ; `catch (Exception) when (stoppingToken.IsCancellationRequested)` la convertit en `OperationCanceledException` (pas de journal `Error`) | `ExpirePendingReservationsJob.cs` |
| R-C4 | Plus d'attente fixe dans les assertions négatives : la règle `OnlyWhenActive` est testée par `RunOnceAsync` appelé directement (unitaire et intégration) ; le timer reste testé pour le cadencement. `InternalsVisibleTo("Aaas.Api.IntegrationTests")` ajouté dans `Aaas.Infrastructure.csproj` | tests ci-dessous |
| R-C5 | Commentaire du rejeu `DbUpdateConcurrencyException` : la tâche d'expiration est aussi un écrivain sans verrou d'événement (commentaire seulement) | `AdminEventService.cs` |

Vérifications après corrections (24/11) :

| Vérification | Résultat |
|---|---|
| `dotnet test tests/Aaas.Application.Tests -m:1` | **120** verts |
| `dotnet test tests/Aaas.Infrastructure.Tests -m:1` | **78** verts (le test d'activité au timer remplacé par 3 tests : sans activité `RunOnceAsync` → 0 appel et aucun scope ; avec activité → 1 appel, drapeau consommé, puis 0 ; timer + activité → 1 passage ; plus 2 tests d'annulation par `SqlException` n° 0 : arrêt de l'hôte sans journal d'erreur, passage direct → `OperationCanceledException` du jeton) ; aucun avertissement |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1 --filter "FullyQualifiedName~ExpirationJob\|FullyQualifiedName~ApiActivity"` | **3** verts ; le test d'activité vérifie en plus `GET /api/inconnu` anonyme → 401 sans drapeau, `GET /api/events` → 200 avec drapeau, et 48 h sans requête par `RunOnceAsync` direct (plus d'attente de 500 ms) |

Points pour le `testeur` et le `code-reviewer` :
- **Testeur** : pas de test d'intégration de l'arrêt de l'hôte pendant un passage réel (couvert en unitaire) ; pas de test de deux instances simultanées (idempotence argumentée par le `WHERE`) ; suite d'intégration complète à rejouer seule.
- **Code-reviewer** : `catch (Exception)` avec `#pragma CA1031` dans `RunOnceAsync` ; `Dispose` du timer ; `ApiFactory` coupe la tâche par défaut (impact sur les tests de la tâche B : aucun, ils n'en dépendaient pas) ; `InternalsVisibleTo` ajouté pour les tests d'intégration.

### 1.10 Réservations, tâche B — traitement par l'administrateur (24/11/2026)

Plan : `plan-reservations.md` § 11 (B1 à B5) ; stories US-E4 à E7 ; libellés `libelles-ecrans.md` v1.1 §§ 2, 10, 11. Règles : `regles-metier.md` § 3 bis ; endpoints : `api.md` « Réservations — administration ».

| Tâche | Fait | Fichiers principaux |
|---|---|---|
| B1 Domain | `Reservation.Refuse(reason, now)` (motif normalisé, ≤ 500), `CancelByAssociation(eventEndsAt, now)` (`event_ended` à la fin), `MarkPaid(eventIsFree, adminId, now)` (ordre : statut final → `reservation_state_changed`, gratuit → `payment_not_applicable`, en attente → `reservation_not_confirmed` ; déjà payée → sans effet), `UnmarkPaid()` (tout statut), `AdminActions(...)` → `ReservationActions` ; messages dans `ReservationRules` | `src/Aaas.Domain/Reservations/` |
| B2 Application + Infrastructure | `IIdentityService.GetContactsAsync` (une requête `IN`, `MemberContact`) ; `AdminReservationService` : `ListPendingAsync` (échéance croissante), `GetEventRegistrantsAsync` (3 groupes triés, occupation sur le statut effectif), `ConfirmAsync`, `RefuseAsync` (400 `errors.reason` après `Trim`), `CancelAsync`, `MarkPaidAsync`, `UnmarkPaidAsync` ; `rowversion` + **réapplication unique** si le statut effectif n'a pas changé (même schéma que l'annulation membre) ; DTO admin | `src/Aaas.Application/Reservations/AdminReservationService.cs`, `ReservationDtos.cs`, `Accounts/IIdentityService.cs`, `Infrastructure/Identity/IdentityService.cs` |
| B3 API | `AdminReservationsController` (`AdminOnly`, `no-store` par `NoStoreMiddleware`) : `GET /api/admin/reservations/pending`, `GET /api/admin/events/{eventId}/reservations`, `POST .../confirm|refuse|cancel|mark-paid|unmark-paid` ; refus avec corps **facultatif** (`EmptyBodyBehavior.Allow`) ; routes de repli (404 admin, 403 membre, 401 visiteur) | `src/Aaas.Api/Controllers/AdminReservationsController.cs` |
| B4 Front | `AdminReservationsApi`, modèles admin ; `/admin/demandes` (`PendingRequests`), `/admin/evenements/:id/inscrits` (`EventRegistrants`, déclarée avant `:id`) ; `RefuseDialog` (motif facultatif, compteur, 500) ; `AdminReservationActions` (dialogue → appel → résultat typé) ; `PendingCountStore` + « Demandes (N) » dans `AdminLayout` ; liens « Inscrits » depuis la liste et la fiche admin ; coordonnées avec `tel:`, `wa.me`, `mailto:`, jamais persistées | `front/src/app/features/admin/reservations/`, `core/api/admin-reservations.api.ts`, `admin-layout.ts`, `admin.routes.ts` |
| B5 Docs | `api.md`, `regles-metier.md` (§ 3 bis, codes, § 6), `architecture.md` (v9.1 : § 0, § 5, § 6), `exploitation.md` (curl admin), fiches `angular.md` § 18 et `ef-core-concurrence.md` § 12, ce paragraphe | `docs/` |

Vérifications (24/11) :

| Vérification | Résultat |
|---|---|
| `dotnet build src/Aaas.Api` | OK, 0 avertissement |
| `dotnet test tests/Aaas.Domain.Tests -m:1` | **215** verts (dont `ReservationAdminTests` : théorie **6 statuts × 7 actions** = 42 cas, échue non écrite, motif, `event_ended`, « Payée » / gratuit / en attente, **payée puis annulée** par le membre et par l'association, actions) |
| `dotnet test tests/Aaas.Application.Tests -m:1` | **110** verts (dont `AdminReservationServiceGuardTests` : motif 501 → 400, 500 + espaces accepté, pagination) |
| `dotnet test tests/Aaas.Infrastructure.Tests -m:1` | **63** verts |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1` | **280** verts (2 min 8 s), dont 31 de la tâche B : `AdminReservationTests` (liste par échéance + `no-store` + compteur ; confirmer / double confirmation ; échéance − 1 min / échéance ; refus avec motif visible du membre puis redemande ; refus sans corps, `{}`, blanc, 500, 501 ; annulation admin J−2 h et pendant l'attente ; `event_ended` puis « Payée » après la fin ; payée → annulée par le membre → reste payée → retrait ; payée → annulée par l'association ; gratuit ; inscrits groupés et triés avec compte supprimé et échue non écrite ; inscrits vides / 404 ; **R11, R12, R14 atteints par de vraies confirmations**, puis dépublication après annulation admin ; 401/403 + `no-store` sur les 7 routes ; identifiants mal formés ; **journaux sans e-mail, téléphone, nom ni motif**) et `AdminReservationConcurrencyTests` (`Category=Concurrency`, 5 tours chacun : confirmer contre refuser, confirmer contre annulation du membre, double confirmation de deux admins) |
| `npm run test:ci` | **318** tests, 25 fichiers, verts (22 nouveaux dans `admin-reservations.spec.ts`) ; couverture totale 91,6 % ; `features/admin/reservations` 98,7 %, `event-registrants` 90,9 %, `pending-requests` 91,8 % |
| `npm run lint` | OK |
| `npm run build` | OK sans avertissement ; bundle initial **428,1 Ko bruts / 109 Ko transférés** (budget 500 Ko) ; `event-registrants` chargé à la demande (10,6 Ko) |
| Pile `docker compose` du porteur, `pile-e2e.sh` | **non touchées** (consigne) ; parcours navigateur de la tâche B non rejoué |

Décisions prises pendant la tâche B (hors plan, à relire) :

| # | Décision | Pourquoi |
|---|---|---|
| T-B1 | `CancelByAssociation(eventEndsAt, now)`, `MarkPaid(eventIsFree, adminId, now)`, `Confirm(now)` reçoivent des valeurs et non l'entité `Event` (l'ADR 0018 écrit `(ev, …)`) | même raison que T-A2 : les services ne chargent que quelques colonnes de l'événement |
| T-B2 | « Payée » sur une réservation **refusée, expirée ou annulée** → `reservation_state_changed` (message « déjà traitée » ou « expirée », `currentStatus`) plutôt que `reservation_not_confirmed` ; ce dernier est réservé à une demande **en attente** (avec `currentStatus: "Pending"`) | le message « Confirmez d'abord la demande… » n'a pas de sens pour une réservation annulée ; la ligne de l'admin est périmée, le front la relit |
| T-B3 | « Payée » sur un événement gratuit passe **avant** « en attente » (`payment_not_applicable` même pour une demande en attente) ; « Payée » deux fois : 200 sans effet (date et admin d'origine conservés) | réponse la plus stable pour l'admin ; double clic sans erreur |
| T-B4 | Annulation par l'association : contrôle du statut **avant** celui de la fin d'événement ; « fin » = `now >= EndsAt` (comme `HasEnded`) | une réservation déjà annulée reste « déjà traitée » même après la fin |
| T-B5 | Motif de refus : longueur contrôlée **après** suppression des espaces, dans le service (pas d'attribut `[MaxLength]`) ; corps du `POST .../refuse` facultatif | un motif de 500 caractères entouré d'espaces est accepté ; `curl -X POST` sans corps refuse sans motif |
| T-B6 | Concurrence admin : **réapplication unique** si le statut effectif relu est inchangé (comme le correctif de revue de `MemberReservationService.CancelAsync`) | sinon un déplacement de date ou une « Payée » simultanée donnerait « déjà traitée » à tort |
| T-B7 | `AdminReservationDto` sans `cancellableUntil` (en plus de `canCancel`, retiré par le plan) | date propre au membre ; l'admin a `actions` |
| T-B8 | « Autres » triées par **date de demande** décroissante ; confirmées : comptes supprimés à la fin ; tri des noms insensible à la casse (culture invariante, repli ordinal sans ICU) | « la plus récente d'abord » (US-E4) sans définir une date de « dernier changement » |
| T-B9 | Dialogue de refus : aide « Le motif sera visible par le membre. **Ne mettez pas de données personnelles dans le motif.** » | rappel demandé par l'ADR 0021 § 4, absent de `libelles-ecrans.md` § 10 : **à valider par l'expert-fonctionnel** |
| T-B10 | Front : après une action (succès ou 409), la liste est **relue** (`reload()`) au lieu de patcher la ligne avec `currentStatus` ; message d'erreur en bandeau de page (la ligne peut disparaître de « Demandes à traiter ») | une seule source de vérité, code plus simple ; coût : un GET de plus |
| T-B11 | Liens de contact admin : « WhatsApp » (`wa.me`) proposé pour tout membre (on ne sait pas s'il a WhatsApp) | demande de la tâche ; à confirmer par l'expert-fonctionnel |

Points pour le `testeur` :
- front : pagination de « Demandes à traiter » (`?page=`), `PendingCountStore` quand deux `refresh()` se chevauchent, erreur de chargement des inscrits (404 « Événement introuvable. »), ligne « administrateur supprimé » ;
- intégration : `GetContactsAsync` = **une** requête pour 20 inscrits (compter les `Executed DbCommand` sur `AspNetUsers`, comme le test de `GetManyAsync`) ; admin contre tâche d'expiration (tâche C) ; réapplication unique (déplacement de date pendant une confirmation) ;
- stabilité : `dotnet test tests/Aaas.Api.IntegrationTests -m:1 --filter "Category=Concurrency"` plusieurs fois.

Points pour le `code-reviewer` :
- `AdminReservationService.TransitionAsync` : boucle de réapplication, `ChangeTracker.Clear()`, relecture du statut effectif ; `ToDto` lit les coordonnées par dictionnaire (compte absent → `null`) ;
- `ReservationEventRow` (`internal`, désormais dans `ReservationEventRow.cs`) réutilisé par le service admin ;
- `AdminReservationsController` : deux méthodes de repli (`MalformedId`, `MalformedEventId`) pour garder le message « Événement introuvable. » côté événement ;
- front : `toggled` (ensemble immuable pour `busyIds`), `asRows` (typage du contexte de `ng-template`), `PendingCountStore.refresh()` (désabonnement de la requête précédente).

Corrections de revue de la tâche B (24/11, points facultatifs 4 à 9) :

| # | Correction | Fichiers |
|---|---|---|
| R4 | `TransitionAsync(…, reapplyIfStatusChanged)` : « Retirer Payée » (permis dans tous les statuts) est réappliqué même si le statut a changé entre-temps (annulation du membre → 200 au lieu de 409) | `AdminReservationService.cs` |
| R5 | Second conflit d'affilée **sans** changement de statut → 409 `concurrent_update` au lieu de « déjà traitée » ; avec changement → toujours `reservation_state_changed` | `AdminReservationService.cs`, `api.md`, `regles-metier.md` |
| R6 | « Demandes à traiter » : page vide alors que `totalCount > 0` → navigation vers la dernière page (`computed` + `effect`, `replaceUrl`) ; chargement affiché pendant la redirection | `pending-requests.ts`, `.html` |
| R7 | Noms accessibles avec contexte (texte visible en tête, WCAG 2.5.3) : « WhatsApp — Prénom Nom (nouvel onglet) », « Confirmer — Prénom Nom, Événement » (demandes), « Confirmer / Refuser / Marquer comme payée / Retirer la mention payée / Annuler la réservation — Prénom Nom » (inscrits), « Inscrits — Titre » (liste admin) | `reservations/row-labels.ts`, `pending-requests.html`, `event-registrants.html`, `admin-event-list.html` |
| R8 | `ReservationEventRow` déplacé dans son fichier ; projections recopiées commentées dans `AdminReservationService` | `Application/Reservations/ReservationEventRow.cs` |
| R9 | Motif > 500 dans le Domain : `ArgumentException` (filet inatteignable, l'Application renvoie 400 avant) au lieu de `validation_failed` (aurait donné un 409 trompeur) | `Reservation.cs`, `ReservationAdminTests` |
| — | ADR 0007 aligné sur le libellé retenu « Payée le {date} par un administrateur supprimé » (code et `api.md` déjà conformes) | `docs/adr/0007-…` |

Vérifications : Domain **215**, Application **120**, Infrastructure **74**, Intégration **286** (dont `AdminReservationReapplyTests`, 3 tests **déterministes** : un `SaveChangesInterceptor` écrit sur la ligne depuis une autre connexion juste avant l'`UPDATE` de l'admin) ; front **320** tests verts, `lint` OK, `build` OK.

### 1.11 Actualités (étape 8) — 24/11/2026

Plan : [`plan-actualites.md`](plan-actualites.md) (valeurs par défaut P-N1 à P-N7 **acceptées** par le porteur) ; ADR **0022** et **0023** passés à « Accepté (porteur, 24/11/2026) ». Libellés : `libelles-ecrans.md` v1.3 §§ 2, 3, 4, 12 à 14. Règles : `regles-metier.md` § 4 bis, § 5, § 6 ; endpoints : `api.md` « Actualités » ; exploitation : `exploitation.md` § 2 bis (pile `news`) et § 7 (`AddNews`, SQL, curl).

| Tâche | Fait | Fichiers principaux |
|---|---|---|
| N1 Domain | `NewsStatus` (1, 2 figés), `NewsFields`, `NewsRules` (normalisation `\r\n` → `\n` + `Trim`, limites 200 / 8 000 comptées après, `Excerpt` : 200 caractères sur une fin de mot, « … », émoji intact, paramètre `moreFollows`), `NewsArticle` (`Create`, `Update`, `Publish` avec `PublishedAt ??= now`, `Unpublish` qui garde la date) ; `ErrorCodes.NewsStateChanged` | `src/Aaas.Domain/News/`, `Common/ErrorCodes.cs` |
| N2 Infrastructure | `NewsArticleConfiguration` (table `News`, `Content` `nvarchar(max)`, 2 `CHECK`, `IX_News_Status_PublishedAt (Status, PublishedAt, Id)`), `DbSet<NewsArticle> News` ; **migration `AddNews`** (script relu : 1 table, 2 `CHECK`, 1 index, aucune donnée) | `Persistence/Configurations/NewsArticleConfiguration.cs`, `Persistence/Migrations/20261124201130_AddNews.cs` |
| N3 Application | `PagingQuery` **déplacée** dans `Common/` (abstraite, taille par défaut en paramètre du constructeur), `QueryablePaging.Page<T>` (remplace `EventQueries.Page`), `NotFoundMessages.PublicContent` partagé ; DTO ; `PublicNewsService` (projection `SUBSTRING` de 401 caractères), `AdminNewsService` (`rowversion` sans `IEventLock`, ordre 404 → 409 → 400) | `src/Aaas.Application/Common/`, `src/Aaas.Application/News/` |
| N4 API | `NewsController` (`/api/news`, anonyme, repli `{id}` → 404), `AdminNewsController` (`/api/admin/news`, `AdminOnly`, repli `MalformedId` → 401/403/404) ; `no-store` par le middleware existant | `src/Aaas.Api/Controllers/` |
| N5 Tests | voir ci-dessous | `tests/*/News/`, `Support/NewsApiTestBase.cs` |
| N6 Front public | modèles, `NewsApi`, `toParagraphs`, `<app-news-article>` (interpolation seulement), `/actualites` (10 par page), `/actualites/:id` ; `/` → `/actualites` ; « Actualités » en tête du menu ; logo → `/` ; « Retour à l'accueil » sur « Contenu introuvable » et « Accès réservé » ; **règle ESLint** contre `[innerHTML]`, `innerHTML`, `insertAdjacentHTML`, `document.write`, `bypassSecurityTrust*` | `front/src/app/shared/news/`, `features/public/news-*`, `app.routes.ts`, `layout/`, `front/eslint.config.js` |
| N7 Front admin | `AdminNewsApi`, `/admin/actualites` (filtre, « Première publication »), `/admin/actualites/nouvelle` et `/:id` (validateur `trimmedText`, compteur compté comme l'API, aide « Texte simple… », aperçu `toSignal(valueChanges)`, flux 409 sur toutes les actions, `news_state_changed` → relecture, suppression confirmée 204/404), sous-menu « Actualités » en premier ; `pile-e2e.sh news` | `front/src/app/features/admin/news/`, `admin.routes.ts`, `admin-layout.ts`, `front/e2e/pile-e2e.sh` |
| N8 Docs | `api.md`, `regles-metier.md`, `architecture.md` (§ 0, § 1, § 3, § 4 `Content`/`PublishedAt`, index, § 5, § 6, § 13 question 4 tranchée), `exploitation.md`, ce paragraphe ; fiches **`securite-web-xss.md`** (nouvelle), `angular.md` § 19, `ef-core-concurrence.md` § 13 | `docs/` |

Vérifications (24/11/2026) :

| Vérification | Résultat |
|---|---|
| `dotnet build` (solution) | OK, **0 avertissement** |
| `dotnet test tests/Aaas.Domain.Tests -m:1` | **242** verts (27 nouveaux : cycle de vie, republication avec la date d'origine, modification sans toucher la date, texte non encodé, limites 200/201 et 8 000/8 001, 8 000 écrits avec des `\r\n`, extrait : court, espaces, 200 exacts, coupe sur un mot, mot de 300, émoji à cheval, `moreFollows`) |
| `dotnet test tests/Aaas.Application.Tests -m:1` | **133** verts (13 nouveaux : DTO, version, pas de limite dans le DTO, tailles par défaut 10 / 20, **listes existantes toujours à 20** après le déplacement de `PagingQuery`, bornes) |
| `dotnet test tests/Aaas.Infrastructure.Tests -m:1` | **78** verts (inchangé) |
| `dotnet test tests/Aaas.Api.IntegrationTests -m:1` (suite complète, seule sur le Docker) | **348** verts (3 min 17 s), dont **62** actualités : liste triée, 25 → 10/10/5/vide, ordre stable à date égale, vide, 400 de pagination, `SUBSTRING` dans le SQL, détail, **404 identique** (brouillon, dépubliée, inconnue, `abc`, `123`), dépubliée cachée malgré sa date, jeton expiré ignoré, membre, 23:30Z renvoyé en UTC ; création 201 + `Location` + `no-store`, cycle complet avec dates simulées, suppression 204 puis 404, 400 par champ, 8 001 / 8 000 en `\r\n` / titre 201, **XSS renvoyée à l'identique** (base, public, admin), version périmée, 409 avant 400, deux `PUT` concurrents, `news_state_changed`, liste admin et filtre, `CHECK` 547, brouillon daté accepté, **journaux sans titre ni texte** ; accès : 12 routes × visiteur 401 / membre 403 avec `no-store`, 5 routes mal formées admin 404, écritures visiteur sur `/api/news/abc` 401 |
| `npm run lint` | OK (avec la nouvelle règle anti-`innerHTML`, vérifiée sur un fichier jetable : 3 erreurs attendues) |
| `npm run test:ci` | **367** tests, 26 fichiers, verts ; couverture totale 90,3 % (lignes 91,6 %) ; nouveaux (`features/news.spec.ts`) : `toParagraphs`, **DOM sans `script`/`img`/`b` pour un texte malveillant** (composant, détail, liste, aperçu), 23:30Z → « 6 décembre 2026 », 404 et id non GUID sans appel, liste vide, page au-delà, pagination, liste admin, formulaire (création + `replaceUrl`, texte blanc refusé, compteur après `Trim`, 400, version remplacée, 409 sur Enregistrer et Publier, Publier grisé si modifié, `news_state_changed`, suppression 204 / 404 / annulée, garde de sortie, aperçu), routes et en-tête |
| `npm run build` | OK sans avertissement ; bundle initial **429 Ko bruts / 109 Ko transférés** (budget 500 Ko) ; `admin-news-form` chargé à la demande (12,4 Ko) |
| Pile jetable `pile-e2e.sh up` + `news` + `reservations` + `parcours` + `down` | `news` : **18 `OK`** (dont texte renvoyé à l'identique à travers nginx, `/` servi) ; `reservations` : 13 `OK` ; `parcours` navigateur : 12 `OK`, aucune violation CSP ; pile supprimée. **La pile `docker compose` du porteur n'a pas été touchée** |

Décisions prises pendant la tranche (hors plan, à relire) :

| # | Décision | Pourquoi |
|---|---|---|
| T-N1 | Pas de `[MaxLength]` sur le **titre** du DTO non plus (le plan le disait facultatif) | même raison que pour le texte : la longueur est comptée après `Trim` par le Domain (un titre de 200 caractères entouré d'espaces est accepté) ; une seule source |
| T-N2 | `NewsRules.Excerpt(text, moreFollows)` et projection de **401** caractères (`ExcerptSourceLength + 1`) | sans cela, un texte de plus de 400 caractères dont le début contient beaucoup d'espaces pourrait tenir en 200 une fois réduit et s'afficher **sans** « … » |
| T-N3 | `PagingQuery` devient **abstraite** (constructeur `protected`) ; constantes `MaxPageSize`, `DefaultPageSize` | aucune instance directe n'existait ; empêche une requête sans taille par défaut explicite |
| T-N4 | Le 404 public d'une actualité porte `Cache-Control: no-cache,no-store` | posé par l'`ExceptionHandlerMiddleware` d'ASP.NET Core sur toute réponse d'erreur (déjà vrai pour les événements) ; seules les réponses **200** publiques sont sans `no-store` (testé) ; documenté dans `api.md` |
| T-N5 | Formulaire admin : « Supprimer » **désactivé aussi** tant que le formulaire est modifié | libellés § 13 (UX-7) : Publier, Dépublier, Supprimer |
| T-N6 | Fiche admin introuvable : « Actualité introuvable. » + « Retour à la liste » (pas la page publique « Contenu introuvable ») | libellés § 13 |
| T-N7 | Aperçu : formulaire masqué par `[hidden]` (pas `@if`) | la saisie, l'état « modifié » et la garde de sortie restent intacts pendant l'aperçu |
| T-N8 | `/admin` (sans sous-chemin) redirige toujours vers `/admin/evenements` alors que « Actualités » est en premier dans le sous-menu | moins de changements ; **à trancher** par l'expert-fonctionnel si l'on veut `/admin` → actualités |
| T-N9 | Règle ESLint étendue à `[srcdoc]`, `outerHTML`, `insertAdjacentHTML`, `document.write` | mêmes puits XSS que `innerHTML` |
| T-N10 | Compteur du texte formaté par `DecimalPipe` (« 8 000 » avec l'espace fine de la locale `fr`) | cohérent avec le reste de l'interface ; le libellé « 123 / 8 000 » est respecté |

Points pour le `testeur` :
- intégration : `PUT` sur une actualité **supprimée** entre-temps (404) ; publier pendant un `PUT` concurrent (409 `concurrent_update` attendu côté publier) ; texte de 8 000 caractères **multi-octets** (émojis : `Length` compte les unités UTF-16, comme `nvarchar`) ; `?status=draft` en minuscules (accepté, casse ignorée) ;
- front : `AdminNewsForm` sur changement de fiche pendant un envoi (`isStale`), « Fermer » du dialogue 409 (saisie conservée + bandeau), dépublier → « Dépublier » remplacé par « Publier » ; `AdminNewsList` : changement de filtre et pagination (`?status=&page=`) par le routeur ; annonce `aria-live` sous 200 caractères restants ; liens du sous-menu admin.

Points pour le `code-reviewer` :
- recherche `innerHTML|bypassSecurityTrust` dans `front/src` : **aucune occurrence** (règle ESLint active) ;
- `PagingQuery` déplacée (`Common/`) : `CamelCaseQueryNamesProvider` et `ReservationDtos` ne changent que de `using` ; `EventQueries.Page` supprimée au profit de `QueryablePaging.Page<T>` ;
- `PublicNewsService.ListAsync` : projection anonyme + `Excerpt` en mémoire ; `PublishedAt!.Value` justifié par `CK_News_PublishedAt` ;
- `AdminNewsService.TransitionAsync` (délégué `Action<NewsArticle, DateTimeOffset>`) ; `UpdateAsync` : `ConcurrencyGuard` avant la validation ;
- tests d'intégration : base `NewsApiTestBase` à horloge simulée **immobile** (tri à date égale) ; `PublicNewsTests` active le journal SQL d'EF (`Information`) pour vérifier `SUBSTRING`.

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

**Poste actuel du porteur (24/11)** : `docker compose up -d --build` appliquera aussi `AddNews` (table `News`, rien d'autre ne change ; vérification : `exploitation.md` § 7, « Actualités »). la pile `docker compose` tourne, mais ses images datent d'avant la tranche Réservations. `docker compose up -d --build` applique `AddReservations` au démarrage (comptes, événements et numéro conservés) ; vérification SQL : `exploitation.md` § 7. Historique : la pile `docker compose` qui tourne date du 11/11 (image d'API de l'**étape 1**, sans `/api/events`) et n'a pas de service `front`. Pour passer à l'état courant : `docker compose up -d --build` (reconstruit l'API et le front ; la migration `AddEventsAndAssociationSettings` s'applique au démarrage sur la base existante, les comptes sont conservés). Puis saisir le numéro de contact dans « Administration » → « Numéro de contact ».

## 3. Ce qui reste à faire (ordre proposé)

Recommandation du plan front (question P-1, défaut accepté par le porteur) : avancer par **tranches verticales** — pour chaque étape : API, puis ses écrans, puis tests et docs. Le cahier § 8 n'a pas encore été mis à jour en ce sens (à faire par l'`expert-fonctionnel` avec une ligne d'historique).

| # | Bloc | API | Front | Références |
|---|---|---|---|---|
| 1 | **Finir l'étape 2** | tests d'intégration manquants, revue `code-reviewer` | revue du front faite et corrigée le 23/11 (lien d'évitement, focus après navigation, chargement annulable du formulaire admin, id non-GUID, déconnexion, changement de compte dans un autre onglet, script e2e) ; relancer `pile-e2e.sh` (non rejoué depuis) | `architecture.md` § 0 |
| 2 | ~~**Réservations, tâche A**~~ : **fait le 24/11/2026** (§ 1.5) ; reste : revue `code-reviewer`, passage du `testeur` (§ 1.8), guides utilisateur (`documentaliste`) | — | — | `plan-reservations.md` |
| 3 | **Réservations, tâches B puis C** (`plan-reservations.md` § 11, B1 à B5, C1 et C2) — **B faite le 24/11/2026** (§ 1.10), C en cours (§ 1.9) : demandes à traiter, confirmer / refuser (motif), annuler (admin), « Payée », inscrits ; tâche d'expiration (`BackgroundService`, seulement s'il y a eu de l'activité) | B1 : `Refuse`, `CancelByAssociation`, `MarkPaid`, `UnmarkPaid` (`Confirm` existe déjà) ; B2 : `IIdentityService.GetContactsAsync`, `AdminReservationService` ; C1 : `ExpireDueAsync`, `ApiActivity`, `ExpirePendingReservationsJob`, sous-section `Reservations:ExpirationJob` | `/admin/demandes`, `/admin/evenements/:id/inscrits`, dialogue de refus, liens « Inscrits » | US-E4 à E7, US-D5, ADR 0005, 0006, 0020, 0021 |
| 4 | **Annulation d'un événement** (étape 7) et suppression d'un publié sans réservation | transition `Cancelled` | bouton « Annuler l'événement » | US-E3, Q29 |
| 5 | ~~**Actualités** (étape 8)~~ : **fait le 24/11/2026** (§ 1.11) ; reste : revue `code-reviewer`, passage du `testeur`, guides (`documentaliste`) | — | — | US-B1, B2, E1 |
| 6 | **Suppression de compte** | suppression physique, réservations détachées, dernier admin protégé (RG-09) | bouton dans « Mon compte » + confirmation | US-F2, ADR 0007 |
| 7 | **S3 : CI/CD et Azure** | ~~`ci.yml`~~ **fait le 24/11/2026, à valider au premier push** (build `-warnaserror`, tests .NET projet par projet avec Testcontainers et artefact TRX, `npm ci`/`lint`/`test:ci`/`build`, `docker build` des deux images sans publication ; `dependabot.yml` ; `exploitation.md` § 10) ; **reste à faire** : `cd.yml` (GHCR, migration bundle, App Service, Static Web Apps), OIDC, Key Vault, actions « avant production » de `architecture.md` § 9 | `environment.azure.ts` + `fileReplacements`, `staticwebapp.config.json` (repli SPA, mêmes en-têtes de sécurité, `connect-src` élargi à l'URL d'App Service), `Cors:AllowedOrigins` = URL Static Web Apps | `architecture.md` § 10-11, ADR 0017 § 2 |
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
| D6 | ADR 0001 à 0003 et 0007 à 0013 toujours « Proposé » | porteur | validation formelle à faire (questions ouvertes : `architecture.md` § 13) ; 0004 à 0006 et 0018 à 0021 **acceptés** le 24/11/2026 |
| D10 | Décisions prises pendant la tâche A des réservations (T-A1 à T-A10) | porteur / architecte | § 1.7, à relire ; T-A9 : ajouter « Mes réservations » au § 2 de `libelles-ecrans.md` (expert-fonctionnel) |
| D7 | Durée de session 60 min sans « rester connecté » ; saisie perdue si la session expire pendant un long formulaire (UX-5) | porteur | défaut validé pour le MVP ; à revoir après usage réel |
| D8 | Message SQL brut dans les autres erreurs 500 (option B recommandée) ; limitation de débit avant production | porteur | ouvert (`architecture.md` § 9 et § 13) |
| D9 | Suite e2e outillée (Playwright) | porteur | non au MVP (Q-T1) ; le script CDP `front/e2e/` sert de contrôle manuel |

## 5. Pour une nouvelle session (agents)

- Lire dans l'ordre : `CLAUDE.md`, ce fichier, `docs/cahier-des-charges.md`, `docs/technique/architecture.md` (§ 0, § 6, § 13), puis le plan ou l'ADR de la tâche.
- Rôles : `expert-fonctionnel` → `architecte` → `expert-technique` → `testeur` → `code-reviewer` → `documentaliste` (CLAUDE.md).
- Le porteur fait lui-même ses commits (identité `Khalil-B-L`) ; un instantané intermédiaire du front a été committé le 23/11 (`ae6ab12`), le reste du travail de S2 est dans l'arbre de travail, non committé.
- Fiches de révision à compléter au fil de l'eau : `docs/revision/` (`angular.md`, `docker.md`, `flux-ecran-api-base.md`, `aspnet-identity-jwt.md`, `ef-core-concurrence.md`, `sql-server-verrous-isolation.md`, `dotnet-erreurs-journalisation.md`, `dotnet-background-service.md`, `securite-web-xss.md`, `github-actions.md`).
- **Tâche B faite le 24/11/2026** (§ 1.10) : reste la revue `code-reviewer`, le passage du `testeur` et les guides admin (`documentaliste`). **Tâche C faite le 24/11/2026** (§ 1.9), même suite (revue, testeur). **Prochaine tâche technique** : bloc 4 (annulation d'un événement, `CancelByAssociation` est prêt). Le travail de la tâche A est dans l'arbre de travail, **non committé** (le porteur committe lui-même).
- Toute modification d'un DTO dans `api.md` se reporte dans `front/src/app/core/models/api-models.ts` ; tout message de validation de l'API dans `front/src/app/shared/forms/validation-messages.ts`.
