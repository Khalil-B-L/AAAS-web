using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>
/// Règles qui dépendent de l'heure (RG-14, R28, Q28), avec une horloge simulée injectée dans l'API.
/// Le jeton est obtenu avant d'avancer l'horloge : le test ne dépend pas de la durée de vie du jeton.
/// </summary>
public sealed class EventsClockTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
    private FakeTimeProvider? _clock;

    protected override TimeProvider? ClockOverride => _clock ??= new FakeTimeProvider(_start);

    private FakeTimeProvider Clock => (FakeTimeProvider)ClockOverride!;

    [Fact(DisplayName = "Événement terminé : absent de la liste publique, détail 200 avec hasEnded, plus modifiable (event_ended)")]
    public async Task Ended_event_is_hidden_from_list_but_visible_in_detail_and_not_editable()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(2)));
        var version = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin))).GetProperty("version").GetString();
        Assert.Equal(1, await PublicCountAsync());

        Clock.SetUtcNow(begin.AddHours(3));

        Assert.Equal(0, await PublicCountAsync());
        var detail = await SendAsync(HttpMethod.Get, $"/api/events/{id}", null);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var json = await ReadJsonAsync(detail);
        Assert.True(json.GetProperty("hasEnded").GetBoolean());
        Assert.True(json.GetProperty("hasStarted").GetBoolean());
        Assert.False(json.GetProperty("isBookable").GetBoolean());
        Assert.Equal("Published", json.GetProperty("status").GetString());

        var update = await SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin,
            EventBodyAt(begin, begin.AddHours(4), version: version));
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        Assert.Equal("event_ended", (await ReadJsonAsync(update)).GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Limite : un événement est terminé exactement à sa fin (EndsAt <= maintenant), pas une seconde avant")]
    public async Task Event_ends_exactly_at_its_end_instant()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(2)));
        var endsAt = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin))).GetProperty("endsAt").GetDateTimeOffset();

        Clock.SetUtcNow(endsAt.AddSeconds(-1));
        Assert.Equal(1, await PublicCountAsync());

        Clock.SetUtcNow(endsAt);
        Assert.Equal(0, await PublicCountAsync());
        Assert.True((await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/events/{id}", null))).GetProperty("hasEnded").GetBoolean());
    }

    [Fact(DisplayName = "Événement en cours : listé, hasStarted vrai, hasEnded faux, non réservable ; modifiable sans déplacer le début")]
    public async Task Started_event_is_listed_not_bookable_and_still_editable()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(2)));
        var admin0 = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin));
        var startsAt = admin0.GetProperty("startsAt").GetDateTimeOffset();
        var endsAt = admin0.GetProperty("endsAt").GetDateTimeOffset();

        Clock.SetUtcNow(startsAt.AddHours(1));

        var item = (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/events", null))).GetProperty("items").EnumerateArray().Single();
        Assert.True(item.GetProperty("hasStarted").GetBoolean());
        Assert.False(item.GetProperty("hasEnded").GetBoolean());
        Assert.False(item.GetProperty("isBookable").GetBoolean());

        // Début inchangé, fin repoussée dans le futur : accepté (R28).
        var update = await SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin,
            EventBodyAt(startsAt, endsAt.AddHours(1), title: "Titre modifié", version: admin0.GetProperty("version").GetString()));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Titre modifié", (await ReadJsonAsync(update)).GetProperty("title").GetString());
    }

    [Fact(DisplayName = "R28/Q28 : événement commencé, début inchangé, fin déjà passée → 400 errors.endsAt")]
    public async Task Started_event_with_past_end_is_rejected_on_endsAt()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(4)));
        var admin0 = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin));
        var startsAt = admin0.GetProperty("startsAt").GetDateTimeOffset();

        Clock.SetUtcNow(startsAt.AddHours(2));

        // Fin = début + 1 h : après le début, mais déjà passée à « maintenant » (début + 2 h).
        var response = await SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin,
            EventBodyAt(startsAt, startsAt.AddHours(1), version: admin0.GetProperty("version").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        Assert.Equal("La date de fin doit être dans le futur.", errors.GetProperty("endsAt")[0].GetString());
        Assert.False(errors.TryGetProperty("startsAt", out _)); // début inchangé : pas d'erreur sur le début
    }

    [Fact(DisplayName = "Événement commencé : déplacer le début dans le passé → 400 errors.startsAt")]
    public async Task Started_event_moved_start_in_the_past_is_rejected()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(4)));
        var admin0 = await ReadJsonAsync(await SendAsync(HttpMethod.Get, $"/api/admin/events/{id}", admin));
        var startsAt = admin0.GetProperty("startsAt").GetDateTimeOffset();

        Clock.SetUtcNow(startsAt.AddHours(1));

        var response = await SendAsync(HttpMethod.Put, $"/api/admin/events/{id}", admin,
            EventBodyAt(startsAt.AddMinutes(10), startsAt.AddHours(3), version: admin0.GetProperty("version").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("startsAt", out _));
    }

    [Fact(DisplayName = "Publier un brouillon dont le début est passé → 409 event_started")]
    public async Task Publishing_a_started_draft_is_refused()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = (await CreateEventAsync(admin, EventBodyAt(begin, begin.AddHours(2)))).GetProperty("id").GetGuid();

        Clock.SetUtcNow(begin.AddMinutes(30));

        var response = await SendAsync(HttpMethod.Post, $"/api/admin/events/{id}/publish", admin);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("event_started", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Liste admin : un événement terminé quitte « à venir » et apparaît dans period=past avec hasEnded")]
    public async Task Admin_list_moves_ended_event_to_past()
    {
        var admin = await AdminTokenAsync();
        var begin = _start.AddDays(1);
        var id = await CreatePublishedEventAsync(admin, EventBodyAt(begin, begin.AddHours(2)));

        Clock.SetUtcNow(begin.AddHours(5));

        var upcoming = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/events", admin));
        Assert.Equal(0, upcoming.GetProperty("totalCount").GetInt32());
        var past = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/admin/events?period=past", admin));
        var item = past.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.True(item.GetProperty("hasEnded").GetBoolean());
        Assert.True(item.GetProperty("hasStarted").GetBoolean());
    }

    private async Task<int> PublicCountAsync() =>
        (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/events", null))).GetProperty("totalCount").GetInt32();
}
