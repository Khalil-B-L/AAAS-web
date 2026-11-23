using Aaas.Domain.Common;

namespace Aaas.Domain.Events;

/// <summary>
/// Événement de l'association (US-E2, US-C1, US-C2). Toutes les règles reçoivent <c>now</c> et l'occupation
/// en paramètre : aucune ne lit l'horloge ni la base (ADR 0003, 0012).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1716:Identifiers should not match keywords",
    Justification = "« Event » n'est réservé qu'en VB.NET ; le projet est uniquement en C# et c'est le terme métier.")]
public sealed class Event
{
    public const string StartedMessage = "Cet événement a déjà commencé : il ne peut plus être publié.";
    public const string EndedMessage = "Cet événement est terminé : il ne peut plus être modifié.";
    public const string CancelledMessage = "Cet événement est annulé : il ne peut plus être modifié ni publié.";
    public const string AlreadyPublishedStateMessage = "Cet événement est déjà publié.";
    public const string NotPublishedStateMessage = "Cet événement n'est pas publié.";
    public const string HasActiveReservationsMessage =
        "Cet événement a des demandes en attente ou des réservations confirmées : il ne peut pas être dépublié. Annulez-le à la place.";
    public const string CannotDeletePublishedMessage =
        "Cet événement a déjà été publié : il ne peut pas être supprimé. Seul un brouillon jamais publié peut l'être ; dépubliez-le ou annulez-le à la place.";

    private Event()
    {
        // Pour EF Core.
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public int Capacity { get; private set; }

    /// <summary>En TND, au plus 3 décimales ; null = gratuit (jamais 0, ADR 0010).</summary>
    public decimal? Price { get; private set; }

    public EventStatus Status { get; private set; }

    /// <summary>Dernière publication ; null tant que l'événement est en brouillon (remis à null par la dépublication).</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>
    /// Première publication, jamais effacée. Distingue « brouillon jamais publié » (supprimable, R15 étape 2)
    /// de « publié puis dépublié » (des membres ont pu le voir, et à partir de l'étape 4 y réserver).
    /// </summary>
    public DateTimeOffset? FirstPublishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>rowversion SQL Server, renseignée par la base à chaque écriture (concurrence entre admins, ADR 0006).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public bool IsFree => Price is null;

    /// <summary>Seul un brouillon jamais publié peut être supprimé (R15 à l'étape 2, RG-07).</summary>
    public bool CanBeDeleted => EventRules.CanBeDeleted(Status, FirstPublishedAt);

    public static Event Create(EventDetails details, DateTimeOffset now)
    {
        ThrowIfInvalid(EventRules.Validate(details, previousStartsAt: null, now));

        var created = new Event
        {
            Status = EventStatus.Draft,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime(),
        };
        created.Apply(details);
        return created;
    }

    public bool HasStarted(DateTimeOffset now) => StartsAt <= now;

    public bool HasEnded(DateTimeOffset now) => EndsAt <= now;

    public int RemainingSeats(EventOccupancy occupancy) => EventRules.RemainingSeats(Capacity, occupancy);

    public bool IsFull(EventOccupancy occupancy) => EventRules.IsFull(Capacity, occupancy);

    public bool IsBookable(DateTimeOffset now, EventOccupancy occupancy) =>
        EventRules.IsBookable(Status, StartsAt, Capacity, now, occupancy);

    /// <summary>R12 : des inscrits, et la date (début ou fin) ou le prix change. Le lieu n'en fait pas partie (Q21).</summary>
    public bool RequiresRegistrantsWarning(EventDetails details, EventOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(details);
        return occupancy.Active > 0
            && (details.StartsAt != StartsAt
                || details.EndsAt != EndsAt
                || EventRules.NormalizePrice(details.Price) != Price);
    }

    /// <summary>R28 : annulé ou terminé → plus modifiable. Commencé mais pas terminé → modifiable.</summary>
    public void EnsureCanBeEdited(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException(ErrorCodes.EventCancelled, CancelledMessage);
        }

        if (HasEnded(now))
        {
            throw new BusinessRuleException(ErrorCodes.EventEnded, EndedMessage);
        }
    }

    /// <summary>
    /// Ordre des contrôles : état (annulé, terminé), valeurs (filet : déjà vérifiées par Application en 400),
    /// R11 capacité, puis R12 avertissement. Une capacité trop basse est signalée même si l'avertissement est confirmé.
    /// </summary>
    public void Update(EventDetails details, DateTimeOffset now, EventOccupancy occupancy, bool confirmRegistrantsWarning)
    {
        ArgumentNullException.ThrowIfNull(details);
        EnsureCanBeEdited(now);
        ThrowIfInvalid(EventRules.Validate(details, previousStartsAt: StartsAt, now));

        if (details.Capacity < occupancy.Active)
        {
            throw new BusinessRuleException(
                ErrorCodes.CapacityBelowReserved,
                $"{occupancy.Active} places sont déjà occupées ({occupancy.Confirmed} confirmées, {occupancy.Pending} en attente). " +
                "La capacité ne peut pas être inférieure.");
        }

        if (!confirmRegistrantsWarning && RequiresRegistrantsWarning(details, occupancy))
        {
            throw new BusinessRuleException(
                ErrorCodes.RegistrantsWarning,
                $"Cet événement a {occupancy.Active} inscrits (demandes en attente et réservations confirmées). " +
                "Pensez à les prévenir de ce changement. Confirmez pour enregistrer.");
        }

        Apply(details);
        UpdatedAt = now.ToUniversalTime();
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException(ErrorCodes.EventCancelled, CancelledMessage);
        }

        if (Status != EventStatus.Draft)
        {
            throw new BusinessRuleException(ErrorCodes.EventStateChanged, AlreadyPublishedStateMessage);
        }

        if (HasStarted(now))
        {
            throw new BusinessRuleException(ErrorCodes.EventStarted, StartedMessage);
        }

        var utcNow = now.ToUniversalTime();
        Status = EventStatus.Published;
        PublishedAt = utcNow;
        FirstPublishedAt ??= utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>R14 : seulement sans demande en attente ni réservation confirmée.</summary>
    public void Unpublish(DateTimeOffset now, EventOccupancy occupancy)
    {
        if (Status == EventStatus.Cancelled)
        {
            throw new BusinessRuleException(ErrorCodes.EventCancelled, CancelledMessage);
        }

        if (Status != EventStatus.Published)
        {
            throw new BusinessRuleException(ErrorCodes.EventStateChanged, NotPublishedStateMessage);
        }

        if (occupancy.Active > 0)
        {
            throw new BusinessRuleException(ErrorCodes.EventHasActiveReservations, HasActiveReservationsMessage);
        }

        Status = EventStatus.Draft;
        PublishedAt = null; // FirstPublishedAt est conservé : l'événement reste « déjà publié »
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>
    /// R15 à l'étape 2 : un brouillon jamais publié n'a jamais pu recevoir de réservation, il peut donc disparaître.
    /// L'étape 7 élargira la règle (« aucune réservation, jamais », Q29).
    /// </summary>
    public void EnsureCanBeDeleted()
    {
        if (!CanBeDeleted)
        {
            throw new BusinessRuleException(ErrorCodes.EventAlreadyPublished, CannotDeletePublishedMessage);
        }
    }

    private void Apply(EventDetails details)
    {
        Title = details.Title.Trim();
        Description = details.Description.Trim();
        Location = details.Location.Trim();
        StartsAt = details.StartsAt.ToUniversalTime();
        EndsAt = details.EndsAt.ToUniversalTime();
        Capacity = details.Capacity;
        Price = EventRules.NormalizePrice(details.Price);
    }

    private static void ThrowIfInvalid(IReadOnlyList<FieldError> errors)
    {
        // Filet : Application valide d'abord (400 par champ, avec la même EventRules.Validate et la même heure).
        // Arriver ici signale un appel sans validation préalable, donc un bug de code, pas une saisie à corriger.
        // C'est pourquoi on garde BusinessRuleException (le Domain ne connaît pas la validation de requête
        // d'Application) : la réponse serait un 409 avec le code validation_failed, et non le 400 par champ.
        // Incohérence assumée : aucun endpoint ne peut la produire, et un test de service le détecterait.
        if (errors.Count > 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.ValidationFailed,
                "Données d'événement invalides : " + string.Join(" ", errors.Select(e => $"{e.Field} : {e.Message}")));
        }
    }
}
