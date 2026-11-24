using Aaas.Domain.Common;

namespace Aaas.Application.Accounts;

/// <summary>
/// Port vers ASP.NET Core Identity (implémenté dans Infrastructure).
/// Application ne voit ni UserManager ni AppUser : elle reste testable avec une doublure.
/// </summary>
public interface IIdentityService
{
    Task<CreateUserResult> CreateUserAsync(NewUser user, string password, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

    /// <summary>Vérifie le mot de passe et applique le blocage après échecs consécutifs (R21).</summary>
    Task<PasswordCheckResult> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task RecordLoginAsync(Guid userId, DateTimeOffset loggedInAt, CancellationToken cancellationToken);

    /// <summary>Faux si le compte n'existe plus ou si son security stamp a changé (révocation des jetons, ADR 0002).</summary>
    Task<bool> IsSecurityStampValidAsync(Guid userId, string securityStamp, CancellationToken cancellationToken);

    /// <summary>
    /// Coordonnées de plusieurs comptes en UNE requête (ADR 0021 § 3, pas de N+1). Un identifiant sans compte
    /// (supprimé) est absent du dictionnaire : l'appelant affiche « Compte supprimé ». Réservé aux vues admin.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MemberContact>> GetContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>Coordonnées d'un membre vues par un administrateur (RG-15) ; jamais journalisées.</summary>
public sealed record MemberContact(Guid Id, string FirstName, string LastName, string Email, string PhoneNumber);

/// <summary>Compte tel que vu par Application. <see cref="SecurityStamp"/> sert uniquement à émettre le jeton.</summary>
public sealed record UserAccount(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset PrivacyConsentAt,
    string PrivacyPolicyVersion,
    DateTimeOffset? LastLoginAt,
    string SecurityStamp);

/// <summary>Données d'un compte à créer ; la date sert de date de création, de consentement et de première connexion.</summary>
public sealed record NewUser(
    string FirstName,
    string LastName,
    string Email,
    PhoneNumber PhoneNumber,
    DateTimeOffset RegisteredAt,
    string PrivacyPolicyVersion);

public abstract record CreateUserResult
{
    private CreateUserResult() { }

    public sealed record Created(UserAccount User) : CreateUserResult;

    public sealed record EmailAlreadyUsed : CreateUserResult;

    /// <summary>Erreurs par champ JSON (ex. « password », « email »), messages en français.</summary>
    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : CreateUserResult;
}

public enum PasswordCheckStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
}

public sealed record PasswordCheckResult(PasswordCheckStatus Status, UserAccount? User = null, DateTimeOffset? LockoutEnd = null);
