using Aaas.Application.Accounts;
using Aaas.Application.Options;
using Aaas.Domain.Common;
using Aaas.Infrastructure.Identity;
using Aaas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aaas.Infrastructure.Initialization;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>true en local et dans docker compose ; false en Azure (migrations appliquées par la CI).</summary>
    public bool MigrateOnStartup { get; set; }
}

/// <summary>
/// Initialisation idempotente au démarrage (ADR 0008) : migrations, rôles, premier administrateur (R22, RG-09).
/// Peut être relancée sans risque : elle ne crée jamais un second admin.
/// </summary>
public sealed class DatabaseInitializer(
    AppDbContext db,
    RoleManager<IdentityRole<Guid>> roleManager,
    UserManager<AppUser> userManager,
    IIdentityService identity,
    TimeProvider time,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<BootstrapOptions> bootstrapOptions,
    IOptions<PrivacyPolicyOptions> privacyPolicyOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (databaseOptions.Value.MigrateOnStartup)
        {
            logger.LogInformation("Application des migrations EF Core...");
            await db.Database.MigrateAsync(cancellationToken);
        }

        await EnsureRolesAsync();
        await EnsureFirstAdminAsync(cancellationToken);
    }

    private async Task EnsureRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await CreateRoleAsync(role);
            }
        }
    }

    /// <summary>
    /// Deux instances sur une base neuve peuvent créer le même rôle en même temps. Selon le moment, la seconde
    /// échoue au RoleValidator (DuplicateRoleName) ou à l'INSERT (index unique RoleNameIndex, SQL 2601).
    /// Dans les deux cas, le rôle existe : c'est le résultat voulu. On ne relance que s'il manque toujours.
    /// </summary>
    private async Task CreateRoleAsync(string role)
    {
        IdentityResult result;
        try
        {
            result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueKeyViolation(ex))
        {
            // L'INSERT refusé reste suivi (état Added) : sans Clear, le prochain SaveChanges le retenterait.
            db.ChangeTracker.Clear();
            if (!await roleManager.RoleExistsAsync(role))
            {
                throw;
            }

            LogRoleCreatedConcurrently(role);
            return;
        }

        if (!result.Succeeded && await roleManager.RoleExistsAsync(role))
        {
            LogRoleCreatedConcurrently(role);
            return;
        }

        EnsureSucceeded(result, $"création du rôle {role}");
    }

    private void LogRoleCreatedConcurrently(string role)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Rôle {Role} créé en parallèle par une autre instance.", role);
        }
    }

    private async Task EnsureFirstAdminAsync(CancellationToken cancellationToken)
    {
        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return; // un admin existe déjà : la configuration Bootstrap est ignorée
        }

        var bootstrap = bootstrapOptions.Value;
        if (!bootstrap.IsComplete)
        {
            logger.LogWarning(
                "Aucun administrateur n'existe et la configuration Bootstrap:* est incomplète (manquant : {MissingKeys}). " +
                "L'API démarre sans administrateur : renseignez ces valeurs (docker compose : BOOTSTRAP_ADMIN_* dans .env) " +
                "puis redémarrez l'API.",
                string.Join(", ", bootstrap.MissingKeys));
            return;
        }

        var email = bootstrap.AdminEmail!.Trim();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            await HandleExistingAccountAsync(existing, bootstrap);
            return;
        }

        if (BootstrapOptions.KnownExamplePasswords.Contains(bootstrap.AdminPassword!))
        {
            throw new InvalidOperationException(
                "Configuration Bootstrap invalide : Bootstrap:AdminPassword reprend une valeur d'exemple publique. Choisissez un mot de passe aléatoire.");
        }

        if (!PhoneNumber.TryParse(bootstrap.AdminPhone, out var phone))
        {
            throw new InvalidOperationException($"Bootstrap:AdminPhone invalide. {PhoneNumber.ExpectedFormatMessage}");
        }

        var newUser = new NewUser(
            bootstrap.AdminFirstName!.Trim(),
            bootstrap.AdminLastName!.Trim(),
            email,
            phone!,
            time.GetUtcNow(),
            privacyPolicyOptions.Value.CurrentVersion);

        // L'admin est aussi « User » : il garde les droits d'un membre (et un compte promu les a déjà).
        var result = await identity.CreateUserAsync(newUser, bootstrap.AdminPassword!, [Roles.User, Roles.Admin], cancellationToken);
        switch (result)
        {
            case CreateUserResult.Created:
                logger.LogInformation("Premier administrateur créé.");
                break;
            case CreateUserResult.EmailAlreadyUsed:
                await HandleEmailTakenConcurrentlyAsync(email, bootstrap);
                break;
            case CreateUserResult.Invalid invalid:
                // Échec immédiat et explicite plutôt qu'une API sans admin (ex. mot de passe trop faible).
                throw new InvalidOperationException(
                    "Configuration Bootstrap invalide : " + string.Join(" ", invalid.Errors.SelectMany(e => e.Value)));
        }
    }

    /// <summary>
    /// L'e-mail a été pris entre notre vérification et notre INSERT : soit une autre instance a créé l'admin
    /// (cas normal, rien à faire), soit quelqu'un s'est inscrit au même moment (même règle qu'un compte existant).
    /// </summary>
    private async Task HandleEmailTakenConcurrentlyAsync(string email, BootstrapOptions bootstrap)
    {
        // L'INSERT refusé reste suivi (état Added) : sans Clear, le prochain SaveChanges le retenterait.
        db.ChangeTracker.Clear();

        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            logger.LogInformation("Premier administrateur déjà créé par une autre instance.");
            return;
        }

        var existing = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException("Échec de l'initialisation : e-mail Bootstrap:AdminEmail signalé comme pris, mais compte introuvable.");
        await HandleExistingAccountAsync(existing, bootstrap);
    }

    /// <summary>Un compte porte déjà Bootstrap:AdminEmail et aucun admin n'existe (ADR 0008).</summary>
    private async Task HandleExistingAccountAsync(AppUser existing, BootstrapOptions bootstrap)
    {
        if (!bootstrap.PromoteExistingAccount)
        {
            // Promotion implicite refusée : le compte a pu être créé par n'importe qui via l'inscription publique.
            // Pas d'e-mail dans le message (R24) : il finit dans les journaux du conteneur.
            throw new InvalidOperationException(
                "Un compte existe déjà avec Bootstrap:AdminEmail et aucun administrateur n'existe. " +
                "Il n'est pas promu automatiquement : promouvez-le manuellement (docs/technique/exploitation.md § 6), " +
                "ou, après avoir vérifié que ce compte vous appartient, définissez Bootstrap:PromoteExistingAccount=true.");
        }

        // Promotion demandée explicitement : on garde le mot de passe du membre.
        // Course non gérée ici : une seule instance doit tourner pendant la promotion (exploitation.md § 5).
        if (!await userManager.IsInRoleAsync(existing, Roles.User))
        {
            EnsureSucceeded(await userManager.AddToRoleAsync(existing, Roles.User), "ajout du rôle User");
        }

        EnsureSucceeded(await userManager.AddToRoleAsync(existing, Roles.Admin), "promotion du premier administrateur");
        // Nouveau security stamp : les jetons émis avant la promotion (sans le rôle Admin) sont révoqués.
        EnsureSucceeded(await userManager.UpdateSecurityStampAsync(existing), "renouvellement du security stamp");
        // Warning, pas Information : trace d'audit (élévation de privilèges) visible même avec un niveau de log relevé.
        // Identifiant technique plutôt que l'e-mail (R24).
        logger.LogWarning(
            "Audit : compte existant {UserId} promu premier administrateur (Bootstrap:PromoteExistingAccount=true). " +
            "Remettez cette option à false.", existing.Id);
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Échec de l'initialisation ({operation}) : " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}

public static class DatabaseInitializerExtensions
{
    /// <summary>À appeler depuis Program.cs avant app.Run().</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(cancellationToken);
    }
}
