# Fiche de révision — EF Core et SQL Server : concurrence, verrous, migrations

> À relire la veille d'un entretien. Exemples tirés de l'étape 2 du projet AAAS (événements, numéro de contact).

## 1. Concurrence optimiste : `rowversion`

- **C'est quoi** : une colonne `rowversion` (8 octets, ex-`timestamp`) que **SQL Server incrémente lui-même** à chaque `INSERT`/`UPDATE` de la ligne. Valeur unique dans la base, jamais saisie par l'application.
- **Pourquoi** : deux admins ouvrent la même fiche ; le second qui enregistre écraserait en silence le travail du premier (*lost update*). Avec une version, le second reçoit 409 et recharge.
- **EF Core** : `builder.Property(e => e.RowVersion).IsRowVersion();` → la propriété est un *concurrency token* généré par la base. EF l'ajoute au `WHERE` :

  ```sql
  UPDATE [Events] SET [Title] = @p0 ... OUTPUT INSERTED.[RowVersion]
  WHERE [Id] = @p1 AND [RowVersion] = @p2;
  ```

  0 ligne touchée → `DbUpdateConcurrencyException`. Le `OUTPUT INSERTED.[RowVersion]` relit la nouvelle version : après `SaveChanges`, l'entité suivie la contient déjà (c'est pour ça que le DTO renvoyé par le `PUT` porte la nouvelle `version`).
- **Projet** : la version voyage en Base64 dans le champ `version` des DTO admin (`RowVersions.Encode`). À la modification, deux contrôles :
  1. `ConcurrencyGuard.EnsureSameVersion(entité.RowVersion, versionDuClient)` → 409 `concurrent_update` ;
  2. filet : `DbUpdateConcurrencyException` (écriture glissée entre la lecture et le `SaveChanges`) traduite aussi en 409 (`SaveChangesOrConflictAsync`).
- **Piège** : EF compare avec la valeur **lue** (`OriginalValue`), pas avec celle du client. Sans la comparaison explicite n° 1, une fiche ouverte il y a 10 minutes passerait si personne n'a écrit *pendant la requête*. Alternative : affecter `Entry(e).Property(x => x.RowVersion).OriginalValue = versionDuClient`.
- **Validation de l'entrée** : `[RowVersion]` vérifie un Base64 de **exactement 8 octets** (`Convert.TryFromBase64String` dans un tampon de 8 : une valeur plus longue échoue).

## 2. Verrou pessimiste : `UPDLOCK`

- **C'est quoi** : `SELECT ... FROM [Events] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = @id` dans une transaction pose un **verrou de mise à jour (U)** sur la ligne jusqu'au `COMMIT`. Une autre transaction qui demande le même verrou **attend** ; les lectures ordinaires ne sont pas bloquées. `ROWLOCK` : verrou de ligne, pas de page.
- **Pourquoi pas seulement `rowversion` ?** La règle « capacité ≥ places occupées » (R11) dépend d'**autres lignes** (les réservations, étape 4). Une demande de place qui ne modifie pas la ligne `Events` ne change pas sa version : seul un verrou sérialise « je compte, je décide, j'écris ». C'est l'équivalent SQL d'un `lock` C#, mais entre plusieurs instances de l'API.
- **Pourquoi `UPDLOCK` et pas `HOLDLOCK`/`SERIALIZABLE` ?** Deux transactions qui lisent avec un verrou partagé puis veulent écrire se bloquent mutuellement (deadlock). Le verrou U n'est compatible qu'avec des verrous S : un seul « candidat à l'écriture » à la fois.
- **RCSI** (READ_COMMITTED_SNAPSHOT, par défaut sur Azure SQL) : les lectures lisent une version validée sans verrou ; mais un `SELECT` **avec `UPDLOCK`** est une lecture verrouillante qui lit la dernière version validée. Le verrou fonctionne donc aussi sous RCSI (les tests d'intégration activent RCSI).
- **EF Core** : `db.Events.FromSql($"SELECT * FROM [Events] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}")`. L'interpolation `{id}` devient un **paramètre SQL** (`FromSql` prend un `FormattableString`) : pas d'injection. Ne jamais utiliser `FromSqlRaw` avec une concaténation.
- **Piège** : EF compose la requête brute en **sous-requête** (`SELECT TOP(2) [e].* FROM (SELECT * ... WITH (UPDLOCK, ROWLOCK) ...) AS [e]` pour `SingleOrDefault`). SQL Server accepte les indicateurs dans une table dérivée. Vérification sans base : `query.ToQueryString()` (test `EventLockSqlTests`).
- **Projet** : `IEventLock` (Application) / `EventLock` (Infrastructure). Utilisé par la modification, la dépublication et, depuis la tranche Réservations, **la demande de place** ; plus tard par l'annulation d'un événement (étape 7). Isolation, index filtré et test déterministe du verrou : fiche [`sql-server-verrous-isolation.md`](sql-server-verrous-isolation.md).

## 3. Transaction manuelle + stratégie de reprise

- `EnableRetryOnFailure()` rejoue une opération après une erreur **transitoire** (deadlock 1205, base Azure SQL en pause, coupure réseau).
- Une transaction ouverte à la main (`BeginTransactionAsync`) est **refusée** par la stratégie de reprise (*InvalidOperationException : the configured execution strategy does not support user-initiated transactions*) si elle n'est pas enveloppée :

  ```csharp
  var strategy = db.Database.CreateExecutionStrategy();
  var attempt = 0;
  return strategy.ExecuteAsync(state, async (_, s, ct) =>
  {
      if (attempt++ > 0) db.ChangeTracker.Clear();    // seul un rejeu repart d'un suivi vide
      await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
      var ev = await LockedEventQuery(db, s.EventId).SingleOrDefaultAsync(ct);
      var result = await s.Work(ev, ct);              // règles du Domain
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return result;
  }, verifySucceeded: null, ct);
  ```

- **Pourquoi `ChangeTracker.Clear()`** : si la première tentative a modifié l'entité puis échoué, la seconde rechargerait sinon l'entité déjà suivie (et ses modifications), au lieu de l'état réel de la base.
- **Pourquoi seulement au rejeu** : à la première tentative, vider le suivi jetterait sans bruit ce que l'appelant suivait déjà. Contrat de `IEventLock` : seules les modifications faites **dans `work`** sont garanties (au rejeu, une modification faite avant l'appel est perdue).
- **Conséquence** : `work` peut être exécuté deux fois : pas d'effet de bord hors de la base (envoi de mail, log métier) à l'intérieur.
- **Piège `verifySucceeded: null`** : si la connexion tombe **après** un `COMMIT` réussi mais avant que le client en reçoive la confirmation, la stratégie croit à un échec et rejoue tout le bloc. La nouvelle tentative relit la ligne **déjà modifiée** : sa version a changé, donc l'admin reçoit un **409 `concurrent_update`** alors que sa modification est bien enregistrée (pour une dépublication rejouée : 409 `event_state_changed`, l'événement étant déjà en brouillon). Cas rare, et sans danger (rien n'est écrit deux fois) : l'admin recharge et voit sa modification. Pour l'éviter, il faudrait un `verifySucceeded` qui interroge la base (« ma version est-elle déjà appliquée ? ») : complexité jugée inutile ici.

## 4. Migrations

| Commande | Effet |
|---|---|
| `dotnet ef migrations add AddEventsAndAssociationSettings -p src/Aaas.Infrastructure -s src/Aaas.Api -o Persistence/Migrations` | Compare le modèle au **snapshot** (`AppDbContextModelSnapshot.cs`) et génère le C# de la différence. N'ouvre aucune connexion. |
| `dotnet ef migrations script InitialIdentity AddEventsAndAssociationSettings -p ... -s ...` | SQL entre deux migrations : **à relire avant de valider** (CHECK, index, `rowversion`, `INSERT`). |
| `dotnet ef migrations script --idempotent` | Script rejouable (`IF NOT EXISTS ... __EFMigrationsHistory`) : celui de la CI. |
| `dotnet ef migrations remove` | Retire la dernière migration **non appliquée** (et restaure le snapshot). |

- **Piège `--no-build`** : l'outil lit la DLL existante ; juste après un `add`, elle ne contient pas encore la migration → « The migration '...' was not found ».
- **Code généré** : les migrations sont déclarées `generated_code = true` dans `.editorconfig` (sinon CA1861 « tableau constant en argument » à chaque `columns: new[] { ... }`). On ne retouche pas une migration à la main, et jamais une migration déjà sur `main`.
- **Au démarrage** (`Database:MigrateOnStartup=true`, local et compose) : `Database.MigrateAsync()` applique les migrations manquantes ; sur un volume existant, les données de l'étape 1 sont conservées. En Azure : *migration bundle* en CI.

## 5. Données de départ : `HasData`

- `builder.HasData(new { Id = 1, ContactWhatsApp = false })` → la migration contient un `INSERT`. La ligne fait partie du **schéma versionné**, comme une table.
- Objet **anonyme** : l'entité `AssociationSettings` n'a pas de constructeur public (sa ligne ne vient jamais de l'application).
- Les colonnes générées par la base (`rowversion`) ne sont pas fournies.
- **Piège** : modifier la valeur de `HasData` génère un `UPDATE` dans la migration suivante ; ce n'est pas un moyen de saisir des données métier modifiables (le numéro de contact est saisi par l'admin, pas par `HasData`).
- **Table à une ligne** : `CHECK ([Id] = 1)` + `ValueGeneratedNever()` (pas d'`IDENTITY`).

## 6. Contraintes `CHECK` et types

- `builder.ToTable("Events", t => t.HasCheckConstraint("CK_Events_Capacity", "[Capacity] >= 1"));` → filet en base si un script ou un bug contourne le Domain. Violation = erreur SQL **547** (500 côté API, puisque le Domain valide avant).
- **`decimal(p, s)`** : `HasPrecision(9, 3)` = 9 chiffres dont 3 décimales (jusqu'à 999 999,999). Stockage SQL Server : précision 1-9 → **5 octets**, 10-19 → 9 octets : `decimal(10,3)` coûterait 4 octets de plus pour rien.
- `System.Text.Json` lit un nombre JSON directement en `decimal` (pas de passage par `double`, pas de perte). Vérifier les décimales : `decimal.Round(p, 3) != p` → refus (pas d'arrondi silencieux).
- **Enum `: byte`** → `tinyint` ; valeurs **figées** (1, 2, 3) car présentes en base et dans un `CHECK`.

## 7. Requêtes de liste

- `AsNoTracking()` : lecture seule, pas de suivi (plus rapide, pas de surprise au `SaveChanges`).
- **Projection** (`Select(EventRow.Projection)`) : seules les colonnes utiles sont lues (pas la description de 4 000 caractères). Les calculs restent dans le Domain grâce à des fonctions statiques (`EventRules.RemainingSeats`).
- **Pagination** : `Skip((page-1)*pageSize).Take(pageSize)` → `OFFSET ... ROWS FETCH NEXT ... ROWS ONLY`. **Toujours un tri stable** (`OrderBy(StartsAt).ThenBy(Id)`), sinon deux lignes de même date peuvent changer de page d'un appel à l'autre. Borne haute sur `page` : sans elle, `(page-1)*pageSize` déborde un `int`.
- `COUNT(*)` séparé pour `totalCount`.
- Expression réutilisable : `static Expression<Func<Event, bool>> IsListedPublicly(DateTimeOffset now)` — une `Expression` (et non un `Func`) pour qu'EF la traduise en SQL.

## 8. EF Core dans la couche Application

- Application référence `Microsoft.EntityFrameworkCore` (DbSet, LINQ, `DbUpdateConcurrencyException`) mais **pas** `SqlServer` ni `Relational` : pas de `Database`, pas de transaction, pas de SQL brut. Ce qui en a besoin passe par un port (`IEventLock`).
- `IAppDbContext` expose `DbSet<Event>`, `DbSet<AssociationSettings>`, `DbSet<Reservation>`, `SaveChangesAsync`. Enregistré `AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())` : **même instance** que celle d'Identity et du verrou pendant la requête.
- Pas de repository : un `DbSet` est déjà un repository + unit of work.

## 9. `ExecuteUpdateAsync` : une mise à jour en masse sans charger les entités

- **C'est quoi** : `db.Reservations.Where(...).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, ReservationStatus.Expired), ct)` envoie **un seul** `UPDATE ... WHERE ...` ; aucune entité n'est chargée ni suivie. En EF Core 10, la méthode est dans le paquet de base (pas besoin de `Relational` dans Application).
- **Projet** : `ReservationExpiryService.ExpireDueForEventAsync` écrit « Expirée » sur les demandes échues d'un événement, sous le verrou, avant une nouvelle demande (libère l'index unique filtré).
- **Pièges** : le `ChangeTracker` n'est pas mis à jour (une entité déjà chargée garde l'ancien statut) ; pas de contrôle de `rowversion` (mais SQL Server change quand même la `rowversion` des lignes touchées : une transition concurrente sur l'une d'elles échouera en conflit, ce qu'on veut) ; la requête doit être **idempotente** si le bloc peut être rejoué (`WHERE Status = 1`).

## 10. Une règle, deux écritures : Domain et SQL

`Reservation.IsActive(now)` (C#, en mémoire) et `ReservationQueries.IsActive(now)` (`Expression<Func<Reservation, bool>>`, traduite en SQL) disent la même chose. Pour qu'elles ne divergent pas : un **test de concordance** (`ReservationRulesTests.Active_rule_matches_in_sql_and_in_memory`) insère les 6 statuts × échéance passée / exacte / future et compare les deux ensembles. Composer deux expressions (« active » ET « événement non terminé ») : on remplace le paramètre de l'une par celui de l'autre avec un petit `ExpressionVisitor` (EF ne sait pas traduire l'appel d'un délégué).

## 11. Rejouer une fois après un conflit qui ne change rien au métier

Un `DbUpdateConcurrencyException` dit « la ligne a changé », pas « la règle est violée ». Exemples du projet (revue du 24/11) :

- **Membre** (`MemberReservationService.CancelAsync`) : l'admin a déplacé la date, l'échéance a été recalculée, le statut est toujours « En attente ». On relit le statut **effectif** ; s'il est égal à celui vu avant `CancelByUser`, `db.ChangeTracker.Clear()`, relecture, règle réappliquée **une fois**. Sinon 409 `reservation_state_changed`.
- **Admin** (`AdminEventService.UpdateAsync`) : `ex.Entries.All(e => e.Entity is Reservation)` → seul un membre a bougé (la fiche admin n'est pas périmée) : on rejoue le bloc verrouillé une fois au lieu de `concurrent_update`.
- **Pièges** : sans `ChangeTracker.Clear()`, EF renvoie l'entité suivie (modifiée, ancienne `rowversion`) au lieu de la ligne relue ; rejouer **sans limite** peut boucler ; un verrou sur l'événement ne protège **pas** les lignes de réservation qu'une autre opération modifie sans ce verrou.
- **Tester** : une `SqlConnection` de test fait `UPDATE ... SET ExpiresAt = ...` dans une transaction **non validée** ; la requête de l'API lit l'ancienne version (RCSI), son `UPDATE` attend ; `COMMIT` → 0 ligne touchée → rejeu (`ReservationBoundaryApiTests`).

## Questions d'entretien

- **Optimiste ou pessimiste ?** Optimiste (`rowversion`) quand les conflits sont rares et qu'on peut dire « rechargez » ; pessimiste (`UPDLOCK`) quand une décision dépend d'un état partagé qui ne doit pas bouger entre la lecture et l'écriture (dernière place, capacité). Le projet combine les deux.
- **Comment EF détecte-t-il un conflit ?** Le token est dans le `WHERE` de l'`UPDATE` ; 0 ligne affectée → `DbUpdateConcurrencyException`.
- **Pourquoi `CreateExecutionStrategy().ExecuteAsync` ?** Avec la reprise automatique, EF doit pouvoir rejouer toute la transaction ; il refuse une transaction utilisateur hors de ce bloc.
- **`FromSql` et injection SQL ?** `FromSql($"... {id}")` paramètre les valeurs interpolées ; `FromSqlRaw("... " + id)` non.
- **`ExecuteUpdate` ou charger puis `SaveChanges` ?** `ExecuteUpdate` pour une mise à jour en masse sans règle par ligne (expiration) ; charger + méthode du Domain + `SaveChanges` quand la règle métier doit s'appliquer à chaque ligne (annulation, recalcul d'échéance).
- **`HasData` pour quoi faire ?** Des données qui font partie du schéma (ligne singleton, valeurs de référence), versionnées avec les migrations.

## Exercice (facultatif)

Écrire un test d'intégration qui ouvre une transaction SQL à la main avec `WITH (UPDLOCK)` sur un événement, lance un `PUT` de cet événement dans une autre tâche, vérifie qu'il ne répond pas tant que la transaction est ouverte, puis qu'il répond 200 après le `COMMIT`.

## 12. Tâche B : transitions admin sans verrou, une requête pour les coordonnées

- **Pas d'`UPDLOCK` pour confirmer, refuser, annuler, « Payée »** (ADR 0019 § 1) : aucune de ces actions ne peut faire **monter** le nombre de places prises. La seule protection utile est la `rowversion` de la **réservation** : deux admins qui cliquent en même temps → un `UPDATE ... WHERE Id = @id AND RowVersion = @lue` touche 1 ligne, l'autre 0 → `DbUpdateConcurrencyException` → 409 `reservation_state_changed` avec le statut relu. Test : `AdminReservationConcurrencyTests` (5 tours « confirmer contre refuser », « confirmer contre annulation par le membre », « double confirmation »).
- **Deux chemins vers le même 409** : si la seconde requête lit **après** l'écriture de la première, c'est le **Domain** qui refuse (`Confirm` sur une Confirmée) ; si elle a lu **avant**, c'est la **rowversion**. Les tests vérifient le résultat (un 200, un 409 avec le statut du gagnant), pas le chemin.
- **Réapplication unique** (même schéma que § 11) : si la ligne a changé mais pas son statut effectif (échéance recalculée, « Payée » posée par un autre admin), `ChangeTracker.Clear()`, relecture, nouvelle tentative ; un second conflit → 409 **`concurrent_update`** (« déjà traitée » serait faux : le statut n'a pas bougé). Une action valable **dans tous les statuts** (« Retirer Payée ») passe `reapplyIfStatusChanged: true` : le changement de statut ne la rend pas caduque.
- **Rendre une course déterministe** : un `SaveChangesInterceptor` (≈ un middleware autour de `SaveChanges`) écrit sur la ligne **depuis une autre connexion** dans `SavingChangesAsync`, donc juste avant l'`UPDATE` d'EF, qui touche 0 ligne. Branché pour un seul test par `factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(i))))` (`ConfigureDbContext`, EF Core 9+ : complète l'enregistrement existant). Piège : `UPDATE T SET X = X` change **quand même** la `rowversion`. Test : `AdminReservationReapplyTests`.
- **N+1 évité** : la liste des inscrits charge les réservations (1 requête), puis **toutes** les coordonnées (membres et admins « Payée ») en **une** requête `WHERE Id IN (...)` via le port `IIdentityService.GetContactsAsync` (Application ne voit pas `AppUser`, ADR 0001). Contrat : un compte supprimé est **absent** du dictionnaire → « Compte supprimé ».

Question d'entretien : **pourquoi ne pas verrouiller aussi la confirmation ?** Elle ne change pas l'occupation (En attente et Confirmée occupent toutes deux une place) ; verrouiller allongerait la file d'attente des demandes sur un événement très demandé, sans rien garantir de plus.

## 13. Actualités : `nvarchar(max)`, `CHECK`, projection `SUBSTRING`, *offset* contre *keyset*

### `nvarchar(n)` contre `nvarchar(max)`

- `nvarchar(n)` va jusqu'à **4 000** caractères (8 000 octets, une page de données). Au-delà, il faut `nvarchar(max)` (jusqu'à 2 Go), stocké hors de la ligne quand il dépasse la page.
- `nvarchar(max)` ne peut pas être **clé** d'index (il peut figurer en `INCLUDE`). Ici `News.Content` n'est ni indexé ni trié : aucune conséquence.
- La **limite métier** (8 000 caractères) est dans le Domain (`NewsRules`), comptée après normalisation. La colonne n'est que le contenant. EF : une propriété `string` sans `HasMaxLength` devient `nvarchar(max)`.

### `CHECK` comme filet

```sql
CONSTRAINT [CK_News_Status] CHECK ([Status] IN (1, 2)),
CONSTRAINT [CK_News_PublishedAt] CHECK ([Status] = 1 OR [PublishedAt] IS NOT NULL)
```

Déclarés dans `NewsArticleConfiguration` (`ToTable("News", t => t.HasCheckConstraint(...))`). Un `UPDATE` SQL direct qui viole la règle échoue avec l'**erreur 547** (testé dans `AdminNewsTests.Check_constraints`). Le second `CHECK` exprime une implication : « publiée ⇒ datée » s'écrit `NOT publiée OR datée`.

### Projection partielle : ne pas transférer le texte entier

```csharp
.Select(n => new { n.Id, n.Title, n.PublishedAt, Head = n.Content.Substring(0, NewsRules.ExcerptSourceLength + 1) })
```

EF traduit en `SUBSTRING([n].[Content], 1, 401)`. La liste ne lit que 401 caractères par ligne au lieu de 8 000. Le 401ᵉ caractère indique s'il reste une suite (« … » même si le début, une fois les espaces réduits, tient en 200). L'extrait est ensuite coupé en mémoire par une fonction pure du Domain (`NewsRules.Excerpt`). Vérifié dans les journaux EF (`PublicNewsTests.Long_text_excerpt`, catégorie `Microsoft.EntityFrameworkCore.Database.Command` passée en `Information` pour cette classe).

### Pagination *offset* contre *keyset*

| | *Offset* (retenu, ADR 0011) | *Keyset* / curseur |
|---|---|---|
| SQL | `ORDER BY PublishedAt DESC, Id OFFSET 20 ROWS FETCH NEXT 10 ROWS ONLY` | `WHERE PublishedAt < @d OR (PublishedAt = @d AND Id > @id) ORDER BY … FETCH NEXT 10` |
| Coût | le serveur lit puis jette les lignes de l'`OFFSET` : coût qui grandit avec le numéro de page | constant (recherche dans l'index) |
| Stabilité | une insertion pendant la lecture décale tout (doublon d'une page à l'autre) | stable |
| Navigation | « Page 3 sur 5 », lien direct `?page=3` | seulement « suivant » / « précédent » |

Retenir : *keyset* pour les flux infinis et les gros volumes ; *offset* pour les listes courtes avec numéros de page. Dans les deux cas, il faut un **tri total** (`ThenBy(Id)`), sinon deux lignes de même date peuvent changer d'ordre entre deux requêtes (testé : `PublicNewsTests.Stable_order_on_same_date`). Le projet a maintenant `QueryablePaging.Page<T>(query)` générique, et `PagingQuery` reçoit sa taille par défaut **par le constructeur** : un initialiseur de propriété ne se redéfinit pas dans une classe dérivée.

### Concurrence sans verrou

Les actualités n'ont **pas** de verrou `UPDLOCK` : aucune règle ne dépend d'autres lignes (pas de décompte comme pour les places). `rowversion` suffit (ADR 0006, 0022) : `PUT` avec `version` donne 409 si elle est périmée ; publier, dépublier et supprimer se font sans `version`, mais le `rowversion` chargé reste dans le `WHERE` de l'`UPDATE`/`DELETE` et fait échouer une écriture croisée (`DbUpdateConcurrencyException` → 409).

### Questions d'entretien

1. **Pourquoi `nvarchar(max)` plutôt que `nvarchar(8000)` ?** `nvarchar(n)` s'arrête à 4 000 caractères ; au-delà, c'est `max`.
2. **Pourquoi ajouter `Id` au tri d'une liste paginée ?** Sans tri total, SQL Server peut rendre deux lignes de même clé dans un ordre différent à chaque requête : une ligne apparaît sur deux pages, ou sur aucune.
3. **Quand utiliser un verrou pessimiste plutôt que `rowversion` ?** Quand une règle porte sur **plusieurs lignes** (capacité d'un événement) : `rowversion` ne protège qu'une ligne.

### Exercice (facultatif)

Écrivez la version *keyset* de `PublicNewsService.ListAsync` (paramètres `afterPublishedAt`, `afterId`) et comparez les plans d'exécution sur 100 000 lignes.
