using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

[assembly: AssemblyFixture(typeof(Aaas.Api.IntegrationTests.Support.SqlServerFixture))]

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>
/// Un seul conteneur SQL Server pour toute la série (démarrage lent), partagé via une « assembly fixture » xUnit v3.
/// Chaque test crée sa propre base dans ce conteneur (voir ApiTestBase) : isolation sans redémarrer SQL Server.
/// Conteneur dédié aux tests : le stack docker compose du développeur n'est ni utilisé ni modifié.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>Même tag que docker-compose.yml : un tag précis rend les tests reproductibles (2022-latest bouge à chaque CU).</summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU27-GDR2-ubuntu-22.04";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Crée une base vide avec READ_COMMITTED_SNAPSHOT activé (comme Azure SQL) et renvoie sa chaîne de connexion.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "aaas_" + Guid.NewGuid().ToString("N");
        await using (var connection = new SqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE DATABASE [{name}]; ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;";
            await command.ExecuteNonQueryAsync();
        }

        var builder = new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = name };
        return builder.ConnectionString;
    }

    public async Task DropDatabaseAsync(string connectionString)
    {
        var name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
        await command.ExecuteNonQueryAsync();
    }
}
