using Aaas.Domain.Common;

namespace Aaas.Domain.Reservations;

/// <summary>Calculs et messages des réservations (RG-02 à RG-05, ADR 0005, 0018). Aucune lecture d'horloge ni de base.</summary>
public static class ReservationRules
{
    public const int RefusalReasonMaxLength = 500;

    // Messages : docs/fonctionnel/libelles-ecrans.md § 11 (v1.1), repris à l'identique.
    public const string EventFullMessage = "Événement complet";
    public const string ReservationsClosedMessage = "Les réservations sont closes";
    public const string EventCancelledMessage = "Cet événement a été annulé.";
    public const string AlreadyReservedMessage =
        "Vous avez déjà une demande en cours ou une réservation pour cet événement";
    public const string CancellationDeadlinePassedMessage =
        "Le délai d'annulation est dépassé : contactez l'association";
    public const string AlreadyHandledMessage = "Cette demande a déjà été traitée ou annulée.";

    /// <summary>reservation_state_changed vu par le membre (son annulation arrive après l'échéance).</summary>
    public const string ExpiredForMemberMessage = "Votre demande a expiré. Vous pouvez en faire une nouvelle.";

    /// <summary>reservation_state_changed vu par l'admin (confirmer, refuser, annuler une demande échue).</summary>
    public const string ExpiredForAdminMessage = "Cette demande a expiré : le membre doit faire une nouvelle demande.";

    /// <summary>RG-08 : « Payée » posée sur une demande encore en attente (reservation_not_confirmed).</summary>
    public const string NotConfirmedMessage = "Confirmez d'abord la demande avant de la marquer payée";

    /// <summary>RG-08 : événement gratuit (payment_not_applicable).</summary>
    public const string PaymentNotApplicableMessage = "Cet événement est gratuit : aucun paiement à enregistrer";

    /// <summary>US-E5 : annulation par l'association après la fin de l'événement (event_ended).</summary>
    public const string EventEndedForCancellationMessage = "Cet événement est terminé : la réservation ne peut plus être annulée.";

    public const string RefusalReasonTooLongMessage = "Le motif ne doit pas dépasser 500 caractères.";

    /// <summary>Motif de refus : espaces retirés, vide → null (« sans motif », US-E6).</summary>
    public static string? NormalizeRefusalReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    /// <summary>
    /// RG-04 : 48 h après la demande, ou au début de l'événement si c'est plus tôt.
    /// Exemple (US-D5) : demande le 10/03 à 10 h, début le 20/03 → 12/03 à 10 h ; début le 11/03 à 9 h → 11/03 à 9 h.
    /// </summary>
    public static DateTimeOffset ComputeExpiresAt(DateTimeOffset requestedAt, TimeSpan pendingLifetime, DateTimeOffset eventStartsAt)
    {
        var byLifetime = requestedAt.ToUniversalTime() + pendingLifetime;
        var start = eventStartsAt.ToUniversalTime();
        return byLifetime <= start ? byLifetime : start;
    }

    /// <summary>RG-05 : une réservation confirmée s'annule jusqu'à « début − délai » inclus (exactement 24 h avant : accepté).</summary>
    public static DateTimeOffset ConfirmedCancellationLimit(DateTimeOffset eventStartsAt, TimeSpan confirmedCancellationDeadline) =>
        eventStartsAt.ToUniversalTime() - confirmedCancellationDeadline;

    /// <summary>
    /// 409 reservation_state_changed avec le statut EFFECTIF relu (extension currentStatus, ADR 0018 § 4) :
    /// levée par le Domain (transition interdite) et par Application (conflit de rowversion, ADR 0019 § 5).
    /// Le message diffère selon qui agit quand la demande a expiré (libelles-ecrans.md § 11).
    /// </summary>
    public static BusinessRuleException StateChanged(ReservationStatus current, bool forMember) => new(
        ErrorCodes.ReservationStateChanged,
        current != ReservationStatus.Expired ? AlreadyHandledMessage
            : forMember ? ExpiredForMemberMessage
            : ExpiredForAdminMessage,
        new Dictionary<string, object?> { [ErrorDetailKeys.CurrentStatus] = current.ToString() });
}
