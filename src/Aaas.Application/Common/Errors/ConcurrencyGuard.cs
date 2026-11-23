using Aaas.Application.Abstractions;
using Aaas.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.Common.Errors;

/// <summary>Concurrence optimiste entre admins (ADR 0006) : une version périmée donne 409 concurrent_update.</summary>
public static class ConcurrencyGuard
{
    public const string ConflictMessage =
        "Cette fiche a été modifiée par un autre administrateur entre-temps. Rechargez-la, puis refaites vos changements.";

    public static BusinessRuleException Conflict() => new(ErrorCodes.ConcurrentUpdate, ConflictMessage);

    /// <summary>Compare la version lue en base à celle envoyée par le client.</summary>
    public static void EnsureSameVersion(byte[] current, byte[] expected)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!current.AsSpan().SequenceEqual(expected))
        {
            throw Conflict();
        }
    }

    /// <summary>
    /// SaveChanges avec le filet d'EF : la colonne rowversion est dans le WHERE de l'UPDATE/DELETE ;
    /// si une autre écriture est passée entre notre lecture et notre enregistrement, 0 ligne est touchée
    /// et EF lève DbUpdateConcurrencyException, traduite en 409.
    /// </summary>
    public static async Task SaveChangesOrConflictAsync(this IAppDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Pas de journalisation : conflit attendu, et le message EF ne contient rien d'utile au client.
            throw Conflict();
        }
    }
}
