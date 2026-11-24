# Fiche de révision — Tâches de fond .NET : `BackgroundService`, `PeriodicTimer`, scopes

> Exemple du projet : `src/Aaas.Infrastructure/Jobs/ExpirePendingReservationsJob.cs` (ADR 0020), tests dans `tests/Aaas.Infrastructure.Tests/Jobs/` et `tests/Aaas.Api.IntegrationTests/Reservations/ExpirationJobTests.cs`.

## 1. Les briques

| Notion | Définition courte |
|---|---|
| `IHostedService` | Interface à deux méthodes, `StartAsync` / `StopAsync`, appelées par l'hôte (`app.RunAsync()`) au démarrage et à l'arrêt. Enregistrée par `services.AddHostedService<T>()` (= singleton). |
| `BackgroundService` | Classe de base qui implémente `IHostedService` : on écrit seulement `ExecuteAsync(CancellationToken stoppingToken)`, une boucle longue. `StartAsync` lance `ExecuteAsync` sans l'attendre ; `StopAsync` annule `stoppingToken` puis attend la fin (délai : `HostOptions.ShutdownTimeout`, 30 s par défaut). `ExecuteTask` expose la tâche en cours. |
| `PeriodicTimer` (.NET 6) | Minuterie « à attendre » : `while (await timer.WaitForNextTickAsync(ct))`. Pas de callback, pas de réentrance : si un passage dure plus que l'intervalle, **un** tick est mémorisé, jamais deux passages en parallèle. |
| `PeriodicTimer(interval, TimeProvider)` (.NET 8) | Même chose avec une horloge injectée : en test, `FakeTimeProvider.Advance(5 min)` déclenche le tick immédiatement. |
| `IServiceScopeFactory` | Crée un scope DI à la demande : `await using var scope = factory.CreateAsyncScope();` puis `scope.ServiceProvider.GetRequiredService<...>()`. |

## 2. Le squelette du projet (à savoir réécrire)

```csharp
public sealed partial class ExpirePendingReservationsJob(
    IServiceScopeFactory scopeFactory, ApiActivity activity, TimeProvider timeProvider,
    IOptions<ReservationOptions> options, ILogger<ExpirePendingReservationsJob> logger) : BackgroundService
{
    private PeriodicTimer? _timer;

    public override Task StartAsync(CancellationToken ct)
    {
        if (!options.Value.ExpirationJob.Enabled) return Task.CompletedTask;      // ExecuteAsync jamais lancé
        _timer = new PeriodicTimer(options.Value.ExpirationJob.Interval, timeProvider);
        return base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            do { await RunOnceAsync(stoppingToken); }                              // 1er passage au démarrage
            while (await _timer!.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }  // arrêt normal
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        if (options.Value.ExpirationJob.OnlyWhenActive && !activity.TryConsume()) return;  // laisser dormir la base
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();                // un DbContext neuf par passage
            var count = await scope.ServiceProvider.GetRequiredService<IExpireDueReservations>().ExpireDueAsync(now, ct);
            if (count > 0) LogExpired(count);                                        // un nombre, rien d'autre
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { /* journaliser SANS le message SQL ; la boucle continue */ }
    }
}
```

Le travail SQL est dans Application (`ReservationExpiryService.ExpireDueAsync`) : `ExecuteUpdateAsync` = **un** `UPDATE ... WHERE Status = 1 AND ExpiresAt <= @now`, sans charger d'entités, idempotent.

## 3. Pièges (les questions d'entretien sont là)

1. **Injecter un `DbContext` dans le constructeur** d'un hosted service : le service est un singleton, le `DbContext` est scoped. En Development, la validation des scopes refuse de démarrer (« Cannot consume scoped service from singleton ») ; sinon, le même contexte vit des jours, grossit (suivi des entités) et n'est pas thread-safe. **Règle : un scope par passage.**
2. **Exception non attrapée dans `ExecuteAsync`** : depuis .NET 6, `BackgroundServiceExceptionBehavior.StopHost` (défaut) **arrête toute l'API**. Une base indisponible une minute couperait le site. Parade : `try/catch` **par passage**, pas autour de la boucle. Ne pas passer `Ignore` en global : ça masquerait les erreurs des autres services hébergés.
3. **Avaler l'annulation** : `catch (Exception)` attrape aussi `OperationCanceledException` à l'arrêt ; le `catch ... when (token.IsCancellationRequested) { throw; }` placé avant laisse l'arrêt se faire proprement, sans faux journal d'erreur.
4. **`Task.Delay` dans une boucle** : marche, mais dérive (durée du passage + délai) et se teste mal. `PeriodicTimer` + `TimeProvider` est l'outil prévu.
5. **Travail synchrone avant le premier `await`** : avant .NET 10, `StartAsync` exécutait `ExecuteAsync` jusqu'au premier `await` sur le thread de démarrage ; un travail long bloquait le démarrage de l'hôte. Depuis .NET 10, `ExecuteAsync` est lancé sur le pool de threads. Conséquence ici : le timer est créé dans `StartAsync` pour exister quand le test appelle `Advance`.
6. **Plusieurs instances** : chaque instance de l'API lance sa tâche. Soit le travail est **idempotent** (notre cas : `WHERE Status = 1`), soit il faut un verrou distribué (`sp_getapplock`, bail de stockage) ou un planificateur externe (Hangfire, Quartz, Azure Functions timer).
7. **Coût cloud** : une tâche qui interroge la base toutes les 5 minutes empêche Azure SQL serverless de se mettre en pause (même piège qu'une sonde `/health` qui toucherait la base). Ici : drapeau `ApiActivity` (`Volatile.Write` dans un middleware, `Interlocked.Exchange` dans le job) ; pas de requête `/api/` → pas d'accès à la base.
8. **Journaux** : un passage toutes les 5 minutes qui journalise « 0 ligne » noie les journaux ; on n'écrit que si `count > 0`. Jamais d'identifiant de membre ; jamais le message d'une `SqlException` (il peut recopier une valeur), seulement son numéro.
9. **App Service sans « Always On »** (offre F1) : l'application s'endort après ~20 min sans requête, la tâche avec elle. Acceptable ici parce que l'exactitude ne dépend pas de la tâche (statut effectif calculé à la lecture).

## 4. Tester

- **Sans base** : construire le job à la main avec un `ServiceCollection` contenant une doublure *scoped* de `IExpireDueReservations`, un `FakeTimeProvider`, un logger qui capture. `await job.StartAsync(ct)` → premier passage ; `clock.Advance(interval)` → passage suivant ; attendre la doublure (sémaphore), jamais un `Task.Delay` au hasard pour une assertion positive. `await job.StopAsync(ct)` → `job.ExecuteTask.IsCompletedSuccessfully`.
- **Avec base** (WebApplicationFactory + Testcontainers) : remplacer `TimeProvider` par le `FakeTimeProvider` du test, activer la tâche par configuration, `Advance(48 h + 5 min)`, puis **interroger la base en boucle** jusqu'à voir `Status = 4` (le passage est asynchrone).
- Désactiver la tâche par défaut dans les autres tests d'intégration : une ligne qui change « dans le dos » du test rend les assertions instables.

## 5. Commandes utiles

```bash
dotnet test tests/Aaas.Infrastructure.Tests --filter "FullyQualifiedName~Jobs"
dotnet test tests/Aaas.Api.IntegrationTests -m:1 --filter "FullyQualifiedName~ExpirationJob"   # Docker requis
docker compose logs -f api | grep "Expiration des demandes"
```

## 6. Questions d'entretien

- *Différence `IHostedService` / `BackgroundService` ?* — Le second implémente le premier et gère pour vous le lancement non bloquant d'`ExecuteAsync` et l'annulation à l'arrêt.
- *Comment utiliser EF Core dans un `BackgroundService` ?* — `IServiceScopeFactory`, un scope (donc un `DbContext`) par unité de travail, libéré à la fin.
- *Que se passe-t-il si `ExecuteAsync` lève ?* — Depuis .NET 6, l'hôte s'arrête (`StopHost`) ; attraper par itération.
- *Pourquoi `PeriodicTimer` plutôt que `System.Threading.Timer` ?* — Pas de callback concurrent ni de réentrance, `async` naturel, annulation par jeton, testable avec `TimeProvider`.
- *Deux instances de l'API, la tâche tourne deux fois : problème ?* — Pas si l'opération est idempotente ; sinon verrou distribué ou planificateur unique.
