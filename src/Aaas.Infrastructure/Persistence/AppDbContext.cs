using Aaas.Application.Abstractions;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using Aaas.Domain.Settings;
using Aaas.Infrastructure.Identity;
using Aaas.Infrastructure.Persistence.Conventions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<AssociationSettings> AssociationSettings => Set<AssociationSettings>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // D'abord le modèle Identity, puis nos configurations qui le complètent ou le remplacent.
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // ADR 0003 : toutes les dates sont des DateTimeOffset stockées avec un décalage nul (UTC).
        // S'applique aussi aux DateTimeOffset? (dont LockoutEnd d'Identity).
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }
}
