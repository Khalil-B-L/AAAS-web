# Fiche de révision — Gestion d'erreurs, journalisation et données personnelles (.NET 10, EF Core)

> À relire la veille d'un entretien. Exemples tirés du projet AAAS.

## 1. `IExceptionHandler` (ASP.NET Core 8+)

- **C'est quoi** : un service appelé par `app.UseExceptionHandler()` quand une exception remonte du pipeline. `TryHandleAsync` renvoie `true` si l'exception est traitée (réponse écrite), `false` pour passer au handler suivant.
- **Projet** : `src/Aaas.Api/Errors/ApiExceptionHandler.cs` traduit les exceptions métier en `ProblemDetails` (400, 401, 404, 409) et le reste en 500 `internal_error`.
- **Nouveauté .NET 10** : quand un handler renvoie `true`, le middleware **ne journalise plus** l'exception lui-même (« diagnostics supprimés », réglable par `ExceptionHandlerOptions.SuppressDiagnosticsCallback`). Si le handler renvoie `false`, le middleware reprend la main et journalise l'exception **complète** en Error.
- **Piège** : `IProblemDetailsService.TryWriteAsync` renvoie `false` si aucun writer n'accepte l'en-tête `Accept` du client. Renvoyer ce résultat tel quel ferait journaliser l'exception brute par le middleware. Dans le projet, le handler renvoie toujours `true`.

## 2. Données personnelles dans les journaux (R24)

Règle : jamais d'e-mail, de téléphone ou de nom dans les logs ; on journalise des `Guid`, des numéros d'erreur, des types.

| Source de fuite | Pourquoi | Parade dans le projet |
|---|---|---|
| `SqlException` 2601/2627 (doublon) | Le message contient la valeur dupliquée : `The duplicate key value is (ALICE@EXEMPLE.TN)` | `SqlServerErrors.TryGetUniqueKeyViolationNumber` ; on journalise le **numéro**, pas l'exception |
| EF Core : tout événement d'échec SQL (`SaveChangesFailed`, `CommandError`, `QueryIterationFailed`, `ConnectionError`, `TransactionError`, `NonQueryOperationFailed`, `ExecuteUpdateFailed`, `ExecuteDeleteFailed`) | Journalisés en Error par EF **avec l'exception jointe**, avant le `catch` de l'appelant | `ConfigureWarnings` : `SaveChangesFailed` ignoré, les autres en Debug. L'appelant (`ApiExceptionHandler`, `Program.cs`) journalise seul, en filtrant |
| Paramètres SQL | Visibles avec `EnableSensitiveDataLogging()` | Jamais activé. Sans lui, EF écrit `@p0='?'` |
| Exception non rattrapée dans `Program.cs` | Le runtime écrit message + `InnerException` sur stderr (donc `docker compose logs`) | `try/catch` autour de l'initialisation : log `Critical` sûr, puis exception **neutre** relancée |

**Piège** : « paramètres masqués » ne veut pas dire « exception masquée ». En Debug sur `Microsoft.EntityFrameworkCore`, `CommandError` réécrit l'exception, donc l'e-mail.

**Piège 2 (bug réel du projet)** : ne traiter que `SaveChangesFailed` et `CommandError` ne suffit pas. Une **lecture** en échec (`RoleExistsAsync`, `FirstOrDefaultAsync`, `ToListAsync`...) émet `CoreEventId.QueryIterationFailed` (catégorie `Microsoft.EntityFrameworkCore.Query`, Error, exception jointe). Constaté au démarrage sur une base sans schéma : le message SQL brut sortait avant même le `catch` de `Program.cs`. Pour dresser la liste exhaustive, on peut énumérer par réflexion les `EventDefinition` de `CoreResources`/`RelationalResources` et garder ceux de niveau ≥ Warning.

```csharp
options.ConfigureWarnings(w => w
    .Ignore(CoreEventId.SaveChangesFailed)
    .Log(
        (RelationalEventId.CommandError, LogLevel.Debug),
        (CoreEventId.QueryIterationFailed, LogLevel.Debug),
        (RelationalEventId.ConnectionError, LogLevel.Debug),
        (RelationalEventId.TransactionError, LogLevel.Debug) /* + NonQuery/ExecuteUpdate/ExecuteDelete Failed */));
```

- **`Ignore` ou `Log(..., Debug)` ?** `Ignore` supprime l'événement à tout niveau ; `Debug` le garde pour le poste de développement. On n'ignore que les vrais doublons (`SaveChangesFailed` répète `CommandError`).
- **Exception assumée** : `ExecutionStrategyRetrying` (Information, exception jointe) reste visible : erreurs transitoires seulement, messages sans donnée, utile pour voir les réveils d'Azure SQL serverless.

## 3. Reconnaître une erreur SQL sans lire le message

```csharp
// SqlServerErrors.cs : on parcourt la chaîne InnerException (DbUpdateException enveloppe SqlException)
for (var current = exception; current is not null; current = current.InnerException)
    if (current is SqlException sql) { number = sql.Number; return true; }
```

Numéros utiles : **2601** (index unique), **2627** (contrainte unique / PK), **18456** (login refusé), **1205** (deadlock, transitoire), **547** (clé étrangère).

Le message SQL est localisé et porteur de données : ne jamais faire `ex.Message.Contains("duplicate")`.

## 4. Concurrence au démarrage (plusieurs instances)

- Schéma « vérifier puis insérer » (`RoleExistsAsync` puis `CreateAsync`) : deux instances peuvent passer la vérification ensemble. L'**index unique** tranche ; la perdante reçoit une `DbUpdateException` 2601.
- Réponse : `catch (DbUpdateException ex) when (IsUniqueKeyViolation(ex))`, puis `db.ChangeTracker.Clear()` (l'entité refusée resterait en état `Added` et serait renvoyée au prochain `SaveChanges`), puis **revérifier** l'état voulu. On ne relance que si l'état n'est pas atteint.
- Filtre d'exception `when` : la pile n'est pas déroulée si le filtre est faux ; `throw;` conserve la pile d'origine (contrairement à `throw ex;`).

## 5. Niveaux de log

- `Critical` : l'application s'arrête (échec d'initialisation).
- `Error` : requête en échec inattendu (500).
- `Warning` : situation anormale mais gérée, **ou trace d'audit** qui doit rester visible même si le niveau minimal est relevé (promotion d'un compte en admin).
- `Information` / `Debug` : déroulement normal / détail technique.
- `logger.IsEnabled(level)` avant un log dont les arguments coûtent (analyseur CA1873).

## 6. Questions d'entretien

- **Pourquoi ne pas journaliser `ex.ToString()` partout ?** Le message et les `InnerException` peuvent contenir des données personnelles (RGPD) ou des secrets ; on journalise des identifiants et des codes.
- **Que se passe-t-il si `IExceptionHandler.TryHandleAsync` renvoie `false` ?** Le handler suivant est essayé, puis le comportement par défaut du middleware, qui journalise l'exception (et la relance si personne ne répond).
- **Comment gérer « créer si absent » avec plusieurs instances ?** Contrainte d'unicité en base + interception de la violation + revérification (ou `MERGE`/verrou applicatif). Le `if (!exists)` seul ne suffit pas.
