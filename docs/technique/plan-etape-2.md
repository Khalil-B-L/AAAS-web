# Plan d'implémentation — Étape 2 : événements (admin), numéro de contact, consultation publique

> Rédigé par l'agent `architecte` le 18/11/2026. Destinataires : `expert-technique` (tâches T1 à T12), `testeur` (§ 9), `code-reviewer`, porteur.
> Références : cahier des charges v1.1 ; `docs/fonctionnel/mvp.md` v3 (US-E2, US-E8, US-C1, US-C2 ; R2, R7, R11, R12, R14, R15, R19, R26) ; `architecture.md` v7 ; ADR 0001 à 0013.
> Statut : **proposé**. Les ADR 0009 à 0013 sont au statut « Proposé » ; les valeurs par défaut marquées *(E2-n)* attendent une réponse (§ 13), le développement peut avancer avec elles.
> **Mise à jour du 19/11/2026 (`expert-technique`, implémentation)** : alignement sur le cahier v1.2 et `mvp.md` v4 (décisions du porteur, Q18 à Q29) : suppression d'un brouillon jamais publié (`DELETE /api/admin/events/{id}`, colonne `FirstPublishedAt`), retrait du numéro de contact (`DELETE /api/admin/settings/contact`, avec `version`), `hasEnded` dans les DTO publics et mention « Terminé », fin toujours dans le futur (R28, Q28), numéro fixe accepté. Les passages modifiés sont signalés « *(v4)* ». État : **implémenté** (T1 à T8, T10 sans lancement de la pile, T11) ; T9 (tests d'intégration complets) au `testeur`, T12 au `code-reviewer`.

---

## 0. Périmètre

Cette étape regroupe les étapes 2 et 3 de l'ordre conseillé (`mvp.md` § 6).

| Dans l'étape 2 | Hors étape 2 (étape) |
|---|---|
| US-E2 : créer (brouillon), prix, capacité, modifier (avec avertissement si inscrits, y compris un événement commencé), publier, dépublier ; *(v4)* **supprimer un brouillon jamais publié** | annuler un événement, supprimer un événement publié sans réservation : US-E3 (étape 7, Q29) |
| US-E8 : lire, enregistrer (mobile ou fixe) et *(v4)* **retirer** le numéro de contact ; avertissement à la publication si absent | afficher le numéro après une demande : US-D4 (étape 4) |
| US-C1 : liste publique des événements à venir, prix, places restantes, mentions « Complet », « Annulé » | bouton « Demander une place » actif : US-D1 (étape 4) |
| US-C2 : détail public (visiteur), *(v4)* y compris d'un événement terminé, avec `hasEnded` | « ma demande / ma réservation » sur le détail : étape 4 |
| Mécanismes anticipés : `IEventLock` (verrou `UPDLOCK`), `version` (`rowversion`), service unique de décompte `EventOccupancyReader` | table `Reservations`, décompte réel : étape 4 |
| Socle : EF Core dans Application, `IAppDbContext`, `PagedResult<T>`, validation des dates avec décalage | écrans Angular : semaine 2 |

Rien n'est ajouté au périmètre fonctionnel. Les seuls choix « en plus » sont techniques (bornes de saisie, pagination admin) et sont signalés. *(v4)* La suppression d'un brouillon et le retrait du numéro ont été ajoutés au périmètre par le porteur (cahier v1.2, RG-07, RG-10).

---

## 1. Notions nouvelles (résumé pour la révision)

| Notion | En deux phrases | Où on la voit |
|---|---|---|
| **Concurrence optimiste, `rowversion`** | SQL Server change automatiquement une colonne `rowversion` à chaque écriture d'une ligne. On renvoie au client la valeur lue (`version`, en Base64) ; s'il la renvoie périmée, quelqu'un a modifié la fiche entre-temps → 409, au lieu d'écraser son travail. | `Event`, `AssociationSettings` (ADR 0006, 0012, 0013) |
| **Verrou pessimiste `UPDLOCK`** | `SELECT ... WITH (UPDLOCK, ROWLOCK)` réserve la ligne jusqu'à la fin de la transaction : une autre transaction qui demande le même verrou attend. Sert à sérialiser tout ce qui dépend du nombre de places prises. | `IEventLock` (ADR 0004, 0012) |
| **Stratégie d'exécution EF** | Avec `EnableRetryOnFailure`, EF rejoue une opération après une erreur transitoire (Azure SQL qui se réveille). Une transaction ouverte à la main doit donc être enveloppée dans `CreateExecutionStrategy().ExecuteAsync(...)` pour être rejouée en bloc. | `EventLock` |
| **Données de départ d'une migration (`HasData`)** | La migration insère des lignes fixes (ici la ligne unique des paramètres). Elles font partie du schéma versionné, comme une table. | `AssociationSettings` |
| **Contraintes `CHECK`** | Règle vérifiée par SQL Server à chaque écriture (`Capacity >= 1`). Filet de sécurité si un script ou un bug contourne le Domain. | `Events`, `AssociationSettings` |
| **Pagination par numéro de page** | `OFFSET (page-1)*pageSize ROWS FETCH NEXT pageSize ROWS ONLY` côté SQL (`Skip/Take` en LINQ), plus un `COUNT(*)` pour le total. Toujours avec un tri stable. | ADR 0011 |
| **ISO 8601 avec décalage** | `2026-12-05T10:00+01:00` désigne un instant sans ambiguïté ; sans décalage, l'heure dépend de qui la lit. L'API refuse une date sans décalage. | ADR 0009 |
| **Endpoint anonyme avec jeton facultatif** | Sur un endpoint `[AllowAnonymous]`, le jeton est quand même lu s'il est présent (`User` rempli) ; un jeton expiré ne donne pas 401, l'appel est traité comme anonyme. Servira à l'étape 4. | `GET /api/events/{id}` |

Docker et migrations : voir § 12 ; Angular : voir § 13.

---

## 2. Décisions (renvois)

| Sujet | Décision | ADR |
|---|---|---|
| Statuts, « à venir », visibilité du détail, dates en entrée, validation | `Draft/Published/Cancelled` stockés, commencé/terminé calculés ; liste = `EndsAt > now` ; dates en chaîne + `[DateTimeWithOffset]` ; règles de valeurs dans le Domain → 400 par champ | [0009](../adr/0009-evenement-cycle-de-vie-dates-et-validation.md) |
| Prix | `decimal(9,3) NULL`, 0 → `NULL`, pas de `Money`, nombre JSON, refus au-delà de 3 décimales | [0010](../adr/0010-prix-des-evenements-en-tnd.md) |
| Listes | `page`/`pageSize`, `PagedResult<T>`, tri serveur stable, filtres admin `status`/`period`, décompte groupé | [0011](../adr/0011-listes-pagination-tri-et-filtres.md) |
| Modification | `IEventLock` + `version` ; avertissement `registrants_warning` en deux temps ; `EventOccupancyReader` renvoie zéro jusqu'à l'étape 4 | [0012](../adr/0012-modification-d-un-evenement-verrou-version-et-avertissement.md) |
| Numéro de contact | table `AssociationSettings` à une ligne créée par la migration ; lecture connectés, écriture admin ; avertissement non bloquant à la publication | [0013](../adr/0013-parametres-de-l-association-table-singleton.md) |

Rappels des ADR existants appliqués ici : 0001 (couches, controllers, exceptions → `ProblemDetails`, EF Core dans Application **à partir de cette étape**), 0003 (UTC, `TimeProvider`), 0004 (verrou), 0005 (décompte des actives), 0006 (`concurrent_update`, `event_state_changed` sur le modèle de `reservation_state_changed`).

---

## 3. Domain (`src/Aaas.Domain`)

### 3.1 Fichiers

| Fichier | Contenu |
|---|---|
| `Events/EventStatus.cs` | `enum EventStatus : byte { Draft = 1, Published = 2, Cancelled = 3 }` — valeurs figées |
| `Events/EventOccupancy.cs` | `readonly record struct EventOccupancy(int Pending, int Confirmed)` ; `Active` ; `None` |
| `Events/EventFields.cs` | constantes des noms JSON : `title`, `description`, `location`, `startsAt`, `endsAt`, `capacity`, `price` |
| `Events/EventRules.cs` | constantes (`TitleMaxLength = 200`, `LocationMaxLength = 200`, `DescriptionMaxLength = 4000`, `MinCapacity = 1`, `MaxCapacity = 10_000`, `MaxPrice = 99_999.999m`, `PriceDecimals = 3`) ; `static decimal? NormalizePrice(decimal?)` ; `static IReadOnlyList<FieldError> Validate(EventDetails details, DateTimeOffset? previousStartsAt, DateTimeOffset now)` |
| `Events/EventDetails.cs` | `sealed record EventDetails(string Title, string Description, string Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Capacity, decimal? Price)` : les données saisissables, passées d'un bloc |
| `Events/Event.cs` | entité (§ 3.2) |
| `Common/FieldError.cs` | `sealed record FieldError(string Field, string Message)` |
| `Settings/AssociationSettings.cs` | entité (§ 3.3) |
| `Common/ErrorCodes.cs` | **ajouts** (§ 7) |

### 3.2 `Event`

Propriétés (setters privés) : `Id` (Guid), `Title`, `Description`, `Location`, `StartsAt`, `EndsAt`, `Capacity`, `Price` (`decimal?`), `Status`, `PublishedAt` (`DateTimeOffset?`), *(v4)* `FirstPublishedAt` (`DateTimeOffset?`, première publication, jamais remise à vide), `CreatedAt`, `UpdatedAt`, `RowVersion` (`byte[]`, géré par la base). *(v4)* `bool CanBeDeleted` (brouillon et `FirstPublishedAt` nul). Constructeur privé sans paramètre pour EF.

Méthodes (toutes reçoivent `now` ; aucune ne lit l'horloge) :

| Méthode | Règle | Erreur |
|---|---|---|
| `static Event Create(EventDetails d, DateTimeOffset now)` | `EventRules.Validate(d, previousStartsAt: null, now)` ; `Status = Draft` ; prix normalisé ; dates en UTC ; `CreatedAt = UpdatedAt = now` | `BusinessRuleException(validation_failed)` si appelée avec des données invalides (filet) |
| `bool HasStarted(now)` / `bool HasEnded(now)` | `StartsAt <= now` / `EndsAt <= now` | — |
| `bool IsFree` | `Price is null` | — |
| `int RemainingSeats(EventOccupancy o)` | R7 : `Math.Max(0, Capacity - o.Active)` | — |
| `bool IsFull(EventOccupancy o)` | `o.Active >= Capacity` | — |
| `bool IsBookable(now, EventOccupancy o)` | R2 : `Published` **et** pas commencé **et** pas complet (`Cancelled` ≠ `Published`, donc non réservable) | — |
| `bool RequiresRegistrantsWarning(EventDetails d, EventOccupancy o)` | R12 : `o.Active > 0` et (`StartsAt`, `EndsAt` ou prix normalisé différents) | — |
| `void EnsureCanBeEdited(DateTimeOffset now)` | annulé → refus ; terminé → refus. Appelée par `Update`, et par le service **avant** la validation des valeurs (ordre des contrôles, § 5.3) | `event_cancelled`, `event_ended` |
| `void Update(EventDetails d, DateTimeOffset now, EventOccupancy o, bool confirmRegistrantsWarning)` | ordre : 1) `Cancelled` → refus ; 2) terminé → refus ; 3) `Validate(d, previousStartsAt: StartsAt, now)` (début dans le futur seulement s'il change) ; 4) R11 `d.Capacity < o.Active` → refus ; 5) R12 avertissement non confirmé → refus ; 6) application, `UpdatedAt = now` | `event_cancelled`, `event_ended`, `validation_failed` (filet), `capacity_below_reserved`, `registrants_warning` |
| `void Publish(DateTimeOffset now)` | `Draft` seulement ; pas commencé ; `PublishedAt = UpdatedAt = now` ; *(v4)* `FirstPublishedAt ??= now` | `event_cancelled`, `event_state_changed`, `event_started` |
| `void Unpublish(DateTimeOffset now, EventOccupancy o)` | `Published` seulement ; R14 `o.Active == 0` ; `PublishedAt = null` (`FirstPublishedAt` conservé), `UpdatedAt = now` | `event_cancelled` *(ajouté : un annulé n'est pas « publié »)*, `event_state_changed`, `event_has_active_reservations` |
| *(v4)* `void EnsureCanBeDeleted()` | R15 étape 2 (RG-07) : `Draft` **et** `FirstPublishedAt` nul ; règle partagée `EventRules.CanBeDeleted(status, firstPublishedAt)` (aussi utilisée par la liste admin) | `event_already_published` |

Messages (`title`, en français, sans heure : ADR 0003) :
- `capacity_below_reserved` : « {Active} places sont déjà occupées ({Confirmed} confirmées, {Pending} en attente). La capacité ne peut pas être inférieure. »
- `registrants_warning` : « Cet événement a {Active} inscrits (demandes en attente et réservations confirmées). Pensez à les prévenir de ce changement. Confirmez pour enregistrer. »
- `event_has_active_reservations` : « Cet événement a des demandes en attente ou des réservations confirmées : il ne peut pas être dépublié. Annulez-le à la place. »
- `event_started` : « Cet événement a déjà commencé : il ne peut plus être publié. »
- `event_ended` : « Cet événement est terminé : il ne peut plus être modifié. »
- `event_cancelled` : « Cet événement est annulé : il ne peut plus être modifié ni publié. »
- `event_state_changed` : « Cet événement est déjà publié. » / « Cet événement n'est pas publié. » (selon le cas ; le front recharge la fiche).
- *(v4)* `event_already_published` : « Cet événement a déjà été publié : il ne peut pas être supprimé. Seul un brouillon jamais publié peut l'être ; dépubliez-le ou annulez-le à la place. »

Messages de validation (`EventRules.Validate`, clés `EventFields`) :
- `startsAt` : « La date de début doit être dans le futur. »
- `endsAt` : « La fin doit être après le début. » ; *(v4, R28, Q28)* sinon, si la fin est atteinte ou passée : « La date de fin doit être dans le futur. » (seule protection utile à la modification d'un événement commencé ; à la création, déjà impliquée par « début futur et fin après début »)
- `capacity` : « La capacité doit être comprise entre 1 et 10 000 places. »
- `price` : « Le prix ne peut pas être négatif. » / « Le prix est en dinars, avec au plus 3 décimales (millimes). » / « Le prix ne doit pas dépasser 99 999,999 TND. »
- titre, lieu, description vides ou trop longs : déjà couverts par le DTO, revérifiés ici (filet, mêmes messages).

### 3.3 `AssociationSettings`

`const int SingletonId = 1` ; `Id`, `ContactPhone` (`string?`, E.164), `ContactWhatsApp` (`bool`), `UpdatedAt` (`DateTimeOffset?`), `RowVersion`. `bool IsContactConfigured => ContactPhone is not null`. `void UpdateContact(PhoneNumber phone, bool whatsApp, DateTimeOffset now)`. *(v4)* `void RemoveContact(DateTimeOffset now)` : numéro `null`, WhatsApp `false` (R8). Pas de constructeur public : la ligne vient de la migration (fabrique `internal CreateInitial()` pour les tests du Domain).

*(v4)* Calculs statiques dans `EventRules` (`RemainingSeats`, `IsFull`, `IsBookable`, `CanBeDeleted`) : les listes projettent quelques colonnes sans charger l'entité, et appliquent la même règle que les méthodes de `Event`, qui y délèguent.

---

## 4. Persistance (`src/Aaas.Infrastructure`) et migration

### 4.1 Schéma

**`Events`**

| Colonne | Type | Contrainte |
|---|---|---|
| `Id` | `uniqueidentifier` | PK (Guid séquentiel généré par EF) |
| `Title` | `nvarchar(200)` | NOT NULL |
| `Description` | `nvarchar(4000)` | NOT NULL |
| `Location` | `nvarchar(200)` | NOT NULL |
| `StartsAt`, `EndsAt` | `datetimeoffset` | NOT NULL, `CK_Events_Dates CHECK (EndsAt > StartsAt)` |
| `Capacity` | `int` | NOT NULL, `CK_Events_Capacity CHECK (Capacity >= 1)` |
| `Price` | `decimal(9,3)` | NULL, `CK_Events_Price CHECK (Price IS NULL OR Price > 0)` |
| `Status` | `tinyint` | NOT NULL, `CK_Events_Status CHECK (Status IN (1, 2, 3))` |
| `PublishedAt` | `datetimeoffset` | NULL |
| *(v4)* `FirstPublishedAt` | `datetimeoffset` | NULL ; marqueur persistant « déjà publié » (la dépublication remet `PublishedAt` à vide) |
| `CreatedAt`, `UpdatedAt` | `datetimeoffset` | NOT NULL |
| `RowVersion` | `rowversion` | `IsRowVersion()` |

Index : `IX_Events_Status_StartsAt (Status, StartsAt)`. Pas d'index unique (deux événements peuvent avoir le même titre). `Description` en `nvarchar(4000)` plutôt que `max` : la limite de saisie est 4 000 *(E2-5)* ; à élargir par migration si besoin. `CancelledAt` arrivera avec l'étape 7.

**`AssociationSettings`** : voir ADR 0013 (`Id int` PK + `CK_AssociationSettings_Singleton CHECK (Id = 1)`, `ContactPhone nvarchar(12) NULL`, `ContactWhatsApp bit NOT NULL DEFAULT 0`, `UpdatedAt datetimeoffset NULL`, `RowVersion`), **une ligne insérée par la migration** (`HasData`). *Implémentation : pas de `DEFAULT 0` sur `ContactWhatsApp` (une valeur par défaut sur un `bool` déclenche un avertissement EF et n'apporte rien : la seule ligne est insérée par la migration avec `0`).*

### 4.2 Fichiers

| Fichier | Contenu |
|---|---|
| `Persistence/Configurations/EventConfiguration.cs` | table, longueurs, `HasPrecision(9, 3)`, conversion `Status` → `byte` (par défaut pour un enum `: byte`), `IsRowVersion()`, index, `ToTable(t => t.HasCheckConstraint(...))` |
| `Persistence/Configurations/AssociationSettingsConfiguration.cs` | `ValueGeneratedNever()` sur `Id`, contrainte singleton, `HasData(new { Id = 1, ContactWhatsApp = false })` (objet anonyme : pas de constructeur public sur l'entité) |
| `Persistence/AppDbContext.cs` | `DbSet<Event> Events`, `DbSet<AssociationSettings> AssociationSettings` ; implémente `IAppDbContext` |
| `Persistence/EventLock.cs` | implémentation de `IEventLock` (ADR 0012 § 1) |
| `DependencyInjection.cs` | `AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())` (même instance que le reste de la requête) ; `AddScoped<IEventLock, EventLock>()` |
| `Persistence/Migrations/<horodatage>_AddEventsAndAssociationSettings.cs` | générée par `dotnet ef migrations add AddEventsAndAssociationSettings` |

Point d'attention `EventLock` : `FromSql($"SELECT * FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = {eventId}")` est interpolé **en paramètre** (pas de concaténation, pas d'injection SQL). Suivi de `.AsTracking().SingleOrDefaultAsync()` : EF place la requête brute en sous-requête, ce que SQL Server accepte avec les indicateurs de verrou. Vérifier dans le SQL journalisé en `Debug` local que le `WITH (UPDLOCK, ROWLOCK)` est bien présent. *Fait : test `EventLockSqlTests` (`ToQueryString()`, sans base) ; la requête est isolée dans `EventLock.LockedEventQuery`.*

Vérifier aussi le script de migration avant de le valider : `dotnet ef migrations script InitialIdentity AddEventsAndAssociationSettings -p src/Aaas.Infrastructure -s src/Aaas.Api` doit montrer les 5 `CHECK`, l'index, la colonne `rowversion` et l'`INSERT` de la ligne `Id = 1`.

---

## 5. Application (`src/Aaas.Application`)

### 5.1 Socle

| Fichier | Contenu |
|---|---|
| `Aaas.Application.csproj` + `Directory.Packages.props` | ajout de `Microsoft.EntityFrameworkCore` (10.0.15, déclaré dans `Directory.Packages.props`). **Pas** `SqlServer` ni `Relational` : Application ne voit que `DbSet`, LINQ et `DbUpdateConcurrencyException` (ADR 0001 point 3) |
| `Abstractions/IAppDbContext.cs` | `DbSet<Event> Events { get; }`, `DbSet<AssociationSettings> AssociationSettings { get; }`, `Task<int> SaveChangesAsync(CancellationToken)` |
| `Abstractions/IEventLock.cs` | signature de l'ADR 0012 § 1 |
| `Common/PagedResult.cs` | `sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)` |
| `Common/Validation/DateTimeWithOffsetAttribute.cs` | ADR 0009 § 3 ; vide → laissé à `[Required]` |
| `Common/Validation/RowVersionAttribute.cs` | `version` : Base64 valide décodant **8 octets** ; sinon « La version de la fiche est invalide. Rechargez-la. » |
| `Common/RowVersions.cs` | `Encode(byte[]) → string`, `Decode(string) → byte[]` |
| `Common/Errors/ConcurrencyGuard.cs` | `static void EnsureSameVersion(byte[] current, byte[] expected)` → `BusinessRuleException(concurrent_update, …)` ; et traduction d'une `DbUpdateConcurrencyException` en la même erreur |
| `Common/FieldErrors.cs` | `ThrowIfAny(IReadOnlyList<FieldError>)` → `RequestValidationException` (regroupe par champ) |
| `Accounts/Validation/ValidationAttributes.cs` | **déplacer** `TunisianPhoneNumberAttribute` dans `Common/Validation/` (il sert maintenant aussi aux paramètres) ; adapter les `using` |

### 5.2 Paramètres (US-E8)

`Settings/SettingsDtos.cs` :
- `ContactSettingsDto(string? PhoneNumber, bool WhatsApp, bool IsConfigured, string Version, DateTimeOffset? UpdatedAt)` ;
- `UpdateContactSettingsRequest { [Required] [TunisianPhoneNumber] PhoneNumber; bool WhatsApp; [Required] [RowVersion] Version }`.

- *(v4)* `RemoveContactSettingsRequest { [Required] [RowVersion] Version }`.

`Settings/SettingsService.cs` : `GetContactAsync`, `UpdateContactAsync(request, ct)` (charge la ligne `Id = 1`, compare la version, `UpdateContact(PhoneNumber.Parse(...), ..., now)`, `SaveChanges`, traduit la `DbUpdateConcurrencyException`), *(v4)* `RemoveContactAsync(request, ct)` (même contrôle de version, `RemoveContact(now)`), `IsContactConfiguredAsync` (pour la publication).

### 5.3 Événements

`Events/EventDtos.cs` (entrée) :

| DTO | Champs |
|---|---|
| `CreateEventRequest` | `title` (`[Required]`, ≤ 200), `description` (`[Required]`, ≤ 4 000), `location` (`[Required]`, ≤ 200), `startsAt`, `endsAt` (`string`, `[Required]`, `[DateTimeWithOffset]`), `capacity` (`int?`, `[Required]`), `price` (`decimal?`) |
| `UpdateEventRequest` | les mêmes + `version` (`[Required]`, `[RowVersion]`) + `confirmRegistrantsWarning` (`bool`, défaut `false`) |
| `PublicEventListQuery` | `page` (`[Range(1, 10 000)]`, défaut 1 ; *implémentation : borne haute ajoutée, sinon `(page-1)*pageSize` déborde*), `pageSize` (`[Range(1, 50)]`, défaut 20). Clés d'erreur en camelCase grâce à `CamelCaseQueryNamesProvider` (Api) |
| `AdminEventListQuery` | `page`, `pageSize` + `status` (`EventStatus?`) + `period` (`EventPeriod`, défaut `Upcoming` ; enum `Upcoming`, `Past`, `All`) |

`Events/EventDtos.cs` (sortie) :

| DTO | Champs |
|---|---|
| `EventSummaryDto` (public) | `id`, `title`, `location`, `startsAt`, `endsAt`, `price` (`null` = gratuit), `capacity`, `remainingSeats`, `status` (`Published` ou `Cancelled`), `hasStarted`, *(v4)* `hasEnded`, `isFull`, `isBookable` |
| `EventDetailDto` (public) | `EventSummaryDto` + `description` |
| `AdminEventSummaryDto` | `id`, `title`, `location`, `startsAt`, `endsAt`, `status`, `capacity`, `price`, `pendingCount`, `confirmedCount`, `remainingSeats`, `hasStarted`, `hasEnded`, *(v4)* `canBeDeleted` |
| `AdminEventDto` | `AdminEventSummaryDto` + `description`, `publishedAt`, *(v4)* `firstPublishedAt`, `createdAt`, `updatedAt`, `version` |
| `PublishEventResponse` | `event` (`AdminEventDto`), `warnings` (`[{ code, message }]`, vide si rien à signaler) |

Le front choisit la mention à afficher, par priorité : `status = Cancelled` → « Annulé » ; *(v4)* `hasEnded` → « Terminé » (détail d'un événement terminé, RG-14 ; jamais dans la liste) ; `hasStarted` → « Réservations closes » ; `isFull` → « Complet » ; sinon « N places restantes ».

Services :

| Fichier | Méthodes |
|---|---|
| `Events/EventOccupancyReader.cs` | `GetAsync(eventId, now, ct)`, `GetManyAsync(ids, now, ct)` ; **étape 2 : renvoie `EventOccupancy.None`** (ADR 0012 § 4) |
| `Events/EventQueries.cs` | expressions LINQ réutilisables : `IsListedPublicly(now)` (`Status IN (Published, Cancelled) && EndsAt > now`), `IsVisiblePublicly` (`Status IN (Published, Cancelled)`), `InPeriod(period, now)` |
| `Events/AdminEventService.cs` | `CreateAsync`, `GetAsync`, `ListAsync`, `UpdateAsync` (sous `IEventLock` : version, occupation, `Event.Update`), `PublishAsync` (sans verrou ; `rowversion` ; avertissement `contact_not_configured`), `UnpublishAsync` (sous `IEventLock`), *(v4)* `DeleteAsync` (sans verrou ni `version` : un brouillon jamais publié n'a aucune réservation ; `EnsureCanBeDeleted`, `Remove`, `rowversion` en filet si un autre admin publie au même instant) |
| `Events/PublicEventService.cs` | `ListAsync(query, ct)`, `GetAsync(id, ct)` (404 si brouillon ou absent) |
| `DependencyInjection.cs` | `AddScoped` des quatre services |

Détail de `UpdateAsync` (le cas le plus riche) :
1. la validation automatique du DTO est passée (sinon 400 avant d'arriver ici) ;
2. `now = timeProvider.GetUtcNow()` ; conversion des dates (`ParseExact`, `ToUniversalTime`) ; `EventDetails` ;
3. `eventLock.ExecuteWithLockedEventAsync(id, async (ev, ct) => { ... })` :
   - `ConcurrencyGuard.EnsureSameVersion(ev.RowVersion, RowVersions.Decode(request.Version))` → 409 `concurrent_update` ;
   - `ev.EnsureCanBeEdited(now)` → 409 `event_cancelled` ou `event_ended` ;
   - `FieldErrors.ThrowIfAny(EventRules.Validate(details, ev.StartsAt, now))` → 400 ;
   - `occupancy = await occupancyReader.GetAsync(id, now, ct)` ;
   - `ev.Update(details, now, occupancy, request.ConfirmRegistrantsWarning)` → 409 éventuels ;
   - retourne le DTO construit **après** `SaveChanges` (nouvelle `version`) : `IEventLock` enregistre puis valide la transaction ; le DTO est construit à partir de l'entité suivie, dont `RowVersion` a été relue par EF.

Ordre des contrôles dans `UpdateAsync` : `not_found` → `concurrent_update` → `event_cancelled` / `event_ended` → 400 de valeurs → `capacity_below_reserved` → `registrants_warning`. Raison : on ne signale pas une erreur de saisie sur une fiche périmée.

---

## 6. API (`src/Aaas.Api`)

### 6.1 Endpoints

| Verbe et route | Accès | Entrée | Sortie | Codes |
|---|---|---|---|---|
| `GET /api/events?page=&pageSize=` | anonyme | query | `PagedResult<EventSummaryDto>` | 200, 400 |
| `GET /api/events/{id:guid}` | anonyme | — | `EventDetailDto` | 200, 404 |
| `GET /api/admin/events?status=&period=&page=&pageSize=` | `AdminOnly` | query | `PagedResult<AdminEventSummaryDto>` | 200, 400, 401, 403 |
| `GET /api/admin/events/{id:guid}` | `AdminOnly` | — | `AdminEventDto` | 200, 401, 403, 404 |
| `POST /api/admin/events` | `AdminOnly` | `CreateEventRequest` | `AdminEventDto`, en-tête `Location: /api/admin/events/{id}` | **201**, 400, 401, 403 |
| `PUT /api/admin/events/{id:guid}` | `AdminOnly` | `UpdateEventRequest` | `AdminEventDto` | 200, 400, 401, 403, 404, 409 (`concurrent_update`, `event_cancelled`, `event_ended`, `capacity_below_reserved`, `registrants_warning`) |
| `POST /api/admin/events/{id:guid}/publish` | `AdminOnly` | — | `PublishEventResponse` | 200, 401, 403, 404, 409 (`event_state_changed`, `event_cancelled`, `event_started`) |
| `POST /api/admin/events/{id:guid}/unpublish` | `AdminOnly` | — | `AdminEventDto` | 200, 401, 403, 404, 409 (`event_state_changed`, `event_cancelled`, `event_has_active_reservations`) |
| *(v4)* `DELETE /api/admin/events/{id:guid}` | `AdminOnly` | — | — | **204**, 401, 403, 404, 409 (`event_already_published`, `concurrent_update`) |
| `GET /api/settings/contact` | connecté (`User` ou `Admin`) | — | `ContactSettingsDto` | 200, 401 |
| `PUT /api/admin/settings/contact` | `AdminOnly` | `UpdateContactSettingsRequest` | `ContactSettingsDto` | 200, 400, 401, 403, 409 (`concurrent_update`) |
| *(v4)* `DELETE /api/admin/settings/contact` | `AdminOnly` | `RemoveContactSettingsRequest` (`{ version }`, corps JSON) | `ContactSettingsDto` (`phoneNumber: null`, `whatsApp: false`) | 200, 400, 401, 403, 409 (`concurrent_update`) |

Controllers : `EventsController` (`[AllowAnonymous]` au niveau de la classe), `AdminEventsController` (`[Authorize(Policy = Policies.AdminOnly)]`), `SettingsController` (authentifié par défaut), `AdminSettingsController` (`AdminOnly`). Routes `{id:guid}` : un identifiant mal formé donne 404, comme un identifiant inconnu.

Publier et dépublier sont des **actions** (`POST .../publish`) et non un `PUT` du statut : l'intention est explicite, la règle de transition est dans le Domain (ADR 0006 point 1), et le client n'envoie pas de `version` pour ces actions.

*(v4)* Deux suppressions, deux choix :
- **brouillon** (`DELETE /api/admin/events/{id}`) : action **sans** `version`, comme publier ; la règle « jamais publié » suffit, et le `rowversion` empêche de supprimer un événement qu'un autre admin vient de publier ;
- **numéro de contact** (`DELETE /api/admin/settings/contact`) : **avec** `version` dans un corps JSON, parce que le retrait efface une donnée qu'un autre admin vient peut-être de saisir (même logique que le `PUT`). Écartés : `version` en paramètre d'URL (le `+` du Base64 est lu comme une espace, et Angular ne l'encode pas) ; `PUT` avec `phoneNumber: null` (un champ oublié retirerait le numéro).

### 6.2 Exemples

Création :

```json
POST /api/admin/events
{
  "title": "Atelier couture",
  "description": "Initiation, matériel fourni.",
  "location": "Local de l'association, Tunis",
  "startsAt": "2026-12-05T10:00+01:00",
  "endsAt": "2026-12-05T12:00+01:00",
  "capacity": 30,
  "price": 12.5
}
→ 201
{
  "id": "…", "title": "Atelier couture", "description": "…", "location": "…",
  "startsAt": "2026-12-05T09:00:00+00:00", "endsAt": "2026-12-05T11:00:00+00:00",
  "status": "Draft", "capacity": 30, "price": 12.5,
  "pendingCount": 0, "confirmedCount": 0, "remainingSeats": 30,
  "hasStarted": false, "hasEnded": false,
  "publishedAt": null, "createdAt": "…", "updatedAt": "…", "version": "AAAAAAAAB9E="
}
```

Avertissement (étape 4 et suivantes, quand il y a des inscrits) :

```json
PUT /api/admin/events/{id}   { …, "startsAt": "2026-12-06T10:00+01:00", "version": "AAAAAAAAB9E=" }
→ 409 { "title": "Cet événement a 12 inscrits (…). Confirmez pour enregistrer.", "status": 409, "code": "registrants_warning" }
PUT /api/admin/events/{id}   { …, "version": "AAAAAAAAB9E=", "confirmRegistrantsWarning": true }
→ 200 { …, "version": "AAAAAAAAB+M=" }
```

Publication sans numéro de contact :

```json
POST /api/admin/events/{id}/publish
→ 200 { "event": { …, "status": "Published" },
        "warnings": [ { "code": "contact_not_configured", "message": "Aucun numéro de contact n'est configuré : …" } ] }
```

### 6.3 Réglage transverse

Messages de liaison de modèle en français (`AddControllers(o => o.ModelBindingMessageProvider.Set...Accessor(...))`) : à cette étape apparaissent les premiers **paramètres de requête** (`page=abc`, `period=demain`), dont le message par défaut est anglais (« The value 'abc' is not valid »). Les erreurs de **type dans le corps JSON** (`"capacity": "trente"`) restent signalées sous la clé `$.capacity` avec un message technique : risque accepté (le front envoie toujours les bons types), à noter dans `api.md`.

---

## 7. Codes d'erreur (ajouts à `ErrorCodes`)

| Code | HTTP | Sens |
|---|---|---|
| `concurrent_update` | 409 | La fiche a été modifiée par un autre admin ; recharger (ADR 0006) |
| `event_state_changed` | 409 | Publier un événement déjà publié, dépublier un brouillon ; recharger |
| `event_cancelled` | 409 | Action impossible sur un événement annulé |
| `event_started` | 409 | Publication d'un événement déjà commencé |
| `event_ended` | 409 | Modification d'un événement terminé |
| `event_has_active_reservations` | 409 | Dépublication avec des réservations actives (R14) |
| `capacity_below_reserved` | 409 | Capacité inférieure aux places occupées (R11) |
| `registrants_warning` | 409 | Changement de date ou de prix avec des inscrits, à confirmer (R12) |
| `contact_not_configured` | — | **Avertissement** (pas une erreur), dans `warnings` de la publication |
| *(v4)* `event_already_published` | 409 | Suppression d'un événement qui a déjà été publié (R15 étape 2, RG-07) |

Le `code` `validation_failed` (400) couvre toutes les erreurs de saisie, avec `errors` par champ.

---

## 8. Anticiper les réservations (R7, R11, R12, R14, R15)

| Règle | Étape 2 | Étape 4 (table `Reservations`) | Étape 7 |
|---|---|---|---|
| R7 places restantes | `Event.RemainingSeats(occupancy)` ; occupation = 0 → `remainingSeats = capacity` | `EventOccupancyReader` compte `Confirmed` + `Pending` non échues (`ReservationQueries.IsActive(now)`, ADR 0005), groupé par événement | — |
| R2 réservable | `Event.IsBookable(now, occupancy)` exposé (`isBookable`) | utilisé par la demande de place sous verrou | — |
| R11 capacité | règle dans `Event.Update`, testée en Domain ; jamais déclenchée en vrai | déclenchée ; test d'intégration avec réservations, et test en parallèle d'une demande de place (ADR 0004) | — |
| R12 avertissement | idem | idem | — |
| R14 dépublication | idem, sous verrou | idem | annulation de l'événement sous le même verrou |
| R15 suppression | *(v4)* `DELETE` d'un **brouillon jamais publié** (`FirstPublishedAt` nul : aucune réservation n'a pu exister) | inchangé | R15 complète (Q29) : un événement publié sans aucune réservation devient supprimable ; `HasEverHadReservationsAsync` + FK `NO ACTION` ; `EnsureCanBeDeleted` s'élargit |

Un seul fichier à modifier à l'étape 4 pour les décomptes : `EventOccupancyReader.cs`. Le testeur de l'étape 4 reprend la liste « tests différés » du § 9.4.

---

## 9. Tests (pour l'agent `testeur`)

### 9.1 `Aaas.Domain.Tests` (sans base, instants fixes ; « 10 h Tunis = 09:00Z » en commentaire)

- **Création** : données valides → `Draft`, dates UTC, `CreatedAt = UpdatedAt = now`. Début = `now` → erreur `startsAt` ; début = `now + 1 min` → OK ; fin = début → erreur `endsAt` ; fin avant début → erreur ; capacité 0 → erreur, 1 → OK, 10 000 → OK, 10 001 → erreur. Plusieurs erreurs à la fois → toutes listées.
- **Prix** : `null` → gratuit ; 0 → `null` ; 12.5 et 12.500 → 12.5 ; 12.5001 → erreur ; −1 → erreur ; 99 999.999 → OK ; 100 000 → erreur.
- **Publication** : brouillon à venir → `Published`, `PublishedAt = now` ; déjà publié → `event_state_changed` ; annulé → `event_cancelled` ; début atteint (exactement `now`) → `event_started`.
- **Dépublication** : publié, occupation 0 → `Draft`, `PublishedAt = null` ; occupation (0 confirmée, 1 en attente) → `event_has_active_reservations` ; brouillon → `event_state_changed`.
- **Modification** :
  - R11 : 20 places, (10 confirmées, 5 en attente), capacité 12 → `capacity_below_reserved` et message « 15 places … (10 confirmées, 5 en attente) » ; capacité 15 → OK et `IsFull` ; capacité 25 sur un événement complet de 20 → 5 places restantes.
  - R12 : occupation > 0 et début, fin ou prix changé sans confirmation → `registrants_warning` ; avec confirmation → OK ; titre seul changé → pas d'avertissement ; occupation 0 → pas d'avertissement ; prix 0 → `null` sur un événement déjà gratuit → pas de changement, pas d'avertissement.
  - Priorité : capacité trop basse **et** date changée sans confirmation → `capacity_below_reserved`.
  - Événement commencé, début inchangé, description modifiée → OK ; début déplacé dans le passé → erreur `startsAt` ; événement terminé (`EndsAt = now`) → `event_ended` ; annulé → `event_cancelled`.
- **R7 / R2** : 20 places, (8 confirmées, 4 en attente) → 8 restantes ; (15, 5) → 0, complet, non réservable ; occupation > capacité (cas impossible mais défensif) → 0, jamais négatif ; brouillon, annulé ou commencé → non réservable.
- **`AssociationSettings.UpdateContact`** : numéro normalisé, WhatsApp, `UpdatedAt`.

### 9.2 `Aaas.Application.Tests` (validation des DTO, sans base)

- `[DateTimeWithOffset]` : `2026-12-05T10:00+01:00`, `2026-12-05T10:00:00+01:00`, `2026-12-05T09:00:00Z` acceptés ; `2026-12-05T10:00`, `05/12/2026 10:00`, `demain` refusés avec le message attendu ; vide → seulement le message `[Required]`.
- `[RowVersion]` : Base64 de 8 octets accepté ; `abc`, Base64 de 4 octets refusés.
- `CreateEventRequest` : chaque champ obligatoire manquant → clé camelCase attendue ; titre de 201 caractères refusé.
- `UpdateContactSettingsRequest` : `abc` → message RG-11 ; `+216 20 123 456` accepté.
- Requêtes de liste : `pageSize = 0` et `51` refusés, `page = 0` refusé.

### 9.3 `Aaas.Api.IntegrationTests` (Testcontainers, `FakeTimeProvider` si nécessaire)

Accès :
- Visiteur : `GET /api/events` 200 ; `GET /api/settings/contact` **401** ; toute route `/api/admin/...` 401.
- Membre (`User`) : `GET /api/settings/contact` 200 ; toute route `/api/admin/...` **403** et aucune donnée modifiée (relire en base).
- Admin : toutes 2xx dans le cas nominal.

Événements :
- `POST` → 201, `Location` correct, statut `Draft` ; `startsAt` envoyé `10:00+01:00` relu `09:00:00+00:00` (API **et** colonne en base à décalage nul).
- Date sans décalage → 400 `errors.startsAt` ; date passée → 400 `errors.startsAt` ; plusieurs champs invalides → 400 avec plusieurs clés.
- Brouillon : absent de `GET /api/events`, `GET /api/events/{id}` → 404 `not_found`.
- Publier → 200, présent dans la liste ; republier → 409 `event_state_changed`.
- Publier sans numéro de contact → `warnings[0].code = "contact_not_configured"` ; après `PUT` du numéro → `warnings` vide.
- Dépublier → 200, `Draft`, absent de la liste ; dépublier un brouillon → 409 `event_state_changed`.
- *(v4)* Supprimer un brouillon jamais publié → 204, absent en base ; publié, ou publié puis dépublié → 409 `event_already_published` ; inconnu → 404 ; membre → 403.
- *(v4)* Détail public d'un événement terminé → 200 avec `hasEnded: true` ; modification d'un événement commencé avec une fin déjà passée → 400 `errors.endsAt` (R28, Q28).
- Liste publique : tri par `startsAt` ; deux événements au même `startsAt` dans un ordre stable sur deux appels ; événement terminé absent ; événement en cours présent avec `hasStarted = true` et `isBookable = false` (*E2-1*) ; événement annulé présent avec `status = "Cancelled"` (préparé par `UPDATE Events SET Status = 3` via `ApiFactory.ExecuteAsync`) ; détail d'un événement terminé publié → 200 (*E2-2*).
- Pagination : 25 événements, `pageSize=10` → 3 pages, `totalCount = 25`, page 4 → `items` vide ; `pageSize=51` → 400 avec message français ; `page=abc` → 400 avec message français.
- Liste admin : brouillons inclus ; `period=past` ; `status=Draft`.
- Prix : `0` → `price: null` ; `12.500` → `12.5` ; `12.5001` → 400 `errors.price`.
- Concurrence : deux `PUT` **simultanés** avec la même `version` → exactement un 200 et un 409 `concurrent_update` ; un `PUT` avec une `version` déjà périmée → 409 ; la `version` change après chaque modification et après publication.
- Modification d'un événement terminé (horloge simulée avancée) → 409 `event_ended`.

Paramètres :
- Après migration : exactement une ligne dans `AssociationSettings`, `ContactPhone` NULL ; `GET` → `isConfigured: false`, `phoneNumber: null`.
- `PUT` admin `+216 20 123 456` → 200, `phoneNumber: "+21620123456"` ; `abc` → 400 `errors.phoneNumber` ; `0021620123456` → 400 ; `version` périmée → 409 `concurrent_update`.
- *(v4)* Numéro fixe `+216 71 123 456` → 200 ; `+33 6 12 34 56 78` → 400.
- *(v4)* `DELETE /api/admin/settings/contact` avec la `version` courante → 200, `phoneNumber: null`, `whatsApp: false`, `isConfigured: false` ; version périmée → 409 ; corps JSON vide → 400 (« Le corps de la requête est obligatoire. ») ; sans `Content-Type` → 415 ; membre → 403 ; publication suivante → avertissement `contact_not_configured`.
- Membre `PUT` → 403.

Schéma (filets en base, par SQL direct) : `INSERT` dans `AssociationSettings` avec `Id = 2` échoue ; `UPDATE Events SET Capacity = 0` échoue ; `Price = 0` échoue ; `EndsAt = StartsAt` échoue.

Journaux : aucune trace contenant un numéro de contact ou un e-mail lors d'un 409 ou d'un 400 (`ApiFactory.Logs`).

### 9.4 Tests différés à l'étape 4 (à reporter dans son plan)

R11 avec de vraies réservations (et message chiffré) ; R12 avertissement puis confirmation ; R14 dépublication refusée ; R7 avec demandes en attente échues non comptées ; réduction de capacité **en parallèle** d'une demande de place (ADR 0004) ; `GetManyAsync` sur une page de 20 événements (une seule requête de décompte).

---

## 10. Tâches ordonnées (`expert-technique`)

Chaque tâche se termine par un build vert et ses tests ; le `testeur` peut écrire les tests d'une tâche dès qu'elle est livrée.

| # | Tâche | Fichiers principaux | Dépend de | Vérifiable avec curl ? |
|---|---|---|---|---|
| T1 | Domain : `EventStatus`, `EventOccupancy`, `EventFields`, `EventDetails`, `EventRules`, `FieldError`, `Event`, `AssociationSettings`, codes d'erreur | `src/Aaas.Domain/Events/*`, `Settings/*`, `Common/*` | — | non (tests Domain § 9.1) |
| T2 | Application, socle : paquet EF Core, `IAppDbContext`, `IEventLock`, `PagedResult`, `RowVersions`, `ConcurrencyGuard`, `FieldErrors`, attributs `[DateTimeWithOffset]`, `[RowVersion]`, déplacement de `[TunisianPhoneNumber]` | `Directory.Packages.props`, `src/Aaas.Application/Abstractions/*`, `Common/*` | T1 | non (tests § 9.2) |
| T3 | Infrastructure : configurations EF, `DbSet`, `IAppDbContext` sur `AppDbContext`, `EventLock`, enregistrements DI ; **migration `AddEventsAndAssociationSettings`** ; relire le script SQL (§ 4.2) | `src/Aaas.Infrastructure/Persistence/*`, `DependencyInjection.cs` | T2 | oui : `docker compose up --build`, puis requête SQL (§ 11) |
| T4 | Paramètres : `SettingsService`, DTO, `SettingsController`, `AdminSettingsController` ; *(v4)* retrait du numéro (`DELETE`, avec `version`) | `src/Aaas.Application/Settings/*`, `src/Aaas.Api/Controllers/*Settings*` | T3 | **oui** (§ 11, étapes 2-3) |
| T5 | Événements admin : `EventOccupancyReader` (renvoie zéro), `EventQueries`, `AdminEventService`, DTO, `AdminEventsController` (création, lecture, liste, modification) ; *(v4)* suppression d'un brouillon jamais publié (`DELETE`, `FirstPublishedAt`), fin toujours future (R28) | `src/Aaas.Application/Events/*`, `src/Aaas.Api/Controllers/AdminEventsController.cs` | T3 | **oui** |
| T6 | Publication / dépublication (*(v4)* `FirstPublishedAt` posé à la première publication, conservé à la dépublication), avertissement `contact_not_configured` | `AdminEventService`, `AdminEventsController` | T4, T5 | **oui** |
| T7 | Consultation publique : `PublicEventService`, `EventsController` | `src/Aaas.Application/Events/PublicEventService.cs`, `src/Aaas.Api/Controllers/EventsController.cs` | T5 | **oui** (sans jeton) |
| T8 | Messages français de liaison de modèle (§ 6.3) | `src/Aaas.Api/Program.cs` | T7 | oui (`?page=abc`) |
| T9 | Tests d'intégration § 9.3 (avec le `testeur`) | `tests/Aaas.Api.IntegrationTests/Events/*`, `Settings/*` | T4-T8 | — |
| T10 | Vérification `docker compose up --build` de bout en bout + scénario curl § 11 | — | T9 | oui |
| T11 | Documentation (§ 14) | `docs/technique/*`, `docs/revision/*` | T10 | — |
| T12 | Relecture `code-reviewer` | — | T11 | — |

Ordre retenu : **paramètres avant événements** (T4 avant T5/T6) parce que c'est le cas le plus simple pour poser `IAppDbContext` et le contrôle de `version`, et que la publication (T6) a besoin de savoir si le numéro est configuré.

---

## 11. Vérification manuelle avec curl (Git Bash, `docker compose up --build`)

`jq` n'est pas installé sur le poste : les valeurs sont extraites avec `sed`. Les dates doivent être dans le futur au moment du test.

```bash
API=http://localhost:8080
H_JSON=(-H "Content-Type: application/json" -H "Accept: application/json")

# 1. Jetons (admin : identifiants de .env ; membre : compte créé avec POST /api/auth/register)
ADMIN=$(curl -s -X POST $API/api/auth/login "${H_JSON[@]}" -d '{"email":"<admin>","password":"<mot de passe>"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')
MEMBRE=$(curl -s -X POST $API/api/auth/login "${H_JSON[@]}" -d '{"email":"<membre>","password":"<mot de passe>"}' \
  | sed -E 's/.*"accessToken":"([^"]+)".*/\1/')

# 2. Numéro de contact : visiteur 401, membre 200 (non configuré), admin enregistre
curl -i $API/api/settings/contact                                   # 401
curl -s $API/api/settings/contact -H "Authorization: Bearer $MEMBRE" # isConfigured: false + version
V=$(curl -s $API/api/settings/contact -H "Authorization: Bearer $ADMIN" | sed -E 's/.*"version":"([^"]+)".*/\1/')
curl -i -X PUT $API/api/admin/settings/contact "${H_JSON[@]}" -H "Authorization: Bearer $MEMBRE" \
  -d "{\"phoneNumber\":\"+216 20 123 456\",\"whatsApp\":true,\"version\":\"$V\"}"     # 403
curl -i -X PUT $API/api/admin/settings/contact "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" \
  -d "{\"phoneNumber\":\"abc\",\"whatsApp\":true,\"version\":\"$V\"}"                 # 400 errors.phoneNumber

# 3. Créer un événement (brouillon)
curl -i -X POST $API/api/admin/events "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" -d '{
  "title":"Atelier couture","description":"Initiation","location":"Tunis",
  "startsAt":"2026-12-05T10:00+01:00","endsAt":"2026-12-05T12:00+01:00","capacity":30,"price":12.5}'
# 201, Location: /api/admin/events/<id>, startsAt "2026-12-05T09:00:00+00:00", status "Draft"
ID=<id lu dans Location>

# 4. Erreurs de saisie
curl -i -X POST $API/api/admin/events "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" -d '{
  "title":"X","description":"Y","location":"Z","startsAt":"2026-12-05T10:00","endsAt":"2020-01-01T10:00+01:00","capacity":0,"price":-1}'
# 400 : errors.startsAt (décalage manquant) ; puis, une fois corrigé, errors.endsAt / capacity / price

# 5. Brouillon invisible, publication, visible
curl -i $API/api/events/$ID                                          # 404
curl -s -X POST $API/api/admin/events/$ID/publish -H "Authorization: Bearer $ADMIN" -H "Accept: application/json"
curl -s "$API/api/events?page=1&pageSize=20"                         # l'événement, remainingSeats 30, price 12.5
curl -i -X POST $API/api/admin/events/$ID/publish -H "Authorization: Bearer $ADMIN"   # 409 event_state_changed

# 6. Concurrence : deux modifications avec la même version
V=$(curl -s $API/api/admin/events/$ID -H "Authorization: Bearer $ADMIN" | sed -E 's/.*"version":"([^"]+)".*/\1/')
BODY="{\"title\":\"Atelier couture (complet)\",\"description\":\"Initiation\",\"location\":\"Tunis\",\"startsAt\":\"2026-12-05T10:00+01:00\",\"endsAt\":\"2026-12-05T12:00+01:00\",\"capacity\":40,\"price\":12.5,\"version\":\"$V\"}"
curl -i -X PUT $API/api/admin/events/$ID "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" -d "$BODY"   # 200
curl -i -X PUT $API/api/admin/events/$ID "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" -d "$BODY"   # 409 concurrent_update

# 7. Dépublication
curl -i -X POST $API/api/admin/events/$ID/unpublish -H "Authorization: Bearer $ADMIN"   # 200, status Draft

# 8. Membre sur une route admin
curl -i $API/api/admin/events -H "Authorization: Bearer $MEMBRE"     # 403 forbidden

# 9. (v4) Suppression : l'événement a été publié puis dépublié → 409 ; un nouveau brouillon → 204
curl -i -X DELETE $API/api/admin/events/$ID -H "Authorization: Bearer $ADMIN"           # 409 event_already_published

# 10. (v4) Numéro de contact : enregistrer un fixe, puis le retirer avec la version courante
V=$(curl -s $API/api/settings/contact -H "Authorization: Bearer $ADMIN" | sed -E 's/.*"version":"([^"]+)".*/\1/')
curl -s -X PUT $API/api/admin/settings/contact "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" \
  -d "{\"phoneNumber\":\"+216 71 123 456\",\"whatsApp\":true,\"version\":\"$V\"}"        # 200, +21671123456
V=$(curl -s $API/api/settings/contact -H "Authorization: Bearer $ADMIN" | sed -E 's/.*"version":"([^"]+)".*/\1/')
curl -i -X DELETE $API/api/admin/settings/contact "${H_JSON[@]}" -H "Authorization: Bearer $ADMIN" \
  -d "{\"version\":\"$V\"}"                                                             # 200, phoneNumber null, whatsApp false
```

Non vérifiable avec curl à cette étape (pas de réservations) : R11, R12, R14 en conditions réelles (§ 9.4), « Complet », et l'annulation (étape 7 ; un `UPDATE ... SET Status = 3` en SQL permet de voir la mention « Annulé » dans la liste).

---

## 12. Ce que l'étape apporte côté Docker et migrations

- **Aucun nouveau service** dans `docker-compose.yml`, aucune nouvelle variable d'environnement, aucun secret.
- **Reconstruire l'image** : `docker compose up -d --build api`. Comme `Aaas.Application.csproj` et `Directory.Packages.props` changent (paquet EF Core), la couche `dotnet restore` du Dockerfile est invalidée **une fois** : le premier build est plus long, les suivants réutilisent le cache tant que seuls des `.cs` changent. Bonne occasion d'observer le cache de couches (`docker build` affiche `CACHED` sur les étapes réutilisées).
- **Migration au démarrage** : `Database__MigrateOnStartup=true` dans compose → au démarrage, l'API applique `AddEventsAndAssociationSettings` sur la base existante du volume `sqlserver-data` ; les comptes de l'étape 1 sont conservés. Vérification :

  ```bash
  MSYS_NO_PATHCONV=1 docker compose exec sqlserver bash -c '/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -I -d Aaas -Q "SELECT MigrationId FROM __EFMigrationsHistory; SELECT * FROM AssociationSettings;"'
  ```

  Attendu : `..._InitialIdentity`, `..._AddEventsAndAssociationSettings`, et une ligne `Id = 1` avec `ContactPhone` à `NULL`.
- **Repartir de zéro** : `docker compose down -v` supprime le volume (donc la base et l'admin) ; au démarrage suivant, toutes les migrations et le premier admin sont rejoués. À ne faire qu'en local.
- **Index filtré et `sqlcmd -I`** : `Events` et `AssociationSettings` n'ont pas d'index filtré, mais l'option `-I` reste la règle pour tous les scripts (`exploitation.md` § 6).
- **Azure (S3)** : aucune nouvelle ressource. La migration sera appliquée par le *migration bundle* de la CI. `AssociationSettings` est créée vide en production : **l'admin devra saisir le numéro de contact après le premier déploiement** (à ajouter à la check-list de mise en ligne d'`exploitation.md`).

---

## 13. Ce que l'étape prépare côté Angular (semaine 2)

L'API de l'étape 2 fixe les contrats que les écrans consommeront. Rien n'est codé côté front maintenant.

| Élément Angular (S2) | Rôle | Contrat utilisé |
|---|---|---|
| `core/models/event.ts`, `paged-result.ts`, `contact-settings.ts` | interfaces TypeScript recopiées des DTO (camelCase, dates en `string` ISO) | § 5.3 |
| `core/api/events.api.ts`, `admin-events.api.ts`, `settings.api.ts` | services `providedIn: 'root'` qui appellent `HttpClient` ; l'interceptor JWT existant ajoute le jeton | § 6.1 |
| `features/public/events/event-list` (`/evenements`) | signal `page = signal(1)`, chargement de la page, `computed` pour la mention (Annulé > Réservations closes > Complet > N places restantes), message « Aucun événement prévu pour le moment » si `totalCount = 0` | `PagedResult<EventSummaryDto>` |
| `features/public/events/event-detail` (`/evenements/:id`) | paramètre de route lu avec `input()` (liaison des paramètres de route aux entrées du composant) ; 404 → page « Contenu introuvable » ; bouton « Demander une place » qui redirige vers `/connexion?returnUrl=...` pour un visiteur | `EventDetailDto` |
| `features/admin/events` (`/admin/evenements`, `/admin/evenements/nouveau`, `/admin/evenements/:id`) | liste avec filtres `status`/`period` ; formulaire réactif typé ; boutons Publier / Dépublier | admin § 6.1 |
| `features/admin/settings` (`/admin/parametres`) | formulaire numéro + case « WhatsApp » ; même validateur que l'inscription (+216) | `ContactSettingsDto` |
| `shared/pipes/tnd-price.pipe.ts` | « Gratuit », « 15 TND, à régler sur place », « 12,500 TND, à régler sur place » (ADR 0010) | `price` |
| `core/tunis-time.ts` : `toTunisIso(local: string)` | le champ `<input type="datetime-local">` renvoie `2026-12-05T10:00` **sans fuseau** ; on ajoute `+01:00` (ADR 0003, 0009) | `startsAt`, `endsAt` |
| `core/api-error.ts` | lit `code` et `errors` des `ProblemDetails` : erreurs 400 recopiées sur les contrôles du formulaire (`form.controls[clé].setErrors({ server: message })`) ; 409 `registrants_warning` → `confirm()` puis renvoi avec `confirmRegistrantsWarning: true` ; 409 `concurrent_update` → message et rechargement de la fiche | §§ 6, 7 |

Liaison Docker ↔ Angular (rappel pour S2) : en local, le conteneur `front` (nginx) servira les fichiers Angular compilés et relaiera `/api` vers `api:8080` ; le front appelle donc `/api/events` en chemin relatif, sans CORS. En développement hors Docker (`ng serve`), un fichier `proxy.conf.json` jouera le même rôle.

---

## 14. Documentation à mettre à jour (même tâche que le code)

| Document | Mise à jour | Par |
|---|---|---|
| `docs/technique/api.md` | les 10 endpoints, DTO, codes ; `PagedResult` ; dates avec décalage obligatoire ; limite connue des erreurs de type JSON (`$.champ`) | expert-technique |
| `docs/technique/regles-metier.md` | section « Événements » (statuts, transitions, R2, R7, R11, R12, R14, validations, endroit du code) ; section « Numéro de contact » ; nouveaux codes d'erreur ; « en attendant l'étape 4 » (§ 8) | expert-technique |
| `docs/technique/exploitation.md` | migration `AddEventsAndAssociationSettings` dans la liste ; vérification SQL du § 12 ; check-list de mise en ligne : saisir le numéro de contact | expert-technique |
| `docs/revision/ef-core-concurrence.md` (nouvelle fiche) | `rowversion`, `DbUpdateConcurrencyException`, `UPDLOCK`, stratégie d'exécution et `ChangeTracker.Clear()`, `HasData`, `CHECK`, `decimal(p,s)` | expert-technique |
| `docs/revision/docker.md` | cache de couches observé lors de l'ajout d'un paquet ; migration au démarrage sur un volume existant ; `down -v` | expert-technique |
| `docs/guide-utilisateur/` | rien à cette étape (pas d'écran) ; noter pour S2 : guide admin « créer et publier un événement », « numéro de contact » | documentaliste (S2) |
| `docs/technique/architecture.md` | fait (v7) | architecte |

---

## 15. Risques

| Risque | Parade |
|---|---|
| Le verrou `UPDLOCK` n'est pas réellement posé (requête brute réécrite par EF) | vérifier le SQL en `Debug` local ; test de deux `PUT` simultanés |
| Tentative rejouée par la stratégie d'exécution avec un état EF résiduel | `ChangeTracker.Clear()` au début de chaque tentative (ADR 0012) |
| Date envoyée sans décalage par un futur client et décalée d'une heure | refus 400 (ADR 0009) ; test dédié |
| Prix arrondi silencieusement | refus au-delà de 3 décimales ; test 12.5001 |
| Oubli de modifier `EventOccupancyReader` à l'étape 4 | commentaire `// Étape 4`, tests différés § 9.4 reportés dans le plan de l'étape 4 |
| Numéro de contact absent en production | avertissement à la publication ; check-list de mise en ligne |
| `Application` dépend désormais d'EF Core | assumé (ADR 0001) ; vérifier en revue qu'aucun type `SqlServer`/`Relational` n'y entre |
| Messages anglais pour les erreurs de type dans le JSON | risque accepté, documenté ; le front envoie les bons types |

---

## 16. Incohérences relevées et questions ouvertes

À trancher par l'`expert-fonctionnel` (avec l'association si besoin) ou le porteur. Les valeurs par défaut permettent d'avancer ; aucune ne change le périmètre.

| # | Sujet | Constat | Valeur par défaut retenue | Pour |
|---|---|---|---|---|
| E2-1 | « À venir » | US-C1 exclut « un événement passé » sans dire ce qu'est un événement **en cours** | listé jusqu'à sa fin (`EndsAt > now`), avec « Réservations closes » | expert-fonctionnel |
| E2-2 | Détail d'un événement terminé | R19 : pas d'archive ; mais « Mes réservations » (US-D2) affichera des événements passés | détail accessible par son adresse, absent de la liste | expert-fonctionnel |
| E2-3 | Format du prix | US-C1 : « 15 TND » ; US-E2 : « 12,500 TND » | entier sans décimales, sinon 3 décimales | expert-fonctionnel |
| E2-4 | Avertissement « inscrits » | R12 cite date et prix ; un changement de **lieu** concerne aussi les inscrits | lieu exclu (texte de R12) | expert-fonctionnel |
| E2-5 | Bornes de saisie | non spécifiées | capacité ≤ 10 000, prix ≤ 99 999,999 TND, description ≤ 4 000 caractères, titre et lieu ≤ 200 | expert-fonctionnel |
| E2-6 | Modifier un événement commencé ou annulé | non spécifié | commencé : permis (début non déplaçable dans le passé) ; terminé ou annulé : refusé | expert-fonctionnel |
| E2-7 | Retirer le numéro de contact | US-E8 ne prévoit que l'enregistrement et le changement | pas de suppression au MVP | expert-fonctionnel |
| E2-8 | Avertissement « numéro non configuré » | US-E8 : « quand j'essaie de publier … un avertissement » : bloquant ou non ? | non bloquant, renvoyé avec la publication | expert-fonctionnel |
| E2-9 | Supprimer un brouillon créé par erreur | la suppression est en US-E3 (étape 7) ; pendant les étapes 2 à 6, un brouillon erroné ne peut que rester en brouillon | pas d'endpoint avant l'étape 7 | porteur |
| E2-10 | Type du prix | la demande de conception citait `decimal(10,3)` ; `architecture.md` v6 prévoyait `decimal(9,3)` | `decimal(9,3)` (ADR 0010) | porteur |
| E2-11 | Objet valeur `Money` | prévu dans `architecture.md` v6 | abandonné (ADR 0010) | porteur |
| E2-12 | Numéro de contact fixe | Q17 applique « +216 + 8 chiffres » ; un fixe tunisien a aussi 8 chiffres, donc accepté | même règle RG-11 | expert-fonctionnel (Q17) |
| E2-13 | Taille de page publique | non spécifiée pour les événements (10 pour les actualités) | 20 | porteur |

### Réponses (19/11/2026, cahier v1.2, `mvp.md` v4 : Q18 à Q29)

| # | Réponse | Effet sur l'implémentation |
|---|---|---|
| E2-1 (Q18) | défaut confirmé : événement en cours listé jusqu'à sa fin | aucun |
| E2-2 (Q19) | **porteur** : détail d'un terminé accessible, absent de la liste | `hasEnded` ajouté aux DTO publics ; mention « Terminé » (§ 5.3) |
| E2-3 (Q20), E2-4 (Q21), E2-8 (Q25), E2-13 (Q27) | défauts confirmés | aucun |
| E2-5 (Q22) | **porteur** : bornes validées (RG-13, R29) | aucun |
| E2-6 (Q23) | **porteur** : commencé modifiable ; terminé ou annulé non (R28, RG-12) | aucun |
| Q28 (nouvelle) | la fin d'un événement commencé ne peut pas être placée dans le passé | `EventRules.Validate` : fin toujours dans le futur (`errors.endsAt`) |
| E2-7 (Q24) | **porteur, écart** : un admin peut retirer le numéro (WhatsApp repasse à non) | `DELETE /api/admin/settings/contact` (avec `version`), `AssociationSettings.RemoveContact` |
| E2-9 (Q26) | **porteur, écart** : un brouillon jamais publié peut être supprimé dès l'étape 2 | `DELETE /api/admin/events/{id}`, colonne `FirstPublishedAt`, code `event_already_published` |
| Q29 (nouvelle) | à partir de l'étape 7, R15 d'origine (publié sans aucune réservation : supprimable) | rien à coder maintenant ; § 8 |
| E2-12 (Q17) | **porteur** : mobile ou fixe tunisien, même règle RG-11 | aucun (déjà le cas) ; tests avec `+216 71 123 456` |
