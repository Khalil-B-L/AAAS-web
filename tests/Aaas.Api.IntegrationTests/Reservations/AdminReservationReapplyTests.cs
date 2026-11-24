using System.Net;
using System.Net.Http.Headers;
using Aaas.Api.IntegrationTests.Support;
using Aaas.Domain.Reservations;
using Aaas.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// Réapplication après conflit de rowversion (AdminReservationService.TransitionAsync), rendue déterministe : un
/// intercepteur écrit sur la ligne (autre connexion) juste AVANT l'UPDATE de l'admin, qui touche donc 0 ligne.
/// Une vraie course (Task.WhenAll) ne tomberait dans cette fenêtre que par chance.
/// </summary>
public sealed class AdminReservationReapplyTests(SqlServerFixture sql) : AdminReservationApiTestBase(sql)
{
    [Fact(DisplayName = "Retrait de « Payée » pendant une annulation par le membre → 200, Annulée par le membre, plus payée")]
    public async Task Unmark_paid_while_member_cancels()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, price: 15m);
        var id = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);
        await AdminActionOkAsync(admin, id, "confirm");
        await AdminActionOkAsync(admin, id, "mark-paid");

        var response = await WithConcurrentWriteAsync(
            id, "UPDATE Reservations SET Status = 5, CancelledAt = SYSUTCDATETIME() WHERE Id = @id", times: 1, admin, "unmark-paid");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal("CancelledByUser", Str(json, "status"));
        Assert.False(json.GetProperty("isPaid").GetBoolean());
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Reservations WHERE Id = @id AND IsPaid = 0 AND Status = 5", ("@id", id)));
    }

    [Fact(DisplayName = "Confirmer : un conflit sans changement de statut → réappliqué, 200")]
    public async Task Confirm_reapplied_once_when_status_unchanged()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var id = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);

        var response = await WithConcurrentWriteAsync(id, TouchRow, times: 1, admin, "confirm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Confirmed", Str(await ReadJsonAsync(response), "status"));
    }

    [Fact(DisplayName = "Confirmer : deux conflits de suite sans changement de statut → 409 concurrent_update, demande toujours en attente")]
    public async Task Confirm_second_conflict_is_concurrent_update()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var id = await RequestSeatIdAsync((await RegisterMemberAsync()).Token, eventId);

        var response = await WithConcurrentWriteAsync(id, TouchRow, times: 2, admin, "confirm");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("concurrent_update", await CodeAsync(response));
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT CAST(Status AS int) FROM Reservations WHERE Id = @id", ("@id", id)));
    }

    // UPDATE sans changement de valeur : SQL Server change quand même la rowversion.
    private const string TouchRow = "UPDATE Reservations SET RequestedAt = RequestedAt WHERE Id = @id";

    private async Task<HttpResponseMessage> WithConcurrentWriteAsync(
        Guid reservationId, string sql, int times, string adminToken, string action)
    {
        var interceptor = new ConcurrentWriteInterceptor(Factory.ConnectionString, reservationId, sql, times);
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/reservations/{reservationId}/{action}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        await response.Content.LoadIntoBufferAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, interceptor.Remaining);
        return response;
    }

    private sealed class ConcurrentWriteInterceptor(string connectionString, Guid reservationId, string sql, int times)
        : SaveChangesInterceptor
    {
        private int _remaining = times;

        public int Remaining => Math.Max(_remaining, 0);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var targetsReservation = eventData.Context!.ChangeTracker.Entries<Reservation>()
                .Any(e => e.Entity.Id == reservationId && e.State == EntityState.Modified);
            if (targetsReservation && Interlocked.Decrement(ref _remaining) >= 0)
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("@id", reservationId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
