using Aaas.Application.Options;
using Aaas.Application.Reservations;
using Aaas.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aaas.Infrastructure.Jobs;

/// <summary>
/// Tâche de fond qui écrit « Expirée » sur les demandes échues (ADR 0005 § 5, ADR 0020). Elle range la base ;
/// l'exactitude des places ne dépend pas d'elle (filtre à la lecture, matérialisation sous verrou par événement).
/// - PeriodicTimer + TimeProvider injecté : les tests avancent le temps (FakeTimeProvider.Advance) au lieu d'attendre.
/// - Un scope DI par passage : le job est un singleton, AppDbContext est scoped.
/// - try/catch par passage : une exception sortie d'ExecuteAsync arrêterait toute l'API (StopHost, défaut depuis .NET 6).
/// - Plusieurs instances : sans coordination, l'UPDATE est idempotent (« WHERE Status = Pending »).
/// </summary>
public sealed partial class ExpirePendingReservationsJob(
    IServiceScopeFactory scopeFactory,
    ApiActivity activity,
    TimeProvider timeProvider,
    IOptions<ReservationOptions> options,
    ILogger<ExpirePendingReservationsJob> logger) : BackgroundService
{
    private readonly ExpirationJobOptions _settings = options.Value.ExpirationJob;
    private PeriodicTimer? _timer;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Enabled)
        {
            // ExecuteAsync n'est jamais lancé : aucun timer, aucun accès à la base.
            LogDisabled();
            return Task.CompletedTask;
        }

        // Créé ici et non dans ExecuteAsync (lancé sur un thread du pool depuis .NET 10) : quand StartAsync rend
        // la main, le timer existe déjà, donc un Advance du test qui suit ne peut pas être « perdu ».
        _timer = new PeriodicTimer(_settings.Interval, timeProvider);
        LogStarted(_settings.Interval, _settings.OnlyWhenActive);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = _timer!;
        try
        {
            // Premier passage au démarrage, puis un par intervalle. Si un passage dure plus que l'intervalle,
            // PeriodicTimer ne cumule pas les ticks manqués : un seul passage de rattrapage, jamais en parallèle.
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Arrêt de l'hôte : sortie normale, rien à journaliser.
        }
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    /// <summary>Un passage. Ne lève jamais, sauf OperationCanceledException à l'arrêt.</summary>
    internal async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        // Heure lue AVANT de consommer l'activité : un passage décide de son « maintenant » au moment où il
        // démarre, pas après une attente éventuelle (et les tests peuvent s'appuyer sur cet ordre).
        var now = timeProvider.GetUtcNow();
        if (_settings.OnlyWhenActive && !activity.TryConsume())
        {
            // Personne n'a appelé l'API : on laisse la base dormir (Azure SQL serverless, ADR 0020 § 4).
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var expirer = scope.ServiceProvider.GetRequiredService<IExpireDueReservations>();
            var count = await expirer.ExpireDueAsync(now, stoppingToken);
            if (count > 0)
            {
                LogExpired(count);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // SqlClient annulé lève souvent une SqlException (« Operation cancelled by user », n° 0) et non une
            // OperationCanceledException : c'est l'arrêt de l'hôte, pas un échec à journaliser en Error.
            throw new OperationCanceledException(stoppingToken);
        }
#pragma warning disable CA1031 // volontaire : aucune exception ne doit sortir de la boucle (StopHost arrêterait l'API)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // R24 / ADR 0002 : le message d'une SqlException peut recopier une valeur ; on ne journalise que le type
            // et le numéro. Les autres exceptions (EF, configuration) n'en contiennent pas : trace complète.
            if (SqlServerErrors.TryGetSqlErrorNumber(ex, out var sqlErrorNumber))
            {
                LogSqlFailure(ex.GetType().FullName, sqlErrorNumber);
            }
            else
            {
                LogFailure(ex);
            }
        }
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information,
        Message = "Expiration des demandes : tâche démarrée (intervalle {Interval}, seulement après activité de l'API : {OnlyWhenActive}).")]
    private partial void LogStarted(TimeSpan interval, bool onlyWhenActive);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information,
        Message = "Expiration des demandes : tâche désactivée (Reservations:ExpirationJob:Enabled = false).")]
    private partial void LogDisabled();

    // Un nombre, jamais d'identifiant de réservation ni de membre (ADR 0020 § 5).
    [LoggerMessage(EventId = 5003, Level = LogLevel.Information,
        Message = "Expiration des demandes : {Count} demande(s) passée(s) en Expirée.")]
    private partial void LogExpired(int count);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Error,
        Message = "Expiration des demandes : échec du passage ({ExceptionType}, erreur SQL {SqlErrorNumber}) ; détail non journalisé, nouvel essai au passage suivant.")]
    private partial void LogSqlFailure(string? exceptionType, int sqlErrorNumber);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Error,
        Message = "Expiration des demandes : échec du passage ; nouvel essai au passage suivant.")]
    private partial void LogFailure(Exception exception);
}
