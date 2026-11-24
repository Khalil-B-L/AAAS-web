using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using Aaas.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aaas.Infrastructure.Persistence.Configurations;

/// <summary>Table Reservations (ADR 0018 § 2). Les CHECK et l'index unique filtré sont des filets : la règle est dans le Domain.</summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    /// <summary>
    /// RG-03 : une seule demande active (En attente ou Confirmée) par membre et par événement.
    /// Valeurs 1 et 2 figées (ReservationStatus). UserId NULL (compte supprimé) n'entre pas dans l'unicité.
    /// </summary>
    public const string ActiveUniqueIndexName = "UX_Reservations_Event_User_Active";

    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", table =>
        {
            table.HasCheckConstraint("CK_Reservations_Status", "[Status] BETWEEN 1 AND 6");
            table.HasCheckConstraint("CK_Reservations_Deadline", "[ExpiresAt] > [RequestedAt]");
            // RG-08 : « Payée » seulement sur une confirmée, ou une réservation payée puis annulée (remboursement hors appli).
            table.HasCheckConstraint("CK_Reservations_Paid", "[IsPaid] = 0 OR [Status] IN (2, 5, 6)");
            table.HasCheckConstraint("CK_Reservations_PaidAt", "[IsPaid] = 0 OR [PaidAt] IS NOT NULL");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd(); // GUID séquentiel généré par EF à l'ajout

        builder.Property(r => r.Status).HasConversion<byte>();
        builder.Property(r => r.RefusalReason).HasMaxLength(ReservationRules.RefusalReasonMaxLength);
        builder.Property(r => r.IsPaid).HasDefaultValue(false);
        builder.Property(r => r.RowVersion).IsRowVersion();

        // Pas de navigation dans le Domain (Reservation ne connaît ni Event ni AppUser) : relations déclarées ici seulement.
        // NO ACTION partout : un événement ou un compte référencé ne disparaît pas en cascade (RG-07, ADR 0007 :
        // la suppression de compte mettra UserId à NULL explicitement, bloc 6).
        builder.HasOne<Event>().WithMany().HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.PaidByAdminId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(r => new { r.EventId, r.UserId })
            .IsUnique()
            .HasFilter("[Status] IN (1, 2) AND [UserId] IS NOT NULL")
            .HasDatabaseName(ActiveUniqueIndexName);

        // Décompte des places (EventOccupancyReader) : WHERE EventId IN (...) AND Status IN (1, 2), ExpiresAt lue
        // dans l'index (INCLUDE) sans aller chercher la ligne. Couvre aussi la clé étrangère EventId.
        builder.HasIndex(r => new { r.EventId, r.Status })
            .IncludeProperties(r => r.ExpiresAt)
            .HasDatabaseName("IX_Reservations_EventId_Status");

        // Tâche d'expiration (WHERE Status = 1 AND ExpiresAt <= @now) et liste admin des demandes par échéance.
        builder.HasIndex(r => new { r.Status, r.ExpiresAt }).HasDatabaseName("IX_Reservations_Status_ExpiresAt");

        // IX_Reservations_UserId (« Mes réservations ») et IX_Reservations_PaidByAdminId : créés par convention pour les clés étrangères.
    }
}
