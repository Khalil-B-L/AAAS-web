using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// Plan § 9.4 n° 6 (ADR 0006, 0019 § 5) : transitions concurrentes sur la MÊME réservation, sans verrou d'événement.
/// La rowversion (ou, si la seconde requête lit après la première, la règle du Domain) garantit qu'une seule action
/// s'applique ; l'autre reçoit 409 reservation_state_changed avec le statut réellement en base.
/// Plusieurs tours par test : un seul tour ne prouverait la course que par chance.
/// </summary>
[Trait("Category", "Concurrency")]
public sealed class AdminReservationConcurrencyTests(SqlServerFixture sql) : AdminReservationApiTestBase(sql)
{
    private const int Rounds = 5;

    [Fact(DisplayName = "Confirmer et refuser en parallèle : un 200, un 409 reservation_state_changed avec le statut du gagnant")]
    public async Task Confirm_versus_refuse()
    {
        var admin = await AdminTokenAsync();
        var otherAdmin = await AdminTokenAsync(); // deux sessions, comme deux administrateurs
        var eventId = await PublishedEventAsync(admin, capacity: Rounds);
        var tokens = await MemberTokensAsync(Rounds);

        for (var i = 0; i < Rounds; i++)
        {
            var id = await RequestSeatIdAsync(tokens[i], eventId);

            var responses = await Task.WhenAll(
                AdminActionAsync(admin, id, "confirm"),
                AdminActionAsync(otherAdmin, id, "refuse", new { reason = "Doublon" }));

            var winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            var winnerStatus = Str(await ReadJsonAsync(winner), "status");
            var problem = await ReadJsonAsync(loser);
            Assert.Equal("reservation_state_changed", Str(problem, "code"));
            Assert.Equal(winnerStatus, Str(problem, "currentStatus"));
            Assert.Equal("Cette demande a déjà été traitée ou annulée.", Str(problem, "title"));

            var stored = await Factory.ScalarAsync<int>("SELECT CAST(Status AS int) FROM Reservations WHERE Id = @id", ("@id", id));
            Assert.Equal(winnerStatus == "Confirmed" ? 2 : 3, stored);
        }
    }

    [Fact(DisplayName = "Confirmer et annulation par le membre en parallèle : jamais d'état incohérent, 409 avec le statut relu")]
    public async Task Confirm_versus_member_cancel()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: Rounds);
        var tokens = await MemberTokensAsync(Rounds);

        for (var i = 0; i < Rounds; i++)
        {
            var id = await RequestSeatIdAsync(tokens[i], eventId);

            var both = await Task.WhenAll(AdminActionAsync(admin, id, "confirm"), CancelAsync(tokens[i], id));
            var (confirm, cancel) = (both[0], both[1]);

            var stored = await Factory.ScalarAsync<int>("SELECT CAST(Status AS int) FROM Reservations WHERE Id = @id", ("@id", id));
            // Annulation d'abord → la confirmation échoue (Annulée par le membre). Confirmation d'abord → le membre peut
            // encore annuler (J−10, avant la limite de 24 h) : deux 200. S'ils se croisent, le perdant reçoit 409.
            if (confirm.StatusCode == HttpStatusCode.Conflict)
            {
                var problem = await ReadJsonAsync(confirm);
                Assert.Equal("reservation_state_changed", Str(problem, "code"));
                Assert.Equal("CancelledByUser", Str(problem, "currentStatus"));
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
                Assert.Equal(5, stored);
            }
            else if (cancel.StatusCode == HttpStatusCode.Conflict)
            {
                Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
                Assert.Equal("Confirmed", Str(await ReadJsonAsync(cancel), "currentStatus"));
                Assert.Equal(2, stored);
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
                Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
                Assert.Equal(5, stored);
            }
        }
    }

    [Fact(DisplayName = "Double clic « Confirmer » de deux admins : un 200, un 409 currentStatus Confirmed")]
    public async Task Double_confirm()
    {
        var admin = await AdminTokenAsync();
        var otherAdmin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: Rounds);
        var tokens = await MemberTokensAsync(Rounds);

        for (var i = 0; i < Rounds; i++)
        {
            var id = await RequestSeatIdAsync(tokens[i], eventId);

            var responses = await Task.WhenAll(AdminActionAsync(admin, id, "confirm"), AdminActionAsync(otherAdmin, id, "confirm"));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("Confirmed", Str(await ReadJsonAsync(loser), "currentStatus"));
        }

        Assert.Equal(Rounds, await ActiveCountInDatabaseAsync(eventId));
    }
}
