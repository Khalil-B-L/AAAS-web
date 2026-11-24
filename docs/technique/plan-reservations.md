# Plan d'implémentation — Tranche « Réservations » (blocs 2 et 3 de `etat-avancement.md`)

> Rédigé par l'agent `architecte` le 24/11/2026. Destinataires : `expert-technique` (tâches § 11), `testeur` (§ 9), `code-reviewer`, `expert-fonctionnel` (§ 15), porteur.
> Références : cahier des charges v1.3 (§ 4.1, § 5.1, RG-01 à RG-08, RG-10, RG-12, § 6.4) ; `fonctionnel/mvp.md` v4 (US-C2, US-D1 à D5, US-E4 à E7 ; R1 à R10, R13, R24) ; `architecture.md` v8 ; ADR 0004, 0005, 0006, 0007, 0012, 0013 ; **nouveaux ADR 0018 à 0021**.
> Statut : **accepté** (porteur, 24/11/2026 : valeurs par défaut P-R1 à P-R8 retenues ; ADR 0004 à 0006 et 0018 à 0021 acceptés). **Tâche A réalisée le 24/11/2026** (état : `etat-avancement.md`).

---

## 0. Périmètre et découpage

Méthode du cahier § 8 : tranches verticales. Chaque tâche livre API **puis** écrans **puis** tests et docs, utilisable sous `docker compose up`.

| Tâche | Contenu | Stories | Parcours démontrable à la fin |
|---|---|---|---|
| **A — Demander, suivre, annuler (membre)** | table `Reservations`, demande sous verrou, décompte réel des places, « ma demande » sur le détail, numéro à contacter, `/mes-reservations`, annulation par le membre, recalcul des échéances quand la date d'un événement change | US-D1, D2, D3, D4 ; US-C2 (partie membre) ; R7 réel ; R11, R12, R14 deviennent atteignables | un membre demande la dernière place, voit le numéro, annule ; un second membre la reprend ; une demande non traitée cesse de compter après 48 h (sans tâche de fond) |
| **B — Traiter (admin)** | demandes à traiter (tous événements), confirmer, refuser avec motif, liste des inscrits d'un événement, annulation par l'admin, mention « Payée » | US-E4, E5, E6, E7 ; US-D5 (« une demande expirée ne se confirme plus ») | l'admin confirme ou refuse, marque « Payée », annule un désistement ; le membre voit le résultat |
| **C — Expiration automatique** | `BackgroundService` qui écrit « Expirée », sans réveiller la base inutilement | US-D5 (rangement de la base) | après 48 h simulées, la demande apparaît « Expirée » en base et dans les listes |

Hors tranche : annulation d'un événement et suppression d'un publié sans réservation (bloc 4, US-E3 ; le Domain prépare `CancelByAssociation`), suppression de compte (bloc 6, ADR 0007 ; le schéma la prépare : `UserId` et `PaidByAdminId` nullables).

Pourquoi cet ordre : A contient la partie la plus risquée (dernière place) et rend vraies les règles déjà codées à l'étape 2 (R11, R12, R14). Sans B, les demandes ne sont jamais confirmées mais **expirent correctement** (filtre à la lecture, ADR 0005) : A est livrable seul. C est un rangement, pas une garantie : il vient en dernier.

---

## 1. Notions à réviser (pédagogie)

### 1.1 Les verrous SQL Server en 5 minutes

| Verrou | Pris par | Compatible avec | Image |
|---|---|---|---|
| **S** (partagé) | une lecture en Read Committed **sans** RCSI | S, U | « je lis, ne modifiez pas pendant ma lecture » |
| **U** (mise à jour) | `SELECT ... WITH (UPDLOCK)`, ou la recherche de lignes d'un `UPDATE` | S seulement | « je lis **avec l'intention** d'écrire ; un seul à la fois » |
| **X** (exclusif) | `INSERT`, `UPDATE`, `DELETE` | rien | « j'écris » |

- **`UPDLOCK`** : deux transactions qui veulent le même verrou U sur la ligne de l'événement **font la queue**. C'est exactement ce qu'on veut : « compter puis insérer » devient atomique par événement. Les lectures ordinaires (liste publique) ne sont pas bloquées.
- **`ROWLOCK`** : demande un verrou de **ligne** plutôt que de page, pour ne pas bloquer les événements voisins.
- Le verrou dure **jusqu'au `COMMIT`** : c'est pour cela qu'il faut une transaction explicite (`IEventLock`).
- **Interblocage** (*deadlock*) : A attend B qui attend A ; SQL Server tue l'une des deux (erreur 1205). On l'évite en prenant les verrous **toujours dans le même ordre** (d'abord l'événement, ADR 0004) ; s'il arrive quand même, la stratégie d'exécution d'EF rejoue le bloc.

### 1.2 Niveaux d'isolation (question d'entretien classique)

| Niveau | Ce qu'il empêche | Coût |
|---|---|---|
| Read Uncommitted | rien (lectures « sales ») | — |
| **Read Committed** (défaut) | lecture de données non validées | lecteurs et écrivains se bloquent (verrous S) |
| **Read Committed Snapshot (RCSI)** — défaut d'Azure SQL | idem, mais un lecteur lit la **dernière version validée** au début de l'instruction, sans verrou S | versions stockées dans tempdb ; lecteurs jamais bloqués |
| Repeatable Read | une ligne relue change entre deux lectures | verrous S gardés jusqu'à la fin |
| **Serializable** | lignes « fantômes » (un `COUNT` qui change) | verrous de **plage** ; interblocages fréquents sur « compter puis insérer » |
| **Snapshot** | tout, par versions, à l'échelle de la **transaction** | conflits d'écriture levés en erreur |

Notre choix (ADR 0004, 0019) : **Read Committed (RCSI activé) + `UPDLOCK` sur l'événement**. Le comptage se fait **après** l'obtention du verrou ; sous RCSI, il lit les données validées **au début de l'instruction**, donc il voit l'insertion de la demande précédente. Piège : en isolation `SNAPSHOT`, l'instantané daterait du début de la transaction et le comptage serait faux.

### 1.3 Index unique filtré

`CREATE UNIQUE INDEX UX_Reservations_Event_User_Active ON Reservations(EventId, UserId) WHERE Status IN (1, 2) AND UserId IS NOT NULL`

- Unicité **seulement** sur les lignes qui passent le filtre : un membre peut avoir dix réservations refusées pour un événement, mais une seule en attente ou confirmée (RG-03).
- Valeurs de statut **figées** : changer la valeur de « Confirmée » casserait l'index sans erreur de compilation.
- Contrainte technique : toute session qui écrit dans la table doit avoir `QUOTED_IDENTIFIER ON` (EF et SSMS oui ; `sqlcmd` seulement avec `-I`).
- C'est un **filet** : la règle est d'abord vérifiée par le Domain sous verrou ; l'index garantit qu'un script ou un bug ne la contourne pas.
- Conséquence pratique : une demande « En attente » échue mais pas encore écrite « Expirée » bloque l'index ; d'où la matérialisation des expirations de l'événement **avant** l'insertion (ADR 0005 § 4).

### 1.4 Concurrence optimiste (`rowversion`) vs pessimiste (`UPDLOCK`)

- **Pessimiste** : on empêche le conflit en faisant attendre (verrou). Adapté quand le conflit est **probable et coûteux** : la dernière place.
- **Optimiste** : on laisse faire et on détecte le conflit à l'écriture (`WHERE RowVersion = @lue` ; 0 ligne → `DbUpdateConcurrencyException`). Adapté quand le conflit est **rare** : deux admins sur la même demande.

### 1.5 `BackgroundService`, `PeriodicTimer`, `TimeProvider`

- `IHostedService` : service démarré et arrêté avec l'hôte ASP.NET Core. `BackgroundService` en est la base pratique : on écrit `ExecuteAsync(stoppingToken)`.
- C'est un **singleton** : pour utiliser un `DbContext` (*scoped*), on crée un **scope par passage** (`IServiceScopeFactory`).
- Depuis .NET 6, une exception qui sort de `ExecuteAsync` **arrête l'application** : on attrape dans la boucle, on journalise, on continue.
- `PeriodicTimer(interval, timeProvider)` : attend le prochain « tic » sans dériver ; avec `FakeTimeProvider`, le test avance le temps (`Advance(TimeSpan.FromMinutes(5))`) au lieu d'attendre.
- `TimeProvider` : une seule source d'heure pour tout le code (ADR 0003). **On lit `now` une fois par opération**, et pour la demande de place, **après** l'obtention du verrou.

### 1.6 Statut effectif

Une demande « En attente » dont l'échéance est passée **est** expirée, même si la base dit encore 1. Le Domain calcule `EffectiveStatus(now)` ; les DTO n'exposent que lui ; les décomptes filtrent `IsActive(now)`. La base est rattrapée par l'écriture sous verrou et par la tâche de fond.

---

## 2. Décisions par défaut (résumé)

| # | Sujet | Décision par défaut | ADR |
|---|---|---|---|
| R-1 | Places par demande | **1** ; pas de colonne `Seats` (cahier § 4.2) | 0018 |
| R-2 | Plusieurs demandes par membre et événement | **plusieurs lignes**, **une seule active** (index unique filtré) ; nouvelle ligne après refus, expiration, annulation | 0018 |
| R-3 | Statuts | 1 En attente, 2 Confirmée, 3 Refusée, 4 Expirée, 5 Annulée par le membre, 6 Annulée par l'association (`tinyint`, figés) | 0018 |
| R-4 | Places prises | **En attente non échues + Confirmées** ; décompte calculé par `EventOccupancyReader`, jamais stocké | 0004, 0005 |
| R-5 | Échéance | `ExpiresAt = min(RequestedAt + 48 h, StartsAt)`, stockée ; **recalculée** sous verrou si le début de l'événement change (sans ranimer une demande échue) | 0005, 0018 |
| R-6 | Concurrence | demande de place sous `UPDLOCK` (`IEventLock`, `now` lu après le verrou) ; transitions par `rowversion` ; index filtré en filet | 0004, 0006, 0019 |
| R-7 | Double clic | pas d'`Idempotency-Key` ; 409 `already_reserved` + `reservationId`, traité comme un succès silencieux par le front | 0019 |
| R-8 | Erreurs | 409 avec `code` dédié ; `currentStatus` et `reservationId` en extensions (`BusinessRuleException.Details`) | 0018 |
| R-9 | Expiration automatique | `BackgroundService` + `PeriodicTimer(5 min, TimeProvider)`, `ExecuteUpdate` idempotent, **seulement si l'API a eu de l'activité** (ne pas réveiller Azure SQL) | 0005, 0020 |
| R-10 | Routes | `/api/me/...` (identité tirée du jeton) et `/api/admin/...` ; détail public inchangé, « ma demande » par un appel séparé (204 si aucune) | 0021 |
| R-11 | Données vues par l'admin | prénom, nom, téléphone, e-mail, dates, motif, « Payée » (date, admin) ; `Cache-Control: no-store` ; rien dans les journaux | 0021 |
| R-12 | Numéro de contact | jamais recopié ; lu par le front (`GET /api/settings/contact`) | 0013, 0021 |
| R-13 | Délais | `appsettings.json` section `Reservations` (48 h, 24 h, 5 min), validés au démarrage ; pas modifiables par l'admin au MVP | 0020 |

---

## 3. Données et migration `AddReservations`

Schéma complet : ADR 0018 § 2. Résumé :

```
Reservations
  Id uniqueidentifier PK
  EventId uniqueidentifier NOT NULL  FK Events(Id) NO ACTION
  UserId uniqueidentifier NULL       FK AspNetUsers(Id) NO ACTION
  Status tinyint NOT NULL            CK BETWEEN 1 AND 6
  RequestedAt, ExpiresAt datetimeoffset NOT NULL   CK ExpiresAt > RequestedAt
  DecidedAt, CancelledAt, PaidAt datetimeoffset NULL
  RefusalReason nvarchar(500) NULL
  IsPaid bit NOT NULL DEFAULT 0      CK IsPaid = 0 OR Status IN (2,5,6) ; CK IsPaid = 0 OR PaidAt IS NOT NULL
  PaidByAdminId uniqueidentifier NULL FK AspNetUsers(Id) NO ACTION
  RowVersion rowversion
Index : UX_Reservations_Event_User_Active (filtré), IX_Reservations_EventId_Status INCLUDE (ExpiresAt),
        IX_Reservations_Status_ExpiresAt, IX_Reservations_UserId, IX_Reservations_PaidByAdminId
```

Points de configuration EF (Infrastructure, `ReservationConfiguration`) :
- deux clés étrangères vers `AppUser` sans navigation dans le Domain : `HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.NoAction)` (idem `PaidByAdminId`) ;
- `HasOne<Event>().WithMany().HasForeignKey(r => r.EventId).OnDelete(NoAction)` ;
- `HasIndex(...).IsUnique().HasFilter("[Status] IN (1, 2) AND [UserId] IS NOT NULL")` ; `IncludeProperties(r => r.ExpiresAt)` sur l'index de décompte ;
- `Status` en `byte`, `RowVersion` en `IsRowVersion()`, `RefusalReason` `HasMaxLength(500)`.

Migration : `dotnet ef migrations add AddReservations`, puis **relire le script** (`dotnet ef migrations script AddEventsAndAssociationSettings AddReservations`) : filtre de l'index, `INCLUDE`, `NO ACTION` sur les trois clés étrangères, contraintes `CHECK`. Appliquée au démarrage (`Database:MigrateOnStartup`), sans donnée à reprendre.

---

## 4. Domain (`src/Aaas.Domain`)

| Fichier | Contenu |
|---|---|
| `Reservations/ReservationStatus.cs` | enum `: byte` (1 à 6) |
| `Reservations/Reservation.cs` | propriétés de l'ADR 0018 ; `Request`, `Confirm`, `Refuse`, `CancelByUser`, `CancelByAssociation`, `Expire`, `MarkPaid`, `UnmarkPaid`, `RescheduleDeadline` ; `IsActive(now)`, `IsDue(now)`, `EffectiveStatus(now)`, `CanBeCancelledByUser(ev, now, deadline)` (pour `canCancel` des DTO), `CancellableUntil(ev)` |
| `Reservations/ReservationRules.cs` | `ComputeExpiresAt(requestedAt, lifetime, startsAt)`, constantes (motif ≤ 500), messages français |
| `Events/Event.cs` | `EnsureCanAcceptRequest(now, occupancy)` |
| `Common/BusinessRuleException.cs` | propriété facultative `Details` |
| `Common/ErrorCodes.cs` | `event_full`, `reservations_closed`, `already_reserved`, `reservation_state_changed`, `cancellation_deadline_passed`, `reservation_not_confirmed`, `payment_not_applicable` ; réutilisés : `event_cancelled` (demande sur un événement annulé : « Cet événement a été annulé. »), `event_ended` (annulation par l'admin après la fin : « Cet événement est terminé : la réservation ne peut plus être annulée. »), `not_found` |

Messages (validés dans `libelles-ecrans.md` v1.1 § 11 ; côté membre, une demande expirée donne « Votre demande a expiré. Vous pouvez en faire une nouvelle. ») : « Événement complet », « Les réservations sont closes », « Vous avez déjà une demande en cours ou une réservation pour cet événement », « Le délai d'annulation est dépassé : contactez l'association », « Confirmez d'abord la demande avant de la marquer payée », « Cet événement est gratuit : aucun paiement à enregistrer », « Cette demande a expiré : le membre doit faire une nouvelle demande. », « Cette demande a déjà été traitée ou annulée. »

---

## 5. Application (`src/Aaas.Application`)

| Fichier | Rôle |
|---|---|
| `Abstractions/IAppDbContext.cs` | `DbSet<Reservation> Reservations` |
| `Options/ReservationOptions.cs` | `PendingLifetime`, `ConfirmedCancellationDeadline`, `ExpirationJob { Enabled, Interval, OnlyWhenActive }` + validation (bornes) |
| `Common/Errors/UniqueConstraintViolationException.cs` | exception neutre levée par Infrastructure (ADR 0019 § 3) |
| `Reservations/ReservationQueries.cs` | `IsActive(now)` (expression traduite en SQL), `IsCurrentForMember(now)`, tris |
| `Events/EventOccupancyReader.cs` | **réécrit** : `Where(IsActive(now)).GroupBy(EventId)` avec `Count(r => r.Status == Pending)` et `Count(r => r.Status == Confirmed)` ; une requête pour `GetManyAsync` |
| `Reservations/MemberReservationService.cs` | `RequestAsync(userId, eventId)` (sous `IEventLock`, ADR 0019 § 2), `ListAsync(userId, scope, page)`, `GetAsync(userId, id)`, `GetForEventAsync(userId, eventId)`, `CancelAsync(userId, id)` |
| `Reservations/AdminReservationService.cs` (B) | `ListPendingAsync(page)`, `GetEventRegistrantsAsync(eventId)`, `ConfirmAsync`, `RefuseAsync(reason)`, `CancelAsync`, `MarkPaidAsync(adminId)`, `UnmarkPaidAsync` |
| `Reservations/ReservationExpiryService.cs` (A pour la matérialisation par événement, C pour le global) | `ExpireDueForEventAsync(eventId, now)`, `ExpireDueAsync(now)` (`ExecuteUpdateAsync`) |
| `Reservations/ReservationDtos.cs` | `CreateReservationRequest { eventId }`, `RefuseReservationRequest { reason? }`, `MyReservationDto`, `AdminReservationDto`, `EventRegistrantsDto`, `MemberContactDto` |
| `Reservations/TransitionRunner` (privé aux services) | charge, applique la méthode du Domain, `SaveChanges` ; sur `DbUpdateConcurrencyException` : relit et lève `reservation_state_changed` avec `currentStatus` |
| `Accounts/IIdentityService.cs` (B) | `GetContactsAsync(ids)` (ADR 0021 § 3) |
| `Events/AdminEventService.cs` (A) | dans `UpdateAsync`, si `startsAt` change : recalcul des échéances des demandes non échues (ADR 0018 § 5), sous le verrou existant |

Ordre des contrôles dans `RequestAsync` (dans `work`, après le verrou) : `now` → brouillon : 404 « Contenu introuvable. » → matérialisation des échues de l'événement → occupation et « a déjà une active ? » → `Reservation.Request` (annulé, commencé, déjà inscrit, complet) → `Add`. Une `UniqueConstraintViolationException` → 409 `already_reserved`.

---

## 6. Infrastructure et API

Infrastructure :
- `Persistence/Configurations/ReservationConfiguration.cs`, `AppDbContext` (`DbSet`), migration `AddReservations` ;
- `Persistence/EventLock.cs` : traduction `DbUpdateException` unique → `UniqueConstraintViolationException` sans `InnerException` ;
- `Identity/IdentityService.cs` (B) : `GetContactsAsync` ;
- `Jobs/ExpirePendingReservationsJob.cs`, `Jobs/ApiActivity.cs` (C) ; `DependencyInjection.cs` : options `Reservations` (`ValidateOnStart`), `AddHostedService` toujours appelé, `Enabled` lu dans `StartAsync` (écart T-C2, ADR 0020 « Mise en œuvre »).

API :
- `Controllers/MyReservationsController.cs` (A), `Controllers/AdminReservationsController.cs` (B), routes de repli pour identifiant mal formé (comme `AdminEventsController.MalformedId`, D1) ;
- `Errors/ApiExceptionHandler.cs` : recopie `BusinessRuleException.Details` dans les extensions ;
- filtre `NoStore` (`Cache-Control: no-store`) sur les contrôleurs `/api/me` et `/api/admin` ;
- middleware d'activité (C) : `ApiActivity.RecordRequest()` lève un drapeau (consommé par le job, `TryConsume`) après une réponse `/api/` de code < 400 (pas `/health`, pas les 401/404 d'un robot) ; écart T-C1, ADR 0020 « Mise en œuvre ».

---

## 7. Endpoints

### 7.1 Membre (connecté : rôle `User`, donc aussi un admin)

| Verbe et route | Entrée | Sortie | Erreurs |
|---|---|---|---|
| `POST /api/me/reservations` | `{ eventId }` | **201** `MyReservationDto`, `Location: /api/me/reservations/{id}` | 400 `errors.eventId` ; 401 ; 404 `not_found` (inconnu ou brouillon) ; 409 `event_cancelled`, `reservations_closed`, `already_reserved` (+ `reservationId`), `event_full` |
| `GET /api/me/reservations?scope=current\|history&page=&pageSize=` | — | `PagedResult<MyReservationDto>` | 400 (paramètres) |
| `GET /api/me/reservations/{id}` | — | `MyReservationDto` | 404 (inconnue **ou d'un autre membre**) |
| `GET /api/me/events/{eventId}/reservation` | — | 200 `MyReservationDto` (la plus récente) ou **204** | 404 si l'événement n'est pas visible publiquement |
| `POST /api/me/reservations/{id}/cancel` | — | 200 `MyReservationDto` | 404 ; 409 `reservation_state_changed` (+ `currentStatus`), `cancellation_deadline_passed` |

**`MyReservationDto`** : `id`, `status` (effectif : `Pending`, `Confirmed`, `Refused`, `Expired`, `CancelledByUser`, `CancelledByAssociation`), `requestedAt`, `expiresAt` (renseignée seulement si `Pending`), `decidedAt`, `refusalReason`, `cancelledAt`, `isPaid`, `paidAt`, `canCancel`, `cancellableUntil`, `event { id, title, location, startsAt, endsAt, price, status, hasStarted, hasEnded, isVisible }` (`isVisible` faux si l'événement est repassé en brouillon : pas de lien).

### 7.2 Administrateur (`AdminOnly`)

| Verbe et route | Entrée | Sortie | Erreurs |
|---|---|---|---|
| `GET /api/admin/reservations/pending?page=&pageSize=` | — | `PagedResult<AdminReservationDto>`, par échéance croissante | 400 |
| `GET /api/admin/events/{eventId}/reservations` | — | `EventRegistrantsDto` | 404 |
| `POST /api/admin/reservations/{id}/confirm` | — | `AdminReservationDto` | 404 ; 409 `reservation_state_changed` |
| `POST /api/admin/reservations/{id}/refuse` | `{ reason? }` (≤ 500, vide → `null`) | `AdminReservationDto` | 400 `errors.reason` ; 404 ; 409 `reservation_state_changed` |
| `POST /api/admin/reservations/{id}/cancel` | — | `AdminReservationDto` | 404 ; 409 `reservation_state_changed`, `event_ended` |
| `POST /api/admin/reservations/{id}/mark-paid` | — | `AdminReservationDto` | 404 ; 409 `reservation_not_confirmed`, `payment_not_applicable` |
| `POST /api/admin/reservations/{id}/unmark-paid` | — | `AdminReservationDto` | 404 |

**`AdminReservationDto`** : champs de `MyReservationDto` (sans `canCancel`) + `paidBy { firstName, lastName } | null`, `member { id, firstName, lastName, email, phoneNumber } | null` (`null` = « Compte supprimé »), `event { id, title, startsAt, endsAt, price, status }`, `actions { canConfirm, canRefuse, canCancel, canMarkPaid, canUnmarkPaid }` (calculés par le Domain).

**`EventRegistrantsDto`** : `event { id, title, startsAt, endsAt, capacity, price, status, hasStarted, hasEnded }`, `occupancy { pending, confirmed, active, remaining }`, `pending[]` (de la plus ancienne à la plus récente, US-E4), `confirmed[]` (nom puis prénom), `others[]` (refusées, expirées, annulées ; la plus récente d'abord).

Toutes ces réponses : `Cache-Control: no-store`. Les actions sont des `POST .../action` sans `version` (ADR 0006).

---

## 8. Impact sur les événements existants

| Élément existant | Effet de la tranche | Action |
|---|---|---|
| `EventOccupancyReader` (renvoie 0) | décompte réel | réécrit (§ 5) ; **un seul fichier**, comme prévu par l'ADR 0012 |
| `PUT /api/admin/events/{id}` | `capacity_below_reserved` et `registrants_warning` deviennent possibles ; le front les gère déjà | ajouter le recalcul des échéances si `startsAt` change (ADR 0018 § 5) |
| `POST .../unpublish` | `event_has_active_reservations` devient possible | aucune (règle déjà codée) |
| `DELETE /api/admin/events/{id}` | inchangé (brouillon jamais publié = aucune réservation possible) ; la clé étrangère `NO ACTION` est un filet | aucune |
| DTO admin `pendingCount`, `confirmedCount` ; public `remainingSeats`, `isFull`, `isBookable` | valeurs réelles | aucune ; lien « Inscrits » ajouté dans la liste et la fiche admin (B) |
| Annulation d'un événement | **bloc 4** : prendra le verrou, passera les actives en « Annulée par l'association » (`CancelByAssociation` prêt) | rien dans cette tranche |
| Événement repassé en brouillon après des réservations finales | ses réservations restent dans l'historique du membre, sans lien (`isVisible = false`) | DTO |

Les tests différés du plan d'étape 2 (§ 9.4) sont repris au § 9.4 ci-dessous.

---

## 9. Tests (pour le `testeur`)

### 9.1 `Aaas.Domain.Tests` (instants fixes, sans base ni horloge)

- **Échéance** : demande le 10/03 10 h, début le 20/03 → `ExpiresAt` 12/03 10 h ; début le 11/03 9 h → 11/03 9 h (US-D5).
- **Statut effectif** : à `ExpiresAt − 1 s` En attente ; **à `ExpiresAt` exactement** Expirée.
- **Tableau des transitions** (`mvp.md` § 2) : chaque transition permise, et **chaque** transition interdite depuis chaque statut final (théorie xUnit `[Theory]` sur les 6 statuts × 7 actions).
- `Request` : ordre des refus (annulé > clos > déjà inscrit > complet) ; brouillon refusé ; 19 actives sur 20 → acceptée ; 20 sur 20 → `event_full`.
- `Confirm` à échéance − 1 min → Confirmée ; à l'échéance → `reservation_state_changed`, statut courant Expirée.
- `CancelByUser` : En attente 5 h avant → OK ; Confirmée 3 jours avant → OK ; **exactement 24 h avant → OK** ; 24 h − 1 s → `cancellation_deadline_passed` ; payée annulée → reste payée.
- `CancelByAssociation` : 2 h avant le début → OK ; après la fin → `event_ended`.
- `MarkPaid` : En attente → `reservation_not_confirmed` ; événement gratuit → `payment_not_applicable` ; `UnmarkPaid` sur une annulée payée → non payée.
- `RescheduleDeadline` : début avancé → échéance ramenée ; début reculé → échéance allongée, plafonnée à demande + 48 h ; demande échue → inchangée.
- `Event.EnsureCanAcceptRequest` : publié/brouillon/annulé/commencé/complet.

### 9.2 `Aaas.Application.Tests`

- Validation des DTO : `eventId` absent ou non GUID ; `reason` de 501 caractères ; `reason` vide → `null`.
- `ReservationOptions` : 0 h, intervalle de 10 s ou de 2 h refusés.

### 9.3 `Aaas.Infrastructure.Tests` (sans base)

- `ExpirePendingReservationsJob` avec `FakeTimeProvider` et une doublure du service : un appel par `Advance(interval)` ; exception au 1er passage → journal `Error` **sans** le message SQL (exception fabriquée par `SqlExceptionFactory`, numéro 2627) puis appel au 2e passage ; aucune activité → aucun appel ; arrêt propre (`StopAsync`) sans journal d'erreur.
- `EventLock` : une `DbUpdateException` d'unicité devient `UniqueConstraintViolationException` sans `InnerException`.

### 9.4 `Aaas.Api.IntegrationTests` (Testcontainers, RCSI activé)

**Concurrence réelle** (cœur de RG-02) :
1. **Dernière place** : événement à 1 place, 20 membres distincts, 20 `POST` lancés ensemble (`Task.WhenAll`) → exactement **un** 201, dix-neuf 409 `event_full` ; en base `COUNT(*) WHERE Status IN (1,2) = 1`. Variante : 3 places, 30 membres → 3 réussites.
2. **Même membre, double clic** : deux `POST` simultanés → un 201, un 409 `already_reserved` avec `reservationId` égal au premier ; une seule ligne active.
3. **Test déterministe du verrou** : une connexion `SqlConnection` ouvre une transaction et exécute `SELECT * FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = @id` ; un `POST` de demande est lancé ; il **ne doit pas** se terminer en 1 s ; après `COMMIT` de la connexion de test, il se termine en 201. (Montre que le verrou est réellement posé, ce que les tests parallèles ne prouvent que statistiquement.)
4. Réduction de capacité **en parallèle** de demandes (capacité 5 → 3 pendant que 5 membres demandent) : jamais `actives > Capacity` en base ; chaque `PUT` est soit 200, soit 409 `capacity_below_reserved`.
5. Dépublication en parallèle d'une demande : soit dépublié et la demande en 404, soit demande 201 et dépublication 409 `event_has_active_reservations` ; jamais un brouillon avec une réservation active.
6. Confirmer et refuser la même demande en parallèle → un 200, un 409 `reservation_state_changed` avec `currentStatus` ; confirmer contre annulation par le membre, idem.

**Règles et parcours** :
- Demande réussie : 201, `status: "Pending"`, `expiresAt` = demande + 48 h (horloge simulée) ; `remainingSeats` diminue de 1 sur `GET /api/events/{id}`.
- Brouillon → 404 ; annulé (préparé en SQL) → 409 `event_cancelled` ; commencé → `reservations_closed`.
- **Échue sans tâche de fond** (`FakeTimeProvider.Advance(48 h)`) : place rendue dans le détail public ; le même membre peut redemander (201) et l'ancienne ligne est écrite Expirée.
- Confirmation à échéance − 1 min → 200 ; à l'échéance → 409, `currentStatus: "Expired"`.
- Annulation membre : confirmée à J−3 → 200 ; à J−10 h → 409 `cancellation_deadline_passed` ; annuler deux fois → 409, une seule place libérée.
- `GET /api/me/events/{id}/reservation` : 204 sans réservation ; 200 avec la plus récente.
- « Mes réservations » : `scope=current` (à venir, triées) et `scope=history` ; membre B ne voit pas les réservations de A ; B annule la réservation de A → **404**.
- Accès : visiteur → 401 sur `/api/me/...` ; membre → 403 sur `/api/admin/reservations/...` ; identifiant mal formé → 404 (admin), 401/403 sinon.
- Admin : liste des demandes par échéance ; inscrits groupés et total « 15 / 30 » ; refus avec motif visible par le membre ; « Payée » avec date et nom de l'admin ; « Payée » sur gratuit → 409.
- Changement de date d'un événement avec des demandes en attente : `ExpiresAt` ramenée au nouveau début.
- **Tests différés de l'étape 2** : R11 avec message chiffré ; R12 avertissement puis confirmation ; R14 ; R7 avec demandes échues non comptées ; `GetManyAsync` sur 20 événements = **une** requête de décompte (compter les commandes SQL via un intercepteur EF de test ou les journaux `CommandExecuted`).
- **Tâche (C)** : `Reservations:ExpirationJob:Enabled=true`, `OnlyWhenActive=false`, `Advance(48 h + 5 min)` → ligne en base à 4.
- **Concordance** : pour un jeu de réservations (6 statuts × échéance passée/future), `ReservationQueries.IsActive(now)` en SQL et `Reservation.IsActive(now)` en mémoire donnent le même ensemble.
- **Schéma** : deux insertions SQL actives pour le même (événement, membre) → erreur 2601 ; `IsPaid = 1` sur une En attente → violation de `CHECK`.
- **Journaux** : sur tout le parcours (demande, confirmation, refus avec motif, échec 409), aucun message capturé ne contient l'e-mail, le téléphone, le nom ni le motif de refus (`ApiFactory.Logs`).
- En-tête `Cache-Control: no-store` présent sur `/api/me/reservations` et `/api/admin/events/{id}/reservations`.

### 9.5 Front (Vitest)

- Panneau de réservation du détail : chacun des états (visiteur, membre sans demande, en attente avec échéance et numéro, confirmée annulable, confirmée non annulable avec numéro, finale + bouton si réservable, événement complet ou clos sans bouton).
- Double clic : un seul appel HTTP ; 409 `already_reserved` → rechargement silencieux.
- Carte de contact : `tel:+21620123456`, `https://wa.me/21620123456` seulement si WhatsApp ; message R8 si non configuré.
- `/mes-reservations` : deux sections, libellés de statut, « 15 TND, à régler sur place » / « Payée », message vide (US-D2).
- Admin : 409 `reservation_state_changed` → ligne mise à jour avec `currentStatus` et message ; boutons désactivés pendant l'appel ; dialogue de refus (motif facultatif, compteur 500) ; « Marquer comme payée » absent pour un gratuit.

---

## 10. Front Angular

### 10.1 Routes

| Chemin | Composant | Guard | Tâche |
|---|---|---|---|
| `evenements/:id` (existant) | `EventDetail` + `<app-reservation-panel>` | — | A |
| `mes-reservations` | `MyReservations` | `authGuard` | A |
| `admin/demandes` | `AdminPendingRequests` | (admin, `canMatch` existant) | B |
| `admin/evenements/:id/inscrits` | `AdminEventRegistrants` | idem ; **déclarée avant** `evenements/:id` | B |

### 10.2 Fichiers

```
core/api/reservations.api.ts          POST/GET /api/me/... (membre)
core/api/admin-reservations.api.ts    /api/admin/reservations/..., /api/admin/events/{id}/reservations (B)
core/models/api-models.ts             MyReservationDto, AdminReservationDto, EventRegistrantsDto, ReservationStatus
core/contact/contact.store.ts         signal du numéro de contact, chargé une fois par session (GET /api/settings/contact)
shared/reservations/reservation-labels.ts   libellés des statuts, fonction pure (testée)
shared/ui/contact-card.ts             numéro, liens tel: et wa.me, message R8 si absent
features/reservations/reservation-panel/    état « ma demande » sur le détail (A)
features/reservations/my-reservations/      /mes-reservations (A)
features/admin/reservations/pending-requests/   /admin/demandes (B)
features/admin/reservations/event-registrants/  /admin/evenements/:id/inscrits (B)
features/admin/reservations/refuse-dialog.ts    motif facultatif (B)
```

Comportements :
- **UX-9 levé** : le bouton « Demander une place » apparaît pour un membre connecté si `isBookable` et s'il n'a pas de réservation active. Après 201 : panneau « Demande en attente de confirmation, à faire confirmer avant le {expiresAt, heure de Tunis} » + carte de contact (US-D1, D4).
- Le panneau charge `GET /api/me/events/{id}/reservation` **seulement si connecté** (`rxResource` avec `params` à `undefined` sinon). Après une action, il recharge aussi le détail public (places restantes).
- `/mes-reservations` : deux `rxResource` (`current`, `history`) avec `<app-pager>` ; annulation par `ConfirmDialog` ; si payée, texte « Le remboursement éventuel se règle directement avec l'association » ; 409 `cancellation_deadline_passed` → carte de contact.
- En-tête : lien « Mes réservations » pour un connecté ; « Mon compte » renvoie vers « Mes réservations » (US-F3).
- Admin : sous-menu « Demandes (N) » (`totalCount` de `pending?pageSize=1`) ; boutons par ligne désactivés pendant l'appel (signal `busyIds`) ; téléphone cliquable ; 409 `reservation_state_changed` → message « Cette demande a déjà été traitée » et ligne rafraîchie.
- Aucune donnée d'inscrit dans `localStorage`.

Notions Angular nouvelles à expliquer dans `docs/revision/angular.md` : composant enfant avec `input()`/`output()` (panneau dans le détail), `rxResource` conditionnel, état « occupé » par ligne avec un `signal<Set<string>>`, `MatDialog` avec résultat, `HttpClient` et réponse 204 (`null`).

---

## 11. Tâches ordonnées (`expert-technique`)

Chaque tâche se termine par un build vert, ses tests, et la doc concernée (CLAUDE.md).

| # | Tâche | Dépend de | Vérifiable |
|---|---|---|---|
| **A1** | Domain : `ReservationStatus`, `Reservation`, `ReservationRules`, `Event.EnsureCanAcceptRequest`, `Details` sur `BusinessRuleException`, codes ; tests § 9.1 (parties A) | — | tests |
| **A2** | Infrastructure : `ReservationConfiguration`, `DbSet`, **migration `AddReservations`** (relire le script), options `Reservations` validées, traduction d'unicité dans `EventLock` | A1 | `docker compose up --build` + requête SQL |
| **A3** | Application : `ReservationQueries`, **`EventOccupancyReader` réel**, `ReservationExpiryService.ExpireDueForEventAsync`, `MemberReservationService`, recalcul des échéances dans `AdminEventService.UpdateAsync` | A2 | tests d'intégration |
| **A4** | API : `MyReservationsController`, `ApiExceptionHandler` (extensions), filtre `no-store`, routes de repli | A3 | curl (§ 12) |
| **A5** | Tests d'intégration § 9.4 (concurrence 1 à 5, parcours membre, tests différés de l'étape 2) | A4 | — |
| **A6** | Front : `reservations.api`, modèles, `ContactStore`, `contact-card`, panneau du détail (UX-9 levé), `/mes-reservations`, menu ; tests § 9.5 | A4 | navigateur, `pile-e2e.sh` complété |
| **A7** | Docs (§ 13), relecture `code-reviewer` | A5, A6 | — |
| **B1** | Domain : `Confirm`, `Refuse`, `CancelByAssociation`, `MarkPaid`, `UnmarkPaid`, actions calculées ; tests | A1 | tests |
| **B2** | Application + Infrastructure : `IIdentityService.GetContactsAsync`, `AdminReservationService` | B1, A3 | — |
| **B3** | API : `AdminReservationsController` ; tests d'intégration (concurrence 6, parcours admin) | B2 | curl |
| **B4** | Front : `/admin/demandes`, `/admin/evenements/:id/inscrits`, dialogue de refus, liens « Inscrits » depuis la liste et la fiche admin ; tests | B3 | navigateur |
| **B5** | Docs, relecture | B3, B4 | — |
| **C1** | `ReservationExpiryService.ExpireDueAsync`, `ApiActivity` + middleware, `ExpirePendingReservationsJob`, enregistrement conditionnel ; tests § 9.3 et intégration | A3 | journaux `docker compose logs api` |
| **C2** | Docs (exploitation, fiche `dotnet-background-service.md`), relecture | C1 | — |

---

## 12. Docker, CI/CD, Azure

- **Docker** : aucun nouveau service, aucun secret. `docker compose up -d --build` applique `AddReservations` au démarrage (comptes et événements conservés). Variables facultatives : `Reservations__PendingLifetime`, `Reservations__ExpirationJob__Interval` (pour une démonstration locale : `00:01:00` et une durée de vie courte, jamais en production). Vérification : `sqlcmd ... -I -Q "SELECT Status, COUNT(*) FROM Reservations GROUP BY Status"` (`-I` obligatoire : index filtré).
- **curl** (à ajouter à `exploitation.md`) : membre `POST /api/me/reservations` → 201 ; deuxième → 409 `already_reserved` ; admin `GET /api/admin/reservations/pending` ; `POST .../confirm` ; `POST .../mark-paid` ; membre `POST /api/me/reservations/{id}/cancel`.
- **CI (S3)** : rien de nouveau ; les tests de concurrence tournent avec les autres tests Testcontainers. Les marquer `[Trait("Category", "Concurrency")]` pour pouvoir les relancer seuls s'ils se montrent instables.
- **Azure (S3)** : aucune ressource. La tâche ne réveille pas Azure SQL serverless (ADR 0020). Avec plusieurs instances d'App Service, chaque instance a sa tâche : sans conséquence (requête idempotente) ; le verrou est en base, donc valable entre instances.

---

## 13. Documentation à mettre à jour

| Document | Mise à jour | Par | Tâche |
|---|---|---|---|
| `docs/technique/architecture.md` | § 0 et § 1 (ADR 0018 à 0021) ; § 3 (dossiers `Reservations/`, `Jobs/`, contrôleurs, middleware ; table « où vit chaque règle » : RG-02 à RG-05, RG-08) ; § 4 (schéma `Reservations` final : index `INCLUDE`, `CHECK`, sans `DecidedByAdminId`) ; § 5 (endpoints, `no-store`, extensions `currentStatus` / `reservationId`) ; § 6 (routes front) ; § 11 (tâche sans réveil de la base) ; § 13 (questions de ce plan) | architecte | A7, B5, C2 |
| `docs/technique/api.md` | section « Réservations — membre » et « Réservations — administration » (routes, DTO, codes) ; conventions : extensions des 409, réponse 204, `Cache-Control` ; mentions « possible à partir de l'étape 4 » retirées | expert-technique | A7, B5 |
| `front/src/app/core/models/api-models.ts` | DTO recopiés de `api.md` | expert-technique | A6, B4 |
| `docs/technique/regles-metier.md` | nouvelle section « Réservations » : cycle de vie, statut effectif, échéance et son recalcul, délais, places, « Payée », endroit du code ; nouveaux codes ; § 6 (UX-9 levé) | expert-technique | A7, B5 |
| `docs/technique/exploitation.md` | options `Reservations`, journaux de la tâche, vérifications SQL, curl | expert-technique | A7, C2 |
| `docs/revision/ef-core-concurrence.md` | verrous S/U/X, isolation et RCSI, index filtré, test déterministe du verrou, `ExecuteUpdate` et `rowversion` | expert-technique | A7 |
| `docs/revision/dotnet-background-service.md` (nouvelle) | § 1.5 avec le code réel | expert-technique | C2 |
| `docs/revision/angular.md` | § 10.2 « notions nouvelles » | expert-technique | A6, B4 |
| `docs/fonctionnel/libelles-ecrans.md` | libellés réservation et admin (§ 15, QF-8) | expert-fonctionnel | avant A6 |
| `docs/fonctionnel/politique-confidentialite.md` | les admins voient les coordonnées des inscrits (ADR 0021) | expert-fonctionnel | avant B4 |
| `docs/guide-utilisateur/` | membre : demander, suivre, annuler ; admin : traiter, inscrits, « Payée » | documentaliste | après B5 |
| `docs/technique/etat-avancement.md` | blocs 2 et 3 | expert-technique | chaque fin de tâche |

---

## 14. Risques

| Risque | Parade |
|---|---|
| Verrou non posé réellement (requête réécrite) | test déterministe § 9.4-3 ; test `ToQueryString` existant (`EventLockSqlTests`) |
| `now` lu avant le verrou (décompte avec une heure périmée) | lecture dans `work` ; revue |
| Demande échue qui bloque l'index unique | matérialisation par événement sous verrou avant l'insertion ; test « redemander après 48 h » |
| Échéance au-delà du début après un déplacement de date | recalcul sous verrou (ADR 0018 § 5) ; test dédié |
| Tâche de fond qui arrête l'API sur une erreur | `try/catch` par passage ; test d'exception |
| Tâche qui empêche la pause d'Azure SQL | condition d'activité (ADR 0020) |
| Fuite de données personnelles (journaux, cache, message SQL) | règles ADR 0021 § 4 ; test des journaux ; exception d'unicité sans `InnerException` |
| Tests de concurrence instables en CI | assertions sur l'invariant (jamais plus d'actives que la capacité) plutôt que sur un ordre ; catégorie dédiée |
| Liste des inscrits non paginée | volume associatif ; repli ADR 0011 |
| Statut affiché faux pour une demande échue non écrite | `EffectiveStatus(now)` dans **tous** les DTO ; test de concordance |

---

## 15. Questions ouvertes

### 15.1 Pour l'`expert-fonctionnel` (avec l'association si besoin)

| # | Question | Valeur par défaut retenue |
|---|---|---|
| QF-1 | Un **administrateur** peut-il demander une place pour lui-même (il a aussi le rôle membre) ? | **Oui**, même parcours qu'un membre. |
| QF-2 | Quand l'admin **avance ou recule la date** d'un événement, l'échéance des demandes en attente suit-elle ? | **Oui** : ramenée au nouveau début s'il est plus tôt ; allongée s'il recule, sans dépasser 48 h après la demande. Le délai de 24 h pour annuler une réservation confirmée se compte depuis le début **actuel**. |
| QF-3 | Sur le détail d'un événement, que voit un membre dont la **dernière demande** est refusée, expirée ou annulée ? | Une ligne d'information (« Votre demande précédente a été refusée : motif… ») et le bouton « Demander une place » si l'événement est réservable. |
| QF-4 | La mention **« Payée »** peut-elle être posée après la fin de l'événement ? | **Oui**, sans limite de date (l'encaissement se fait souvent sur place). |
| QF-5 | La mention « Payée » peut-elle être **retirée** sur une réservation déjà annulée (erreur de saisie) ? | **Oui**. Elle ne peut être **posée** que sur une Confirmée d'un événement payant. |
| QF-6 | Liste des **inscrits** : ordre des confirmées ? | Par nom puis prénom (feuille d'émargement). En attente : de la plus ancienne à la plus récente (US-E4). |
| QF-7 | « Mes réservations » : une réservation **confirmée d'un événement terminé** est-elle « passée » ? | Oui, section « Historique » avec le statut « Confirmée » et la mention « Terminé ». |
| QF-8 | **Libellés** à valider dans `libelles-ecrans.md` : statuts (« En attente de confirmation », « Confirmée », « Refusée », « Expirée », « Annulée par vous », « Annulée par l'association »), messages du § 4, titres « Demandes à traiter », « Inscrits », « Mes réservations », rappel sous le motif de refus. | Ceux du § 4 et de cette ligne. |
| QF-9 | La **politique de confidentialité** doit-elle dire que les administrateurs voient prénom, nom, téléphone et e-mail des inscrits ? | Oui (texte à compléter, version de la politique à incrémenter). |
| QF-10 | Faut-il **limiter les redemandes** d'un membre refusé (refus, nouvelle demande, refus…) ? | Non (RG-03 l'autorise) ; l'admin refuse à nouveau. |

### 15.2 Décisions du porteur (valeur par défaut recommandée)

| # | Décision | Recommandation |
|---|---|---|
| P-R1 | Valider formellement les ADR 0004, 0005, 0006 (toujours « Proposé ») et les nouveaux 0018 à 0021 avant le code de la tâche A | **Oui** : ils deviennent du code dans cette tranche. |
| P-R2 | Tâche d'expiration conditionnée à l'activité de l'API (ADR 0020) | **Oui** (coût Azure SQL serverless). |
| P-R3 | Intervalle de la tâche | **5 minutes**. |
| P-R4 | Délais (48 h, 24 h) en configuration, non modifiables depuis l'interface au MVP | **Oui** (une colonne dans `AssociationSettings` plus tard si l'association le demande, ADR 0013). |
| P-R5 | Pas d'`Idempotency-Key` (409 `already_reserved` suffit) | **Oui**. |
| P-R6 | Pas d'enregistrement de l'admin qui confirme ou refuse (`DecidedByAdminId`) | **Non enregistré** (minimisation) ; seul l'admin qui marque « Payée » l'est (R13). |
| P-R7 | Liste des inscrits non paginée | **Oui** au MVP. |
| P-R8 | Ordre des tâches A → B → C | **Oui** ; B et C peuvent être inversées si l'on veut montrer la tâche de fond plus tôt (C ne dépend que de A3). |
