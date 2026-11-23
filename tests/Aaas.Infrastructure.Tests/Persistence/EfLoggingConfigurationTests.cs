using Aaas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aaas.Infrastructure.Tests.Persistence;

/// <summary>
/// Garde-fou de configuration (R24, ADR 0002) : EF Core ne doit journaliser aucune exception SQL au-dessus de Debug,
/// car son message peut contenir une donnée personnelle. Complète les tests d'intégration, qui ne provoquent pas
/// tous ces événements (ExecuteUpdate/Delete, connexion, transaction...). Aucun conteneur : la base n'est jamais ouverte.
/// </summary>
public sealed class EfLoggingConfigurationTests
{
    private static WarningsConfiguration BuildWarningsConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=factice;Database=factice;User Id=sa;Password=factice;TrustServerCertificate=True",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<AppDbContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core.WarningsConfiguration;
    }

    public static TheoryData<string, EventId> DowngradedEvents => new()
    {
        { nameof(RelationalEventId.CommandError), RelationalEventId.CommandError },
        { nameof(CoreEventId.QueryIterationFailed), CoreEventId.QueryIterationFailed },
        { nameof(RelationalEventId.NonQueryOperationFailed), RelationalEventId.NonQueryOperationFailed },
        { nameof(RelationalEventId.ExecuteUpdateFailed), RelationalEventId.ExecuteUpdateFailed },
        { nameof(RelationalEventId.ExecuteDeleteFailed), RelationalEventId.ExecuteDeleteFailed },
        { nameof(RelationalEventId.ConnectionError), RelationalEventId.ConnectionError },
        { nameof(RelationalEventId.TransactionError), RelationalEventId.TransactionError },
    };

    [Theory(DisplayName = "EF : événement d'échec SQL -> journalisé en Debug au plus (jamais Warning/Error)")]
    [MemberData(nameof(DowngradedEvents))]
    public void Sql_failure_events_are_logged_at_debug_only(string name, EventId eventId)
    {
        var warnings = BuildWarningsConfiguration();

        Assert.True(warnings.GetBehavior(eventId) == WarningBehavior.Log, $"{name} doit rester journalisé (Log) pour le diagnostic en développement.");
        Assert.Equal(LogLevel.Debug, warnings.GetLevel(eventId));
    }

    [Fact(DisplayName = "EF : SaveChangesFailed -> ignoré (doublon de CommandError)")]
    public void SaveChangesFailed_is_ignored()
    {
        var warnings = BuildWarningsConfiguration();

        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(CoreEventId.SaveChangesFailed));
    }
}
