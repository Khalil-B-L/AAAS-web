namespace Aaas.Domain.Reservations;

/// <summary>
/// Statut stocké d'une demande de place (tinyint, ADR 0018). Valeurs FIGÉES : elles sont dans une contrainte CHECK
/// et dans le filtre de l'index unique UX_Reservations_Event_User_Active (« Status IN (1, 2) ») ; changer une valeur
/// casserait l'index sans erreur de compilation.
/// </summary>
public enum ReservationStatus : byte
{
    Pending = 1,
    Confirmed = 2,
    Refused = 3,
    Expired = 4,
    CancelledByUser = 5,
    CancelledByAssociation = 6,
}
