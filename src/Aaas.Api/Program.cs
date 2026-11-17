using System.Text.Json.Serialization;
using Aaas.Api.Auth;
using Aaas.Api.Errors;
using Aaas.Application;
using Aaas.Infrastructure;
using Aaas.Infrastructure.Initialization;
using Aaas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddJwtAuthentication()
    .AddApiProblemDetails()
    .AddExceptionHandler<ApiExceptionHandler>();

builder.Services
    .AddControllers(options =>
        // Les clés d'erreur de validation reprennent les noms JSON (« phoneNumber ») au lieu des noms C# (« PhoneNumber »).
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins) // uniquement le front (cahier § 6.2) ; liste vide = aucune origine
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
// Donne un corps ProblemDetails aux 401/403/404 vides produits par le framework (ex. jeton absent).
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json, développement uniquement
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Liveness : l'API répond, sans toucher la base. Utilisé par Docker et App Service : un ping par minute
// sur la base empêcherait Azure SQL serverless de se mettre en pause (et consommerait l'offre gratuite).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
// Readiness : l'API et la base répondent.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(Aaas.Infrastructure.DependencyInjection.ReadyHealthCheckTag),
}).AllowAnonymous();

// ValidateOnStart ne s'exécute qu'au démarrage de l'hôte (RunAsync) : on le déclenche avant de toucher la base,
// pour qu'une clé JWT absente soit signalée comme telle et non masquée par une erreur SQL.
app.Services.GetRequiredService<IStartupValidator>().Validate();
try
{
    await app.Services.InitializeDatabaseAsync(app.Lifetime.ApplicationStopping);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    // Critical : l'API ne démarrera pas. Les InvalidOperationException « nues » (celles de DatabaseInitializer, ou
    // de configuration EF) ont des messages sans donnée personnelle (R24). Une exception SQL/EF enveloppée
    // (ex. violation d'unicité) peut contenir un e-mail dans son message ou celui de son InnerException.
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aaas.Startup");
    if (ex.GetType() == typeof(InvalidOperationException) && ex.InnerException is null)
    {
        startupLogger.LogCritical(ex, "Échec de l'initialisation de la base : l'API s'arrête.");
        throw;
    }

    const string safeMessage =
        "Échec de l'initialisation de la base : l'API s'arrête. Détail non journalisé (peut contenir des données " +
        "personnelles) : vérifier la base, les migrations et la configuration Bootstrap.";
    var exceptionType = ex.GetType().FullName;
    if (startupLogger.IsEnabled(LogLevel.Critical))
    {
        int? sqlErrorNumber = SqlServerErrors.TryGetSqlErrorNumber(ex, out var number) ? number : null;
        startupLogger.LogCritical(
            safeMessage + " Type : {ExceptionType}, erreur SQL : {SqlErrorNumber}.", exceptionType, sqlErrorNumber);
    }

    // Pas de « throw; » ici : le runtime écrirait l'exception d'origine (message SQL compris) sur stderr,
    // donc dans « docker compose logs ». On relance une exception neutre, sans InnerException.
    throw new InvalidOperationException($"{safeMessage} ({exceptionType})");
}

await app.RunAsync();

// Rendue publique pour WebApplicationFactory<Program> (tests d'intégration).
public partial class Program;
