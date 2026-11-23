# ADR 0012 — Modification d'un événement : verrou, version, avertissement, et règles qui dépendent des réservations

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 18/11/2026
- **Mise en œuvre** : étape 2 (mécanismes) ; étape 4 (décompte réel des réservations) ; étape 7 (annulation et suppression)
- **Références** : cahier RG-02, RG-06, RG-07 ; mvp US-E2, R7, R11, R12, R14, R15 ; ADR 0004 (verrou `UPDLOCK`), ADR 0005 (expiration), ADR 0006 (`rowversion`)

## Contexte

Une modification d'événement par un admin peut entrer en conflit avec :
1. **un autre admin** qui modifie la même fiche (perte silencieuse d'une modification) : ADR 0006, `rowversion` + champ `version` ;
2. **une demande de place** (étape 4) qui arrive pendant une réduction de capacité ou une dépublication : ADR 0004, verrou `UPDLOCK` sur la ligne de l'événement.

Il faut aussi :
- l'**avertissement** de R12 (« changer la date ou le prix d'un événement avec des inscrits : avertissement, puis enregistrement après confirmation ») ;
- appliquer R11 (capacité ≥ réservations actives), R14 (dépublication seulement sans réservation active) et R15 (suppression seulement sans aucune réservation) **avant que la table `Reservations` existe** (étape 4), sans réécrire la logique à l'étape 4.

## Décision

### 1. Verrou + version, combinés

Les opérations qui **dépendent du nombre de réservations actives** prennent le verrou de l'événement, dans une transaction : `PUT /api/admin/events/{id}` (capacité, avertissement), `POST .../unpublish`, et plus tard l'annulation et la suppression (étape 7) et la demande de place (étape 4).

Port `IEventLock` (Application), implémenté dans Infrastructure :

```csharp
Task<TResult> ExecuteWithLockedEventAsync<TResult>(
    Guid eventId, Func<Event, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken);
```

L'implémentation :
1. passe par `Database.CreateExecutionStrategy().ExecuteAsync(...)` (obligatoire avec `EnableRetryOnFailure`) ;
2. au début de **chaque tentative**, vide le suivi des entités (`ChangeTracker.Clear()`) : une tentative rejouée ne doit pas réutiliser l'état de la précédente ;
3. ouvre une transaction (Read Committed), charge l'événement avec `FromSql($"SELECT * FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = {eventId}")` (entité suivie) ; absent → `NotFoundException` ;
4. appelle `work(event)` (règles du Domain, modifications), puis `SaveChangesAsync` et `Commit`.

C'est le **seul** endroit avec du SQL propre à SQL Server et de la plomberie de transaction ; Application ne voit ni `DbTransaction` ni `DatabaseFacade`.

Dans `work`, la **version** envoyée par le client est comparée à `event.RowVersion` (lue sous verrou) : différente → 409 `concurrent_update` (ADR 0006). Comme la ligne est verrouillée, aucune autre écriture ne peut s'intercaler entre la comparaison et l'enregistrement ; la configuration `IsRowVersion()` d'EF reste un filet (une `DbUpdateConcurrencyException` est aussi traduite en `concurrent_update`).

Point important : une **demande de place** (étape 4) prend le verrou mais **ne modifie pas** la ligne `Events` : la `version` de l'événement ne change pas quand des membres réservent. Un admin qui corrige le titre pendant que des demandes arrivent n'est donc pas gêné par un faux conflit.

Les opérations qui ne dépendent pas du décompte (`POST .../publish`) n'ont pas besoin du verrou : simple chargement, règle de transition du Domain, `SaveChanges` avec contrôle `rowversion` (une course entre deux publications donne 409 `event_state_changed` au second, après relecture).

### 2. Occupation passée en paramètre au Domain

Le Domain ne fait aucune requête. Il reçoit un objet valeur :

```csharp
public readonly record struct EventOccupancy(int Pending, int Confirmed)
{
    public int Active => Pending + Confirmed;
    public static EventOccupancy None => new(0, 0);
}
```

Méthodes de `Event` qui l'utilisent : `RemainingSeats(occupancy)` (R7, jamais négatif), `IsBookable(now, occupancy)` (R2), `Update(..., occupancy, confirmRegistrantsWarning)` (R11, R12), `Unpublish(occupancy)` (R14). L'étape 7 ajoutera `EnsureCanBeDeleted(hasEverHadReservations)` (R15).

### 3. Avertissement « inscrits » en deux temps, contrôlé par le serveur

- `UpdateEventRequest` contient `confirmRegistrantsWarning` (booléen, `false` par défaut).
- Si `occupancy.Active > 0` **et** que `startsAt`, `endsAt` ou `price` change **et** que `confirmRegistrantsWarning` est faux → 409 `registrants_warning`, `title` = « Cet événement a 12 inscrits (demandes en attente et réservations confirmées). Pensez à les prévenir de ce changement. Confirmez pour enregistrer. » Rien n'est enregistré.
- Le front affiche ce message dans une boîte de confirmation, puis renvoie **la même requête** avec `confirmRegistrantsWarning: true` → 200. Les réservations sont conservées (R12).
- La règle « quels champs déclenchent l'avertissement » est dans le Domain (`Event.RequiresRegistrantsWarning(...)`). Le lieu n'en fait pas partie (texte de R12) : *question ouverte E2-4*.
- R11 est vérifiée **avant** l'avertissement : une capacité trop basse donne 409 `capacity_below_reserved` (« 15 places sont déjà occupées (10 confirmées, 5 en attente). La capacité ne peut pas être inférieure. »), même si l'avertissement est confirmé.

### 4. En attendant la table `Reservations` (étapes 2 et 3)

- Tout décompte passe par **un seul** service d'Application, `EventOccupancyReader` :
  - `Task<EventOccupancy> GetAsync(Guid eventId, DateTimeOffset now, CancellationToken)` ;
  - `Task<IReadOnlyDictionary<Guid, EventOccupancy>> GetManyAsync(IReadOnlyCollection<Guid> eventIds, DateTimeOffset now, CancellationToken)` (listes, ADR 0011).
- **À l'étape 2, il renvoie `EventOccupancy.None`** sans toucher la base, avec un commentaire `// Étape 4 : compter les réservations actives (ADR 0005, ReservationQueries.IsActive(now))`. Conséquences pendant les étapes 2-3 : places restantes = capacité, dépublication toujours permise, avertissement jamais déclenché, capacité réductible jusqu'à 1. C'est exact : aucune réservation ne peut exister.
- **À l'étape 4**, seul ce service change : requête groupée sur `Reservations` avec le filtre `IsActive(now)` (Confirmed, ou Pending non échue). Sous verrou, la demande de place matérialise d'abord les expirations de l'événement (ADR 0005) ; pour la modification et la dépublication, le filtre `IsActive(now)` suffit (une demande échue n'occupe plus de place, qu'elle soit écrite `Expired` ou non).
- **Étape 7** : `HasEverHadReservationsAsync(eventId)` (R15) sera ajouté au même service ; la clé étrangère `Reservations → Events` en `NO ACTION` sert de filet en base.
- Les règles R11, R12, R14 sont **testées dès l'étape 2** dans `Aaas.Domain.Tests` avec des occupations quelconques ; les tests d'intégration correspondants (avec de vraies réservations) sont listés dans le plan de l'étape 4.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Avertissement uniquement dans le front (à partir de `pendingCount`/`confirmedCount` du DTO admin) | Plus simple, mais le nombre d'inscrits peut changer entre l'affichage et l'enregistrement, et un appel direct à l'API (curl, autre client) passerait sans avertissement. Le contrôle serveur est testable bout en bout. |
| Code 428 *Precondition Required* pour l'avertissement | Plus précis en théorie, mais inhabituel ; 409 + `code` distinct suit la convention existante (le front s'appuie sur `code`). |
| Avertissement renvoyé en 200 avec la modification déjà enregistrée | Ne respecte pas « après confirmation, la modification est enregistrée ». |
| Pas de verrou à l'étape 2 (seulement `rowversion`), verrou ajouté à l'étape 4 | Moins de code aujourd'hui, mais le schéma transaction + stratégie d'exécution + verrou serait introduit en même temps que la partie la plus délicate (la dernière place). Le poser maintenant, sur un cas simple, le rend testable et explicable avant l'étape 4. |
| Créer dès l'étape 2 la table `Reservations` (vide) pour avoir de vrais décomptes | Anticipe le modèle de l'étape 4 sans ses règles ; migration à refaire si le modèle évolue. Le service unique `EventOccupancyReader` isole le changement. |
| Méthode « Count » dans un repository d'événements | Pas de repositories (ADR 0001) ; un service de lecture ciblé suffit. |
| Exposer `DatabaseFacade` dans `IAppDbContext` pour gérer les transactions dans Application | Application dépendrait de la plomberie relationnelle d'EF ; le port `IEventLock` garde ce détail dans Infrastructure. |

## Conséquences

- Un seul fichier change à l'étape 4 pour les décomptes (`EventOccupancyReader`), plus la demande de place qui réutilise `IEventLock`.
- Le schéma « verrou + version » est testé à l'étape 2 : deux `PUT` simultanés avec la même `version` → un 200, un 409 `concurrent_update`.
- Le front (S2) gère trois réponses 409 du formulaire d'édition : `registrants_warning` (confirmer et renvoyer), `concurrent_update` (recharger la fiche), `capacity_below_reserved` (erreur affichée près de la capacité).

## Reste à valider

- Par le porteur : avertissement contrôlé par le serveur, verrou posé dès l'étape 2.
- Par l'`expert-fonctionnel` : E2-4 (le changement de lieu déclenche-t-il l'avertissement ?).
