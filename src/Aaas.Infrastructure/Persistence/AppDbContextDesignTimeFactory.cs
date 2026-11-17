using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aaas.Infrastructure.Persistence;

/// <summary>
/// Utilisée uniquement par « dotnet ef » : crée le contexte sans démarrer l'API
/// (qui exige une clé JWT et une chaîne de connexion valides).
/// « migrations add » n'ouvre pas de connexion ; « database update » lit ConnectionStrings__Default.
/// </summary>
internal sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=localhost,1433;Database=Aaas;User Id=sa;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new AppDbContext(options);
    }
}
