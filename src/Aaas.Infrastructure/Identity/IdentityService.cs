using Aaas.Application.Accounts;
using Aaas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Infrastructure.Identity;

/// <summary>
/// Adaptateur entre le port <see cref="IIdentityService"/> et ASP.NET Core Identity.
/// Note : UserManager n'accepte pas de CancellationToken ; il n'est transmis qu'aux requêtes EF directes.
/// </summary>
internal sealed class IdentityService(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    AppDbContext db) : IIdentityService
{
    public async Task<CreateUserResult> CreateUserAsync(
        NewUser newUser, string password, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        // EnableRetryOnFailure interdit une transaction ouverte hors d'une « execution strategy » :
        // le bloc entier (compte + rôles) est rejouable, et atomique grâce à la transaction.
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async ct =>
            {
                db.ChangeTracker.Clear(); // en cas de nouvel essai, repartir d'un suivi vide
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                var user = new AppUser
                {
                    UserName = newUser.Email,
                    Email = newUser.Email,
                    PhoneNumber = newUser.PhoneNumber.Value,
                    FirstName = newUser.FirstName,
                    LastName = newUser.LastName,
                    CreatedAt = newUser.RegisteredAt,
                    PrivacyConsentAt = newUser.RegisteredAt,
                    PrivacyPolicyVersion = newUser.PrivacyPolicyVersion,
                    LastLoginAt = newUser.RegisteredAt, // l'inscription connecte l'utilisateur (US-A1)
                };

                var created = await userManager.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    return MapCreationErrors(created.Errors); // transaction annulée au Dispose
                }

                var addedToRoles = await userManager.AddToRolesAsync(user, roles);
                if (!addedToRoles.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Ajout des rôles impossible : " + string.Join(" ", addedToRoles.Errors.Select(e => e.Code)));
                }

                await transaction.CommitAsync(ct);
                return (CreateUserResult)new CreateUserResult.Created(ToAccount(user, [.. roles]));
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueKeyViolation(ex))
        {
            // Deux inscriptions simultanées avec le même e-mail : le SELECT d'Identity n'a rien vu,
            // l'index unique UX_AspNetUsers_NormalizedEmail a tranché.
            return new CreateUserResult.EmailAlreadyUsed();
        }
    }

    public async Task<PasswordCheckResult> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            return new PasswordCheckResult(PasswordCheckStatus.InvalidCredentials);
        }

        // lockoutOnFailure: true => compteur d'échecs, blocage au 5e échec consécutif, remise à zéro au succès.
        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var roles = await userManager.GetRolesAsync(user);
            return new PasswordCheckResult(PasswordCheckStatus.Succeeded, ToAccount(user, [.. roles]));
        }

        return result.IsLockedOut
            ? new PasswordCheckResult(PasswordCheckStatus.LockedOut, LockoutEnd: user.LockoutEnd)
            : new PasswordCheckResult(PasswordCheckStatus.InvalidCredentials);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            orderby role.Name
            select role.Name!).ToListAsync(cancellationToken);

        return ToAccount(user, roles);
    }

    public Task RecordLoginAsync(Guid userId, DateTimeOffset loggedInAt, CancellationToken cancellationToken) =>
        // Un UPDATE ciblé : ne touche ni au ConcurrencyStamp ni aux autres colonnes gérées par Identity.
        db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastLoginAt, loggedInAt), cancellationToken);

    public Task<bool> IsSecurityStampValidAsync(Guid userId, string securityStamp, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId && u.SecurityStamp == securityStamp, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, MemberContact>> GetContactsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, MemberContact>();
        }

        var ids = userIds.Distinct().ToList();
        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new MemberContact(u.Id, u.FirstName, u.LastName, u.Email ?? string.Empty, u.PhoneNumber ?? string.Empty))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    private static UserAccount ToAccount(AppUser user, IReadOnlyList<string> roles) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.Email ?? string.Empty,
        user.PhoneNumber ?? string.Empty,
        roles,
        user.CreatedAt,
        user.PrivacyConsentAt,
        user.PrivacyPolicyVersion,
        user.LastLoginAt,
        user.SecurityStamp ?? string.Empty);

    private static CreateUserResult MapCreationErrors(IEnumerable<IdentityError> errors)
    {
        var list = errors.ToList();
        if (list.Exists(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
        {
            return new CreateUserResult.EmailAlreadyUsed();
        }

        var byField = list
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password"
                : e.Code is nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) ? "email"
                : string.Empty)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray());
        return new CreateUserResult.Invalid(byField);
    }
}
