using Aaas.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aaas.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        // Les CHECK sont des filets : les règles sont d'abord appliquées par le Domain, mais un script SQL
        // ou un bug qui contournerait le Domain serait arrêté par SQL Server.
        builder.ToTable("Events", table =>
        {
            table.HasCheckConstraint("CK_Events_Dates", "[EndsAt] > [StartsAt]");
            table.HasCheckConstraint("CK_Events_Capacity", "[Capacity] >= 1");
            table.HasCheckConstraint("CK_Events_Price", "[Price] IS NULL OR [Price] > 0");
            table.HasCheckConstraint("CK_Events_Status", "[Status] IN (1, 2, 3)");
        });

        builder.HasKey(e => e.Id);
        // Guid généré par EF à l'ajout (séquentiel, limite la fragmentation de l'index cluster).
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.Title).HasMaxLength(EventRules.TitleMaxLength).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(EventRules.DescriptionMaxLength).IsRequired();
        builder.Property(e => e.Location).HasMaxLength(EventRules.LocationMaxLength).IsRequired();

        // decimal(9,3) : millimes, jusqu'à 999 999,999 ; stocké sur 5 octets (précision ≤ 9), ADR 0010.
        builder.Property(e => e.Price).HasPrecision(9, 3);

        // Enum « : byte » → tinyint ; valeurs figées (1, 2, 3).
        builder.Property(e => e.Status).HasConversion<byte>();

        // rowversion : SQL Server la change à chaque écriture ; EF la met dans le WHERE des UPDATE/DELETE.
        builder.Property(e => e.RowVersion).IsRowVersion();

        // Propriétés calculées du Domain : pas des colonnes.
        builder.Ignore(e => e.IsFree);
        builder.Ignore(e => e.CanBeDeleted);

        // Filtre par statut et tri par date de début (listes publique et admin, ADR 0011).
        builder.HasIndex(e => new { e.Status, e.StartsAt }).HasDatabaseName("IX_Events_Status_StartsAt");
    }
}
