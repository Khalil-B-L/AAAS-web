# ADR 0019 — Réservations : concurrence, double clic et violation d'index

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 24/11/2026
- **Mise en œuvre** : tranche « Réservations » (`plan-reservations.md`, tâches A et B)
- **Références** : cahier RG-02, RG-03, risque § 11 ; mvp US-D1 (« dernière place »), US-E6 (« déjà traitée par un autre admin ») ; **précise** les ADR 0004 (verrou `UPDLOCK`), 0005 (matérialisation), 0006 (`rowversion`), 0012 (`IEventLock`)

## Contexte

Les ADR 0004 et 0006 posent les principes (verrou pessimiste par événement pour la capacité, concurrence optimiste pour les transitions). Le port `IEventLock` existe et sert déjà à la modification et à la dépublication. Restent à fixer pour l'implémentation : la liste exacte des opérations sous verrou, le moment où l'on lit l'heure, la traduction d'une violation de l'index unique sans faire entrer `SqlClient` dans Application, le comportement en cas de double clic, et les tests qui prouvent la garantie sur un vrai SQL Server.

## Décision

### 1. Qui prend le verrou de l'événement

| Opération | Verrou `UPDLOCK` (`IEventLock`) | Contrôle `rowversion` de la réservation | Pourquoi |
|---|---|---|---|
| Demande de place | **oui** | — (insertion) | peut **augmenter** les places prises (RG-02) |
| Modifier l'événement, dépublier | oui (existant) | — | dépendent du décompte (R11, R12, R14) ; recalcul des échéances (ADR 0018 § 5) |
| Annuler l'événement (étape 7, bloc 4) | oui | mise à jour en masse | idem |
| Confirmer | non | **oui** | Pending → Confirmed : le nombre de places prises ne change pas |
| Refuser, annuler (membre ou admin), expirer | non | oui (sauf expiration en masse) | ne font que **libérer** une place |
| Marquer / retirer « payée » | non | oui | sans effet sur les places |

Règle mnémotechnique : **on verrouille ce qui peut faire monter le compteur ou ce qui en dépend ; le reste se contente de `rowversion`.**

### 2. Déroulé de la demande de place

Dans `ExecuteWithLockedEventAsync(eventId, work)` (transaction Read Committed, stratégie d'exécution, ADR 0012) :
1. le verrou est obtenu sur la ligne `Events` (l'attente éventuelle a lieu ici) ;
2. **`now` est lu dans `work`, après le verrou** (`TimeProvider`) : une demande qui a attendu 2 s ne décide pas avec une heure périmée ;
3. événement brouillon → 404 ; matérialisation des demandes échues **de cet événement** (`ExecuteUpdateAsync`, ADR 0005 § 4 : libère l'index unique) ;
4. lecture de l'occupation (`EventOccupancyReader`, filtre `IsActive(now)`) et de l'existence d'une réservation active du membre ;
5. `Reservation.Request(...)` (Domain), `Add`, puis `SaveChanges` et `Commit` par `EventLock`.

Sous `READ_COMMITTED_SNAPSHOT` (Azure SQL, et nos tests), l'étape 4 est une lecture ordinaire : elle lit la dernière version **validée au début de l'instruction**. Comme elle s'exécute après l'obtention du verrou, elle voit l'insertion de la transaction précédente, déjà validée. C'est ce qui rend le décompte exact. Ne **jamais** passer cette transaction en isolation `SNAPSHOT` : l'instantané daterait du début de la transaction et l'insertion concurrente serait invisible.

### 3. Violation de l'index unique : traduction sans `SqlClient` dans Application

Le contrôle de l'étape 4 rend la violation de `UX_Reservations_Event_User_Active` impossible en temps normal (les demandes d'un même événement sont sérialisées). Elle reste un **filet** (script manuel, bug futur). Traduction :
- `EventLock` (Infrastructure, seul endroit qui connaît SQL Server) attrape la `DbUpdateException` dont `SqlServerErrors.IsUniqueKeyViolation` est vrai et relance une `UniqueConstraintViolationException` neutre (Application, `Common/Errors`), **sans `InnerException`** (le message SQL contient les clés dupliquées) ;
- le service de réservation la traduit en 409 `already_reserved`. Sur une insertion de `Reservation`, c'est le seul index unique violable (la clé primaire est un GUID généré par EF).

### 4. Double clic, deuxième onglet, nouvelle tentative réseau

- **Pas d'en-tête `Idempotency-Key`** : le métier est déjà idempotent par nature, puisqu'un membre ne peut avoir qu'une demande active par événement. Le deuxième `POST` reçoit 409 `already_reserved` avec `reservationId` ; le front le traite comme un succès silencieux : il recharge « ma réservation » et affiche la demande existante, sans message d'erreur.
- Le front désactive le bouton dès le clic (signal `submitting`) : confort, pas garantie.
- Les actions admin (confirmer, refuser…) sont des **intentions** (ADR 0006) : un deuxième « confirmer » sur une demande déjà confirmée → 409 `reservation_state_changed` avec `currentStatus: "Confirmed"` ; le front met la ligne à jour et n'affiche qu'une information.

### 5. Transitions sans verrou : `rowversion`

Chargement de la réservation (suivie) et de son événement, règle du Domain, `SaveChanges`. En cas de `DbUpdateConcurrencyException` : relecture de la réservation et 409 `reservation_state_changed` avec le statut **effectif** relu (ADR 0006). Cas couverts : deux admins (confirmer contre refuser), admin contre membre (confirmer contre annuler), admin contre tâche d'expiration (l'`ExecuteUpdate` en masse change aussi la `rowversion`).

### 6. Interblocages et rejeu

Une demande de place (qui met à jour quelques lignes `Reservations` de son événement) et la tâche d'expiration (mise à jour globale) peuvent, en théorie, s'interbloquer en accédant aux mêmes lignes par deux index différents. SQL Server désigne une victime (erreur 1205) : côté requête, la stratégie d'exécution d'EF considère 1205 comme transitoire et **rejoue tout le bloc verrouillé** (d'où `ChangeTracker.Clear()` et l'interdiction d'effet de bord dans `work`, ADR 0012) ; côté tâche, l'erreur est journalisée et le passage suivant recommence (ADR 0020). Au volume d'une association, l'événement est improbable ; on ne le prévient pas autrement.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Verrouiller aussi pour confirmer | Inutile pour la capacité ; allonge la file d'attente sur un événement très demandé. |
| `Idempotency-Key` + table des clés traitées | Une table et une purge de plus pour un cas déjà couvert par RG-03. |
| Renvoyer 200 avec la réservation existante au deuxième `POST` | Masquerait la règle RG-03 aux autres clients (curl, tests) ; le 409 + `reservationId` donne la même expérience au front. |
| Analyser le texte de l'erreur SQL pour trouver le nom de l'index | Message localisable et porteur de données ; inutile puisqu'un seul index unique est violable sur cette insertion. |
| `SqlClient` dans Application pour attraper `SqlException` | Contraire aux règles de dépendance (ADR 0001). |
| Niveau `Serializable` | Écarté par l'ADR 0004 (verrous de plage, interblocages comme mécanisme normal). |

## Conséquences

- Tests d'intégration obligatoires (Testcontainers, `READ_COMMITTED_SNAPSHOT` activé, déjà le cas dans `SqlServerFixture`) : N membres sur la dernière place ; même membre en double ; réduction de capacité et dépublication en parallèle d'une demande ; confirmer contre refuser ; et un **test déterministe du verrou** : une connexion SQL ouvre une transaction qui prend `UPDLOCK` sur l'événement, la demande HTTP doit rester bloquée tant que cette transaction n'est pas validée (détail dans le plan, § 9.4, point 3).
- Exercice de révision proposé : retirer `UPDLOCK` de `EventLock` en local et constater que le test « N membres sur la dernière place » échoue.
- `EventLock` gagne une traduction d'exception (quelques lignes) ; `IEventLock` ne change pas de signature.
