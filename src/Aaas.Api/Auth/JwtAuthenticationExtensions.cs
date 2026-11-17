using System.Security.Claims;
using Aaas.Application.Accounts;
using Aaas.Domain.Common;
using Aaas.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aaas.Api.Auth;

public static class Policies
{
    public const string AdminOnly = nameof(AdminOnly);
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>Identifiant du compte connecté (claim « sub »). Appelé seulement derrière [Authorize].</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Claim « sub » absent."));
}

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure<IOptions<JwtOptions>> : les paramètres sont lus depuis les options validées au démarrage,
        // pas recopiés depuis IConfiguration à la main.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                // Garde les noms courts du jeton (sub, role) au lieu des URI WS-Federation de .NET.
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKeyBytes),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // refuse « alg: none » et tout autre algorithme
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = AppClaimTypes.Role,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = ValidateSecurityStampAsync };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Roles.Admin))
            // Sécurisé par défaut : tout endpoint exige un jeton, sauf [AllowAnonymous] explicite.
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return services;
    }

    /// <summary>Révocation (ADR 0002) : compte supprimé ou security stamp renouvelé => jeton refusé immédiatement.</summary>
    private static async Task ValidateSecurityStampAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var stamp = principal?.FindFirstValue(AppClaimTypes.SecurityStamp);
        if (!Guid.TryParse(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId) || string.IsNullOrEmpty(stamp))
        {
            context.Fail("Jeton incomplet.");
            return;
        }

        var identity = context.HttpContext.RequestServices.GetRequiredService<IIdentityService>();
        if (!await identity.IsSecurityStampValidAsync(userId, stamp, context.HttpContext.RequestAborted))
        {
            context.Fail("Session révoquée.");
        }
    }
}
