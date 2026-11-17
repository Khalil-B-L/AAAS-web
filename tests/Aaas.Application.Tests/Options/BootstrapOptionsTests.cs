using Aaas.Application.Options;

namespace Aaas.Application.Tests.Options;

public sealed class BootstrapOptionsTests
{
    private static BootstrapOptions Complete() => new()
    {
        AdminEmail = "admin@example.org",
        AdminPassword = "Sup3rS3cret-Pwd",
        AdminFirstName = "Amel",
        AdminLastName = "Ben Salah",
        AdminPhone = "+216 20 123 456",
    };

    [Fact(DisplayName = "Configuration complète : aucune clé manquante")]
    public void MissingKeys_Complete_IsEmpty()
    {
        var options = Complete();

        Assert.Empty(options.MissingKeys);
        Assert.True(options.IsComplete);
    }

    [Theory(DisplayName = "E-mail vide, espaces ou absent -> seule la clé Bootstrap:AdminEmail est signalée")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void MissingKeys_EmptyEmail_ReportsOnlyEmailKey(string? email)
    {
        var options = Complete();
        options.AdminEmail = email;

        Assert.Equal(["Bootstrap:AdminEmail"], options.MissingKeys);
        Assert.False(options.IsComplete);
    }

    [Fact(DisplayName = "Tout vide -> les cinq clés, par leur nom, jamais leur valeur")]
    public void MissingKeys_AllEmpty_ListsFiveKeyNames()
    {
        var options = new BootstrapOptions();

        Assert.Equal(
            ["Bootstrap:AdminEmail", "Bootstrap:AdminPassword", "Bootstrap:AdminFirstName", "Bootstrap:AdminLastName", "Bootstrap:AdminPhone"],
            options.MissingKeys);
    }

    [Fact(DisplayName = "PromoteExistingAccount ne compte pas dans les clés requises et vaut false par défaut")]
    public void PromoteExistingAccount_DefaultsToFalse_AndIsNotRequired()
    {
        var options = Complete();

        Assert.False(options.PromoteExistingAccount);
        Assert.True(options.IsComplete);
    }
}
