using System.ComponentModel.DataAnnotations;
using Aaas.Application.Common.Validation;
using Aaas.Domain.Events;

namespace Aaas.Application.Events;

// ---------- Entrées ----------

/// <summary>
/// Champs communs à la création et à la modification. Les dates sont des chaînes avec décalage obligatoire
/// (ADR 0009) ; les règles qui dépendent de l'heure ou de plusieurs champs sont dans le Domain (EventRules).
/// Propriétés initialisées à "" plutôt que « required » : un champ absent donne une erreur française sur ce champ.
/// </summary>
public abstract class EventInput
{
    [Required(ErrorMessage = EventRules.TitleRequiredMessage)]
    [MaxLength(EventRules.TitleMaxLength, ErrorMessage = EventRules.TitleTooLongMessage)]
    public string Title { get; init; } = string.Empty;

    [Required(ErrorMessage = EventRules.DescriptionRequiredMessage)]
    [MaxLength(EventRules.DescriptionMaxLength, ErrorMessage = EventRules.DescriptionTooLongMessage)]
    public string Description { get; init; } = string.Empty;

    [Required(ErrorMessage = EventRules.LocationRequiredMessage)]
    [MaxLength(EventRules.LocationMaxLength, ErrorMessage = EventRules.LocationTooLongMessage)]
    public string Location { get; init; } = string.Empty;

    [Required(ErrorMessage = "La date de début est obligatoire.")]
    [DateTimeWithOffset]
    public string StartsAt { get; init; } = string.Empty;

    [Required(ErrorMessage = "La date de fin est obligatoire.")]
    [DateTimeWithOffset]
    public string EndsAt { get; init; } = string.Empty;

    [Required(ErrorMessage = "La capacité est obligatoire.")]
    public int? Capacity { get; init; }

    /// <summary>Nombre JSON en TND, ou null / absent = gratuit (ADR 0010).</summary>
    public decimal? Price { get; init; }
}

public sealed class CreateEventRequest : EventInput;

public sealed class UpdateEventRequest : EventInput
{
    [Required(ErrorMessage = "La version de la fiche est obligatoire. Rechargez-la.")]
    [RowVersion]
    public string Version { get; init; } = string.Empty;

    /// <summary>R12 : à renvoyer à true après le 409 registrants_warning, une fois l'admin d'accord.</summary>
    public bool ConfirmRegistrantsWarning { get; init; }
}

public enum EventPeriod
{
    /// <summary>Non terminés (EndsAt &gt; maintenant), du plus proche au plus lointain.</summary>
    Upcoming,

    /// <summary>Terminés, du plus récent au plus ancien.</summary>
    Past,

    /// <summary>Tous, du plus récent au plus ancien.</summary>
    All,
}

public class PagingQuery
{
    public const int MaxPage = 10_000;

    // Borne haute : sans elle, (page - 1) * pageSize déborde pour une page énorme (Skip négatif → 500).
    [Range(1, MaxPage, ErrorMessage = "Le numéro de page doit être compris entre 1 et 10 000.")]
    public int Page { get; init; } = 1;

    [Range(1, 50, ErrorMessage = "Le nombre d'éléments par page doit être compris entre 1 et 50.")]
    public int PageSize { get; init; } = 20;
}

public sealed class PublicEventListQuery : PagingQuery;

public sealed class AdminEventListQuery : PagingQuery
{
    public EventStatus? Status { get; init; }

    public EventPeriod Period { get; init; } = EventPeriod.Upcoming;
}

// ---------- Sorties ----------
// Le front choisit la mention publique par priorité (plan § 5.3) :
// status = Cancelled → « Annulé » ; hasEnded → « Terminé » ; hasStarted → « Réservations closes » ;
// isFull → « Complet » ; sinon « N places restantes ».

/// <summary>Ligne de la liste publique (US-C1). Status : Published ou Cancelled. Price null = gratuit.</summary>
public sealed record EventSummaryDto(
    Guid Id,
    string Title,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal? Price,
    int Capacity,
    int RemainingSeats,
    EventStatus Status,
    bool HasStarted,
    bool HasEnded,
    bool IsFull,
    bool IsBookable);

/// <summary>Détail public (US-C2), accessible même terminé (RG-14).</summary>
public sealed record EventDetailDto(
    Guid Id,
    string Title,
    string Description,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal? Price,
    int Capacity,
    int RemainingSeats,
    EventStatus Status,
    bool HasStarted,
    bool HasEnded,
    bool IsFull,
    bool IsBookable);

public sealed record AdminEventSummaryDto(
    Guid Id,
    string Title,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    EventStatus Status,
    int Capacity,
    decimal? Price,
    int PendingCount,
    int ConfirmedCount,
    int RemainingSeats,
    bool HasStarted,
    bool HasEnded,
    bool CanBeDeleted);

/// <summary>Fiche admin, avec la version à renvoyer lors de la modification (ADR 0006).</summary>
public sealed record AdminEventDto(
    Guid Id,
    string Title,
    string Description,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    EventStatus Status,
    int Capacity,
    decimal? Price,
    int PendingCount,
    int ConfirmedCount,
    int RemainingSeats,
    bool HasStarted,
    bool HasEnded,
    bool CanBeDeleted,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? FirstPublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version);

/// <summary>Avertissement non bloquant : l'opération a réussi, mais l'admin doit en être informé.</summary>
public sealed record OperationWarning(string Code, string Message);

public sealed record PublishEventResponse(AdminEventDto Event, IReadOnlyList<OperationWarning> Warnings);
