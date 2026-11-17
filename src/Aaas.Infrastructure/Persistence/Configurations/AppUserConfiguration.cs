using Aaas.Domain.Common;
using Aaas.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aaas.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(PhoneNumber.NormalizedLength);
        builder.Property(u => u.PrivacyPolicyVersion).HasMaxLength(20).IsRequired();

        // Identity crée un index NON unique « EmailIndex » sur NormalizedEmail ; RequireUniqueEmail ne fait
        // qu'un SELECT préalable. L'index unique garantit l'unicité même pour deux inscriptions simultanées.
        // Même jeu de colonnes => EF renvoie l'index existant, qu'on renomme et rend unique.
        builder.HasIndex(u => u.NormalizedEmail)
            .HasDatabaseName("UX_AspNetUsers_NormalizedEmail")
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL");
    }
}
