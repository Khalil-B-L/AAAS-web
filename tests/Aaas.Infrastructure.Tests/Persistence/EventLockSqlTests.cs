using Aaas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Infrastructure.Tests.Persistence;

/// <summary>
/// Risque du plan (§ 15) : EF enveloppe la requête brute dans une sous-requête ; il faut que l'indicateur de verrou
/// y reste. Le test d'intégration « deux PUT simultanés » ne le prouve pas (le rowversion suffit à le faire passer).
/// ToQueryString produit le SQL sans ouvrir de connexion.
/// </summary>
public sealed class EventLockSqlTests
{
    [Fact(DisplayName = "La requête de verrouillage contient WITH (UPDLOCK, ROWLOCK) et un Id paramétré")]
    public void Locked_query_keeps_lock_hints_and_parameter()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=factice;Database=factice;User Id=sa;Password=factice;TrustServerCertificate=True")
            .Options;
        using var db = new AppDbContext(options);

        var sql = EventLock.LockedEventQuery(db, Guid.NewGuid()).ToQueryString();

        Assert.Contains("WITH (UPDLOCK, ROWLOCK)", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Id] = @", sql, StringComparison.Ordinal); // paramètre, pas de valeur concaténée
    }
}
