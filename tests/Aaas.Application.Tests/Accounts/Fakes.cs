using Aaas.Application.Accounts;

namespace Aaas.Application.Tests.Accounts;

/// <summary>Doublure de IIdentityService : enregistre les appels reçus et renvoie des résultats programmables.</summary>
internal sealed class FakeIdentityService : IIdentityService
{
    public NewUser? CreatedUser { get; private set; }
    public string? CreatedPassword { get; private set; }
    public IReadOnlyCollection<string>? CreatedRoles { get; private set; }
    public int CreateCalls { get; private set; }

    public string? CheckedEmail { get; private set; }
    public (Guid UserId, DateTimeOffset At)? RecordedLogin { get; private set; }
    public int RecordLoginCalls { get; private set; }

    /// <summary>Résultat de CreateUserAsync ; par défaut, un compte créé avec les valeurs reçues.</summary>
    public Func<NewUser, IReadOnlyCollection<string>, CreateUserResult>? CreateResult { get; set; }

    public PasswordCheckResult PasswordCheck { get; set; } = new(PasswordCheckStatus.InvalidCredentials);

    public UserAccount? ExistingUser { get; set; }

    public Task<CreateUserResult> CreateUserAsync(NewUser user, string password, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        CreateCalls++;
        CreatedUser = user;
        CreatedPassword = password;
        CreatedRoles = roles;
        CreateUserResult result = CreateResult?.Invoke(user, roles)
            ?? new CreateUserResult.Created(AccountBuilder.From(user, [.. roles]));
        return Task.FromResult(result);
    }

    public Task<PasswordCheckResult> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        CheckedEmail = email;
        return Task.FromResult(PasswordCheck);
    }

    public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(ExistingUser is { } u && u.Id == userId ? u : null);

    public Task RecordLoginAsync(Guid userId, DateTimeOffset loggedInAt, CancellationToken cancellationToken)
    {
        RecordLoginCalls++;
        RecordedLogin = (userId, loggedInAt);
        return Task.CompletedTask;
    }

    public Task<bool> IsSecurityStampValidAsync(Guid userId, string securityStamp, CancellationToken cancellationToken) =>
        Task.FromResult(true);

    public Dictionary<Guid, MemberContact> Contacts { get; } = [];

    public int GetContactsCalls { get; private set; }

    public Task<IReadOnlyDictionary<Guid, MemberContact>> GetContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        GetContactsCalls++;
        IReadOnlyDictionary<Guid, MemberContact> found = userIds.Distinct()
            .Where(Contacts.ContainsKey)
            .ToDictionary(id => id, id => Contacts[id]);
        return Task.FromResult(found);
    }
}

internal sealed class FakeTokenService : ITokenService
{
    public UserAccount? LastUser { get; private set; }

    public AccessToken CreateAccessToken(UserAccount user)
    {
        LastUser = user;
        return new AccessToken("fake-token", new DateTimeOffset(2026, 11, 10, 13, 0, 0, TimeSpan.Zero));
    }
}

internal static class AccountBuilder
{
    public static UserAccount Default(
        Guid? id = null,
        IReadOnlyList<string>? roles = null,
        DateTimeOffset? lastLoginAt = null) => new(
        id ?? Guid.NewGuid(),
        "Salma",
        "Ben Ali",
        "salma@exemple.tn",
        "+21620123456",
        roles ?? ["User"],
        new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero),
        "1.0",
        lastLoginAt,
        "stamp-123");

    public static UserAccount From(NewUser user, IReadOnlyList<string> roles) => new(
        Guid.NewGuid(),
        user.FirstName,
        user.LastName,
        user.Email,
        user.PhoneNumber.Value,
        roles,
        user.RegisteredAt,
        user.RegisteredAt,
        user.PrivacyPolicyVersion,
        user.RegisteredAt,
        "stamp-123");
}
