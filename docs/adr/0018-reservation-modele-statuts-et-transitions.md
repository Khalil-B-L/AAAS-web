# ADR 0018 — Réservation : modèle de données, statuts, transitions et erreurs

- **Statut** : Accepté (porteur, 24/11/2026)
- **Date** : 24/11/2026
- **Mise en œuvre** : tranche « Réservations » (`docs/technique/plan-reservations.md`, tâches A et B)
- **Références** : cahier § 4.2, § 5.1, RG-01 à RG-05, RG-08, RG-12 ; mvp § 2, US-D1 à D5, US-E4 à E7, R1, R4 à R10, R13 ; ADR 0003, 0004, 0005, 0006, 0007, 0012

## Contexte

L'entité `Reservation` est annoncée depuis l'étape 1 (schéma de `architecture.md` § 4, ADR 0004 à 0007) mais pas encore figée. Il faut trancher : une ou plusieurs places par demande, une ou plusieurs lignes par membre et par événement, ce qui est stocké ou calculé (échéance, statut « Expirée »), où vivent les transitions, comment le client apprend l'état réel après un refus, et ce que devient l'échéance d'une demande quand l'admin déplace la date de l'événement (RG-12 le permet).

## Décision

### 1. Une ligne = une demande = une place

- **Pas de colonne « nombre de places »** : le cahier exclut les accompagnants (§ 4.2, R1). Le jour où ils arrivent : une colonne `Seats tinyint NOT NULL DEFAULT 1` et `SUM(Seats)` au lieu de `COUNT(*)` dans `EventOccupancyReader` ; la forme de `EventOccupancy` ne change pas.
- **Plusieurs lignes possibles par (membre, événement)**, mais **une seule active** (En attente ou Confirmée, RG-03). Une nouvelle demande après refus, expiration ou annulation crée une **nouvelle ligne** ; l'historique n'est jamais réécrit. Garantie en base : index unique filtré (ADR 0004).

### 2. Table `Reservations`

| Colonne | Type | Note |
|---|---|---|
| `Id` | `uniqueidentifier` PK | généré par EF (séquentiel) |
| `EventId` | `uniqueidentifier` FK → `Events`, `NO ACTION` | RG-07 : un événement ayant eu des réservations ne se supprime pas |
| `UserId` | `uniqueidentifier NULL` FK → `AspNetUsers`, `NO ACTION` | `NULL` après suppression du compte (ADR 0007) |
| `Status` | `tinyint` | 1 En attente, 2 Confirmée, 3 Refusée, 4 Expirée, 5 Annulée par le membre, 6 Annulée par l'association. **Valeurs figées** (index filtré) |
| `RequestedAt` | `datetimeoffset` | instant de la demande (UTC) |
| `ExpiresAt` | `datetimeoffset` | échéance : `min(RequestedAt + PendingLifetime, Event.StartsAt)` ; conservée après décision (historique) |
| `DecidedAt` | `datetimeoffset NULL` | confirmation ou refus |
| `RefusalReason` | `nvarchar(500) NULL` | facultatif, visible du membre |
| `CancelledAt` | `datetimeoffset NULL` | annulation (membre ou association) |
| `IsPaid` | `bit` défaut 0 | mention, pas un statut (RG-08) |
| `PaidAt` | `datetimeoffset NULL` | |
| `PaidByAdminId` | `uniqueidentifier NULL` FK → `AspNetUsers`, `NO ACTION` | R13 « l'admin est enregistré » ; `NULL` si cet admin supprime son compte |
| `RowVersion` | `rowversion` | concurrence optimiste (ADR 0006) |

Pas de colonne `DecidedByAdminId` ni `ExpiredAt` : aucune story ne les demande (minimisation, R24) ; la date d'expiration est `ExpiresAt`.

Contraintes (filets ; la règle est d'abord dans le Domain) : `CK_Reservations_Status (Status BETWEEN 1 AND 6)`, `CK_Reservations_Deadline (ExpiresAt > RequestedAt)`, `CK_Reservations_Paid (IsPaid = 0 OR Status IN (2, 5, 6))`, `CK_Reservations_PaidAt (IsPaid = 0 OR PaidAt IS NOT NULL)`.

Index :
- `UX_Reservations_Event_User_Active (EventId, UserId) UNIQUE WHERE Status IN (1, 2) AND UserId IS NOT NULL` (RG-03) ;
- `IX_Reservations_EventId_Status (EventId, Status) INCLUDE (ExpiresAt)` (décompte des places ; couvre aussi la clé étrangère `EventId`) ;
- `IX_Reservations_Status_ExpiresAt (Status, ExpiresAt)` (tâche d'expiration, liste admin des demandes à traiter) ;
- `IX_Reservations_UserId` (« Mes réservations », créé par convention d'EF pour la clé étrangère), `IX_Reservations_PaidByAdminId` (idem).

### 3. Domain : `Reservation` porte toutes les transitions

`src/Aaas.Domain/Reservations/` : `ReservationStatus`, `Reservation`, `ReservationRules` (calcul de l'échéance, délai d'annulation). Chaque méthode reçoit `now` (et l'événement quand la règle en dépend) ; aucune ne lit l'horloge ni la base.

| Méthode | De → vers | Refus (409, `code`) |
|---|---|---|
| `Reservation.Request(ev, userId, hasActive, occupancy, now, pendingLifetime)` | → En attente | événement brouillon : **404** `not_found` (on ne révèle pas un brouillon) ; `event_cancelled` ; `reservations_closed` (commencé ou terminé) ; `already_reserved` ; `event_full` — **dans cet ordre** (un membre déjà inscrit à un événement complet apprend qu'il est inscrit) |
| `Confirm(ev, now)` | En attente non échue → Confirmée | `reservation_state_changed` (y compris demande échue : « Cette demande a expiré : le membre doit faire une nouvelle demande. ») |
| `Refuse(reason, now)` | En attente non échue → Refusée | `reservation_state_changed` |
| `CancelByUser(ev, now, confirmedDeadline)` | En attente non échue → Annulée par le membre ; Confirmée → idem si `now <= StartsAt − 24 h` | `reservation_state_changed` ; `cancellation_deadline_passed` |
| `CancelByAssociation(ev, now)` | En attente non échue ou Confirmée → Annulée par l'association | `reservation_state_changed` ; `event_ended` |
| `Expire(now)` | En attente échue → Expirée | aucune (sans effet sinon) |
| `MarkPaid(ev, adminId, now)` | mention sur Confirmée d'un événement payant | `reservation_not_confirmed` ; `payment_not_applicable` (gratuit) |
| `UnmarkPaid()` | retire la mention, quel que soit le statut (correction d'erreur) | sans effet si non payée |
| `RescheduleDeadline(newStartsAt, pendingLifetime, now)` | En attente non échue : échéance recalculée | aucune |

« Échue » = `Status == Pending && now >= ExpiresAt` (ADR 0005). Comme l'échéance ne dépasse jamais le début de l'événement, « confirmer ou annuler une demande avant le début » est **impliqué** par « non échue » : une seule règle, pas deux.

**Statut effectif** : `EffectiveStatus(now)` renvoie Expirée pour une demande échue pas encore écrite ; c'est lui qui est exposé dans **tous** les DTO (ADR 0005 § 3). `IsActive(now)` = Confirmée, ou En attente non échue ; sa traduction SQL (`ReservationQueries.IsActive(now)`, Application) doit concorder (test dédié).

`Event` gagne une seule méthode : `EnsureCanAcceptRequest(now, occupancy)` (ordre des contrôles ci-dessus, hors `already_reserved`), qui réutilise `EventRules.IsBookable`.

### 4. Erreurs : l'état réel voyage avec le 409

`BusinessRuleException` reçoit un dictionnaire facultatif `Details` (`IReadOnlyDictionary<string, object?>`), recopié par `ApiExceptionHandler` dans les extensions du `ProblemDetails` :
- `reservation_state_changed` → `currentStatus` (statut effectif relu) ; le front rafraîchit la ligne sans recharger toute la page (ADR 0006) ;
- `already_reserved` → `reservationId` (le front affiche la demande existante : double clic, deuxième onglet).

Nouveaux codes (tous 409) : `event_full`, `reservations_closed`, `already_reserved`, `reservation_state_changed`, `cancellation_deadline_passed`, `reservation_not_confirmed`, `payment_not_applicable`. Réutilisés : `event_cancelled`, `event_ended`, `not_found` (404). Une réservation d'un autre membre → **404** (on ne révèle pas son existence, `architecture.md` § 5).

### 5. Déplacement de la date d'un événement (RG-12)

Quand `PUT /api/admin/events/{id}` change `startsAt`, **sous le même verrou** (ADR 0012), le service charge les demandes En attente **non échues** de l'événement et appelle `RescheduleDeadline` : `ExpiresAt = min(RequestedAt + PendingLifetime, nouveau début)`. Effets : l'échéance est ramenée au nouveau début s'il est plus tôt (RG-04 « au plus tard au début ») ; elle peut s'allonger si le début recule, sans dépasser 48 h après la demande. Les demandes déjà échues ne sont **jamais ranimées** (filtre `now < ExpiresAt` avant recalcul). Le délai d'annulation de 24 h se mesure toujours par rapport au début **actuel**.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Colonne `Seats` dès maintenant | Hors périmètre (cahier § 4.2) ; ajout facile plus tard (une colonne, un `SUM`). |
| Une ligne unique par (membre, événement), réactivée à chaque nouvelle demande | Perd l'historique (refus puis nouvelle demande) et mélange les dates de deux demandes ; l'index filtré rend inutile cette contrainte. |
| Statut « Expirée » uniquement calculé, jamais écrit | Écarté par l'ADR 0005 (index unique bloqué, requêtes de reporting à filtrer partout). |
| Échéance calculée à la lecture comme `min(ExpiresAt, Event.StartsAt)` | Évite le recalcul au déplacement de date, mais chaque décompte devrait joindre `Events` et la règle serait écrite deux fois (Domain et SQL). Le recalcul sous verrou touche quelques lignes, rarement. |
| `UPDATE ... SET Status = 2 WHERE Id = @id AND Status = 1` pour les transitions | Atomique, mais la règle quitte le Domain (ADR 0006). |
| Codes génériques (`invalid_transition`) | Le front doit afficher des messages différents (expirée, délai dépassé, gratuit) et réagir différemment (afficher le numéro de contact sur `cancellation_deadline_passed`, R8). |
| Exceptions spécialisées par code (`ReservationStateChangedException`) | Un dictionnaire `Details` sur l'exception existante suffit et sert à d'autres codes ; une seule ligne dans `ApiExceptionHandler`. |
| `DecidedByAdminId` (qui a confirmé) | Utile en audit, non demandé ; une donnée personnelle de plus à détacher (ADR 0007). À ajouter si l'association le demande. |

## Conséquences

- Les règles RG-02 à RG-05, RG-08 et le tableau des transitions de `mvp.md` § 2 se testent dans `Aaas.Domain.Tests`, sans base, avec des instants fixes (liste dans le plan, § 9.1).
- `EventOccupancyReader` devient une vraie requête (`ReservationQueries.IsActive(now)`) : les codes `capacity_below_reserved`, `registrants_warning`, `event_has_active_reservations` deviennent atteignables.
- `AdminEventService.UpdateAsync` gagne le recalcul des échéances (§ 5).
- `ApiExceptionHandler` recopie `Details` : le contrat des 409 s'enrichit sans casser les codes existants.
