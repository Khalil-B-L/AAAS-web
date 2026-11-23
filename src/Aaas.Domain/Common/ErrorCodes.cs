namespace Aaas.Domain.Common;

/// <summary>
/// Codes d'erreur stables renvoyés dans l'extension <c>code</c> des ProblemDetails.
/// Le front s'appuie dessus : ne jamais renommer un code existant.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidPhoneNumber = "invalid_phone_number";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string InternalError = "internal_error";

    /// <summary>405 : méthode HTTP non prise en charge par la route (refus du framework).</summary>
    public const string MethodNotAllowed = "method_not_allowed";

    /// <summary>415 : corps sans en-tête Content-Type JSON (refus du framework avant la lecture du corps).</summary>
    public const string UnsupportedMediaType = "unsupported_media_type";

    // Étape 2 : événements et paramètres (tous en 409, sauf l'avertissement contact_not_configured).

    /// <summary>La fiche a été modifiée par un autre admin depuis sa lecture (version périmée, ADR 0006).</summary>
    public const string ConcurrentUpdate = "concurrent_update";

    /// <summary>Transition impossible depuis le statut actuel (publier un publié, dépublier un brouillon).</summary>
    public const string EventStateChanged = "event_state_changed";

    public const string EventCancelled = "event_cancelled";
    public const string EventStarted = "event_started";
    public const string EventEnded = "event_ended";

    /// <summary>R14 : dépublication refusée tant qu'il reste des demandes en attente ou des réservations confirmées.</summary>
    public const string EventHasActiveReservations = "event_has_active_reservations";

    /// <summary>R11 : capacité inférieure aux places déjà occupées.</summary>
    public const string CapacityBelowReserved = "capacity_below_reserved";

    /// <summary>R12 : changement de date ou de prix avec des inscrits, à confirmer par l'admin.</summary>
    public const string RegistrantsWarning = "registrants_warning";

    /// <summary>R15 (étape 2) : seul un brouillon jamais publié peut être supprimé.</summary>
    public const string EventAlreadyPublished = "event_already_published";

    /// <summary>Avertissement non bloquant (pas une erreur) renvoyé avec la publication (US-E8).</summary>
    public const string ContactNotConfigured = "contact_not_configured";
}
