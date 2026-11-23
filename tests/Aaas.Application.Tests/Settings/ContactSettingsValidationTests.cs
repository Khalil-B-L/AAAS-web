using Aaas.Application.Settings;
using Aaas.Domain.Common;
using static Aaas.Application.Tests.Common.ValidationTestHelper;

namespace Aaas.Application.Tests.Settings;

/// <summary>Numéro de contact (US-E8, RG-10, RG-11) : même règle que le téléphone des membres, mobile ou fixe.</summary>
public class ContactSettingsValidationTests
{
    private const string Version = "AAAAAAAAB9E=";

    [Theory(DisplayName = "Numéro tunisien mobile ou fixe : accepté")]
    [InlineData("+216 20 123 456")]
    [InlineData("+216 71 123 456")] // fixe
    [InlineData("+21698765432")]
    public void Tunisian_number_is_accepted(string phone) =>
        Assert.Empty(Validate(new UpdateContactSettingsRequest { PhoneNumber = phone, WhatsApp = true, Version = Version }));

    [Theory(DisplayName = "abc, international, préfixe 00 : refusés avec le message RG-11")]
    [InlineData("abc")]
    [InlineData("+33 6 12 34 56 78")]
    [InlineData("0021620123456")]
    [InlineData("20 123 456")]
    public void Invalid_number_is_rejected(string phone) =>
        Assert.Equal(
            PhoneNumber.ExpectedFormatMessage,
            Validate(new UpdateContactSettingsRequest { PhoneNumber = phone, Version = Version })["PhoneNumber"]);

    [Fact(DisplayName = "Numéro vide : un seul message (obligatoire)")]
    public void Empty_number_is_required() =>
        Assert.Equal(
            "Le numéro de téléphone est obligatoire.",
            Validate(new UpdateContactSettingsRequest { PhoneNumber = "", Version = Version })["PhoneNumber"]);

    [Fact(DisplayName = "Retrait : version obligatoire et au bon format")]
    public void Remove_requires_version()
    {
        Assert.Empty(Validate(new RemoveContactSettingsRequest { Version = Version }));
        Assert.Equal(["Version"], Validate(new RemoveContactSettingsRequest()).Keys);
        Assert.Equal(["Version"], Validate(new RemoveContactSettingsRequest { Version = "abc" }).Keys);
    }
}
