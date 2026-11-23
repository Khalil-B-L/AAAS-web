using Aaas.Domain.Common;
using Aaas.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aaas.Infrastructure.Persistence.Configurations;

internal sealed class AssociationSettingsConfiguration : IEntityTypeConfiguration<AssociationSettings>
{
    public void Configure(EntityTypeBuilder<AssociationSettings> builder)
    {
        // Table à une seule ligne (ADR 0013) : la contrainte empêche une deuxième ligne, même par un script SQL.
        builder.ToTable("AssociationSettings", table =>
            table.HasCheckConstraint("CK_AssociationSettings_Singleton", $"[Id] = {AssociationSettings.SingletonId}"));

        builder.HasKey(s => s.Id);
        // Pas d'IDENTITY : l'Id est toujours 1, fixé par la migration.
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.ContactPhone).HasMaxLength(PhoneNumber.NormalizedLength);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.Ignore(s => s.IsContactConfigured);

        // La ligne fait partie du schéma versionné : la migration l'insère (numéro NULL, WhatsApp à 0).
        // Objet anonyme : l'entité n'a pas de constructeur public.
        builder.HasData(new { Id = AssociationSettings.SingletonId, ContactWhatsApp = false });
    }
}
