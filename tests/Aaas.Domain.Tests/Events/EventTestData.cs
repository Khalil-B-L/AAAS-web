using Aaas.Domain.Events;

namespace Aaas.Domain.Tests.Events;

/// <summary>Instants fixes : aucune horloge réelle dans les tests du Domain.</summary>
internal static class EventTestData
{
    /// <summary>1er décembre 2026, 10 h à Tunis = 09:00Z.</summary>
    public static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>5 décembre 2026, 10 h à Tunis (saisi avec le décalage +01:00) = 09:00Z.</summary>
    public static readonly DateTimeOffset Start = new(2026, 12, 5, 10, 0, 0, TimeSpan.FromHours(1));

    public static EventDetails Details(
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        int capacity = 20,
        decimal? price = null,
        string title = "Atelier couture",
        string description = "Initiation, matériel fourni.",
        string location = "Local de l'association, Tunis")
    {
        var start = startsAt ?? Start;
        return new EventDetails(title, description, location, start, endsAt ?? start.AddHours(2), capacity, price);
    }

    public static Event Draft(EventDetails? details = null) => Event.Create(details ?? Details(), Now);

    public static Event Published(EventDetails? details = null)
    {
        var ev = Draft(details);
        ev.Publish(Now);
        return ev;
    }
}
