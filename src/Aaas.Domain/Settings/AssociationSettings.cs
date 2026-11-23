using Aaas.Domain.Common;

namespace Aaas.Domain.Settings;

/// <summary>
/// Paramètres de l'association : une seule ligne (Id = 1), créée par la migration (ADR 0013).
/// Le numéro de contact (RG-10) suit la règle RG-11 : « +216 » + 8 chiffres, mobile ou fixe.
/// </summary>
public sealed class AssociationSettings
{
    public const int SingletonId = 1;

    private AssociationSettings()
    {
        // Pour EF Core : la ligne vient de la migration, jamais d'un INSERT applicatif.
    }

    public int Id { get; private set; }

    /// <summary>Forme E.164 (+21620123456) ; null = aucun numéro configuré (jamais saisi, ou retiré).</summary>
    public string? ContactPhone { get; private set; }

    public bool ContactWhatsApp { get; private set; }

    /// <summary>null tant que les paramètres n'ont jamais été modifiés.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool IsContactConfigured => ContactPhone is not null;

    public void UpdateContact(PhoneNumber phone, bool whatsApp, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(phone);
        ContactPhone = phone.Value;
        ContactWhatsApp = whatsApp;
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>RG-10 : l'admin retire le numéro ; l'option WhatsApp n'a plus de sens et repasse à false (R8).</summary>
    public void RemoveContact(DateTimeOffset now)
    {
        ContactPhone = null;
        ContactWhatsApp = false;
        UpdatedAt = now.ToUniversalTime();
    }

    /// <summary>État de la ligne juste après la migration. Réservé aux tests du Domain.</summary>
    internal static AssociationSettings CreateInitial() => new() { Id = SingletonId };
}
