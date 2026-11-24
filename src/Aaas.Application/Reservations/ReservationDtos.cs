using System.ComponentModel.DataAnnotations;
using Aaas.Application.Events;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;

namespace Aaas.Application.Reservations;

// ---------- Entrées ----------

/// <summary>Demande de place (US-D1). L'identité du membre vient du jeton, jamais du corps (ADR 0021).</summary>
public sealed class CreateReservationRequest
{
    // Guid? + [Required] : un champ absent donne une erreur française sur « eventId » (et non un Guid.Empty silencieux).
    [Required(ErrorMessage = "L'événement est obligatoire.")]
    public Guid? EventId { get; init; }
}

/// <summary>
/// Refus d'une demande (US-E6). Motif facultatif ; vide ou blanc → null. Longueur contrôlée APRÈS suppression des
/// espaces par le service (400 sur « reason »), pour ne pas refuser 500 caractères entourés d'espaces.
/// </summary>
public sealed class RefuseReservationRequest
{
    public string? Reason { get; init; }
}

/// <summary>Demandes à traiter (US-E6) : pagination seule, tri imposé (échéance croissante).</summary>
public sealed class PendingReservationListQuery : PagingQuery;

public enum MyReservationScope
{
    /// <summary>Actives et événement non terminé, par début croissant.</summary>
    Current,

    /// <summary>Tout le reste (terminées, refusées, expirées, annulées), par début décroissant.</summary>
    History,
}

public sealed class MyReservationListQuery : PagingQuery
{
    public MyReservationScope Scope { get; init; } = MyReservationScope.Current;
}

// ---------- Sorties ----------

/// <summary>Événement vu depuis une réservation. IsVisible faux : repassé en brouillon, le front n'affiche pas de lien.</summary>
public sealed record ReservationEventDto(
    Guid Id,
    string Title,
    string Location,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal? Price,
    EventStatus Status,
    bool HasStarted,
    bool HasEnded,
    bool IsVisible);

/// <summary>
/// Réservation du membre connecté (plan § 7.1). Status = statut EFFECTIF (une demande échue est « Expired »
/// même si la base dit encore Pending, ADR 0005). ExpiresAt n'est renseignée que pour une demande en attente.
/// </summary>
public sealed record MyReservationDto(
    Guid Id,
    ReservationStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? RefusalReason,
    DateTimeOffset? CancelledAt,
    bool IsPaid,
    DateTimeOffset? PaidAt,
    bool CanCancel,
    DateTimeOffset? CancellableUntil,
    ReservationEventDto Event);

// ---------- Administration (US-E4 à E7, ADR 0021) ----------

/// <summary>Coordonnées du membre (RG-15) ; null dans <see cref="AdminReservationDto"/> = « Compte supprimé ».</summary>
public sealed record ReservationMemberDto(Guid Id, string FirstName, string LastName, string Email, string PhoneNumber);

/// <summary>Admin qui a posé « Payée » (R13) ; null alors que IsPaid = vrai → « administrateur supprimé ».</summary>
public sealed record ReservationPaidByDto(string FirstName, string LastName);

public sealed record AdminReservationEventDto(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    decimal? Price,
    EventStatus Status);

/// <summary>Boutons à proposer, calculés par le Domain (le serveur revérifie à chaque action).</summary>
public sealed record AdminReservationActionsDto(bool CanConfirm, bool CanRefuse, bool CanCancel, bool CanMarkPaid, bool CanUnmarkPaid);

/// <summary>
/// Réservation vue par un administrateur (plan § 7.2). Status = statut EFFECTIF ; ExpiresAt seulement si En attente.
/// Ni canCancel ni cancellableUntil du membre : l'admin a ses propres règles (Actions).
/// </summary>
public sealed record AdminReservationDto(
    Guid Id,
    ReservationStatus Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DecidedAt,
    string? RefusalReason,
    DateTimeOffset? CancelledAt,
    bool IsPaid,
    DateTimeOffset? PaidAt,
    ReservationPaidByDto? PaidBy,
    ReservationMemberDto? Member,
    AdminReservationEventDto Event,
    AdminReservationActionsDto Actions);

public sealed record RegistrantsEventDto(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Capacity,
    decimal? Price,
    EventStatus Status,
    bool HasStarted,
    bool HasEnded);

/// <summary>Places : « 15 / 30 places occupées (12 confirmées, 3 en attente) ». Remaining ≥ 0.</summary>
public sealed record RegistrantsOccupancyDto(int Pending, int Confirmed, int Active, int Remaining);

/// <summary>
/// Inscrits d'un événement (US-E4), non paginé (P-R7). Pending : la plus ancienne d'abord ; Confirmed : nom puis
/// prénom (comptes supprimés à la fin) ; Others (refusées, expirées, annulées) : la plus récente demande d'abord.
/// </summary>
public sealed record EventRegistrantsDto(
    RegistrantsEventDto Event,
    RegistrantsOccupancyDto Occupancy,
    IReadOnlyList<AdminReservationDto> Pending,
    IReadOnlyList<AdminReservationDto> Confirmed,
    IReadOnlyList<AdminReservationDto> Others);
