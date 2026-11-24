# Plan d'implémentation — Tranche « Actualités » (étape 8, bloc 5 de `etat-avancement.md`)

> Rédigé par l'agent `architecte` le 24/11/2026. Destinataires : `expert-technique` (tâches § 11), `testeur` (§ 9), `code-reviewer`, `expert-fonctionnel` (§ 14.1), porteur (§ 14.2).
> Références : cahier des charges v1.4 (§ 4.1, § 6.1, § 6.2, § 8 ordre de réalisation) ; `fonctionnel/mvp.md` v5 (US-B1, US-B2, US-E1 ; R18, R26, R27 ; Q15) ; `architecture.md` v9.1 ; ADR 0003, 0006, 0011, 0014 à 0017, 0021 ; **nouveaux ADR 0022 et 0023**.
> Statut : **proposé** (à valider : § 14).

---

## 0. Périmètre et découpage

| Tâche | Contenu | Stories | Parcours démontrable à la fin |
|---|---|---|---|
| **N-API** | table `News`, gestion admin (créer, modifier, publier, dépublier, supprimer), liste et détail publics | US-E1, US-B1, US-B2 (API) | curl : un admin crée, publie, modifie, dépublie, supprime ; un visiteur liste et lit |
| **N-Front** | `/actualites`, `/actualites/:id`, accueil `/` → actualités, menu ; admin `/admin/actualites` (liste, formulaire, aperçu, flux 409) | US-B1, US-B2, US-E1 (écrans) | sous `docker compose up` : l'admin publie une actualité, un visiteur la voit en page d'accueil |

Une seule tranche, deux moitiés (API puis écrans), conformément au cahier § 8. **Aucun lien avec les réservations** : la tranche peut passer avant ou après le bloc 4 (annulation d'un événement), que l'ordre du cahier place avant (décision P-N7).

Hors tranche : images, auteur, épinglage, Markdown, liens cliquables, publication programmée (ADR 0022, 0023).

---

## 1. Notions à réviser (pédagogie)

### 1.1 XSS et rendu de contenu dans Angular

- **XSS** (*cross-site scripting*) : faire exécuter du JavaScript à la victime **dans le site** (donc avec ses droits : son jeton dans `localStorage`, ses requêtes). Trois familles : **stockée** (le script est enregistré en base puis servi à tous, c'est le risque d'un contenu d'actualité), **réfléchie** (le script vient de l'URL et revient dans la page), **DOM** (le JavaScript du front lui-même insère une donnée non sûre dans la page).
- **Règle d'or** : on **encode à la sortie, selon le contexte** (texte HTML, attribut, URL, JavaScript), pas à l'entrée. Le serveur stocke ce qu'on lui envoie ; c'est le moteur de rendu qui encode.
- **Angular encode par défaut** : `{{ x }}` crée un nœud texte (`<script>` s'affiche à l'écran). `[href]` assainit les URL. `[innerHTML]` interprète du HTML **après** le `DomSanitizer`, qui retire scripts et attributs `on*` mais laisse passer beaucoup de balises. `bypassSecurityTrustHtml` désactive tout : c'est la porte ouverte.
- **Défense en profondeur** : la CSP nginx `script-src 'self'` (ADR 0017) empêche un script en ligne de s'exécuter même s'il était injecté. Utile, mais seconde barrière seulement.
- **Notre choix** (ADR 0023) : texte brut, interpolation uniquement, paragraphes découpés par une fonction pure et rendus par `@for`. Zéro `innerHTML` dans le projet.
- **Question d'entretien** : « Pourquoi ne pas faire `HtmlEncode` avant l'`INSERT` ? » → mauvais contexte (le JSON, un futur export ou le RAG n'en veulent pas), doubles encodages (`&amp;amp;`), et on croit être protégé alors que la sortie ne l'est peut-être pas.

### 1.2 Pagination *offset* contre *keyset*

| | *Offset* (`page`, `pageSize`) — **retenu** (ADR 0011) | *Keyset* / curseur |
|---|---|---|
| SQL | `ORDER BY PublishedAt DESC, Id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY` | `WHERE (PublishedAt < @d) OR (PublishedAt = @d AND Id > @id) ORDER BY ... FETCH NEXT 10` |
| Coût | le serveur lit puis jette les `OFFSET` premières lignes : coût qui grandit avec le numéro de page | constant, via l'index |
| Stabilité | une actualité publiée pendant la lecture décale tout : la dernière de la page 1 réapparaît en page 2 | stable |
| Navigation | « Page 3 sur 5 », lien direct vers une page, `?page=3` partageable | seulement « suivant » / « précédent » ; curseur opaque à encoder |
| Ici | quelques centaines d'actualités : coût négligeable ; le décalage est un inconfort mineur | sur-ingénierie |

Retenir pour l'entretien : *keyset* pour les flux infinis et les gros volumes (fil d'actualité, journaux), *offset* pour les listes courtes avec numéros de page. Dans les deux cas, **un tri total** (complété par `Id`) est indispensable, sinon deux lignes à la même date peuvent changer d'ordre d'une page à l'autre.

### 1.3 `nvarchar(n)` contre `nvarchar(max)`

- `nvarchar(n)` : `n` ≤ **4 000** caractères (8 000 octets, la taille d'une page de données). Au-delà : `nvarchar(max)` (jusqu'à 2 Go), stocké hors de la ligne quand il dépasse la page.
- `nvarchar(max)` ne peut pas être clé d'index ; il peut figurer en `INCLUDE`. Ici la colonne `Content` n'est ni indexée ni triée : aucune conséquence.
- La **limite métier** (8 000) est dans le Domain ; la colonne `max` n'est que le contenant.
- **Projection partielle** : `Content.Substring(0, 400)` dans un `Select` EF devient `SUBSTRING([n].[Content], 1, 400)` en SQL : la liste ne transfère pas les textes entiers.

### 1.4 Nom de classe et espace de noms

`namespace Aaas.Domain.News; public class News` : chez un appelant qui a `using Aaas.Domain.News;`, `News` est ambigu (espace de noms ou type ?), erreur CS0118 et avertissement CA1724. D'où `NewsArticle` (ADR 0022 § 1).

### 1.5 Angular : redirection d'accueil, aperçu réactif

- `{ path: '', pathMatch: 'full', redirectTo: 'actualites' }` : `pathMatch: 'full'` est obligatoire pour la route vide (sinon **toutes** les URL commencent par « rien » et seraient redirigées).
- **Aperçu** : `toSignal(form.valueChanges, { initialValue: form.getRawValue() })` transforme le flux des valeurs du formulaire en signal ; le composant `<app-news-article>` le lit et se redessine à chaque frappe (`OnPush` + signals, ADR 0014).

---

## 2. Décisions par défaut (résumé)

| # | Sujet | Décision par défaut | ADR |
|---|---|---|---|
| N-1 | Entité | `NewsArticle` (dossier `Domain/News/`), table `News` ; titre, texte, statut, date de publication, dates techniques, `rowversion` | 0022 |
| N-2 | Auteur, image, épinglage | **aucun** au MVP (minimisation, R18, Q15) | 0022 |
| N-3 | Statuts | 1 Brouillon, 2 Publiée (`tinyint` figé) | 0022 |
| N-4 | Date affichée | `PublishedAt` posée à la **première** publication, conservée par la dépublication et la republication ; modifiée par aucune modification | 0022 |
| N-5 | Suppression | permise depuis les deux statuts, après confirmation dans le front, sans `version` | 0022 |
| N-6 | Concurrence | `rowversion` seule (pas de verrou) : `PUT` avec `version` → 409 `concurrent_update` ; actions sans `version` | 0006, 0022 |
| N-7 | Format | **texte brut**, interpolation seulement, paragraphes sur les lignes vides ; aucun `innerHTML` | 0023 |
| N-8 | Limites | titre 200, texte 8 000 (après `Trim` et `\r\n` → `\n`) | 0023 |
| N-9 | Extrait | 200 caractères au plus, coupé sur un mot, « … » ; calculé par le Domain, pas stocké | 0023 |
| N-10 | Listes | *offset* (ADR 0011) ; public : publiées, `PublishedAt` décroissante puis `Id`, **10** par défaut ; admin : toutes, `UpdatedAt` décroissante puis `Id`, 20 par défaut, filtre `status` | 0011 |
| N-11 | Visibilité | brouillon, dépubliée, inconnue, id mal formé → **même 404** « Contenu introuvable. » ; admin mal formé → 401 / 403 / 404 | 0022 |
| N-12 | Cache | `no-store` sur `/api/admin/news` (middleware existant) ; rien de particulier sur `/api/news` (une dépublication doit être visible tout de suite) | 0021 |
| N-13 | Accueil | `/` → `/actualites` (UX-3) ; lien « Actualités » en tête du menu ; le logo mène à `/` | — |
| N-14 | Aperçu admin | **oui** : bascule « Aperçu » dans le formulaire, même composant que le détail public | 0023 |

---

## 3. Données et migration `AddNews`

```
News
  Id          uniqueidentifier  PK
  Title       nvarchar(200)     NOT NULL
  Content     nvarchar(max)     NOT NULL
  Status      tinyint           NOT NULL   CK_News_Status      (Status IN (1, 2))
  PublishedAt datetimeoffset    NULL       CK_News_PublishedAt (Status = 1 OR PublishedAt IS NOT NULL)
  CreatedAt   datetimeoffset    NOT NULL
  UpdatedAt   datetimeoffset    NOT NULL
  RowVersion  rowversion

  IX_News_Status_PublishedAt (Status, PublishedAt, Id)
```

- `NewsArticleConfiguration` (Infrastructure) : `ToTable("News", t => { t.HasCheckConstraint(...) })`, `HasConversion<byte>()` pour `Status`, `IsRowVersion()`, `HasMaxLength(200)` sur `Title`, `Content` sans longueur (→ `nvarchar(max)`). Les dates passent par la convention UTC existante (`UtcDateTimeOffsetConverter`).
- `IAppDbContext` : `DbSet<NewsArticle> News { get; }`.
- **Migration `AddNews`** : `dotnet ef migrations add AddNews` ; **relire le script** (`dotnet ef migrations script AddReservations AddNews`) : une table, deux `CHECK`, un index, aucune donnée. Appliquée au démarrage par compose (`Database:MigrateOnStartup=true`), comptes, événements et réservations conservés.
- Aucun index filtré : pas de contrainte `QUOTED_IDENTIFIER` nouvelle.
- `architecture.md` § 4 à corriger : `Body` → `Content`, `FirstPublishedAt` → `PublishedAt`, ajout des `CHECK` et de l'index exact.

---

## 4. Domain (`src/Aaas.Domain/News/`)

| Fichier | Contenu |
|---|---|
| `NewsStatus.cs` | `enum NewsStatus : byte { Draft = 1, Published = 2 }` (valeurs figées, commentaire comme `ReservationStatus`) |
| `NewsFields.cs` | `Title = "title"`, `Content = "content"` (clés des erreurs 400) |
| `NewsRules.cs` | constantes `TitleMaxLength = 200`, `ContentMaxLength = 8000`, `ExcerptMaxLength = 200`, `ExcerptSourceLength = 400` ; messages ; `NormalizeContent(string?)` (`\r\n`, `\r` → `\n`, `Trim`) ; `Validate(string? title, string? content) → IReadOnlyList<FieldError>` (après normalisation) ; `Excerpt(string)` (§ 4.1) ; messages `AlreadyPublishedMessage`, `NotPublishedMessage` |
| `NewsArticle.cs` | propriétés à `private set` ; `static Create(title, content, now)` → Brouillon ; `Update(title, content, now)` ; `Publish(now)` (Brouillon → Publiée, `PublishedAt ??= now`, sinon `news_state_changed`) ; `Unpublish(now)` (Publiée → Brouillon, `PublishedAt` conservée, sinon `news_state_changed`) ; `ThrowIfInvalid` en filet (même commentaire que `Event`) |
| `Common/ErrorCodes.cs` | `NewsStateChanged = "news_state_changed"` |

### 4.1 `NewsRules.Excerpt(text)`

1. Remplacer toute suite d'espaces blancs (dont `\n`) par une espace, `Trim`.
2. Longueur ≤ 200 → renvoyer tel quel.
3. Sinon : prendre les 200 premiers caractères ; si le caractère 200 est la moitié haute d'une paire de substitution (`char.IsHighSurrogate`), reculer d'un ; reculer jusqu'à la dernière espace **si** elle existe (sinon coupe nette) ; `TrimEnd` ; ajouter « … » (U+2026).

Le Domain ne lit ni l'horloge ni la base (ADR 0003) : toutes les méthodes reçoivent `now`.

---

## 5. Application (`src/Aaas.Application/News/`)

### 5.1 Socle (petit remaniement)

- **`PagingQuery` déplacée** de `Events/EventDtos.cs` vers `Common/PagingQuery.cs` (elle sert déjà aux réservations) ; la taille par défaut devient un paramètre : `protected PagingQuery(int defaultPageSize = 20)`. Les classes dérivées existantes ne changent pas de comportement. `CamelCaseQueryNamesProvider` : simple `using` à ajuster.
- **`Page<T>`** générique (`Common/QueryablePaging.cs` : `IQueryable<T>.Page(page, pageSize)`) ; `EventQueries.Page` le remplace ou l'appelle.

Pourquoi : sinon la classe de requête des actualités ne peut pas avoir 10 par défaut (un initialiseur de propriété ne se redéfinit pas dans une classe dérivée).

### 5.2 DTO (`NewsDtos.cs`)

| DTO | Champs |
|---|---|
| `NewsInput` (abstraite) | `Title` `[Required]` (message du Domain), `Content` `[Required]` ; **pas** de `[MaxLength]` sur `Content` (ADR 0023 § 3) ; `[MaxLength(200)]` facultatif sur `Title` (aucun `\r` attendu) |
| `CreateNewsRequest : NewsInput` | — |
| `UpdateNewsRequest : NewsInput` | `Version` `[Required]` `[RowVersion]` |
| `PublicNewsListQuery() : PagingQuery(10)` | — |
| `AdminNewsListQuery() : PagingQuery(20)` | `NewsStatus? Status` |
| `NewsSummaryDto` | `Id`, `Title`, `Excerpt`, `PublishedAt` |
| `NewsDetailDto` | `Id`, `Title`, `Content`, `PublishedAt` |
| `AdminNewsSummaryDto` | `Id`, `Title`, `Status`, `PublishedAt?`, `UpdatedAt` |
| `AdminNewsDto` | `Id`, `Title`, `Content`, `Status`, `PublishedAt?`, `CreatedAt`, `UpdatedAt`, `Version` |

### 5.3 Services

- **`PublicNewsService`** (`IAppDbContext`, sans horloge) :
  - `ListAsync(query)` : `Where(Status == Published)`, `CountAsync`, `OrderByDescending(PublishedAt).ThenBy(Id)`, `Page`, projection `{ Id, Title, PublishedAt, Head = Content.Substring(0, ExcerptSourceLength) }`, puis `NewsRules.Excerpt(Head)` en mémoire ;
  - `GetAsync(id)` : publiée seulement, sinon `NotFoundException("Contenu introuvable.")` (même constante que `PublicEventService.NotFoundMessage`, à partager : `Common/Errors/NotFoundMessages.PublicContent`).
- **`AdminNewsService`** (`IAppDbContext`, `TimeProvider`) :
  - `CreateAsync` : `FieldErrors.ThrowIfAny(NewsRules.Validate(...))` → `NewsArticle.Create` → `SaveChangesAsync` ;
  - `GetAsync`, `ListAsync` (`UpdatedAt` décroissante puis `Id`, filtre `Status`) ;
  - `UpdateAsync` : chargement suivi (404 « Actualité introuvable. ») → `ConcurrencyGuard.EnsureSameVersion` → validation 400 → `Update` → `SaveChangesOrConflictAsync`. Ordre : `not_found` → `concurrent_update` → 400 (on ne signale pas une erreur de saisie sur une fiche périmée, comme les événements) ;
  - `PublishAsync`, `UnpublishAsync` : chargement suivi → transition → `SaveChangesOrConflictAsync` ;
  - `DeleteAsync` : chargement suivi → `Remove` → `SaveChangesOrConflictAsync`.
- Enregistrement dans `Application/DependencyInjection.cs` (scoped).
- `now` lu **une fois** par opération (`time.GetUtcNow()`).

---

## 6. API (`src/Aaas.Api/Controllers/`)

### 6.1 Endpoints

| Verbe et route | Accès | Entrée | Réponse | Erreurs |
|---|---|---|---|---|
| `GET /api/news?page=&pageSize=` | anonyme | requête | 200 `PagedResult<NewsSummaryDto>` | 400 (`page`, `pageSize`) |
| `GET /api/news/{id:guid}` | anonyme | — | 200 `NewsDetailDto` | 404 `not_found` « Contenu introuvable. » (brouillon ou inconnue) |
| `GET /api/news/{id}` (repli) | anonyme | — | — | 404, même corps |
| `GET /api/admin/news?status=&page=&pageSize=` | `AdminOnly` | requête | 200 `PagedResult<AdminNewsSummaryDto>` | 400, 401, 403 |
| `GET /api/admin/news/{id:guid}` | `AdminOnly` | — | 200 `AdminNewsDto` | 404 « Actualité introuvable. » |
| `POST /api/admin/news` | `AdminOnly` | `{ title, content }` | **201** `AdminNewsDto` (Brouillon), `Location: /api/admin/news/{id}` | 400 `errors.title` / `errors.content` |
| `PUT /api/admin/news/{id:guid}` | `AdminOnly` | `{ title, content, version }` | 200 `AdminNewsDto` (nouvelle `version`) | 400, 404, 409 `concurrent_update` |
| `POST /api/admin/news/{id:guid}/publish` | `AdminOnly` | — | 200 `AdminNewsDto` | 404, 409 `news_state_changed`, `concurrent_update` |
| `POST /api/admin/news/{id:guid}/unpublish` | `AdminOnly` | — | 200 `AdminNewsDto` | 404, 409 `news_state_changed`, `concurrent_update` |
| `DELETE /api/admin/news/{id:guid}` | `AdminOnly` | — | **204** | 404, 409 `concurrent_update` |
| `GET`/`PUT`/`DELETE /api/admin/news/{id}`, `POST .../{id}/publish|unpublish` (repli) | `AdminOnly` | — | — | 401 visiteur, 403 membre, 404 admin |

Toutes les réponses de `/api/admin/news` (erreurs comprises) portent `Cache-Control: no-store` (préfixe déjà couvert par `NoStoreMiddleware` : **rien à coder**, mais à tester). Un `GET /api/news` réussi compte comme activité pour la tâche d'expiration (réponse < 400, ADR 0020) : sans conséquence.

### 6.2 Fichiers

- `NewsController` (`[Route("api/news")]`, `[AllowAnonymous]`) : copie conforme du patron `EventsController` (dont `GetMalformedId`).
- `AdminNewsController` (`[Route("api/admin/news")]`, `[Authorize(Policy = Policies.AdminOnly)]`) : patron `AdminEventsController` (dont `MalformedId` avec `AcceptVerbs("GET", "PUT", "DELETE")`, `HttpPost("{id}/publish")`, `HttpPost("{id}/unpublish")`).

### 6.3 Exemple

```http
POST /api/admin/news
{ "title": "Assemblée générale", "content": "Chers membres,\n\nL'assemblée générale aura lieu…" }

201 Created
Location: /api/admin/news/6f1c…
{ "id": "6f1c…", "title": "Assemblée générale", "content": "Chers membres,\n\nL'assemblée…",
  "status": "Draft", "publishedAt": null, "createdAt": "2026-11-24T09:00:00+00:00",
  "updatedAt": "2026-11-24T09:00:00+00:00", "version": "AAAAAAAAB9E=" }
```

---

## 7. Front Angular

### 7.1 Routes

| Chemin | Composant | Guard | Remarque |
|---|---|---|---|
| `''` | redirection `actualites` | — | `pathMatch: 'full'` (UX-3) ; spec de la coquille à mettre à jour |
| `actualites` | `NewsList` (`?page=`) | — | titre d'onglet « Actualités » |
| `actualites/:id` | `NewsDetail` | — | id non GUID → `<app-not-found>` sans appel (`isGuid`) |
| `admin/actualites` | `AdminNewsList` | `canMatch` admin existant | filtre statut, pagination |
| `admin/actualites/nouvelle` | `AdminNewsForm` | `unsavedChangesGuard` | **déclarée avant** `admin/actualites/:id` |
| `admin/actualites/:id` | `AdminNewsForm` | `unsavedChangesGuard` | |

`DEFAULT_AFTER_LOGIN` reste `/evenements` (un membre se connecte surtout pour réserver).

### 7.2 Fichiers

```
core/api/news.api.ts                         GET /api/news, /api/news/{id}
core/api/admin-news.api.ts                   /api/admin/news...
core/models/api-models.ts                    NewsStatus, NewsSummaryDto, NewsDetailDto, AdminNewsSummaryDto, AdminNewsDto
shared/news/paragraphs.ts                    toParagraphs(text): string[] (fonction pure, testée)
shared/news/news-article.ts                  <app-news-article [title] [publishedAt] [content]> : h1/h2, <time>, @for des <p>
shared/news/news-labels.ts                   libellés de statut (Brouillon, Publiée)
features/public/news-list/                   /actualites
features/public/news-detail/                 /actualites/:id
features/admin/news/admin-news-list/         /admin/actualites
features/admin/news/admin-news-form/         création, modification, publication, aperçu
layout/header.html                           « Actualités » en premier ; logo → « / »
layout/not-found.ts, layout/forbidden.ts     lien « Retour à l'accueil » (→ « / »), à valider (QN-8)
features/admin/admin-layout.ts               sous-menu « Actualités » (après « Numéro de contact » ou en premier : QN-8)
```

### 7.3 Comportements

- **Liste publique** : `rxResource` sur `?page=` ; cartes `<article>` avec `<h2>` (titre), `<time [attr.datetime]="publishedAt">` (« Publiée le 5 décembre 2026 », date seule, heure de Tunis via `DatePipe` `+0100`), extrait, lien « Lire la suite » + texte masqué « : {titre} » ; message vide « Aucune actualité pour le moment. » ; page au-delà de la dernière : même traitement que la liste des événements ; `<app-pager>` réutilisé avec `pageSize = 10`.
- **Détail** : `<app-news-article>` ; 404 → `<app-not-found>` (l'URL reste) ; lien « ← Retour aux actualités ».
- **Admin, liste** : colonnes titre, statut (texte, pas seulement couleur), date de publication ou « — », dernière modification ; filtre « Tous / Brouillons / Publiées » ; bouton « Nouvelle actualité ».
- **Admin, formulaire** : champs `title` et `content` (`<textarea>` haut, compteur « 123 / 8 000 » relié par `aria-describedby`), validateurs `required` + `maxLength` avec les **messages de l'API** (`validation-messages.ts`) ; erreurs 400 recopiées par `applyServerErrors` (ADR 0016).
  - Création : « Enregistrer » → `POST` → navigation vers `admin/actualites/{id}` (`replaceUrl`), formulaire marqué non modifié.
  - Modification : `version` dans un signal remplacée après chaque réponse ; 409 `concurrent_update` → dialogue « Recharger » / « Fermer » (patron existant).
  - « Publier » / « Dépublier » : désactivés tant que le formulaire est modifié (« Enregistrez d'abord vos modifications ») ; 409 `news_state_changed` → message puis relecture de la fiche.
  - « Supprimer » : `ConfirmDialog` « Supprimer définitivement cette actualité ? » (texte renforcé si publiée : « Elle n'est plus visible du public dès maintenant. ») ; 204 → message flash + retour à la liste ; 404 → « Cette actualité a déjà été supprimée. » + retour à la liste.
  - **Aperçu** : bascule « Modifier / Aperçu » (`MatButtonToggle` ou deux boutons avec `aria-pressed`) ; l'aperçu lit un signal `toSignal(form.valueChanges)` et affiche `<app-news-article>` avec « Brouillon, non publié » si pas de date.
  - Double clic : boutons désactivés pendant l'appel (signal `busy`).
- **Accessibilité** : hiérarchie de titres (h1 page, h2 cartes), `<time datetime>`, liens au nom explicite (WCAG 2.4.4), compteur annoncé sans bavardage (`aria-live="polite"` seulement sous 200 caractères restants), focus après navigation (existant), contraste des statuts selon la palette mesurée (`etat-avancement.md` § 1.4), `npm run lint` (règles d'accessibilité des templates).

### 7.4 Notions Angular à ajouter à `docs/revision/angular.md`

Interpolation contre `innerHTML` et `DomSanitizer` (§ 1.1), `toSignal` sur `valueChanges` (aperçu), composant de présentation réutilisé (détail + aperçu), redirection de la route vide (`pathMatch: 'full'`).

---

## 8. Impacts Docker, CI/CD, Azure, confidentialité

- **Docker** : aucun service, aucune variable. `docker compose up -d --build` applique `AddNews`. Vérification : `sqlcmd ... -I -Q "SELECT Status, COUNT(*) FROM News GROUP BY Status"`. `pile-e2e.sh` : sous-commande `news` à ajouter (créer, publier, lister anonymement, dépublier → 404, supprimer).
- **nginx** : CSP inchangée (aucune ressource externe ; pas d'image).
- **CI/CD (S3)** : rien de nouveau.
- **Azure** : aucune ressource ; `nvarchar(max)` sans effet sur l'offre gratuite d'Azure SQL (32 Go).
- **Politique de confidentialité** : **aucun impact** (aucune donnée personnelle collectée ni affichée : pas d'auteur). Rappel aux admins dans le guide : ne pas publier de données personnelles de membres sans leur accord (responsabilité éditoriale, pas une règle du code).
- **Journaux** : ne jamais journaliser titre ni texte (texte libre) ; seulement l'`Id`.

---

## 9. Tests (pour le `testeur`)

### 9.1 `Aaas.Domain.Tests` (`News/`)

- `Create` : Brouillon, `PublishedAt` nulle, `CreatedAt = UpdatedAt = now` en UTC, titre et texte normalisés.
- `Validate` : titre ou texte absent, vide, blanc → obligatoire ; titre 200 accepté / 201 refusé ; texte 8 000 accepté / 8 001 refusé ; **8 000 caractères avec des `\r\n`** accepté (compté après normalisation) ; espaces autour non comptés.
- `Publish` : Brouillon → Publiée, `PublishedAt = now` ; Publiée → `news_state_changed`.
- `Unpublish` : Publiée → Brouillon, **`PublishedAt` conservée** ; Brouillon → `news_state_changed`.
- Republication : `PublishedAt` d'origine conservée (QN-2).
- `Update` : sur Brouillon et sur Publiée ; `PublishedAt` inchangée, `UpdatedAt = now`.
- `Excerpt` : court inchangé ; `\n` → espace ; espaces multiples réduits ; exactement 200 sans « … » ; 201 coupé sur un mot + « … » ; mot unique de 300 caractères → coupe nette à 200 + « … » ; émoji à cheval sur la position 200 non coupé en deux.

### 9.2 `Aaas.Application.Tests`

- Validation des DTO : titre ou texte manquant → messages français ; `version` mal formée → 400 ; `PublicNewsListQuery` : `pageSize` 10 par défaut, 51 refusé, `page` 0 refusée ; `AdminNewsListQuery` : 20 par défaut ; les requêtes existantes gardent 20 après le déplacement de `PagingQuery`.

### 9.3 `Aaas.Api.IntegrationTests` (`News/`, Testcontainers, `FakeTimeProvider`)

- **US-B1** : 3 publiées + 1 brouillon → 3, de la plus récente à la plus ancienne, extrait présent ; 25 publiées → page 1 = 10 éléments et `totalCount` 25, page 3 = 5, page 4 = 200 avec `items: []` ; même `PublishedAt` → ordre stable par `Id` sur deux appels ; aucune → `totalCount` 0.
- **US-B2** : publiée → 200, texte complet ; brouillon, dépubliée, inconnue, `/api/news/abc` → **404 au corps identique** (`code`, `title`), **anonyme** (pas 401) ; visiteur avec un jeton expiré → 200 sur `/api/news`.
- **US-E1** : création → 201, `Location`, Brouillon, absente du public ; publication → `PublishedAt` = heure simulée ; modification d'une publiée → visible immédiatement, date conservée ; dépublication → public 404 ; republication → date d'origine ; suppression → 204, puis admin 404, second `DELETE` 404 ; champs manquants → 400 `errors.title` / `errors.content` ; texte de 8 001 → 400.
- **Concurrence** : deux `PUT` avec la même `version` en parallèle → un 200, un 409 `concurrent_update` ; `PUT` avec une ancienne `version` → 409 ; publier deux fois → 409 `news_state_changed`.
- **Sécurité** : contenu `<script>alert(1)</script><img src=x onerror=alert(1)>` stocké et renvoyé **à l'identique** (pas d'encodage serveur) ; routes admin (les 7 + repli) : visiteur 401, membre 403, admin 404 sur id mal formé ; `Cache-Control: no-store` sur `/api/admin/news` (succès **et** erreurs) et **absent** de `/api/news`.
- **Schéma** : `INSERT` direct avec `Status = 3` → erreur 547 ; `Status = 2` et `PublishedAt NULL` → 547.
- **Projection** : la requête de liste ne lit pas `Content` entier (commande SQL journalisée contenant `SUBSTRING`) — facultatif.
- **Fuseau** : publication à 23:30Z → `publishedAt` renvoyé en UTC (`…T23:30:00+00:00`), la conversion est l'affaire du front.

### 9.4 Front (Vitest)

- `toParagraphs` : lignes vides multiples, `\r\n`, texte sans ligne vide, espaces en tête/fin, chaîne vide.
- `NewsDetail` : **texte malveillant affiché en texte, aucun élément `script` ni `img` dans le DOM** ; 404 → « Contenu introuvable » ; id non GUID → aucun appel HTTP ; date du lendemain pour 23:30Z.
- `NewsList` : message vide, cartes, pagination (`?page=`), page au-delà de la dernière.
- `AdminNewsForm` : création puis navigation ; 400 recopiées sur les champs ; 409 `concurrent_update` → dialogue ; Publier désactivé quand modifié ; suppression avec confirmation (204 et 404) ; garde de sortie ; aperçu qui suit la saisie.
- `AdminNewsList` : filtre statut, libellés.
- Coquille : `/` redirige vers `/actualites` ; lien « Actualités » dans l'en-tête.

---

## 10. Risques

| Risque | Parade |
|---|---|
| XSS stockée par un futur `[innerHTML]` « pour faire joli » | ADR 0023 ; test DOM malveillant ; point de revue (recherche `innerHTML` / `bypassSecurityTrust` dans `front/src`) |
| Filtre public écrit sur `PublishedAt IS NOT NULL` au lieu du statut | ADR 0022 § 2 ; test « dépubliée → 404 et absente de la liste » |
| Longueur comptée avant normalisation (`\r\n`) | validation dans le Domain après normalisation ; test 8 000 avec `\r\n` |
| Conflit de nom `News` / espace de noms | classe `NewsArticle` |
| Ordre instable entre deux pages | `ThenBy(Id)` ; test |
| Régression des listes existantes lors du déplacement de `PagingQuery` | tests existants de pagination (20 par défaut) relancés |
| Accueil qui casse un lien ou un test existant (`/` → événements) | spec de la coquille mise à jour ; `DEFAULT_AFTER_LOGIN` inchangé |
| Texte libre contenant des données personnelles | hors code : guide admin ; jamais journalisé |

---

## 11. Tâches ordonnées (`expert-technique`)

| # | Tâche | Dépend de | Vérifiable |
|---|---|---|---|
| **N1** | Domain : `NewsStatus`, `NewsFields`, `NewsRules` (validation, normalisation, extrait), `NewsArticle`, `ErrorCodes.NewsStateChanged` ; tests § 9.1 | — | `dotnet test tests/Aaas.Domain.Tests` |
| **N2** | Infrastructure : `NewsArticleConfiguration`, `DbSet`, **migration `AddNews`** (script relu) | N1 | `docker compose up -d --build api` + requête SQL |
| **N3** | Application : `PagingQuery` déplacée et paramétrée, `Page<T>`, DTO, `PublicNewsService`, `AdminNewsService`, message 404 partagé ; tests § 9.2 | N2 | tests |
| **N4** | API : `NewsController`, `AdminNewsController` (routes de repli) | N3 | curl (§ 12) |
| **N5** | Tests d'intégration § 9.3 | N4 | `dotnet test tests/Aaas.Api.IntegrationTests -m:1 --filter "FullyQualifiedName~News"` |
| **N6** | Front public : modèles, `NewsApi`, `toParagraphs`, `<app-news-article>`, `/actualites`, `/actualites/:id`, redirection `/`, menu, liens « Retour à l'accueil » ; tests | N4, libellés (QN-8) | navigateur, `npm run test:ci` |
| **N7** | Front admin : `AdminNewsApi`, liste, formulaire (aperçu, flux 409, suppression), sous-menu ; tests | N6 | navigateur, `pile-e2e.sh news` |
| **N8** | Docs (§ 13), fiches de révision, relecture `code-reviewer` | N5, N7 | — |

---

## 12. Vérification manuelle (curl, Git Bash, `docker compose up --build`)

```bash
# jeton admin : POST /api/auth/login (identifiants de .env)
curl -s -X POST localhost:8080/api/admin/news -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d '{"title":"Assemblée générale","content":"Premier paragraphe.\n\nSecond <script>alert(1)</script>"}' -i   # 201 + Location
curl -s localhost:8080/api/news/$ID -i                         # 404 (brouillon) — même corps que /api/news/abc
curl -s -X POST localhost:8080/api/admin/news/$ID/publish -H "Authorization: Bearer $ADMIN"     # 200, publishedAt
curl -s "localhost:8080/api/news?page=1"                       # 200, excerpt, sans Cache-Control: no-store
curl -s -X PUT localhost:8080/api/admin/news/$ID -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d '{"title":"AG","content":"x","version":"ANCIENNE"}' -i      # 409 concurrent_update
curl -s -X POST localhost:8080/api/admin/news/$ID/unpublish -H "Authorization: Bearer $ADMIN"   # 200 ; public → 404
curl -s -X DELETE localhost:8080/api/admin/news/$ID -H "Authorization: Bearer $ADMIN" -i        # 204
curl -s localhost:8080/api/admin/news/abc -H "Authorization: Bearer $MEMBRE" -i                 # 403
```

---

## 13. Documentation à mettre à jour (même tâche que le code)

| Document | Mise à jour | Par | Tâche |
|---|---|---|---|
| `docs/technique/architecture.md` | § 0 (étape 8) ; § 1 (ADR 0022, 0023) ; § 3 (dossiers `News/`, contrôleurs, `Common/PagingQuery`) et table « où vit chaque règle » (R18) ; § 4 (`News` : `Content`, `PublishedAt`, `CHECK`, index) ; § 5 (endpoints) ; § 6 (routes `/` → `/actualites`, `/actualites`, `/admin/actualites`) ; § 13 question 4 tranchée | architecte | N8 |
| `docs/technique/api.md` | sections « Actualités — consultation publique » et « Actualités — administration » ; code `news_state_changed` | expert-technique | N4 |
| `docs/technique/regles-metier.md` | nouvelle section « Actualités » (cycle de vie, date conservée, limites, extrait, visibilité, endroit du code) ; § 5 codes ; § 6 (rendu texte brut, paragraphes, date de Tunis) | expert-technique | N4, N6 |
| `docs/technique/exploitation.md` | migration `AddNews`, requête SQL, curl, `pile-e2e.sh news` | expert-technique | N2, N7 |
| `front/src/app/core/models/api-models.ts`, `shared/forms/validation-messages.ts` | DTO et messages recopiés d'`api.md` | expert-technique | N6, N7 |
| `docs/revision/angular.md` | § 7.4 ; nouvelle fiche **`docs/revision/securite-web-xss.md`** (§ 1.1, CSP, Trusted Types en piste) | expert-technique | N6 |
| `docs/revision/ef-core-concurrence.md` ou fiche SQL | `nvarchar(max)`, `CHECK`, projection `SUBSTRING`, *offset* / *keyset* (§ 1.2, 1.3) | expert-technique | N3 |
| `docs/fonctionnel/libelles-ecrans.md` | menu (§ 2), nouvelle section « Actualités » (public et admin), lien de « Contenu introuvable » | expert-fonctionnel | avant N6 |
| `docs/fonctionnel/mvp.md` | R18 précisé (limites, date à la republication, extrait), réponses QN | expert-fonctionnel | avant N1 |
| `docs/cahier-des-charges.md` | rien si les valeurs par défaut sont acceptées (pas de changement de périmètre) ; sinon une ligne d'historique | expert-fonctionnel | — |
| `docs/guide-utilisateur/` | visiteur : lire les actualités ; admin : rédiger, publier, dépublier, supprimer, aperçu | documentaliste | après N8 |
| `docs/technique/etat-avancement.md` | bloc 5 | expert-technique | fin de tâche |

---

## 14. Questions ouvertes

### 14.1 Pour l'`expert-fonctionnel` (avec l'association si besoin)

| # | Question | Valeur par défaut |
|---|---|---|
| QN-1 | Longueur maximale du **texte** d'une actualité ? | **8 000 caractères** (titre : 200, comme un événement). |
| QN-2 | Une actualité **dépubliée puis republiée** garde-t-elle sa date d'origine ? | **Oui** (elle ne remonte pas en tête de liste). |
| QN-3 | L'**extrait** de la liste : automatique ou saisi par l'admin ? Quelle longueur ? | **Automatique**, 200 caractères, coupé sur un mot, suivi de « … ». |
| QN-4 | Afficher l'**auteur** (prénom de l'admin) ? | **Non** : signée par l'association (aucune donnée personnelle publiée). |
| QN-5 | Les **adresses web** dans le texte doivent-elles être cliquables ? | **Non** au MVP (texte brut) ; possible plus tard sans risque XSS. |
| QN-6 | Peut-on **supprimer directement** une actualité publiée (sans la dépublier d'abord) ? | **Oui**, après une confirmation qui rappelle qu'elle disparaît du public. |
| QN-7 | Afficher « **Mise à jour le …** » quand une actualité publiée a été modifiée ? | **Non** (seule la date de publication, US-B2). |
| QN-8 | **Libellés** : menu « Actualités » (en premier), page « Actualités », « Aucune actualité pour le moment. », « Lire la suite », « Publiée le {date} », statuts « Brouillon » / « Publiée », boutons « Nouvelle actualité », « Enregistrer », « Publier », « Dépublier », « Supprimer », « Aperçu » ; dialogue de suppression ; « Contenu introuvable » avec « Retour à l'accueil » ; place de « Actualités » dans le sous-menu admin (proposé : en premier). | Ceux-ci, à rédiger dans `libelles-ecrans.md`. |
| QN-9 | Ordre de la **liste admin** ? | Dernière modification d'abord, filtre « Tous / Brouillons / Publiées ». |

### 14.2 Décisions du porteur (valeur par défaut recommandée)

| # | Décision | Recommandation |
|---|---|---|
| P-N1 | Valider les ADR 0022 et 0023 avant le code | **Oui**. |
| P-N2 | Classe `NewsArticle` (table `News`) | **Oui** (conflit d'espace de noms sinon). |
| P-N3 | Pagination *offset* réutilisée (pas de *keyset*) | **Oui** (ADR 0011). |
| P-N4 | Suppression, publication, dépublication **sans `version`** | **Oui** (convention des événements). |
| P-N5 | Petit remaniement : `PagingQuery` dans `Common/` avec taille par défaut paramétrée, `Page<T>` générique | **Oui** (sinon pas de « 10 par défaut » propre). |
| P-N6 | Aperçu dans le formulaire admin | **Oui** (même composant que le détail : faible coût). |
| P-N7 | Ordre : actualités **avant** ou **après** le bloc 4 (annulation d'un événement, prévu avant par le cahier § 8) | **Après le bloc 4**, pour suivre le cahier ; aucune dépendance : l'inverse est possible si l'on veut une page d'accueil plus tôt. |
