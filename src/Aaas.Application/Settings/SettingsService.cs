using Aaas.Application.Abstractions;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Domain.Common;
using Aaas.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.Settings;

/// <summary>Numéro de contact de l'association (US-E8, RG-10, ADR 0013). Une seule ligne, Id = 1, créée par la migration.</summary>
public sealed class SettingsService(IAppDbContext db, TimeProvider time)
{
    internal const string PhoneNumberField = "phoneNumber";

    public async Task<ContactSettingsDto> GetContactAsync(CancellationToken cancellationToken)
    {
        var settings = await db.AssociationSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == AssociationSettings.SingletonId, cancellationToken)
            ?? throw MissingRow();
        return ContactSettingsDto.From(settings);
    }

    public async Task<ContactSettingsDto> UpdateContactAsync(UpdateContactSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Revérifié ici (le DTO l'a déjà fait) : le service peut être appelé par un autre point d'entrée.
        if (!PhoneNumber.TryParse(request.PhoneNumber, out var phone))
        {
            throw RequestValidationException.For(PhoneNumberField, PhoneNumber.ExpectedFormatMessage);
        }

        var settings = await LoadForUpdateAsync(request.Version, cancellationToken);
        settings.UpdateContact(phone!, request.WhatsApp, time.GetUtcNow());
        await db.SaveChangesOrConflictAsync(cancellationToken);
        return ContactSettingsDto.From(settings); // RowVersion relue par EF après l'UPDATE : nouvelle version
    }

    /// <summary>RG-10 : l'admin retire le numéro ; WhatsApp repasse à false (R8).</summary>
    public async Task<ContactSettingsDto> RemoveContactAsync(RemoveContactSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var settings = await LoadForUpdateAsync(request.Version, cancellationToken);
        settings.RemoveContact(time.GetUtcNow());
        await db.SaveChangesOrConflictAsync(cancellationToken);
        return ContactSettingsDto.From(settings);
    }

    /// <summary>Pour l'avertissement non bloquant de la publication (US-E8).</summary>
    public Task<bool> IsContactConfiguredAsync(CancellationToken cancellationToken) =>
        db.AssociationSettings.AnyAsync(
            s => s.Id == AssociationSettings.SingletonId && s.ContactPhone != null, cancellationToken);

    private async Task<AssociationSettings> LoadForUpdateAsync(string version, CancellationToken cancellationToken)
    {
        var settings = await db.AssociationSettings
            .SingleOrDefaultAsync(s => s.Id == AssociationSettings.SingletonId, cancellationToken)
            ?? throw MissingRow();
        // Version lue par l'admin ≠ version en base : un autre admin a enregistré entre-temps (409).
        // Une écriture qui se glisserait entre cette comparaison et SaveChanges est arrêtée par le rowversion d'EF.
        ConcurrencyGuard.EnsureSameVersion(settings.RowVersion, RowVersions.Decode(version));
        return settings;
    }

    // La ligne fait partie du schéma : son absence signale une migration non appliquée (500, pas 404).
    private static InvalidOperationException MissingRow() =>
        new("La ligne AssociationSettings (Id = 1) est absente : migration AddEventsAndAssociationSettings non appliquée ?");
}
