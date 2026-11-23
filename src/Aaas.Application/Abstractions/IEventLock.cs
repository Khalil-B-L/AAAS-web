using Aaas.Domain.Events;

namespace Aaas.Application.Abstractions;

/// <summary>
/// Exécute <c>work</c> sur l'événement verrouillé (UPDLOCK) dans une transaction, puis enregistre et valide (ADR 0004, 0012).
/// À utiliser pour toute opération qui dépend du nombre de places occupées : modification (R11, R12), dépublication (R14),
/// et plus tard demande de place (étape 4), annulation et suppression (étape 7).
/// L'entité passée à <c>work</c> est suivie : ses modifications sont enregistrées après <c>work</c>.
/// Événement absent : NotFoundException. <c>work</c> peut être rejoué (erreur transitoire) : il ne doit pas avoir
/// d'effet de bord hors de la base.
/// Seules les modifications faites dans <c>work</c> sont garanties : en cas de rejeu, le suivi des entités est
/// vidé avant la nouvelle tentative, donc une modification faite avant l'appel (entité déjà suivie) peut être perdue.
/// </summary>
public interface IEventLock
{
    Task<TResult> ExecuteWithLockedEventAsync<TResult>(
        Guid eventId,
        Func<Event, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken);
}
