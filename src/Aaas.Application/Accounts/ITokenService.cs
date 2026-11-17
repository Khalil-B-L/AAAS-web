namespace Aaas.Application.Accounts;

/// <summary>Émission du jeton d'accès (JWT, implémenté dans Infrastructure).</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(UserAccount user);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
