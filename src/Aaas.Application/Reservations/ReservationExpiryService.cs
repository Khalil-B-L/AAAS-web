using Aaas.Application.Abstractions;
using Aaas.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.Reservations;

/// <summary>
/// Passage global de la tâche de fond (ADR 0020). Interface pour que les tests du job puissent la doubler
/// (simuler une exception SQL, compter les appels) sans base.
/// </summary>
public interface IExpireDueReservations
{
    /// <summary>Écrit « Expirée » sur toutes les demandes En attente échues ; renvoie le nombre de lignes.</summary>
    Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Écrit « Expirée » sur les demandes échues (ADR 0005 § 4-5). L'exactitude ne dépend PAS de ce service
/// (les décomptes filtrent déjà <see cref="ReservationQueries.IsActive"/>) : il range la base et libère l'index
/// unique filtré, pour qu'un membre puisse refaire une demande après l'expiration de la précédente.
/// </summary>
public sealed class ReservationExpiryService(IAppDbContext db) : IExpireDueReservations
{
    /// <summary>
    /// Sous le verrou de l'événement, avant le décompte d'une nouvelle demande. Un seul UPDATE (ExecuteUpdate),
    /// idempotent grâce à « Status = Pending » : le rejouer (stratégie d'exécution) ne change rien de plus.
    /// La rowversion des lignes touchées change aussi : une transition concurrente sur l'une d'elles échouera en 409.
    /// </summary>
    public Task<int> ExpireDueForEventAsync(Guid eventId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Reservations
            .Where(r => r.EventId == eventId && r.Status == ReservationStatus.Pending && r.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, ReservationStatus.Expired), cancellationToken);

    /// <summary>
    /// Tâche de fond : UPDATE Reservations SET Status = 4 WHERE Status = 1 AND ExpiresAt &lt;= @now (index
    /// IX_Reservations_Status_ExpiresAt). Sans verrou d'événement : la condition « Status = Pending » suffit.
    /// Rejouée, ou lancée par deux instances de l'API en même temps, elle ne touche chaque ligne qu'une fois ;
    /// une confirmation concurrente sur une ligne touchée échoue en 409 (rowversion), comme ci-dessus.
    /// </summary>
    public Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Reservations
            .Where(r => r.Status == ReservationStatus.Pending && r.ExpiresAt <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Status, ReservationStatus.Expired), cancellationToken);
}
