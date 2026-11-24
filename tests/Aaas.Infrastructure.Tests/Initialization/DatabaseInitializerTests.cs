using System.Reflection;
using Aaas.Application.Accounts;
using Aaas.Application.Options;
using Aaas.Domain.Common;
using Aaas.Infrastructure.Identity;
using Aaas.Infrastructure.Initialization;
using Aaas.Infrastructure.Persistence;
using Aaas.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Infrastructure.Tests.Initialization;

/// <summary>
/// ADR 0008 : premier administrateur, y compris la course au démarrage (deux instances, ou inscription glissée
/// dans l'intervalle). Aucune base : UserManager/RoleManager/IIdentityService sont des doublures, et l'AppDbContext
/// n'est jamais interrogé (MigrateOnStartup = false) ; il sert uniquement de ChangeTracker.
/// </summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private const string AdminEmail = "admin@example.org";
    private const string AdminPassword = "Sup3rS3cret-Pwd";

    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=inexistant;Database=x;User Id=sa;Password=x;TrustServerCertificate=True").Options);

    private readonly FakeUserManager _users = new();
    private readonly FakeRoleManager _roles = new();
    private readonly FakeIdentityService _identity = new();
    private readonly CapturingLogger _logger = new();
    private readonly BootstrapOptions _bootstrap = new()
    {
        AdminEmail = AdminEmail,
        AdminPassword = AdminPassword,
        AdminFirstName = "Amel",
        AdminLastName = "Ben Salah",
        AdminPhone = "+216 20 123 456",
    };

    public DatabaseInitializerTests()
    {
        _users.Db = _db;
        _roles.ExistingRoles.UnionWith(Roles.All); // rôles déjà présents, sauf test dédié
    }

    public void Dispose() => _db.Dispose();

    private Task RunAsync() => new DatabaseInitializer(
        _db,
        _roles,
        _users,
        _identity,
        new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero)),
        Options.Create(new DatabaseOptions { MigrateOnStartup = false }),
        Options.Create(_bootstrap),
        Options.Create(new PrivacyPolicyOptions { CurrentVersion = "1.0" }),
        _logger).InitializeAsync(CancellationToken.None);

    /// <summary>Simule le résidu d'un INSERT refusé : l'entité reste suivie à l'état Added (cf. HandleEmailTakenConcurrentlyAsync).</summary>
    private void LeaveRejectedInsertInChangeTracker() => _db.Users.Add(new AppUser { UserName = AdminEmail, Email = AdminEmail });

    private AppUser Member(params string[] roles)
    {
        var user = new AppUser { Id = Guid.NewGuid(), UserName = AdminEmail, Email = AdminEmail };
        _users.RolesByUser[user.Id] = [.. roles];
        return user;
    }

    private List<string> RolesOf(AppUser user) => _users.RolesByUser[user.Id];

    private void AssertNoSecretInOutput(Exception? exception = null)
    {
        var texts = _logger.Messages.Concat(exception is null ? [] : [exception.Message]);
        foreach (var text in texts)
        {
            Assert.DoesNotContain(AdminEmail, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AdminPassword, text, StringComparison.Ordinal);
        }
    }

    // ---------- Course au démarrage : l'INSERT échoue sur « e-mail déjà utilisé » ----------

    [Fact(DisplayName = "Course : e-mail pris puis admin créé par l'autre instance -> rien à faire, aucune exception")]
    public async Task Race_EmailTaken_AdminCreatedByOtherInstance_DoesNothing()
    {
        _identity.OnCreate = () =>
        {
            LeaveRejectedInsertInChangeTracker();
            _users.Admins.Add(Member(Roles.User, Roles.Admin)); // l'autre instance a gagné
            return new CreateUserResult.EmailAlreadyUsed();
        };

        await RunAsync();

        Assert.Equal(1, _identity.CreateCalls);
        Assert.Equal(1, _users.FindByEmailCalls); // seulement la recherche initiale : pas de seconde recherche, l'admin existe
        Assert.Empty(_users.RoleAdditions);
        Assert.Equal(0, _users.SecurityStampUpdates);
        AssertNoSecretInOutput();
    }

    [Fact(DisplayName = "Course : le ChangeTracker est vidé avant de revérifier qu'un admin existe")]
    public async Task Race_EmailTaken_ChangeTrackerIsClearedBeforeRecheck()
    {
        _identity.OnCreate = () =>
        {
            LeaveRejectedInsertInChangeTracker();
            Assert.NotEmpty(_db.ChangeTracker.Entries()); // précondition : le résidu est bien là
            _users.Admins.Add(Member(Roles.User, Roles.Admin));
            return new CreateUserResult.EmailAlreadyUsed();
        };

        await RunAsync();

        // Premier GetUsersInRoleAsync (avant création) : tracker vide ; second (revérification) : doit l'être aussi.
        Assert.Equal(2, _users.TrackedEntriesAtGetUsersInRole.Count);
        Assert.All(_users.TrackedEntriesAtGetUsersInRole, count => Assert.Equal(0, count));
        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact(DisplayName = "Course : e-mail pris, aucun admin, option absente -> arrêt, message sans e-mail, aucun rôle donné")]
    public async Task Race_EmailTaken_NoAdmin_WithoutOption_ThrowsWithoutEmailAndGrantsNothing()
    {
        var registered = Member(Roles.User);
        _identity.OnCreate = () =>
        {
            LeaveRejectedInsertInChangeTracker();
            _users.Accounts[AdminEmail] = registered; // inscription glissée dans l'intervalle
            return new CreateUserResult.EmailAlreadyUsed();
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Contains("PromoteExistingAccount", ex.Message);
        AssertNoSecretInOutput(ex);
        Assert.Empty(_users.RoleAdditions);
        Assert.Equal(0, _users.SecurityStampUpdates);
        Assert.Equal([Roles.User], RolesOf(registered));
        Assert.Empty(_db.ChangeTracker.Entries()); // Clear() a eu lieu avant la décision
    }

    [Fact(DisplayName = "Course : e-mail pris, aucun admin, PromoteExistingAccount=true -> compte promu [User, Admin], stamp renouvelé")]
    public async Task Race_EmailTaken_NoAdmin_WithOption_PromotesAccount()
    {
        _bootstrap.PromoteExistingAccount = true;
        var registered = Member(Roles.User);
        _identity.OnCreate = () =>
        {
            LeaveRejectedInsertInChangeTracker();
            _users.Accounts[AdminEmail] = registered;
            return new CreateUserResult.EmailAlreadyUsed();
        };

        await RunAsync();

        Assert.Equal(new[] { Roles.User, Roles.Admin }, RolesOf(registered).OrderByDescending(r => r));
        Assert.Equal([(registered, Roles.Admin)], _users.RoleAdditions);
        Assert.Equal(1, _users.SecurityStampUpdates);
        Assert.All(_users.TrackedEntriesAtFindByEmail, count => Assert.Equal(0, count));
        AssertNoSecretInOutput();
    }

    [Fact(DisplayName = "Course : e-mail pris mais compte introuvable et aucun admin -> InvalidOperationException explicite")]
    public async Task Race_EmailTaken_AccountVanished_Throws()
    {
        _identity.OnCreate = () => new CreateUserResult.EmailAlreadyUsed();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Contains("introuvable", ex.Message);
        AssertNoSecretInOutput(ex);
        Assert.Empty(_users.RoleAdditions);
    }

    // ---------- Compte existant dès la première vérification (point A1) ----------

    [Fact(DisplayName = "Compte existant, aucun admin, option absente -> arrêt, message sans e-mail ni mot de passe, aucun rôle Admin")]
    public async Task ExistingAccount_NoAdmin_WithoutOption_ThrowsAndGrantsNoAdminRole()
    {
        var existing = Member(Roles.User);
        _users.Accounts[AdminEmail] = existing;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Contains("Bootstrap:AdminEmail", ex.Message); // le NOM du réglage, pas sa valeur
        AssertNoSecretInOutput(ex);
        Assert.Empty(_users.RoleAdditions);
        Assert.Equal(0, _users.SecurityStampUpdates);
        Assert.DoesNotContain(Roles.Admin, RolesOf(existing));
        Assert.Equal(0, _identity.CreateCalls);
    }

    [Fact(DisplayName = "Compte existant, aucun admin, PromoteExistingAccount=true -> rôle Admin ajouté, User conservé, stamp renouvelé")]
    public async Task ExistingAccount_NoAdmin_WithOption_Promotes()
    {
        _bootstrap.PromoteExistingAccount = true;
        var existing = Member(Roles.User);
        _users.Accounts[AdminEmail] = existing;

        await RunAsync();

        Assert.Contains(Roles.Admin, RolesOf(existing));
        Assert.Contains(Roles.User, RolesOf(existing));
        Assert.Equal(1, _users.SecurityStampUpdates);
        Assert.Equal(0, _identity.CreateCalls); // ni création ni changement de mot de passe
        // Trace d'audit : Warning (visible même avec un niveau relevé), sans l'e-mail (R24).
        var audit = Assert.Single(_logger.Entries, e => e.Message.Contains("Audit", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, audit.Level);
        Assert.Contains(existing.Id.ToString(), audit.Message);
        Assert.DoesNotContain(_logger.Entries, e => e.Level >= LogLevel.Error);
        AssertNoSecretInOutput();
    }

    [Fact(DisplayName = "Promotion d'un compte sans rôle User -> User et Admin sont tous deux ajoutés")]
    public async Task ExistingAccount_WithoutUserRole_Promotion_AddsUserAndAdmin()
    {
        _bootstrap.PromoteExistingAccount = true;
        var existing = Member();
        _users.Accounts[AdminEmail] = existing;

        await RunAsync();

        Assert.Equal([Roles.User, Roles.Admin], _users.RoleAdditions.Select(a => a.Role).ToArray());
        Assert.Equal(1, _users.SecurityStampUpdates);
    }

    [Fact(DisplayName = "Admin déjà présent + compte existant -> rien n'est fait, aucune recherche par e-mail")]
    public async Task AdminAlreadyExists_DoesNothing_EvenWithExistingAccountAndOption()
    {
        _bootstrap.PromoteExistingAccount = true;
        _users.Admins.Add(Member(Roles.User, Roles.Admin));
        _users.Accounts[AdminEmail] = Member(Roles.User);

        await RunAsync();

        Assert.Equal(0, _users.FindByEmailCalls);
        Assert.Equal(0, _identity.CreateCalls);
        Assert.Empty(_users.RoleAdditions);
        Assert.Equal(0, _users.SecurityStampUpdates);
    }

    // ---------- Création nominale et erreurs ----------

    [Fact(DisplayName = "Aucun admin, aucun compte -> création avec les rôles User et Admin et les données de la configuration")]
    public async Task NoAdmin_NoAccount_CreatesAdminWithBothRoles()
    {
        await RunAsync();

        Assert.Equal(1, _identity.CreateCalls);
        Assert.Equal([Roles.User, Roles.Admin], _identity.LastRoles);
        Assert.Equal(AdminPassword, _identity.LastPassword);
        Assert.Equal(AdminEmail, _identity.LastUser!.Email);
        Assert.Equal("+21620123456", _identity.LastUser.PhoneNumber.Value);
        Assert.Equal("1.0", _identity.LastUser.PrivacyPolicyVersion);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero), _identity.LastUser.RegisteredAt);
        Assert.Empty(_users.RoleAdditions);
    }

    [Fact(DisplayName = "Configuration incomplète et aucun admin -> avertissement, pas d'exception, pas de création")]
    public async Task IncompleteConfiguration_WarnsAndDoesNotCreate()
    {
        _bootstrap.AdminPassword = null;

        await RunAsync();

        Assert.Equal(0, _identity.CreateCalls);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact(DisplayName = "Création refusée par Identity (mot de passe faible) -> arrêt avec le motif, sans le mot de passe")]
    public async Task Invalid_Configuration_Throws()
    {
        _identity.OnCreate = () => new CreateUserResult.Invalid(new Dictionary<string, string[]> { ["password"] = ["Le mot de passe doit contenir 8 caractères."] });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Contains("8 caractères", ex.Message);
        AssertNoSecretInOutput(ex);
    }

    [Fact(DisplayName = "Mot de passe d'exemple public -> arrêt avant toute création")]
    public async Task KnownExamplePassword_Throws()
    {
        _bootstrap.AdminPassword = "ChangeMe2026";

        await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Equal(0, _identity.CreateCalls);
    }

    [Theory(DisplayName = "Téléphone hors format -> arrêt avant toute création")]
    [InlineData("20123456")]
    [InlineData("0021620123456")]
    [InlineData("+33 6 12 34 56 78")]
    public async Task InvalidPhone_Throws(string phone)
    {
        _bootstrap.AdminPhone = phone;

        await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Equal(0, _identity.CreateCalls);
    }

    [Fact(DisplayName = "Rôles manquants -> Admin et User sont créés ; rôles présents -> rien")]
    public async Task MissingRoles_AreCreated()
    {
        _roles.ExistingRoles.Clear();
        _users.Admins.Add(Member(Roles.User, Roles.Admin)); // isole le test de la partie admin

        await RunAsync();

        Assert.Equivalent(Roles.All, _roles.Created, strict: false);
        Assert.Equal(2, _roles.Created.Count);
    }

    // ---------- EnsureRolesAsync : course à la création des rôles ----------

    private static DbUpdateException UniqueViolation() => new("save", SqlExceptionFactory.Create(2601));

    [Fact(DisplayName = "Rôle : CreateAsync lève 2601 puis le rôle existe -> aucune exception, ChangeTracker vidé")]
    public async Task EnsureRoles_UniqueViolation_RoleNowExists_Continues()
    {
        _roles.ExistingRoles.Clear();
        _users.Admins.Add(Member(Roles.User, Roles.Admin)); // isole la partie admin
        _roles.OnCreate = role =>
        {
            _db.Roles.Add(new IdentityRole<Guid>(role)); // résidu de l'INSERT refusé
            Assert.NotEmpty(_db.ChangeTracker.Entries());
            _roles.ExistingRoles.Add(role); // l'autre instance l'a créé
            throw UniqueViolation();
        };

        await RunAsync();

        Assert.Empty(_db.ChangeTracker.Entries());
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("en parallèle", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Rôle : RoleExistsAsync répond false puis true autour d'un 2601 -> la seconde vérification tranche")]
    public async Task EnsureRoles_UniqueViolation_ExistsFalseThenTrue_Continues()
    {
        _roles.ExistingRoles.Clear();
        _users.Admins.Add(Member(Roles.User, Roles.Admin));
        _roles.ExistsAnswers[Roles.Admin] = new Queue<bool>([false, true]); // avant création, puis après l'échec
        _roles.ExistingRoles.Add(Roles.User);
        _roles.OnCreate = _ => throw UniqueViolation();

        await RunAsync();

        Assert.Equal(1, _roles.CreateCalls);
    }

    [Fact(DisplayName = "Rôle : 2601 et le rôle manque toujours -> l'exception est relancée")]
    public async Task EnsureRoles_UniqueViolation_RoleStillMissing_Rethrows()
    {
        _roles.ExistingRoles.Clear();
        _roles.OnCreate = _ => throw UniqueViolation();

        var ex = await Assert.ThrowsAsync<DbUpdateException>(RunAsync);

        Assert.IsAssignableFrom<Microsoft.Data.SqlClient.SqlException>(ex.InnerException);
    }

    [Fact(DisplayName = "Rôle : DbUpdateException qui n'est pas une violation d'unicité -> relancée, même si le rôle existe")]
    public async Task EnsureRoles_OtherDbUpdateException_Rethrows()
    {
        _roles.ExistingRoles.Clear();
        _roles.OnCreate = role =>
        {
            _roles.ExistingRoles.Add(role);
            throw new DbUpdateException("save", SqlExceptionFactory.Create(18456));
        };

        await Assert.ThrowsAsync<DbUpdateException>(RunAsync);
    }

    [Fact(DisplayName = "Rôle : CreateAsync renvoie DuplicateRoleName et le rôle existe -> on continue")]
    public async Task EnsureRoles_DuplicateRoleName_RoleExists_Continues()
    {
        _roles.ExistingRoles.Clear();
        _users.Admins.Add(Member(Roles.User, Roles.Admin));
        _roles.OnCreate = role =>
        {
            _roles.ExistingRoles.Add(role);
            return IdentityResult.Failed(new IdentityError { Code = "DuplicateRoleName", Description = "doublon" });
        };

        await RunAsync();

        Assert.Equal(Roles.All.Count, _roles.CreateCalls);
    }

    [Fact(DisplayName = "Rôle : CreateAsync échoue et le rôle n'existe pas -> InvalidOperationException avec le motif")]
    public async Task EnsureRoles_FailedAndMissing_Throws()
    {
        _roles.ExistingRoles.Clear();
        _roles.OnCreate = _ => IdentityResult.Failed(new IdentityError { Code = "X", Description = "motif-precis" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(RunAsync);

        Assert.Contains("motif-precis", ex.Message);
    }

    // ---------- Doublures ----------

    private sealed class FakeUserManager : UserManager<AppUser>
    {
        public FakeUserManager()
            : base(UnusedProxy.Create<IUserStore<AppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!)
        {
        }

        public AppDbContext? Db { get; set; }
        public List<AppUser> Admins { get; } = [];
        public Dictionary<string, AppUser> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<Guid, List<string>> RolesByUser { get; } = [];
        public List<(AppUser User, string Role)> RoleAdditions { get; } = [];
        public int SecurityStampUpdates { get; private set; }
        public int FindByEmailCalls { get; private set; }
        public List<int> TrackedEntriesAtGetUsersInRole { get; } = [];
        public List<int> TrackedEntriesAtFindByEmail { get; } = [];

        public override Task<IList<AppUser>> GetUsersInRoleAsync(string roleName)
        {
            TrackedEntriesAtGetUsersInRole.Add(Db!.ChangeTracker.Entries().Count());
            return Task.FromResult<IList<AppUser>>(roleName == Roles.Admin ? [.. Admins] : []);
        }

        public override Task<AppUser?> FindByEmailAsync(string email)
        {
            FindByEmailCalls++;
            TrackedEntriesAtFindByEmail.Add(Db!.ChangeTracker.Entries().Count());
            return Task.FromResult(Accounts.GetValueOrDefault(email));
        }

        public override Task<bool> IsInRoleAsync(AppUser user, string role) => Task.FromResult(RolesByUser[user.Id].Contains(role));

        public override Task<IdentityResult> AddToRoleAsync(AppUser user, string role)
        {
            RoleAdditions.Add((user, role));
            RolesByUser[user.Id].Add(role);
            return Task.FromResult(IdentityResult.Success);
        }

        public override Task<IdentityResult> UpdateSecurityStampAsync(AppUser user)
        {
            SecurityStampUpdates++;
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class FakeRoleManager : RoleManager<IdentityRole<Guid>>
    {
        public FakeRoleManager()
            : base(UnusedProxy.Create<IRoleStore<IdentityRole<Guid>>>(), [], null!, null!, null!)
        {
        }

        public HashSet<string> ExistingRoles { get; } = [];
        public List<string> Created { get; } = [];
        public int CreateCalls { get; private set; }

        /// <summary>Comportement de CreateAsync (peut lever) ; par défaut : crée le rôle.</summary>
        public Func<string, IdentityResult>? OnCreate { get; set; }

        /// <summary>Réponses successives de RoleExistsAsync pour un rôle donné (puis retour à ExistingRoles).</summary>
        public Dictionary<string, Queue<bool>> ExistsAnswers { get; } = [];

        public override Task<bool> RoleExistsAsync(string roleName) =>
            Task.FromResult(ExistsAnswers.TryGetValue(roleName, out var answers) && answers.Count > 0
                ? answers.Dequeue()
                : ExistingRoles.Contains(roleName));

        public override Task<IdentityResult> CreateAsync(IdentityRole<Guid> role)
        {
            CreateCalls++;
            Created.Add(role.Name!);
            if (OnCreate is not null)
            {
                return Task.FromResult(OnCreate(role.Name!));
            }

            ExistingRoles.Add(role.Name!);
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class FakeIdentityService : IIdentityService
    {
        public Func<CreateUserResult>? OnCreate { get; set; }
        public int CreateCalls { get; private set; }
        public NewUser? LastUser { get; private set; }
        public string? LastPassword { get; private set; }
        public string[] LastRoles { get; private set; } = [];

        public Task<CreateUserResult> CreateUserAsync(NewUser user, string password, IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
        {
            CreateCalls++;
            LastUser = user;
            LastPassword = password;
            LastRoles = [.. roles];
            var result = OnCreate?.Invoke()
                ?? new CreateUserResult.Created(new UserAccount(Guid.NewGuid(), user.FirstName, user.LastName, user.Email,
                    user.PhoneNumber.Value, [.. roles], user.RegisteredAt, user.RegisteredAt, user.PrivacyPolicyVersion, user.RegisteredAt, "stamp"));
            return Task.FromResult(result);
        }

        public Task<PasswordCheckResult> CheckPasswordAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordLoginAsync(Guid userId, DateTimeOffset loggedInAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> IsSecurityStampValidAsync(Guid userId, string securityStamp, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, MemberContact>> GetContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>Magasin Identity jamais utilisé (les managers surchargent toutes les méthodes appelées) : toute utilisation échoue.</summary>
#pragma warning disable CA1852 // DispatchProxy exige un type de base non scellé
    public class UnusedProxy : DispatchProxy
    {
        public static T Create<T>() where T : class => Create<T, UnusedProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IDisposable.Dispose)
                ? null
                : throw new NotSupportedException($"Appel inattendu au magasin Identity : {targetMethod?.Name}");
    }

    private sealed class CapturingLogger : ILogger<DatabaseInitializer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IEnumerable<string> Messages => Entries.Select(e => e.Message);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
