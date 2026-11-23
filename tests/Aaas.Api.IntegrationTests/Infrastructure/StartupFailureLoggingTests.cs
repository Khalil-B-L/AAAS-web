using Aaas.Api.IntegrationTests.Support;
using Aaas.Infrastructure.Initialization;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aaas.Api.IntegrationTests.Infrastructure;

/// <summary>
/// ADR 0002 / 0008 (Program.cs) : un échec d'initialisation est journalisé en Critical avec un message sûr, et
/// l'exception relancée n'expose pas le message SQL (pas d'InnerException). Une erreur de configuration garde son message.
/// </summary>
public sealed class StartupFailureLoggingTests(SqlServerFixture sql)
{
    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.SelectMany(Chain))
                {
                    yield return inner;
                }
            }

            if (current.InnerException is null)
            {
                yield break;
            }
        }
    }

    [Fact(DisplayName = "Démarrage : erreur SQL (base sans schéma) -> Critical sans message SQL, exception relancée sans InnerException")]
    public async Task Sql_failure_at_startup_is_logged_safely_and_rethrown_without_inner_exception()
    {
        var cs = await sql.CreateDatabaseAsync(); // base vide, migrations désactivées : AspNetRoles n'existe pas (SQL 208)
        try
        {
            await using var factory = new ApiFactory(cs, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });

            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            var chain = Chain(ex).ToList();
            var rethrown = Assert.Single(chain, e => e.Message.StartsWith("Échec de l'initialisation de la base", StringComparison.Ordinal));
            Assert.IsType<InvalidOperationException>(rethrown);
            Assert.Null(rethrown.InnerException);
            // Type et numéro SQL répétés dans le message relancé : l'indice survit même si les logs sont perdus.
            Assert.Contains("erreur SQL : 208", rethrown.Message);
            Assert.Contains("Microsoft.Data.SqlClient.SqlException", rethrown.Message);
            Assert.DoesNotContain(chain, e => e is SqlException);
            Assert.DoesNotContain(chain, e => e.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Nom d'objet non valide", StringComparison.OrdinalIgnoreCase));

            var critical = Assert.Single(factory.Logs.Messages, m => m.StartsWith("[Critical]", StringComparison.Ordinal));
            Assert.Contains("Aaas.Startup", critical);
            Assert.Contains("Détail non journalisé", critical);
            Assert.Contains("erreur SQL : 208", critical); // numéro SQL, sans le message
            Assert.DoesNotContain("   at ", critical); // aucune exception (ni pile d'appels) jointe
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }


    /// <summary>
    /// Garantit qu'aucun message SQL brut n'apparaît dans aucun journal lors d'un échec SQL au démarrage (base sans schéma).
    /// Régression d'origine, aujourd'hui corrigée (ConfigureWarnings dans DependencyInjection.cs) : EF Core journalisait
    /// « [Error] Microsoft.EntityFrameworkCore.Query » (QueryIterationFailed) avec la SqlException jointe.
    /// </summary>
    [Fact(DisplayName = "Démarrage : erreur SQL de requête -> aucun journal ne contient le message SQL brut (régression QueryIterationFailed)")]
    public async Task Sql_failure_at_startup_leaves_no_raw_sql_message_in_any_log()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await using var factory = new ApiFactory(cs, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });

            Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            // Précondition : l'échec vient bien de l'accès à la base (base vide, MigrateOnStartup=false, première requête
            // sur AspNetRoles -> SQL 208). Sans elle, un démarrage qui échoue plus tôt (configuration) laisserait ce test vert à tort.
            Assert.Contains(factory.Logs.Messages, m => m.StartsWith("[Critical]", StringComparison.Ordinal) && m.Contains("erreur SQL : 208", StringComparison.Ordinal));

            var leaks = factory.Logs.Messages
                .Where(m => m.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
                .Select(m => m[..Math.Min(200, m.Length)])
                .ToList();
            Assert.True(leaks.Count == 0, "Message SQL brut journalisé : " + string.Join(" ## ", leaks));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    /// <summary>
    /// Prouve la rétrogradation des événements EF (QueryIterationFailed -> Debug) : une lecture en échec n'émet aucune
    /// entrée Warning/Error/Critical de la catégorie Microsoft.EntityFrameworkCore* portant une SqlException.
    /// Détecte un retour en arrière : sans la règle, EF émet « [Error] Microsoft.EntityFrameworkCore.Query: ... SqlException ».
    /// </summary>
    [Fact(DisplayName = "Démarrage : lecture en échec -> aucune entrée EF au-dessus de Debug ne porte de SqlException")]
    public async Task Failed_read_produces_no_ef_entry_above_debug_carrying_a_sql_exception()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await using var factory = new ApiFactory(cs, new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });

            Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            // Précondition : l'échec vient bien de l'accès à la base (base vide, MigrateOnStartup=false, première requête
            // sur AspNetRoles -> SQL 208), donc EF a bien traité une lecture en échec. Sans elle, ce test passerait à tort.
            Assert.Contains(factory.Logs.Messages, m => m.StartsWith("[Critical]", StringComparison.Ordinal) && m.Contains("erreur SQL : 208", StringComparison.Ordinal));

            var offending = factory.Logs.Messages
                .Where(m => m.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    && !m.StartsWith("[Debug]", StringComparison.Ordinal)
                    && !m.StartsWith("[Trace]", StringComparison.Ordinal)
                    && m.Contains("SqlException", StringComparison.Ordinal))
                .Select(m => m[..Math.Min(200, m.Length)])
                .ToList();
            Assert.True(offending.Count == 0, "Entrée EF avec SqlException : " + string.Join(" ## ", offending));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Démarrage : InvalidOperationException de configuration -> Critical avec son message, relancée telle quelle")]
    public async Task Configuration_failure_keeps_its_message()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Bootstrap:AdminEmail"] = $"admin.{Guid.NewGuid():N}@example.org",
                ["Bootstrap:AdminPassword"] = "AdminTest2026",
                ["Bootstrap:AdminFirstName"] = "Admin",
                ["Bootstrap:AdminLastName"] = "Association",
                ["Bootstrap:AdminPhone"] = "71 000 000", // hors format : InvalidOperationException « nue »
            };
            await using var factory = new ApiFactory(cs, overrides);

            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            var rethrown = Assert.Single(Chain(ex), e => e.Message.StartsWith("Bootstrap:AdminPhone invalide", StringComparison.Ordinal));
            Assert.IsType<InvalidOperationException>(rethrown);
            Assert.Null(rethrown.InnerException);
            var critical = Assert.Single(factory.Logs.Messages, m => m.StartsWith("[Critical]", StringComparison.Ordinal));
            Assert.Contains("Bootstrap:AdminPhone invalide", critical); // message de l'exception jointe conservé
            Assert.DoesNotContain("Détail non journalisé", critical);
            Assert.DoesNotContain(overrides["Bootstrap:AdminEmail"]!, string.Join("\n", factory.Logs.Messages), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Démarrage : exception ni SQL ni nue (InnerException non SQL) -> message relancé 'erreur SQL : aucune', Critical sans message brut")]
    public async Task Non_sql_exception_with_inner_exception_is_neutralised_without_leaking_its_message()
    {
        const string secret = "secret.personne@example.org";
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await using var baseFactory = new ApiFactory(cs);
            // Cas rendu atteignable en injectant un DatabaseInitializer qui échoue (la voie « nue » exige InnerException nulle).
            await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DatabaseInitializer>();
                services.AddScoped<DatabaseInitializer>(_ => throw new InvalidOperationException(
                    "Doublon " + secret, new IOException("détail interne " + secret)));
            }));

            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            var chain = Chain(ex).ToList();
            var rethrown = Assert.Single(chain, e => e.Message.StartsWith("Échec de l'initialisation de la base", StringComparison.Ordinal));
            Assert.Null(rethrown.InnerException);
            Assert.Contains("erreur SQL : aucune", rethrown.Message);
            Assert.Contains("System.InvalidOperationException", rethrown.Message);
            Assert.DoesNotContain(chain, e => e.Message.Contains(secret, StringComparison.OrdinalIgnoreCase));

            // Le fournisseur de logs de baseFactory est réutilisé par la fabrique dérivée.
            var critical = Assert.Single(baseFactory.Logs.Messages, m => m.StartsWith("[Critical]", StringComparison.Ordinal));
            Assert.Contains("Détail non journalisé", critical);
            Assert.Contains("erreur SQL : ", critical);
            Assert.DoesNotContain(secret, critical, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("   at ", critical);
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }
}
