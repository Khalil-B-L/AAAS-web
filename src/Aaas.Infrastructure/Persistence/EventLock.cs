using System.Data;
using Aaas.Application.Abstractions;
using Aaas.Application.Common.Errors;
using Aaas.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Infrastructure.Persistence;

/// <summary>
/// Verrou pessimiste sur la ligne d'un événement (ADR 0004, 0012) : seul endroit avec du SQL propre à SQL Server
/// et de la plomberie de transaction. Application n'en voit que le port IEventLock.
/// </summary>
internal sealed class EventLock(AppDbContext db) : IEventLock
{
    public Task<TResult> ExecuteWithLockedEventAsync<TResult>(
        Guid eventId, Func<Event, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        // Avec EnableRetryOnFailure, une transaction ouverte à la main doit passer par la stratégie d'exécution :
        // en cas d'erreur transitoire, c'est tout le bloc (transaction comprise) qui est rejoué, pas une requête isolée.
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;
        return strategy.ExecuteAsync(
            (EventId: eventId, Work: work),
            async (_, state, ct) =>
            {
                // Une tentative rejouée ne doit pas réutiliser les entités suivies (et modifiées) de la précédente.
                // Seulement au rejeu : à la première tentative, on ne jette pas ce que l'appelant suit déjà.
                if (attempt++ > 0)
                {
                    db.ChangeTracker.Clear();
                }

                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

                // UPDLOCK : réserve la ligne jusqu'au COMMIT ; une autre transaction qui demande le même verrou attend.
                // Les lectures ordinaires (listes, détail) ne sont pas bloquées. Même avec READ_COMMITTED_SNAPSHOT
                // (Azure SQL), un SELECT avec UPDLOCK lit la dernière version validée, pas un instantané.
                // ROWLOCK : verrou de ligne, pas de page. {state.EventId} devient un paramètre SQL (pas d'injection).
                var ev = await LockedEventQuery(db, state.EventId).SingleOrDefaultAsync(ct)
                    ?? throw new NotFoundException("Événement introuvable.");

                var result = await state.Work(ev, ct);

                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            verifySucceeded: null,
            cancellationToken);
    }

    /// <summary>
    /// Requête de verrouillage, isolée pour qu'un test vérifie le SQL produit (ToQueryString) : EF place la requête
    /// brute en sous-requête, et l'indicateur WITH (UPDLOCK, ROWLOCK) doit y rester.
    /// </summary>
    internal static IQueryable<Event> LockedEventQuery(AppDbContext db, Guid eventId) =>
        db.Events
            .FromSql($"SELECT * FROM [Events] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {eventId}")
            .AsTracking();
}
