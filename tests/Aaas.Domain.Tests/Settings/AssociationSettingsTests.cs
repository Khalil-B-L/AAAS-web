using Aaas.Domain.Common;
using Aaas.Domain.Settings;

namespace Aaas.Domain.Tests.Settings;

/// <summary>Numéro de contact de l'association (RG-10, RG-11, R8, US-E8).</summary>
public class AssociationSettingsTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Après la migration : aucun numéro configuré, WhatsApp à false, jamais modifié")]
    public void Initial_state()
    {
        var settings = AssociationSettings.CreateInitial();

        Assert.Equal(AssociationSettings.SingletonId, settings.Id);
        Assert.False(settings.IsContactConfigured);
        Assert.Null(settings.ContactPhone);
        Assert.False(settings.ContactWhatsApp);
        Assert.Null(settings.UpdatedAt);
    }

    [Theory(DisplayName = "Enregistrer un numéro mobile ou fixe : forme normalisée, WhatsApp, UpdatedAt")]
    [InlineData("+216 20 123 456", "+21620123456")]
    [InlineData("+216 71 123 456", "+21671123456")] // fixe (RG-11)
    public void Update_contact(string input, string expected)
    {
        var settings = AssociationSettings.CreateInitial();

        settings.UpdateContact(PhoneNumber.Parse(input), whatsApp: true, Now);

        Assert.True(settings.IsContactConfigured);
        Assert.Equal(expected, settings.ContactPhone);
        Assert.True(settings.ContactWhatsApp);
        Assert.Equal(Now, settings.UpdatedAt);
    }

    [Fact(DisplayName = "Retirer le numéro : plus de numéro, WhatsApp désactivé (R8)")]
    public void Remove_contact()
    {
        var settings = AssociationSettings.CreateInitial();
        settings.UpdateContact(PhoneNumber.Parse("+216 20 123 456"), whatsApp: true, Now);

        settings.RemoveContact(Now.AddHours(1));

        Assert.False(settings.IsContactConfigured);
        Assert.Null(settings.ContactPhone);
        Assert.False(settings.ContactWhatsApp);
        Assert.Equal(Now.AddHours(1), settings.UpdatedAt);
    }
}
