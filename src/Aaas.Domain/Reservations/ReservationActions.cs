namespace Aaas.Domain.Reservations;

/// <summary>Actions proposées à l'administrateur pour une réservation, calculées par <see cref="Reservation.AdminActions"/>.</summary>
public sealed record ReservationActions(
    bool CanConfirm,
    bool CanRefuse,
    bool CanCancel,
    bool CanMarkPaid,
    bool CanUnmarkPaid);
