using System.Globalization;
using System.Text.Json.Serialization;
using Aaas.Api.Auth;
using Aaas.Api.Errors;
using Aaas.Api.Jobs;
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
    {
        // Les clés d'erreur de validation reprennent les noms JSON (« phoneNumber ») au lieu des noms C# (« PhoneNumber »).
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
        // Même convention pour les paramètres de requête des listes (?pageSize= → errors.pageSize).
        options.ModelMetadataDetailsProviders.Add(new CamelCaseQueryNamesProvider());
        // « ?page=abc » : message français au lieu de « The value 'abc' is not valid ».
        FrenchModelBindingMessages.Apply(options.ModelBindingMessageProvider);
    })
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins) // uniquement le front (cahier § 6.2) ; liste vide = aucune origine
    .WithHeaders("Authorization", "Content-Type")
    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")));

builder.Services.AddOpenApi();

var app = builder.Build();

// En tête : une requête /api/ compte comme activité si sa réponse est < 400 (pas un robot en 401/404) ; /health jamais (ADR 0020 § 4).
app.UseApiActivityTracking();
// Avant le gestionnaire d'exceptions : l'en-tête doit aussi partir avec les réponses d'erreur.
app.UseNoStoreForPersonalData();
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
        await FlushLogsAsync(app);
        throw;
    }

    const string safeMessage =
        "Échec de l'initialisation de la base : l'API s'arrête. Détail non journalisé (peut contenir des données " +
        "personnelles) : vérifier la base, les migrations et la configuration Bootstrap.";
    var exceptionType = ex.GetType().FullName;
    int? sqlErrorNumber = SqlServerErrors.TryGetSqlErrorNumber(ex, out var number) ? number : null;
    if (startupLogger.IsEnabled(LogLevel.Critical))
    {
        startupLogger.LogCritical(
            safeMessage + " Type : {ExceptionType}, erreur SQL : {SqlErrorNumber}.", exceptionType, sqlErrorNumber);
    }

    await FlushLogsAsync(app);

    // Pas de « throw; » ici : le runtime écrirait l'exception d'origine (message SQL compris) sur stderr,
    // donc dans « docker compose logs ». On relance une exception neutre, sans InnerException. Le type et le
    // numéro SQL y sont répétés : ils restent visibles sur stderr même si le log Critical n'a pas pu partir.
    throw new InvalidOperationException(
        $"{safeMessage} ({exceptionType}, erreur SQL : {sqlErrorNumber?.ToString(CultureInfo.InvariantCulture) ?? "aucune"})");
}

await app.RunAsync();

// Le logger console (et Application Insights) écrit via une file vidée par un thread d'arrière-plan : une exception
// qui sort de Main termine le processus sans attendre ce thread. Libérer l'hôte dispose les fournisseurs de logs,
// qui vident leur file. Un échec de libération ne doit pas masquer l'exception d'origine, relancée juste après.
static async Task FlushLogsAsync(WebApplication app)
{
    try
    {
        await app.DisposeAsync();
    }
#pragma warning disable CA1031 // volontaire : seule l'exception d'initialisation doit remonter
    catch (Exception)
#pragma warning restore CA1031
    {
    }
}

// Rendue publique pour WebApplicationFactory<Program> (tests d'intégration).
public partial class Program;
