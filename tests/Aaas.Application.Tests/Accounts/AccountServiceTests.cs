using Aaas.Application.Accounts;
using Aaas.Application.Common.Errors;
using Aaas.Application.Options;
using Aaas.Domain.Common;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Aaas.Application.Tests.Accounts;

public class AccountServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeIdentityService _identity = new();
    private readonly FakeTokenService _tokens = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly AccountService _sut;

    public AccountServiceTests()
    {
        _sut = new AccountService(
            _identity,
            _tokens,
            _time,
            Microsoft.Extensions.Options.Options.Create(new PrivacyPolicyOptions { CurrentVersion = "2.3" }));
    }

    private static RegisterRequest ValidRegistration(
        string firstName = "Salma",
        string lastName = "Ben Ali",
        string email = "salma@exemple.tn",
        string phone = "+216 20 123 456",
        string password = "motdepasse1",
        bool consent = true) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        PhoneNumber = phone,
        Password = password,
        AcceptPrivacyPolicy = consent,
    };

    // ---------- Inscription (US-A1) ----------

    [Fact(DisplayName = "Inscription : cas nominal, compte créé avec jeton et données du compte")]
    public async Task Register_valid_request_returns_token_and_account()
    {
        var response = await _sut.RegisterAsync(ValidRegistration(), CancellationToken.None);

        Assert.Equal("fake-token", response.AccessToken);
        Assert.Equal("salma@exemple.tn", response.User.Email);
        Assert.Equal("+21620123456", response.User.PhoneNumber);
        Assert.Equal(1, _identity.CreateCalls);
        Assert.Equal("motdepasse1", _identity.CreatedPassword);
    }

    [Fact(DisplayName = "Inscription : sans consentement, refus sur le champ acceptPrivacyPolicy et aucun compte créé")]
    public async Task Register_without_consent_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => _sut.RegisterAsync(ValidRegistration(consent: false), CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("acceptPrivacyPolicy"));
        Assert.Equal(0, _identity.CreateCalls);
    }

    [Theory(DisplayName = "Inscription : téléphone non tunisien refusé sur le champ phoneNumber, aucun compte créé")]
    [InlineData("20 123 456")]
    [InlineData("0021620123456")]
    [InlineData("+33 6 12 34 56 78")]
    [InlineData("+216 2012345")]
    [InlineData("")]
    public async Task Register_invalid_phone_is_rejected(string phone)
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => _sut.RegisterAsync(ValidRegistration(phone: phone), CancellationToken.None));

        Assert.Equal([PhoneNumber.ExpectedFormatMessage], ex.Errors["phoneNumber"]);
        Assert.Equal(0, _identity.CreateCalls);
    }

    [Fact(DisplayName = "Inscription : le rôle imposé est toujours User (jamais Admin)")]
    public async Task Register_always_imposes_user_role()
    {
        var response = await _sut.RegisterAsync(ValidRegistration(), CancellationToken.None);

        Assert.Equal([Roles.User], _identity.CreatedRoles);
        Assert.Equal([Roles.User], response.User.Roles);
        Assert.DoesNotContain(Roles.Admin, _identity.CreatedRoles!);
    }

    [Fact(DisplayName = "Inscription : prénom, nom et e-mail sont débarrassés de leurs espaces de bord")]
    public async Task Register_trims_fields()
    {
        await _sut.RegisterAsync(
            ValidRegistration(firstName: "  Salma ", lastName: "\tBen Ali  ", email: "  salma@exemple.tn "),
            CancellationToken.None);

        var created = _identity.CreatedUser!;
        Assert.Equal("Salma", created.FirstName);
        Assert.Equal("Ben Ali", created.LastName);
        Assert.Equal("salma@exemple.tn", created.Email);
    }

    [Fact(DisplayName = "Inscription : le mot de passe n'est pas modifié (pas de trim)")]
    public async Task Register_does_not_alter_password()
    {
        await _sut.RegisterAsync(ValidRegistration(password: "  mot de passe 1 "), CancellationToken.None);

        Assert.Equal("  mot de passe 1 ", _identity.CreatedPassword);
    }

    [Fact(DisplayName = "Inscription : téléphone normalisé, date et version de politique issues de l'horloge et de la configuration")]
    public async Task Register_normalizes_phone_and_stamps_consent()
    {
        await _sut.RegisterAsync(ValidRegistration(phone: "+216 20 123 456"), CancellationToken.None);

        var created = _identity.CreatedUser!;
        Assert.Equal("+21620123456", created.PhoneNumber.Value);
        Assert.Equal(Now, created.RegisteredAt);
        Assert.Equal("2.3", created.PrivacyPolicyVersion);
    }

    [Fact(DisplayName = "Inscription : e-mail déjà utilisé donne email_taken")]
    public async Task Register_existing_email_throws_email_taken()
    {
        _identity.CreateResult = (_, _) => new CreateUserResult.EmailAlreadyUsed();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.RegisterAsync(ValidRegistration(), CancellationToken.None));

        Assert.Equal(ErrorCodes.EmailTaken, ex.Code);
    }

    [Fact(DisplayName = "Inscription : les erreurs de mot de passe d'Identity deviennent une erreur de validation par champ")]
    public async Task Register_identity_errors_become_validation_errors()
    {
        var errors = new Dictionary<string, string[]> { ["password"] = ["Le mot de passe doit contenir au moins un chiffre."] };
        _identity.CreateResult = (_, _) => new CreateUserResult.Invalid(errors);

        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => _sut.RegisterAsync(ValidRegistration(password: "abcdefgh"), CancellationToken.None));

        Assert.Same(errors, ex.Errors);
    }

    // ---------- Connexion (US-A2) ----------

    [Fact(DisplayName = "Connexion : succès, LastLoginAt mis à jour à l'heure courante")]
    public async Task Login_success_records_last_login()
    {
        var user = AccountBuilder.Default(lastLoginAt: Now.AddDays(-30));
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.Succeeded, user);

        var response = await _sut.LoginAsync(new LoginRequest { Email = "salma@exemple.tn", Password = "motdepasse1" }, CancellationToken.None);

        Assert.Equal(1, _identity.RecordLoginCalls);
        Assert.Equal((user.Id, Now), _identity.RecordedLogin);
        Assert.Equal(Now, response.User.LastLoginAt);
        Assert.Equal("fake-token", response.AccessToken);
    }

    [Fact(DisplayName = "Connexion : LastLoginAt suit l'horloge injectée")]
    public async Task Login_uses_injected_clock()
    {
        var user = AccountBuilder.Default();
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.Succeeded, user);
        _time.Advance(TimeSpan.FromHours(5));

        var response = await _sut.LoginAsync(new LoginRequest { Email = "a@b.tn", Password = "x" }, CancellationToken.None);

        Assert.Equal(Now.AddHours(5), response.User.LastLoginAt);
    }

    [Fact(DisplayName = "Connexion : l'e-mail saisi est débarrassé de ses espaces de bord")]
    public async Task Login_trims_email()
    {
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.Succeeded, AccountBuilder.Default());

        await _sut.LoginAsync(new LoginRequest { Email = "  salma@exemple.tn ", Password = "motdepasse1" }, CancellationToken.None);

        Assert.Equal("salma@exemple.tn", _identity.CheckedEmail);
    }

    [Fact(DisplayName = "Connexion : échec, invalid_credentials et LastLoginAt non modifié")]
    public async Task Login_failure_does_not_record_login()
    {
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.InvalidCredentials);

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _sut.LoginAsync(new LoginRequest { Email = "salma@exemple.tn", Password = "faux" }, CancellationToken.None));

        Assert.Equal(ErrorCodes.InvalidCredentials, ex.Code);
        Assert.Equal(AccountService.InvalidCredentialsMessage, ex.Message);
        Assert.Equal(0, _identity.RecordLoginCalls);
    }

    [Theory(DisplayName = "Connexion bloquée : le message indique les minutes restantes (arrondies au supérieur, minimum 1)")]
    [InlineData(900, "15 minutes")]   // blocage tout juste posé
    [InlineData(841, "15 minutes")]   // 14 min 1 s restantes
    [InlineData(840, "14 minutes")]
    [InlineData(61, "2 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(30, "1 minute")]
    [InlineData(1, "1 minute")]
    [InlineData(0, "1 minute")]       // blocage déjà échu : jamais « 0 minute »
    [InlineData(-120, "1 minute")]
    public async Task Login_locked_out_reports_remaining_minutes(int secondsRemaining, string expectedFragment)
    {
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.LockedOut, LockoutEnd: Now.AddSeconds(secondsRemaining));

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _sut.LoginAsync(new LoginRequest { Email = "salma@exemple.tn", Password = "x" }, CancellationToken.None));

        Assert.Equal(ErrorCodes.AccountLocked, ex.Code);
        Assert.Contains($"réessayez dans {expectedFragment}.", ex.Message);
        Assert.Equal(0, _identity.RecordLoginCalls);
    }

    [Fact(DisplayName = "Connexion bloquée : sans date de fin, le message reste cohérent (1 minute)")]
    public async Task Login_locked_out_without_end_date_says_one_minute()
    {
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.LockedOut, LockoutEnd: null);

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _sut.LoginAsync(new LoginRequest { Email = "salma@exemple.tn", Password = "x" }, CancellationToken.None));

        Assert.Contains("1 minute.", ex.Message);
    }

    [Fact(DisplayName = "Connexion bloquée : le temps restant diminue quand l'horloge avance")]
    public async Task Login_locked_out_message_follows_clock()
    {
        _identity.PasswordCheck = new PasswordCheckResult(PasswordCheckStatus.LockedOut, LockoutEnd: Now.AddMinutes(15));
        _time.Advance(TimeSpan.FromMinutes(10));

        var ex = await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => _sut.LoginAsync(new LoginRequest { Email = "salma@exemple.tn", Password = "x" }, CancellationToken.None));

        Assert.Contains("5 minutes.", ex.Message);
    }

    // ---------- Mon compte ----------

    [Fact(DisplayName = "Mon compte : renvoie le compte sans donnée sensible")]
    public async Task GetCurrentAccount_returns_dto()
    {
        var user = AccountBuilder.Default();
        _identity.ExistingUser = user;

        var dto = await _sut.GetCurrentAccountAsync(user.Id, CancellationToken.None);

        Assert.Equal(user.Email, dto.Email);
        Assert.DoesNotContain(typeof(AccountDto).GetProperties(), p =>
            p.Name.Contains("Stamp", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Mon compte : compte supprimé, NotFoundException")]
    public async Task GetCurrentAccount_unknown_user_throws_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.GetCurrentAccountAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
