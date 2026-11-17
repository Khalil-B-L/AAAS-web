using Aaas.Api.IntegrationTests.Support;
using Aaas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aaas.Api.IntegrationTests.Infrastructure;

/// <summary>
/// R24 / ADR 0002, de façon déterministe (pas de course) : un INSERT refusé par l'index unique sur l'e-mail ne laisse
/// ni l'e-mail ni entrée « [Error] » dans les journaux. Prouve que ConfigureWarnings (DependencyInjection.cs) agit.
/// </summary>
public sealed class UniqueViolationLoggingTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact(DisplayName = "INSERT d'un e-mail déjà présent : DbUpdateException 2601, aucun e-mail ni [Error] dans les journaux")]
    public async Task Duplicate_email_insert_logs_neither_email_nor_error()
    {
        var (email, _, _) = await RegisterMemberAsync();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exception = await UniqueViolation.InsertDuplicateEmailAsync(db, email);
            UniqueViolation.AssertIsUniqueIndexViolation(exception);
        }

        var all = string.Join("\n", Factory.Logs.Messages);
        Assert.NotEmpty(Factory.Logs.Messages);
        Assert.DoesNotContain(email, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Error]", all);
        Assert.DoesNotContain("[Critical]", all);
    }

    /// <summary>
    /// Témoin négatif : le même INSERT sur un AppDbContext SANS ConfigureWarnings fait apparaître l'e-mail et un [Error].
    /// Remplace le « retrait temporaire » de ConfigureWarnings (interdit ici : src/ n'est pas modifiable) : il montre
    /// que le test ci-dessus détecte bien la fuite et que seule la configuration de production l'empêche.
    /// </summary>
    [Fact(DisplayName = "Témoin : sans ConfigureWarnings, le même INSERT journalise l'e-mail et une entrée Error")]
    public async Task Without_configure_warnings_the_same_insert_leaks_the_email()
    {
        var (email, _, _) = await RegisterMemberAsync();
        var provider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(Factory.ConnectionString)
            .UseLoggerFactory(loggerFactory)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            var exception = await UniqueViolation.InsertDuplicateEmailAsync(db, email);
            UniqueViolation.AssertIsUniqueIndexViolation(exception);
        }

        var all = string.Join("\n", provider.Messages);
        Assert.Contains("[Error]", all);
        Assert.Contains(email, all, StringComparison.OrdinalIgnoreCase);
    }
}
