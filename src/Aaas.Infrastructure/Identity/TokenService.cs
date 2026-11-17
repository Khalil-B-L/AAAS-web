using System.Security.Claims;
using Aaas.Application.Accounts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aaas.Infrastructure.Identity;

/// <summary>Émet des JWT HS256 de durée fixe, sans refresh token (ADR 0002).</summary>
internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider time) : ITokenService
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken CreateAccessToken(UserAccount user)
    {
        var jwt = options.Value;
        // Le JWT stocke des secondes entières : on tronque pour renvoyer exactement la valeur de « exp ».
        var now = DateTimeOffset.FromUnixTimeSeconds(time.GetUtcNow().ToUnixTimeSeconds());
        var expiresAt = now.AddMinutes(jwt.LifetimeMinutes);

        // Volontairement minimal : ni e-mail ni téléphone dans un jeton lisible par quiconque l'intercepte.
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(AppClaimTypes.SecurityStamp, user.SecurityStamp),
            .. user.Roles.Select(role => new Claim(AppClaimTypes.Role, role)),
        ];

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(jwt.SigningKeyBytes), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(Handler.CreateToken(descriptor), expiresAt);
    }
}
