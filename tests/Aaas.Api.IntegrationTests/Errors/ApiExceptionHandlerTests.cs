using System.Reflection;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aaas.Api.IntegrationTests.Errors;

/// <summary>
/// ADR 0002 / R24 : une violation d'unicité SQL qui atteint ApiExceptionHandler est journalisée sans l'exception
/// (son message contient la valeur dupliquée, souvent un e-mail). Le handler est internal : instancié par réflexion.
/// Aucune base : tests unitaires du handler, sans conteneur.
/// </summary>
public sealed class ApiExceptionHandlerTests
{
    private const string Secret = "secret.personne@exemple.tn";

    private static readonly Type HandlerType =
        typeof(Program).Assembly.GetType("Aaas.Api.Errors.ApiExceptionHandler", throwOnError: true)!;

    private sealed record Entry(LogLevel Level, string Message, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, formatter(state, exception), exception,
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []));
    }

    private sealed class Harness
    {
        public required IExceptionHandler Handler { get; init; }
        public required DefaultHttpContext Context { get; init; }
        public required Func<List<Entry>> Entries { get; init; }

        public static Harness Create(string? accept)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddProblemDetails();
            var provider = services.BuildServiceProvider();

            var loggerType = typeof(RecordingLogger<>).MakeGenericType(HandlerType);
            var logger = Activator.CreateInstance(loggerType)!;
            var entries = loggerType.GetProperty(nameof(RecordingLogger<int>.Entries))!;
            var handler = (IExceptionHandler)Activator.CreateInstance(
                HandlerType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [provider.GetRequiredService<IProblemDetailsService>(), logger],
                culture: null)!;

            var context = new DefaultHttpContext { RequestServices = provider };
            context.Request.Method = "POST";
            context.Request.Path = "/api/auth/register";
            context.Response.Body = new MemoryStream();
            if (accept is not null)
            {
                context.Request.Headers.Accept = accept;
            }

            return new Harness { Handler = handler, Context = context, Entries = () => (List<Entry>)entries.GetValue(logger)! };
        }
    }

    private static DbUpdateException UniqueViolation(int number) =>
        new("An error occurred while saving", SqlExceptionFactory.Create(number, $"Cannot insert duplicate key. The duplicate key value is ({Secret})."));

    [Theory(DisplayName = "Violation d'unicité non interceptée -> 500, log Error sans exception, avec SqlErrorNumber, sans message SQL")]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task UniqueViolation_IsLoggedWithoutException(int number)
    {
        var harness = Harness.Create("application/json");

        var handled = await harness.Handler.TryHandleAsync(harness.Context, UniqueViolation(number), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, harness.Context.Response.StatusCode);
        var error = Assert.Single(harness.Entries(), e => e.Level == LogLevel.Error);
        Assert.Null(error.Exception);
        Assert.Contains(error.State, kv => kv.Key == "SqlErrorNumber" && Equals(kv.Value, number));
        Assert.Contains($"{number}", error.Message);
        Assert.Contains("DbUpdateException", error.Message);
        Assert.Contains("POST", error.Message);
        Assert.Contains("/api/auth/register", error.Message);
        Assert.DoesNotContain("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(harness.Entries(), e => e.Message.Contains(Secret, StringComparison.OrdinalIgnoreCase));

        harness.Context.Response.Body.Position = 0;
        var body = await new StreamReader(harness.Context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain(Secret, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("internal_error", body);
    }

    [Fact(DisplayName = "Témoin : une autre exception est journalisée en Error avec l'exception jointe")]
    public async Task OtherException_IsLoggedWithException()
    {
        var harness = Harness.Create("application/json");
        var exception = new InvalidOperationException("boum");

        var handled = await harness.Handler.TryHandleAsync(harness.Context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, harness.Context.Response.StatusCode);
        var error = Assert.Single(harness.Entries(), e => e.Level == LogLevel.Error);
        Assert.Same(exception, error.Exception);
    }

    [Fact(DisplayName = "Témoin : une erreur SQL autre qu'unicité (18456) reste journalisée avec l'exception")]
    public async Task OtherSqlError_IsLoggedWithException()
    {
        var harness = Harness.Create("application/json");
        var exception = new DbUpdateException("save", SqlExceptionFactory.Create(18456, "Login failed"));

        await harness.Handler.TryHandleAsync(harness.Context, exception, CancellationToken.None);

        Assert.Same(exception, Assert.Single(harness.Entries(), e => e.Level == LogLevel.Error).Exception);
    }

    [Fact(DisplayName = "Accept: text/plain (aucun writer) : 500 sans corps, et le handler renvoie quand même true")]
    public async Task AcceptTextPlain_StillHandled_Returns500()
    {
        var harness = Harness.Create("text/plain");

        var handled = await harness.Handler.TryHandleAsync(harness.Context, UniqueViolation(2601), CancellationToken.None);

        Assert.True(handled); // false laisserait le middleware journaliser l'exception brute et son message SQL
        Assert.Equal(StatusCodes.Status500InternalServerError, harness.Context.Response.StatusCode);
        Assert.Equal(0, harness.Context.Response.Body.Length);
        Assert.DoesNotContain(harness.Entries(), e => e.Message.Contains(Secret, StringComparison.OrdinalIgnoreCase) || e.Exception is not null);
    }
}
