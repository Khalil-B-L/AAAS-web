using Aaas.Application.Accounts;
using Aaas.Application.Options;
using Aaas.Infrastructure.Identity;
using Aaas.Infrastructure.Initialization;
using Aaas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Aaas.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";
    public const string ReadyHealthCheckTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // TryAdd : les tests d'intégration peuvent enregistrer un FakeTimeProvider avant nous.
        services.TryAddSingleton(TimeProvider.System);

        AddOptions(services);

        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"ConnectionStrings:{ConnectionStringName} est absente (user-secrets en local, ConnectionStrings__Default dans Docker).");
            }

            // Retry : Azure SQL serverless met plusieurs secondes à sortir de pause ; SQL Server en conteneur peut redémarrer.
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());

            // R24 : quand une opération SQL échoue, EF journalise lui-même en Error, SqlException jointe, AVANT que
            // l'appelant ait pu trier ; or ce message peut contenir une donnée (valeur dupliquée = l'e-mail, valeur
            // non convertible...). Règle : EF ne journalise aucune exception SQL au-dessus de Debug ; l'exception
            // remonte toujours, et c'est l'appelant qui journalise en filtrant (ApiExceptionHandler, Program.cs).
            // - SaveChangesFailed : ignoré (doublon de CommandError).
            // - Les autres : rétrogradés en Debug, pas ignorés, pour garder le diagnostic en développement.
            //   CommandError (commande), QueryIterationFailed (lecture : FirstOrDefault, ToList...), NonQueryOperationFailed /
            //   ExecuteUpdateFailed / ExecuteDeleteFailed (ExecuteUpdate/Delete), ConnectionError, TransactionError.
            // Les valeurs des paramètres SQL sont masquées (pas d'EnableSensitiveDataLogging), mais PAS le message de
            // l'exception jointe : activer Debug sur « Microsoft.EntityFrameworkCore » refait apparaître l'e-mail
            // (exploitation.md § 8). ExecutionStrategyRetrying (Information) reste visible : il ne concerne que des
            // erreurs transitoires (délai, deadlock, base en pause) dont le message ne contient pas de donnée.
            options.ConfigureWarnings(w => w
                .Ignore(CoreEventId.SaveChangesFailed)
                .Log(
                    (RelationalEventId.CommandError, LogLevel.Debug),
                    (CoreEventId.QueryIterationFailed, LogLevel.Debug),
                    (RelationalEventId.NonQueryOperationFailed, LogLevel.Debug),
                    (RelationalEventId.ExecuteUpdateFailed, LogLevel.Debug),
                    (RelationalEventId.ExecuteDeleteFailed, LogLevel.Debug),
                    (RelationalEventId.ConnectionError, LogLevel.Debug),
                    (RelationalEventId.TransactionError, LogLevel.Debug)));
        });

        services
            .AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                // UserName = e-mail, déjà validé : on n'impose pas la liste de caractères par défaut (refuserait « ' » ou les accents).
                options.User.AllowedUserNameCharacters = string.Empty;

                // R21 : au moins 8 caractères dont un chiffre et une lettre (lettre : LetterRequiredPasswordValidator).
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                // R21 : blocage 15 minutes après 5 échecs consécutifs, y compris pour les comptes neufs.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddErrorDescriber<FrenchIdentityErrorDescriber>()
            .AddPasswordValidator<LetterRequiredPasswordValidator>();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddScoped<DatabaseInitializer>();

        // /health/ready vérifie la base ; /health (liveness) ne la touche pas, voir Program.cs.
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: [ReadyHealthCheckTag]);

        return services;
    }

    private static void AddOptions(IServiceCollection services)
    {
        // ValidateOnStart : une configuration invalide arrête l'API au démarrage, pas au premier appel.
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(o => o.HasStrongEnoughKey, $"Jwt:SigningKey doit faire au moins {JwtOptions.MinimumKeyBytes} octets (256 bits).")
            .Validate(o => !o.IsKnownExampleKey, "Jwt:SigningKey reprend la valeur d'exemple publique : générez une clé aléatoire (openssl rand -base64 48).")
            .ValidateOnStart();

        services.AddOptions<PrivacyPolicyOptions>()
            .BindConfiguration(PrivacyPolicyOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>().BindConfiguration(DatabaseOptions.SectionName);
        services.AddOptions<BootstrapOptions>().BindConfiguration(BootstrapOptions.SectionName);
    }
}
