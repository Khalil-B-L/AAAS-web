using Aaas.Infrastructure;
using Aaas.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aaas.Infrastructure.Tests.Identity;

/// <summary>
/// R21 : au moins 8 caractères dont une lettre et un chiffre. On exerce les validateurs réellement configurés par
/// AddInfrastructure (longueur et chiffre : Identity ; lettre : LetterRequiredPasswordValidator), sans base de données.
/// </summary>
public sealed class PasswordPolicyTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public PasswordPolicyTests()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=inexistant;Database=x;User Id=sa;Password=x;TrustServerCertificate=True",
            ["Jwt:SigningKey"] = new string('k', 48),
        }).Build();

        _provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private async Task<IdentityResult> ValidateAsync(string password)
    {
        using var scope = _provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var errors = new List<IdentityError>();
        foreach (var validator in manager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(manager, new AppUser(), password);
            errors.AddRange(result.Errors);
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }

    [Theory(DisplayName = "Mots de passe refusés : trop court, sans chiffre, sans lettre")]
    [InlineData("abc")]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    [InlineData("abcdef1")]      // 7 caractères
    [InlineData("1234567")]
    [InlineData("!!!!!!!!")]
    [InlineData("")]
    public async Task Weak_passwords_are_rejected(string password)
    {
        var result = await ValidateAsync(password);

        Assert.False(result.Succeeded);
    }

    [Theory(DisplayName = "Mots de passe acceptés : 8 caractères, une lettre (toute casse) et un chiffre")]
    [InlineData("abcdefg1")]
    [InlineData("ABCDEFG1")]       // majuscules seules : accepté (R21 ne demande pas de minuscule)
    [InlineData("1abcdefg")]
    [InlineData("motdepasse1")]
    [InlineData("élèvé-ça1")]      // lettres accentuées
    [InlineData("a1!!!!!!")]
    public async Task Compliant_passwords_are_accepted(string password)
    {
        var result = await ValidateAsync(password);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors.Select(e => e.Description)));
    }

    [Fact(DisplayName = "Sans chiffre : message d'erreur en français")]
    public async Task Missing_digit_message_is_french()
    {
        var result = await ValidateAsync("abcdefgh");

        Assert.Contains(result.Errors, e => e.Description.Contains("chiffre", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "Sans lettre : code PasswordRequiresLetter et message en français")]
    public async Task Missing_letter_reports_letter_error()
    {
        var result = await ValidateAsync("12345678");

        var error = Assert.Single(result.Errors);
        Assert.Equal(LetterRequiredPasswordValidator.ErrorCode, error.Code);
        Assert.Contains("lettre", error.Description);
    }

    [Fact(DisplayName = "Trop court : message d'erreur en français")]
    public async Task Too_short_message_is_french()
    {
        var result = await ValidateAsync("abc");

        Assert.Contains(result.Errors, e => e.Description.Contains("8 caractères"));
    }
}
