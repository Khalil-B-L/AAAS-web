using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>
/// Démarre l'API réelle (Program.cs) contre une base SQL Server de test.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "cle-de-test-d-au-moins-32-caracteres-0123456789";

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Jwt:SigningKey"] = SigningKey,
        ["Database:MigrateOnStartup"] = "true",
    };

    private readonly TimeProvider? _clock;

    public ApiFactory(string connectionString, Dictionary<string, string?>? overrides = null, TimeProvider? clock = null)
    {
        _clock = clock;
        ConnectionString = connectionString;
        _settings["ConnectionStrings:Default"] = connectionString;
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                if (value is null)
                {
                    _settings.Remove(key);
                }
                else
                {
                    _settings[key] = value;
                }
            }
        }
    }

    public string ConnectionString { get; }

    /// <summary>Tous les messages journalisés pendant le test (pour vérifier l'absence de données personnelles).</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        if (_clock is not null)
        {
            // Remplace l'horloge de l'application (enregistrée par Infrastructure) par l'horloge simulée du test.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(_clock);
            });
        }
    }

    /// <summary>Exécute une requête SQL directement en base (préparation ou vérification).</summary>
    public async Task<T?> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync();
    }
}

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => [.. _messages];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, System.Collections.Concurrent.ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? "" : " " + exception)}");
        }
    }
}
