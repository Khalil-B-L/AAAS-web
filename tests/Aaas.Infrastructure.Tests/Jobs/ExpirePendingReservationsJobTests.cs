using System.Collections.Concurrent;
using Aaas.Application.Options;
using Aaas.Application.Reservations;
using Aaas.Infrastructure.Jobs;
using Aaas.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Infrastructure.Tests.Jobs;

/// <summary>
/// ADR 0020 et plan-reservations § 9.3 : le job sans base. L'horloge simulée remplace l'attente réelle ;
/// la doublure compte les passages, simule des exceptions et vérifie qu'un scope est créé et libéré à chaque passage.
/// </summary>
public sealed class ExpirePendingReservationsJobTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly ExpirerState _state = new();
    private readonly ApiActivity _activity = new();
    private readonly CapturingLogger<ExpirePendingReservationsJob> _logger = new();
    private ServiceProvider? _provider;
    private ExpirePendingReservationsJob? _job;

    [Fact]
    public async Task Sans_condition_d_activite_un_passage_au_demarrage_puis_un_par_intervalle()
    {
        var job = await StartAsync(onlyWhenActive: false);

        await _state.WaitForCallsAsync(1);
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(2);
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(3);

        Assert.Equal([_clock.Start, _clock.Start + Interval, _clock.Start + Interval * 2], _state.Nows);
        await StopAsync(job);
    }

    [Fact]
    public async Task Un_scope_neuf_par_passage_libere_a_la_fin_du_passage()
    {
        var job = await StartAsync(onlyWhenActive: false);
        await _state.WaitForCallsAsync(1);
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(2);
        await StopAsync(job);

        Assert.Equal(2, _state.InstancesCreated);
        Assert.Equal(2, _state.InstancesDisposed);
    }

    // La règle OnlyWhenActive est testée en appelant un passage directement (job non démarré, aucun timer) :
    // une assertion négative n'a alors besoin d'aucune attente. Le cadencement est couvert par les tests du timer.
    [Fact]
    public async Task Sans_activite_de_l_API_un_passage_ne_touche_pas_la_base()
    {
        var job = CreateJob(onlyWhenActive: true);
        var ct = TestContext.Current.CancellationToken;

        await job.RunOnceAsync(ct);
        await job.RunOnceAsync(ct);

        Assert.Equal(0, _state.Calls);
        Assert.Equal(0, _state.InstancesCreated);
    }

    [Fact]
    public async Task Apres_une_requete_api_un_seul_passage_travaille_et_consomme_l_activite()
    {
        var job = CreateJob(onlyWhenActive: true);
        var ct = TestContext.Current.CancellationToken;

        _activity.RecordRequest();
        await job.RunOnceAsync(ct);
        Assert.Equal(1, _state.Calls);
        Assert.False(_activity.HasPendingActivity);

        // Plus de requête depuis : la base est de nouveau laissée en paix.
        await job.RunOnceAsync(ct);
        Assert.Equal(1, _state.Calls);
    }

    [Fact]
    public async Task Avec_la_condition_d_activite_le_timer_declenche_un_passage_apres_une_requete()
    {
        var job = await StartAsync(onlyWhenActive: true);

        _activity.RecordRequest();
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(1);

        Assert.False(_activity.HasPendingActivity);
        await StopAsync(job);
    }

    [Fact]
    public async Task Exception_SQL_journalisee_sans_son_message_puis_le_job_continue()
    {
        // 2627 : le message recopie la valeur dupliquée (ici un e-mail), il ne doit jamais atteindre les journaux.
        _state.FailOnCall[1] = SqlExceptionFactory.Create(2627);
        var job = await StartAsync(onlyWhenActive: false);

        await _state.WaitForCallsAsync(1);
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(2);
        await StopAsync(job);

        var error = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.Contains("2627", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(_logger.Entries, e => e.FullText.Contains("secret@exemple.tn", StringComparison.Ordinal));
        Assert.DoesNotContain(_logger.Entries, e => e.FullText.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Exception_non_SQL_journalisee_avec_sa_pile_puis_le_job_continue()
    {
        _state.FailOnCall[1] = new InvalidOperationException("Configuration EF invalide.");
        var job = await StartAsync(onlyWhenActive: false);

        await _state.WaitForCallsAsync(1);
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(2);
        await StopAsync(job);

        var error = Assert.Single(_logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.False(job.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Journal_de_succes_un_nombre_et_rien_quand_aucune_ligne()
    {
        _state.RowsPerCall.Enqueue(0);
        _state.RowsPerCall.Enqueue(3);
        var job = await StartAsync(onlyWhenActive: false);

        await _state.WaitForCallsAsync(1);
        _clock.Advance(Interval);
        await _state.WaitForCallsAsync(2);
        await StopAsync(job);

        var expired = Assert.Single(_logger.Entries, e => e.EventId.Id == 5003);
        Assert.Equal(LogLevel.Information, expired.Level);
        Assert.Equal("Expiration des demandes : 3 demande(s) passée(s) en Expirée.", expired.Message);
    }

    [Fact]
    public async Task Desactive_aucun_passage_et_un_message_au_demarrage()
    {
        var job = await StartAsync(onlyWhenActive: false, enabled: false);

        // ExecuteTask nul : aucune boucle n'a été lancée, donc aucun passage ne peut survenir plus tard
        // (pas besoin d'attendre pour l'assertion négative).
        Assert.Null(job.ExecuteTask);
        _clock.Advance(Interval);
        Assert.Equal(0, _state.Calls);
        Assert.Contains(_logger.Entries, e => e.EventId.Id == 5002);
        await StopAsync(job);
    }

    [Fact]
    public async Task Arret_propre_pendant_l_attente_sans_journal_d_erreur()
    {
        var job = await StartAsync(onlyWhenActive: false);
        await _state.WaitForCallsAsync(1);

        await StopAsync(job);

        Assert.True(job.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Arret_pendant_un_passage_le_passage_est_annule_sans_journal_d_erreur()
    {
        _state.BlockUntilCancelled = true;
        var job = await StartAsync(onlyWhenActive: false);
        await _state.WaitForCallsAsync(1);

        await StopAsync(job);

        Assert.True(job.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Arret_pendant_un_passage_SqlException_d_annulation_traitee_comme_un_arret()
    {
        // SqlClient annulé lève une SqlException (« Operation cancelled by user », n° 0), pas une OperationCanceledException.
        _state.BlockUntilCancelled = true;
        _state.SqlExceptionOnCancel = true;
        var job = await StartAsync(onlyWhenActive: false);
        await _state.WaitForCallsAsync(1);

        await StopAsync(job);

        Assert.True(job.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Passage_direct_annule_par_SqlException_leve_OperationCanceledException()
    {
        _state.BlockUntilCancelled = true;
        _state.SqlExceptionOnCancel = true;
        var job = CreateJob(onlyWhenActive: false);
        using var cts = new CancellationTokenSource();

        var pass = job.RunOnceAsync(cts.Token);
        await _state.WaitForCallsAsync(1);
        await cts.CancelAsync();

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pass);
        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.Empty(_logger.Entries);
    }

    public async ValueTask DisposeAsync()
    {
        _job?.Dispose();
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        _state.Dispose();
    }

    private async Task<ExpirePendingReservationsJob> StartAsync(bool onlyWhenActive, bool enabled = true)
    {
        var job = CreateJob(onlyWhenActive, enabled);
        await job.StartAsync(TestContext.Current.CancellationToken);
        return job;
    }

    private ExpirePendingReservationsJob CreateJob(bool onlyWhenActive, bool enabled = true)
    {
        var services = new ServiceCollection();
        // Scoped comme le vrai service : une instance par scope, donc par passage.
        services.AddScoped<IExpireDueReservations>(_ => new FakeExpirer(_state));
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var options = new ReservationOptions
        {
            ExpirationJob = new ExpirationJobOptions { Enabled = enabled, Interval = Interval, OnlyWhenActive = onlyWhenActive },
        };
        _job = new ExpirePendingReservationsJob(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _activity,
            _clock,
            Microsoft.Extensions.Options.Options.Create(options),
            _logger);
        return _job;
    }

    private static async Task StopAsync(ExpirePendingReservationsJob job)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        await job.StopAsync(timeout.Token);
    }

    private sealed class ExpirerState : IDisposable
    {
        private readonly ConcurrentQueue<DateTimeOffset> _nows = new();
        private readonly SemaphoreSlim _called = new(0);
        private int _calls;
        private int _created;
        private int _disposed;

        public ConcurrentDictionary<int, Exception> FailOnCall { get; } = new();

        public ConcurrentQueue<int> RowsPerCall { get; } = new();

        public bool BlockUntilCancelled { get; set; }

        public bool SqlExceptionOnCancel { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public int InstancesCreated => Volatile.Read(ref _created);

        public int InstancesDisposed => Volatile.Read(ref _disposed);

        public IReadOnlyList<DateTimeOffset> Nows => [.. _nows];

        public void Created() => Interlocked.Increment(ref _created);

        public void Disposed() => Interlocked.Increment(ref _disposed);

        public int Record(DateTimeOffset now)
        {
            _nows.Enqueue(now);
            var call = Interlocked.Increment(ref _calls);
            _called.Release();
            return call;
        }

        public async Task WaitForCallsAsync(int expected)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (Calls < expected)
            {
                await _called.WaitAsync(timeout.Token);
            }
        }

        public void Dispose() => _called.Dispose();
    }

    private sealed class FakeExpirer : IExpireDueReservations, IAsyncDisposable
    {
        private readonly ExpirerState _state;

        public FakeExpirer(ExpirerState state)
        {
            _state = state;
            state.Created();
        }

        public async Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            var call = _state.Record(now);
            if (_state.BlockUntilCancelled)
            {
                try
                {
                    await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (_state.SqlExceptionOnCancel)
                {
                    throw SqlExceptionFactory.Create(0, "A severe error occurred on the current command. Operation cancelled by user.");
                }
            }

            if (_state.FailOnCall.TryGetValue(call, out var exception))
            {
                throw exception;
            }

            return _state.RowsPerCall.TryDequeue(out var rows) ? rows : 0;
        }

        public ValueTask DisposeAsync()
        {
            _state.Disposed();
            return ValueTask.CompletedTask;
        }
    }
}
