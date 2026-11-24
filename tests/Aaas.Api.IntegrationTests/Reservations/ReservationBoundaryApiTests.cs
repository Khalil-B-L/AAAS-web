using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Aaas.Api.IntegrationTests.Reservations;

/// <summary>
/// Réservations, tâche A : cas aux bornes de l'échéance, états de l'événement, déplacement de la date par l'admin,
/// annulation du membre contre une écriture concurrente, idempotence du double clic, en-tête no-store sur toutes les
/// réponses de /api/me et /api/admin, journaux sans donnée personnelle (RG-03 à RG-05, ADR 0005, 0018, 0019, 0021).
/// Horloge simulée : les jetons sont obtenus AVANT de la faire avancer (JwtBearer utilise l'horloge réelle).
/// </summary>
public sealed class ReservationBoundaryApiTests(SqlServerFixture sql) : ReservationApiTestBase(sql)
{
    private async Task<DateTimeOffset> StartsAtAsync(Guid eventId) =>
        (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/events/{eventId}", null))).GetProperty("startsAt").GetDateTimeOffset();

    private async Task<HttpResponseMessage> MoveStartAsync(string admin, Guid eventId, DateTimeOffset newStart, int capacity = 30) =>
        await PutStartAsync(admin, eventId, await VersionAsync(admin, eventId), newStart, capacity);

    private async Task<string?> VersionAsync(string admin, Guid eventId) =>
        (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{eventId}", admin))).GetProperty("version").GetString();

    private Task<HttpResponseMessage> PutStartAsync(string admin, Guid eventId, string? version, DateTimeOffset newStart, int capacity = 30)
    {
        var body = EventBodyAt(newStart, newStart.AddHours(2), version: version, capacity: capacity);
        body["price"] = 15m;
        body["confirmRegistrantsWarning"] = true;
        return SendAsync(HttpMethod.Put, $"/api/admin/events/{eventId}", admin, body);
    }

    /// <summary>
    /// Ouvre une transaction SQL de test qui exécute <paramref name="sql"/> et GARDE ses verrous jusqu'au COMMIT :
    /// une écriture concurrente « en vol », pour placer une requête de l'API pile entre une lecture et un enregistrement.
    /// </summary>
    private async Task<(SqlConnection Connection, SqlTransaction Transaction)> HoldAsync(string sql, Guid id)
    {
        var connection = new SqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        return (connection, transaction);
    }

    /// <summary>Vérifie que la requête attend bien la transaction de test, puis la valide et renvoie la réponse.</summary>
    private static async Task<HttpResponseMessage> ReleaseAfterWaitingAsync(
        Task<HttpResponseMessage> pending, SqlTransaction transaction, Action? beforeCommit = null)
    {
        var first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.NotSame(pending, first);
        beforeCommit?.Invoke();
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return await pending.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    private async Task<DateTimeOffset> DatabaseExpiresAtAsync(Guid reservationId) =>
        await Factory.ScalarAsync<DateTimeOffset>("SELECT ExpiresAt FROM Reservations WHERE Id = @id", ("@id", reservationId));

    // ---- Échéance = min(demande + 48 h, début), à la seconde près ----

    [Theory(DisplayName = "Échéance = min(demande + 48 h, début) : demande à début − 48 h ± 1 s, − 47 h et − 49 h ; bascule en Expirée à l'instant exact")]
    [InlineData(-49 * 3600)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(47 * 3600 + 3599)]
    public async Task Expiry_at_the_boundary(int secondsAfterStartMinus48h)
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var startsAt = await StartsAtAsync(eventId);
        var member = (await RegisterMemberAsync()).Token;
        var requestedAt = startsAt.AddHours(-48).AddSeconds(secondsAfterStartMinus48h);
        Clock.SetUtcNow(requestedAt);

        var created = await RequestSeatOkAsync(member, eventId);

        var expected = requestedAt.AddHours(48) <= startsAt ? requestedAt.AddHours(48) : startsAt;
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(expected, created.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(expected, await DatabaseExpiresAtAsync(id));
        Assert.Equal(0, await RemainingSeatsAsync(eventId));

        Clock.SetUtcNow(expected.AddSeconds(-1));
        Assert.Equal("Pending", (await GetMyReservationAsync(member, id)).GetProperty("status").GetString());
        Assert.Equal(0, await RemainingSeatsAsync(eventId));

        Clock.SetUtcNow(expected); // l'instant de l'échéance est déjà échu
        var due = await GetMyReservationAsync(member, id);
        Assert.Equal("Expired", due.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, due.GetProperty("expiresAt").ValueKind);
        Assert.False(due.GetProperty("canCancel").GetBoolean());
        Assert.Equal(1, await RemainingSeatsAsync(eventId));
    }

    // ---- États de l'événement ----

    [Fact(DisplayName = "Demande à 1 s du début → 201 (échéance = début) ; au début exact → 409 reservations_closed")]
    public async Task Request_one_second_before_start_then_at_start()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 5);
        var startsAt = await StartsAtAsync(eventId);
        var tokens = await MemberTokensAsync(2);

        Clock.SetUtcNow(startsAt.AddSeconds(-1));
        var created = await RequestSeatOkAsync(tokens[0], eventId);
        Assert.Equal(startsAt, created.GetProperty("expiresAt").GetDateTimeOffset());

        Clock.SetUtcNow(startsAt);
        var closed = await RequestSeatAsync(tokens[1], eventId);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal("reservations_closed", await CodeAsync(closed));
        Assert.Equal("Les réservations sont closes", (await ReadJsonAsync(closed)).GetProperty("title").GetString());
    }

    [Fact(DisplayName = "Événement terminé → 409 reservations_closed ; l'historique du membre reste lisible")]
    public async Task Request_on_ended_event_is_closed()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromHours(3));
        var startsAt = await StartsAtAsync(eventId);
        var tokens = await MemberTokensAsync(2);
        var id = (await RequestSeatOkAsync(tokens[0], eventId)).GetProperty("id").GetGuid();

        Clock.SetUtcNow(startsAt.AddHours(2)); // fin exacte

        var closed = await RequestSeatAsync(tokens[1], eventId);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal("reservations_closed", await CodeAsync(closed));
        var mine = await GetMyReservationAsync(tokens[0], id);
        Assert.Equal("Expired", mine.GetProperty("status").GetString());
        Assert.True(mine.GetProperty("event").GetProperty("hasEnded").GetBoolean());
    }

    [Fact(DisplayName = "Événement annulé : event_cancelled, y compris s'il a aussi commencé (annulé prime sur clos)")]
    public async Task Cancelled_event_takes_precedence_over_closed()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromHours(3));
        var startsAt = await StartsAtAsync(eventId);
        var member = (await RegisterMemberAsync()).Token;
        await Factory.ExecuteAsync("UPDATE Events SET Status = 3 WHERE Id = @id", ("@id", eventId));

        Assert.Equal("event_cancelled", await CodeAsync(await RequestSeatAsync(member, eventId)));
        Clock.SetUtcNow(startsAt.AddHours(1));
        Assert.Equal("event_cancelled", await CodeAsync(await RequestSeatAsync(member, eventId)));
    }

    [Fact(DisplayName = "Événement repassé en brouillon : « ma demande » et nouvelle demande → 404 ; la réservation passée reste lisible, sans lien")]
    public async Task Unpublished_event_hides_itself_but_not_the_history()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await CancelAsync(member, id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"/api/admin/events/{eventId}/unpublish", admin)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", member)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RequestSeatAsync(member, eventId)).StatusCode);
        var mine = await GetMyReservationAsync(member, id);
        Assert.False(mine.GetProperty("event").GetProperty("isVisible").GetBoolean());
        Assert.Equal("CancelledByUser", mine.GetProperty("status").GetString());
    }

    // ---- Isolation entre membres ----

    [Fact(DisplayName = "Un autre membre : même 404 et même titre qu'un identifiant inconnu (existence non révélée), aucune modification")]
    public async Task Another_members_reservation_is_indistinguishable_from_unknown()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var tokens = await MemberTokensAsync(2);
        var id = (await RequestSeatOkAsync(tokens[0], eventId)).GetProperty("id").GetGuid();

        var foreign = await CancelAsync(tokens[1], id);
        var unknown = await CancelAsync(tokens[1], Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var foreignProblem = await ReadJsonAsync(foreign);
        var unknownProblem = await ReadJsonAsync(unknown);
        Assert.Equal(unknownProblem.GetProperty("title").GetString(), foreignProblem.GetProperty("title").GetString());
        Assert.Equal(unknownProblem.GetProperty("code").GetString(), foreignProblem.GetProperty("code").GetString());
        Assert.Equal(1, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", id)));
        Assert.Null(await Factory.ScalarAsync<DateTimeOffset?>("SELECT CancelledAt FROM Reservations WHERE Id = @id", ("@id", id)));
    }

    [Fact(DisplayName = "Un admin n'accède pas, via /api/me, aux réservations des autres : il ne voit que les siennes")]
    public async Task Admin_sees_only_their_own_reservations_under_me()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/me/reservations/{id}", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(admin, id)).StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations", admin))).GetProperty("totalCount").GetInt32());
    }

    // ---- Pagination de la liste ----

    [Fact(DisplayName = "Liste : page hors limites → items vides, totalCount exact ; pageSize 51 et page 0 → 400 ; pages sans doublon ni trou")]
    public async Task List_pagination_bounds()
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;
        var expectedOrder = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(3 + i));
            await RequestSeatOkAsync(member, eventId);
            expectedOrder.Add(eventId);
        }

        var seen = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var json = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/me/reservations?scope=current&page={page}&pageSize=2", member));
            Assert.Equal(5, json.GetProperty("totalCount").GetInt32());
            seen.AddRange(json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("event").GetProperty("id").GetGuid()));
        }

        Assert.Equal(expectedOrder, seen); // tri par début croissant, chaque ligne exactement une fois
        var beyond = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=current&page=4&pageSize=2", member));
        Assert.Equal(0, beyond.GetProperty("items").GetArrayLength());
        Assert.Equal(5, beyond.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, "/api/me/reservations?pageSize=51", member)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(HttpMethod.Get, "/api/me/reservations?page=0", member)).StatusCode);
        var history = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=history", member));
        Assert.Equal(0, history.GetProperty("totalCount").GetInt32());
    }

    // ---- Date déplacée par l'admin ----

    [Fact(DisplayName = "Début reculé : l'échéance d'une demande qui était plafonnée au début est allongée, plafonnée à demande + 48 h")]
    public async Task Moving_start_later_extends_a_capped_deadline()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromHours(30));
        var startsAt = await StartsAtAsync(eventId);
        var member = (await RegisterMemberAsync()).Token;
        var created = await RequestSeatOkAsync(member, eventId);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(startsAt, created.GetProperty("expiresAt").GetDateTimeOffset()); // plafonnée au début (30 h < 48 h)

        var response = await MoveStartAsync(admin, eventId, startsAt.AddDays(5));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Start.AddHours(48), (await GetMyReservationAsync(member, id)).GetProperty("expiresAt").GetDateTimeOffset());
        Assert.Equal(Start.AddHours(48), await DatabaseExpiresAtAsync(id));
    }

    [Fact(DisplayName = "Début avancé : toutes les demandes en attente prennent le nouveau début ; à cet instant elles sont échues et les places rendues")]
    public async Task Moving_start_earlier_pulls_every_pending_deadline()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 2);
        var tokens = await MemberTokensAsync(2);
        var first = (await RequestSeatOkAsync(tokens[0], eventId)).GetProperty("id").GetGuid();
        Clock.Advance(TimeSpan.FromHours(10));
        var second = (await RequestSeatOkAsync(tokens[1], eventId)).GetProperty("id").GetGuid();
        var newStart = Start.AddHours(20);

        var response = await MoveStartAsync(admin, eventId, newStart, capacity: 2);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movedStart = (await ReadJsonAsync(response)).GetProperty("startsAt").GetDateTimeOffset();
        Assert.Equal(movedStart, await DatabaseExpiresAtAsync(first));
        Assert.Equal(movedStart, await DatabaseExpiresAtAsync(second));
        Assert.Equal(0, await RemainingSeatsAsync(eventId));

        Clock.SetUtcNow(movedStart);
        Assert.Equal(2, await RemainingSeatsAsync(eventId));
        Assert.Equal("Expired", (await GetMyReservationAsync(tokens[0], first)).GetProperty("status").GetString());
    }

    [Fact(DisplayName = "Début reculé : une demande déjà échue n'est PAS ranimée (échéance et statut inchangés, place rendue, nouvelle demande possible)")]
    public async Task Moving_start_does_not_revive_a_due_request()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var startsAt = await StartsAtAsync(eventId);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        var expiresAt = await DatabaseExpiresAtAsync(id);
        Clock.SetUtcNow(expiresAt); // échue, la base dit encore « En attente »

        var response = await MoveStartAsync(admin, eventId, startsAt.AddDays(5), capacity: 1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expiresAt, await DatabaseExpiresAtAsync(id));
        Assert.Equal("Expired", (await GetMyReservationAsync(member, id)).GetProperty("status").GetString());
        Assert.Equal(1, await RemainingSeatsAsync(eventId));
        Assert.Equal(HttpStatusCode.Created, (await RequestSeatAsync(member, eventId)).StatusCode);
    }

    [Fact(DisplayName = "Début avancé à moins de 24 h : une confirmée garde son échéance mais n'est plus annulable (limite recalculée sur le nouveau début)")]
    public async Task Moving_start_inside_24_hours_closes_confirmed_cancellation()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(10));
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        await ConfirmInDatabaseAsync(id);
        var expiresAt = await DatabaseExpiresAtAsync(id);

        var response = await MoveStartAsync(admin, eventId, Start.AddHours(10));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movedStart = (await ReadJsonAsync(response)).GetProperty("startsAt").GetDateTimeOffset();
        Assert.Equal(expiresAt, await DatabaseExpiresAtAsync(id));
        var mine = await GetMyReservationAsync(member, id);
        Assert.Equal("Confirmed", mine.GetProperty("status").GetString());
        Assert.False(mine.GetProperty("canCancel").GetBoolean());
        Assert.Equal(movedStart.AddHours(-24), mine.GetProperty("cancellableUntil").GetDateTimeOffset());
        var refused = await CancelAsync(member, id);
        Assert.Equal("cancellation_deadline_passed", await CodeAsync(refused));
    }

    // ---- Annulation du membre contre une écriture concurrente ----

    [Theory(DisplayName = "Annulation pendant qu'une transaction modifie la ligne : 409 reservation_state_changed avec le statut relu, sans écraser l'écriture concurrente")]
    [InlineData(2, "Confirmed")]
    [InlineData(3, "Refused")]
    [InlineData(4, "Expired")]
    [InlineData(6, "CancelledByAssociation")]
    public async Task Cancel_loses_against_a_concurrent_write(int concurrentStatus, string expectedCurrentStatus)
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 3);
        var (email, member, _) = await RegisterMemberAsync();
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        // La transaction de test tient le verrou d'écriture sur la ligne : sous RCSI le SELECT de l'annulation lit
        // l'ancienne version (En attente), puis son UPDATE attend ; au COMMIT la rowversion a changé → 0 ligne touchée.
        await using var connection = new SqlConnection(Factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE Reservations SET Status = @status, DecidedAt = SYSUTCDATETIME() WHERE Id = @id";
            command.Parameters.AddWithValue("@status", (byte)concurrentStatus);
            command.Parameters.AddWithValue("@id", id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        }

        var cancel = CancelAsync(member, id);
        var first = await Task.WhenAny(cancel, Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.NotSame(cancel, first); // l'annulation attend bien l'écriture concurrente
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        var response = await cancel.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("reservation_state_changed", problem.GetProperty("code").GetString());
        Assert.Equal(expectedCurrentStatus, problem.GetProperty("currentStatus").GetString());
        Assert.Equal(
            expectedCurrentStatus == "Expired"
                ? "Votre demande a expiré. Vous pouvez en faire une nouvelle."
                : "Cette demande a déjà été traitée ou annulée.",
            problem.GetProperty("title").GetString());
        Assert.Equal((byte)concurrentStatus, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", id)));
        Assert.Null(await Factory.ScalarAsync<DateTimeOffset?>("SELECT CancelledAt FROM Reservations WHERE Id = @id", ("@id", id)));
        Assert.DoesNotContain(Factory.Logs.Messages, m => m.Contains(email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Annulation pendant un recalcul d'échéance (statut inchangé) : règle réappliquée une fois → 200 CancelledByUser, échéance concurrente conservée")]
    public async Task Cancel_is_replayed_when_a_concurrent_write_keeps_the_status()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 3);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        var rescheduled = (await DatabaseExpiresAtAsync(id)).AddHours(-1);

        // Simule RescheduleDeadlinesAsync : rowversion modifiée, statut toujours « En attente ».
        var (connection, transaction) = await HoldAsync("UPDATE Reservations SET ExpiresAt = DATEADD(HOUR, -1, ExpiresAt) WHERE Id = @id", id);
        await using (connection)
        await using (transaction)
        {
            var response = await ReleaseAfterWaitingAsync(CancelAsync(member, id), transaction);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("CancelledByUser", (await ReadJsonAsync(response)).GetProperty("status").GetString());
        }

        Assert.Equal(5, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", id)));
        Assert.NotNull(await Factory.ScalarAsync<DateTimeOffset?>("SELECT CancelledAt FROM Reservations WHERE Id = @id", ("@id", id)));
        Assert.Equal(rescheduled, await DatabaseExpiresAtAsync(id)); // l'écriture concurrente n'est pas écrasée
        Assert.Equal(3, await RemainingSeatsAsync(eventId));
    }

    [Fact(DisplayName = "Date déplacée pendant qu'un membre annule une demande lue par l'admin : rejeu une fois → 200 (pas concurrent_update), annulation conservée, autres échéances recalculées")]
    public async Task Moving_start_is_replayed_when_a_member_cancels_concurrently()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 3);
        var tokens = await MemberTokensAsync(2);
        var cancelled = (await RequestSeatOkAsync(tokens[0], eventId)).GetProperty("id").GetGuid();
        var kept = (await RequestSeatOkAsync(tokens[1], eventId)).GetProperty("id").GetGuid();
        var cancelledDeadline = await DatabaseExpiresAtAsync(cancelled);
        var version = await VersionAsync(admin, eventId);
        var newStart = Start.AddHours(20); // avant demande + 48 h : les échéances en attente prennent le nouveau début

        // L'annulation du membre (sans verrou) tient la ligne : sous RCSI l'admin lit encore « En attente »,
        // puis son UPDATE attend et trouve une rowversion changée au COMMIT.
        var (connection, transaction) = await HoldAsync(
            "UPDATE Reservations SET Status = 5, CancelledAt = SYSUTCDATETIME() WHERE Id = @id", cancelled);
        await using (connection)
        await using (transaction)
        {
            var response = await ReleaseAfterWaitingAsync(PutStartAsync(admin, eventId, version, newStart, capacity: 3), transaction);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var movedStart = (await ReadJsonAsync(response)).GetProperty("startsAt").GetDateTimeOffset();
            Assert.Equal(movedStart, await DatabaseExpiresAtAsync(kept));
        }

        Assert.Equal(5, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", cancelled)));
        Assert.Equal(cancelledDeadline, await DatabaseExpiresAtAsync(cancelled)); // annulée : échéance non recalculée
        Assert.Equal(2, await RemainingSeatsAsync(eventId));
    }

    [Fact(DisplayName = "Modification qui a attendu le verrou pendant qu'une demande échoit : l'heure est lue sous verrou, la demande échue n'est pas ranimée")]
    public async Task Update_reads_the_clock_after_acquiring_the_lock()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var startsAt = await StartsAtAsync(eventId);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        // Échéance courte posée en base : une échéance plafonnée au début n'échoit qu'au début, où la fiche n'est plus modifiable.
        var deadline = Start.AddHours(1);
        await Factory.ExecuteAsync("UPDATE Reservations SET ExpiresAt = @at WHERE Id = @id", ("@at", deadline), ("@id", id));
        var version = await VersionAsync(admin, eventId);

        // Un autre traitement tient le verrou de l'événement (même indicateur que IEventLock) ; la modification attend.
        var (connection, transaction) = await HoldAsync("SELECT Id FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = @id", eventId);
        await using (connection)
        await using (transaction)
        {
            var response = await ReleaseAfterWaitingAsync(
                PutStartAsync(admin, eventId, version, startsAt.AddDays(2), capacity: 1),
                transaction,
                beforeCommit: () => Clock.SetUtcNow(deadline)); // la demande échoit pendant l'attente

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await ReadJsonAsync(response);
            Assert.Equal(0, json.GetProperty("pendingCount").GetInt32());
            Assert.Equal(1, json.GetProperty("remainingSeats").GetInt32());
        }

        Assert.Equal(deadline, await DatabaseExpiresAtAsync(id)); // non ranimée (sinon : demande + 48 h)
        Assert.Equal("Expired", (await GetMyReservationAsync(member, id)).GetProperty("status").GetString());
    }

    [Fact(DisplayName = "Annulation membre contre rowversion périmée sur une demande échue entre-temps : statut effectif Expired, message membre")]
    public async Task Cancel_after_deadline_reports_expired_even_if_the_row_is_still_pending()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        Clock.SetUtcNow(await DatabaseExpiresAtAsync(id));

        var response = await CancelAsync(member, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Expired", (await ReadJsonAsync(response)).GetProperty("currentStatus").GetString());
        Assert.Equal(1, await Factory.ScalarAsync<byte>("SELECT Status FROM Reservations WHERE Id = @id", ("@id", id)));
    }

    // ---- Idempotence du double clic ----

    [Fact(DisplayName = "Cinq demandes simultanées du même membre : un seul 201, quatre 409 already_reserved portant le même reservationId, une ligne")]
    public async Task Five_simultaneous_requests_create_one_reservation()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10);
        var member = (await RegisterMemberAsync()).Token;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RequestSeatAsync(member, eventId)));

        var created = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();
        var conflicts = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Equal(4, conflicts.Count);
        foreach (var conflict in conflicts)
        {
            var problem = await ReadJsonAsync(conflict);
            Assert.Equal("already_reserved", problem.GetProperty("code").GetString());
            Assert.Equal(id, problem.GetProperty("reservationId").GetGuid());
        }

        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Reservations WHERE EventId = @id", ("@id", eventId)));
        Assert.Equal(9, await RemainingSeatsAsync(eventId));
    }

    [Fact(DisplayName = "Trois demandes successives : 201 puis deux 409 identiques (même reservationId, même message), aucune ligne de plus")]
    public async Task Sequential_repeats_are_idempotent()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 10);
        var member = (await RegisterMemberAsync()).Token;
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();

        for (var i = 0; i < 2; i++)
        {
            var again = await RequestSeatAsync(member, eventId);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            var problem = await ReadJsonAsync(again);
            Assert.Equal(id, problem.GetProperty("reservationId").GetGuid());
            Assert.Equal("Vous avez déjà une demande en cours ou une réservation pour cet événement", problem.GetProperty("title").GetString());
        }

        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM Reservations WHERE EventId = @id", ("@id", eventId)));
    }

    [Fact(DisplayName = "Quatre annulations simultanées : un seul 200, les autres 409 reservation_state_changed (CancelledByUser), place libérée une fois")]
    public async Task Simultaneous_cancellations_release_the_seat_once()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 3);
        var tokens = await MemberTokensAsync(2);
        await RequestSeatOkAsync(tokens[1], eventId); // une autre demande, qui doit rester
        var id = (await RequestSeatOkAsync(tokens[0], eventId)).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CancelAsync(tokens[0], id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var conflicts = responses.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();
        Assert.Equal(3, conflicts.Count);
        foreach (var conflict in conflicts)
        {
            var problem = await ReadJsonAsync(conflict);
            Assert.Equal("reservation_state_changed", problem.GetProperty("code").GetString());
            Assert.Equal("CancelledByUser", problem.GetProperty("currentStatus").GetString());
        }

        Assert.Equal(2, await RemainingSeatsAsync(eventId)); // 3 places − 1 demande restante
        Assert.Equal(1, await ActiveCountInDatabaseAsync(eventId));
    }

    // ---- no-store sur toutes les réponses de /api/me et /api/admin ----

    [Fact(DisplayName = "no-store sur 200, 201, 204, 400, 401, 403, 404, 409 de /api/me et /api/admin (casse du chemin comprise) ; pas sur l'API publique")]
    public async Task No_store_on_every_personal_response()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, capacity: 1);
        var draftId = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        var tokens = await MemberTokensAsync(2);
        var created = await RequestSeatAsync(tokens[0], eventId);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var responses = new Dictionary<string, HttpResponseMessage>
        {
            ["201 demande"] = created,
            ["200 liste"] = await SendAsync(HttpMethod.Get, "/api/me/reservations", tokens[0]),
            ["200 détail"] = await SendAsync(HttpMethod.Get, $"/api/me/reservations/{id}", tokens[0]),
            ["200 ma demande"] = await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", tokens[0]),
            ["204 ma demande"] = await SendAsync(HttpMethod.Get, $"/api/me/events/{eventId}/reservation", tokens[1]),
            ["400 eventId absent"] = await SendAsync(HttpMethod.Post, "/api/me/reservations", tokens[0], new { }),
            ["400 scope"] = await SendAsync(HttpMethod.Get, "/api/me/reservations?scope=abc", tokens[0]),
            ["401 visiteur liste"] = await SendAsync(HttpMethod.Get, "/api/me/reservations", null),
            ["401 visiteur demande"] = await SendAsync(HttpMethod.Post, "/api/me/reservations", null, new { eventId }),
            ["401 visiteur annulation"] = await SendAsync(HttpMethod.Post, $"/api/me/reservations/{id}/cancel", null),
            ["401 jeton invalide"] = await SendAsync(HttpMethod.Get, "/api/me/reservations", "jeton.invalide.xyz"),
            ["401 casse"] = await SendAsync(HttpMethod.Get, "/API/ME/reservations", null),
            ["401 admin visiteur"] = await SendAsync(HttpMethod.Get, "/api/admin/events", null),
            ["403 membre sur admin"] = await SendAsync(HttpMethod.Get, "/api/admin/events", tokens[0]),
            ["403 membre publie"] = await SendAsync(HttpMethod.Post, $"/api/admin/events/{draftId}/publish", tokens[0]),
            ["404 réservation inconnue"] = await SendAsync(HttpMethod.Get, $"/api/me/reservations/{Guid.NewGuid()}", tokens[0]),
            ["404 réservation d'un autre"] = await SendAsync(HttpMethod.Post, $"/api/me/reservations/{id}/cancel", tokens[1]),
            ["404 identifiant mal formé"] = await SendAsync(HttpMethod.Get, "/api/me/reservations/abc", tokens[0]),
            ["404 brouillon"] = await SendAsync(HttpMethod.Post, "/api/me/reservations", tokens[0], new { eventId = draftId }),
            ["404 route inconnue"] = await SendAsync(HttpMethod.Get, "/api/me/inconnue", tokens[0]),
            ["409 complet"] = await RequestSeatAsync(tokens[1], eventId),
            ["409 déjà inscrit"] = await RequestSeatAsync(tokens[0], eventId),
        };

        foreach (var (label, response) in responses)
        {
            Assert.True(response.Headers.CacheControl?.NoStore == true, $"« {label} » ({(int)response.StatusCode}) sans Cache-Control: no-store");
        }

        Assert.Equal(HttpStatusCode.Created, responses["201 demande"].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, responses["204 ma demande"].StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, responses["403 membre sur admin"].StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, responses["403 membre publie"].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, responses["401 casse"].StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, responses["401 jeton invalide"].StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, responses["404 route inconnue"].StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, responses["409 complet"].StatusCode);
        Assert.Equal("event_full", await CodeAsync(responses["409 complet"]));

        var publicResponse = await SendAsync(HttpMethod.Get, $"/api/events/{eventId}", null);
        Assert.NotEqual(true, publicResponse.Headers.CacheControl?.NoStore);
    }

    // ---- Journaux ----

    [Fact(DisplayName = "Journaux : ni e-mail, téléphone, nom, ni jeton, sur les chemins d'erreur (401, 400, 404, 409 clos, annulation tardive)")]
    public async Task Error_paths_log_no_personal_data()
    {
        var admin = await AdminTokenAsync();
        var eventId = await PublishedEventAsync(admin, startsIn: TimeSpan.FromDays(4));
        var startsAt = await StartsAtAsync(eventId);
        var draftId = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();
        var (email, member, _) = await RegisterMemberAsync();
        var (otherEmail, other, _) = await RegisterMemberAsync();
        var id = (await RequestSeatOkAsync(member, eventId)).GetProperty("id").GetGuid();
        await ConfirmInDatabaseAsync(id);

        await SendAsync(HttpMethod.Get, "/api/me/reservations", null);
        await SendAsync(HttpMethod.Get, "/api/me/reservations", member + "x");
        await SendAsync(HttpMethod.Post, "/api/me/reservations", member, new { });
        await SendAsync(HttpMethod.Get, "/api/me/reservations/abc", member);
        await RequestSeatAsync(member, draftId);
        await CancelAsync(other, id);
        Clock.SetUtcNow(startsAt.AddHours(-10));
        Assert.Equal(HttpStatusCode.Conflict, (await CancelAsync(member, id)).StatusCode); // délai dépassé
        Clock.SetUtcNow(startsAt.AddHours(1));
        Assert.Equal(HttpStatusCode.Conflict, (await RequestSeatAsync(other, eventId)).StatusCode); // clos
        await SendAsync(HttpMethod.Get, "/api/admin/events", member); // 403

        var messages = Factory.Logs.Messages;
        Assert.NotEmpty(messages);
        foreach (var forbidden in new[] { email, otherEmail, member, other, "+21620123456", "20 123 456", "Ben Ali", "Salma" })
        {
            Assert.DoesNotContain(messages, m => m.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
