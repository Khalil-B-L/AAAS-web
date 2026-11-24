using System.Diagnostics;
using System.Net;
using Aaas.Api.IntegrationTests.Support;
using Aaas.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// Tâche C (ADR 0005 § 5, ADR 0020) : la tâche de fond réelle, dans l'API réelle, contre SQL Server.
/// L'horloge simulée de l'API pilote aussi le PeriodicTimer : Advance déclenche un passage, sans attendre 5 minutes.
/// Le passage est asynchrone : on interroge la base jusqu'à voir le résultat (délai maximal court).
/// Les cas d'exception sont couverts sans base dans Aaas.Infrastructure.Tests (ExpirePendingReservationsJobTests).
/// </summary>
public abstract class ExpirationJobTestBase(SqlServerFixture sql) : ReservationApiTestBase(sql)
{
    protected const byte Pending = 1;
    protected const byte Confirmed = 2;
    protected const byte Expired = 4;

    protected static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected abstract bool OnlyWhenActive { get; }

    protected override Dictionary<string, string?>? ConfigurationOverrides
    {
        get
        {
            var settings = base.ConfigurationOverrides ?? [];
            settings["Reservations:ExpirationJob:Enabled"] = "true";
            settings["Reservations:ExpirationJob:Interval"] = "00:05:00";
            settings["Reservations:ExpirationJob:OnlyWhenActive"] = OnlyWhenActive ? "true" : "false";
            return settings;
        }
    }

    protected Task<byte> StatusInDatabaseAsync(Guid reservationId) =>
        Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", reservationId));

    protected static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(15))
            {
                Assert.Fail($"Délai dépassé : {what}");
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}

public sealed class ExpirationJobTests(SqlServerFixture sql) : ExpirationJobTestBase(sql)
{
    protected override bool OnlyWhenActive => false;

    [Fact]
    public async Task Demande_echue_ecrite_Expiree_par_la_tache_les_autres_intactes_et_journal_sans_donnee_personnelle()
    {
        var admin = await AdminTokenAsync();
        var (dueEmail, dueToken, _) = await RegisterMemberAsync();
        var (confirmedEmail, confirmedToken, _) = await RegisterMemberAsync();
        var (lateEmail, lateToken, _) = await RegisterMemberAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 3);

        // T : deux demandes ; l'une est confirmée par l'admin (écrite en SQL, la confirmation est l'objet de la tâche B).
        var due = (await RequestSeatOkAsync(dueToken, eventId)).GetProperty("id").GetGuid();
        var confirmed = (await RequestSeatOkAsync(confirmedToken, eventId)).GetProperty("id").GetGuid();
        await ConfirmInDatabaseAsync(confirmed);

        // T + 24 h : une troisième demande, qui échoira à T + 72 h. L'événement est complet.
        Clock.Advance(TimeSpan.FromHours(24));
        var notDue = (await RequestSeatOkAsync(lateToken, eventId)).GetProperty("id").GetGuid();
        Assert.Equal(0, await RemainingSeatsAsync(eventId));

        // T + 48 h 05 : la première demande est échue ; le passage suivant de la tâche l'écrit Expirée.
        Clock.Advance(TimeSpan.FromHours(24) + Interval);
        await WaitUntilAsync(async () => await StatusInDatabaseAsync(due) == Expired, "la demande échue n'a pas été écrite Expirée");

        Assert.Equal(Confirmed, await StatusInDatabaseAsync(confirmed));
        Assert.Equal(Pending, await StatusInDatabaseAsync(notDue));
        Assert.Equal(0, await Factory.ScalarAsync<int>(
            "SELECT COUNT(*) FROM Reservations WHERE Status = 1 AND ExpiresAt <= @now", ("@now", Clock.GetUtcNow())));
        Assert.Equal(1, await RemainingSeatsAsync(eventId));
        Assert.Equal(2, await ActiveCountInDatabaseAsync(eventId));

        // Journal : un nombre, rien d'autre (ADR 0020 § 5).
        var messages = Factory.Logs.Messages;
        Assert.Contains(messages, m => m.Contains("1 demande(s) passée(s) en Expirée", StringComparison.Ordinal));
        string[] personal = [dueEmail, confirmedEmail, lateEmail, "Salma", "Ben Ali", "20 123 456", "20123456", due.ToString(), confirmed.ToString(), notDue.ToString()];
        var jobMessages = messages.Where(m => m.Contains(typeof(ExpirePendingReservationsJob).FullName!, StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(jobMessages);
        Assert.All(jobMessages, m => Assert.DoesNotContain(personal, p => m.Contains(p, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(messages, m => m.StartsWith("[Error]", StringComparison.Ordinal) || m.StartsWith("[Critical]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Apres_la_tache_le_meme_membre_peut_redemander_une_place()
    {
        var admin = await AdminTokenAsync();
        var (_, token, _) = await RegisterMemberAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var first = (await RequestSeatOkAsync(token, eventId)).GetProperty("id").GetGuid();

        Clock.Advance(TimeSpan.FromHours(48) + Interval);
        await WaitUntilAsync(async () => await StatusInDatabaseAsync(first) == Expired, "la demande échue n'a pas été écrite Expirée");

        var second = (await RequestSeatOkAsync(token, eventId)).GetProperty("id").GetGuid();
        Assert.NotEqual(first, second);
        Assert.Equal(Pending, await StatusInDatabaseAsync(second));
    }
}

public sealed class ExpirationJobActivityTests(SqlServerFixture sql) : ExpirationJobTestBase(sql)
{
    protected override bool OnlyWhenActive => true;

    [Fact]
    public async Task Sans_requete_api_aboutie_aucun_passage_sondes_et_401_ne_comptent_pas_puis_une_requete_relance_le_rangement()
    {
        var ct = TestContext.Current.CancellationToken;
        var activity = Factory.Services.GetRequiredService<ApiActivity>();
        var job = Factory.Services.GetServices<IHostedService>().OfType<ExpirePendingReservationsJob>().Single();
        var admin = await AdminTokenAsync();
        var (_, token, _) = await RegisterMemberAsync();
        var eventId = await PublishedEventAsync(admin);
        var reservation = (await RequestSeatOkAsync(token, eventId)).GetProperty("id").GetGuid();

        // Un passage du timer consomme l'activité de la préparation (rien n'est encore échu). Ensuite, aucun
        // passage du timer n'est en vol : les vérifications du drapeau ci-dessous ne peuvent pas être faussées.
        Clock.Advance(Interval);
        await WaitUntilAsync(() => Task.FromResult(!activity.HasPendingActivity), "l'activité n'a pas été consommée");

        // Sondes de liveness (Docker, App Service) : pas une activité.
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync(new Uri("/health", UriKind.Relative), ct)).StatusCode);
        Assert.False(activity.HasPendingActivity);

        // Robot anonyme sur un chemin /api/ inconnu : 401, pas une activité (ne réveille pas Azure SQL serverless).
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync(new Uri("/api/inconnu", UriKind.Relative), ct)).StatusCode);
        Assert.False(activity.HasPendingActivity);

        // Une réponse 200 sur /api/ lève le drapeau ; un passage direct le consomme (rien n'est échu).
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/events", null)).StatusCode);
        Assert.True(activity.HasPendingActivity);
        await job.RunOnceAsync(ct);
        Assert.False(activity.HasPendingActivity);
        Assert.Equal(Pending, await StatusInDatabaseAsync(reservation));

        // 48 h sans visiteur : la demande est échue, mais un passage laisse la base dormir. Passage appelé
        // directement : l'assertion négative n'a besoin d'aucune attente (le passage du timer voit le même drapeau).
        Clock.Advance(TimeSpan.FromHours(48));
        await job.RunOnceAsync(ct);
        Assert.Equal(Pending, await StatusInDatabaseAsync(reservation));

        // Un visiteur consulte la liste publique : le passage suivant range la base.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/events", null)).StatusCode);
        Clock.Advance(Interval);
        await WaitUntilAsync(async () => await StatusInDatabaseAsync(reservation) == Expired, "la demande échue n'a pas été écrite Expirée");
    }
}
