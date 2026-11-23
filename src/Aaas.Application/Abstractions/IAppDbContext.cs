using Aaas.Domain.Events;
using Aaas.Domain.Settings;
using Microsoft.EntityFrameworkCore;

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
