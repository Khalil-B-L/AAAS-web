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
- **Projet** : `IEventLock` (Application) / `EventLock` (Infrastructure). Utilisé par la modification et la dépublication ; plus tard par la demande de place (étape 4) et l'annulation (étape 7).

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
- `IAppDbContext` expose `DbSet<Event>`, `DbSet<AssociationSettings>`, `SaveChangesAsync`. Enregistré `AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())` : **même instance** que celle d'Identity et du verrou pendant la requête.
- Pas de repository : un `DbSet` est déjà un repository + unit of work.

## Questions d'entretien

- **Optimiste ou pessimiste ?** Optimiste (`rowversion`) quand les conflits sont rares et qu'on peut dire « rechargez » ; pessimiste (`UPDLOCK`) quand une décision dépend d'un état partagé qui ne doit pas bouger entre la lecture et l'écriture (dernière place, capacité). Le projet combine les deux.
- **Comment EF détecte-t-il un conflit ?** Le token est dans le `WHERE` de l'`UPDATE` ; 0 ligne affectée → `DbUpdateConcurrencyException`.
- **Pourquoi `CreateExecutionStrategy().ExecuteAsync` ?** Avec la reprise automatique, EF doit pouvoir rejouer toute la transaction ; il refuse une transaction utilisateur hors de ce bloc.
- **`FromSql` et injection SQL ?** `FromSql($"... {id}")` paramètre les valeurs interpolées ; `FromSqlRaw("... " + id)` non.
- **`HasData` pour quoi faire ?** Des données qui font partie du schéma (ligne singleton, valeurs de référence), versionnées avec les migrations.

## Exercice (facultatif)

Écrire un test d'intégration qui ouvre une transaction SQL à la main avec `WITH (UPDLOCK)` sur un événement, lance un `PUT` de cet événement dans une autre tâche, vérifie qu'il ne répond pas tant que la transaction est ouverte, puis qu'il répond 200 après le `COMMIT`.
