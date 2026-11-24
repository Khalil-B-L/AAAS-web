using Aaas.Domain.Events;
using Aaas.Domain.News;
using Aaas.Domain.Reservations;
using Aaas.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Aaas.Application.Abstractions;

/// <summary>
/// Ce que les services d'Application voient de la base : des DbSet et SaveChanges (ADR 0001).
/// Implémentée par AppDbContext (Infrastructure), même instance que le reste de la requête (scoped).
/// Pas de repositories : les requêtes LINQ sont écrites dans les services.
/// </summary>
public interface IAppDbContext
{
    DbSet<Event> Events { get; }

    DbSet<AssociationSettings> AssociationSettings { get; }

    DbSet<Reservation> Reservations { get; }

    DbSet<NewsArticle> News { get; }

    /// <summary>
    /// Pour rejouer une opération après un conflit de rowversion : <c>ChangeTracker.Clear()</c> oublie les entités
    /// modifiées de la tentative ratée avant de relire la base (implémenté par DbContext).
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
