namespace Aaas.Domain.Events;

/// <summary>
/// Statut stocké d'un événement (tinyint). Valeurs figées : elles sont en base et dans une contrainte CHECK.
/// « Commencé » et « terminé » ne sont pas des statuts : ils sont calculés à partir de l'heure courante (ADR 0009).
/// </summary>
public enum EventStatus : byte
{
    Draft = 1,
    Published = 2,
    Cancelled = 3,
}
