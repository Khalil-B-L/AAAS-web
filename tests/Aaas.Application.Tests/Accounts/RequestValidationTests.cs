using System.ComponentModel.DataAnnotations;
using Aaas.Application.Accounts;

namespace Aaas.Application.Tests.Accounts;

/// <summary>Validation déclarative des DTO (appliquée par [ApiController] avant le service).</summary>
public class RequestValidationTests
{
    private static RegisterRequest Valid() => new()
    {
        FirstName = "Salma",
        LastName = "Ben Ali",
        Email = "salma@exemple.tn",
        PhoneNumber = "+216 20 123 456",
        Password = "motdepasse1",
        AcceptPrivacyPolicy = true,
    };

    private static Dictionary<string, string> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results
            .SelectMany(r => r.MemberNames.Select(m => (Member: m, r.ErrorMessage)))
            .ToDictionary(x => x.Member, x => x.ErrorMessage ?? string.Empty);
    }

    [Fact(DisplayName = "Une inscription complète est valide")]
    public void Valid_registration_has_no_error() => Assert.Empty(Validate(Valid()));

    [Fact(DisplayName = "Nom manquant : erreur sur LastName")]
    public void Missing_last_name_is_invalid() =>
        Assert.Contains(nameof(RegisterRequest.LastName), Validate(new RegisterRequest
        {
            FirstName = "Salma",
            Email = "a@b.tn",
            PhoneNumber = "+21620123456",
            Password = "motdepasse1",
            AcceptPrivacyPolicy = true,
        }).Keys);

    [Theory(DisplayName = "E-mail invalide : erreur sur Email")]
    [InlineData("pas-un-email")]
    [InlineData("salma@")]
    [InlineData("")]
    public void Invalid_email_is_rejected(string email) =>
        Assert.Contains(nameof(RegisterRequest.Email), Validate(Valid().With(email: email)).Keys);

    [Theory(DisplayName = "Téléphone invalide ou absent : erreur sur PhoneNumber")]
    [InlineData("12 34")]
    [InlineData("20 123 456")]
    [InlineData("")]
    public void Invalid_phone_is_rejected(string phone) =>
        Assert.Contains(nameof(RegisterRequest.PhoneNumber), Validate(Valid().With(phone: phone)).Keys);

    [Fact(DisplayName = "Consentement non coché : message français sur AcceptPrivacyPolicy")]
    public void Missing_consent_is_rejected()
    {
        var errors = Validate(Valid().With(consent: false));

        Assert.Equal(
            "Vous devez accepter la politique de confidentialité pour créer un compte.",
            errors[nameof(RegisterRequest.AcceptPrivacyPolicy)]);
    }

    [Fact(DisplayName = "Mot de passe de plus de 128 caractères refusé (borne le coût du hachage)")]
    public void Too_long_password_is_rejected() =>
        Assert.Contains(nameof(RegisterRequest.Password), Validate(Valid().With(password: new string('a', 129))).Keys);

    [Fact(DisplayName = "Prénom de plus de 100 caractères refusé")]
    public void Too_long_first_name_is_rejected() =>
        Assert.Contains(nameof(RegisterRequest.FirstName), Validate(Valid().With(firstName: new string('a', 101))).Keys);

    [Fact(DisplayName = "Connexion : e-mail et mot de passe vides refusés")]
    public void Empty_login_is_rejected()
    {
        var errors = Validate(new LoginRequest());

        Assert.Contains(nameof(LoginRequest.Email), errors.Keys);
        Assert.Contains(nameof(LoginRequest.Password), errors.Keys);
    }
}

internal static class RegisterRequestExtensions
{
    public static RegisterRequest With(
        this RegisterRequest r,
        string? firstName = null,
        string? email = null,
        string? phone = null,
        string? password = null,
        bool? consent = null) => new()
    {
        FirstName = firstName ?? r.FirstName,
        LastName = r.LastName,
        Email = email ?? r.Email,
        PhoneNumber = phone ?? r.PhoneNumber,
        Password = password ?? r.Password,
        AcceptPrivacyPolicy = consent ?? r.AcceptPrivacyPolicy,
    };
}
