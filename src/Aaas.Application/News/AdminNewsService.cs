using Aaas.Application.Abstractions;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Domain.News;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.News;

/// <summary>
/// Gestion des actualités par un admin (US-E1). Concurrence optimiste seule (rowversion, ADR 0022 § 4) : aucune règle
/// ne dépend d'autres lignes, donc pas de verrou comme pour les événements. <c>now</c> est lu une fois par opération.
/// </summary>
public sealed class AdminNewsService(IAppDbContext db, TimeProvider time)
{
    public const string NotFoundMessage = "Actualité introuvable.";

    public async Task<AdminNewsDto> CreateAsync(CreateNewsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        FieldErrors.ThrowIfAny(NewsRules.Validate(request.Title, request.Content));

        var created = NewsArticle.Create(request.Title, request.Content, time.GetUtcNow());
        db.News.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(created);
    }

    public async Task<AdminNewsDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await db.News.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);
        return ToDto(found);
    }

    /// <summary>Toutes, la dernière modifiée d'abord, puis Id (QN-9) ; le texte n'est pas lu.</summary>
    public async Task<PagedResult<AdminNewsSummaryDto>> ListAsync(AdminNewsListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filtered = db.News.AsNoTracking();
        if (query.Status is { } status)
        {
            filtered = filtered.Where(n => n.Status == status);
        }

        var total = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(n => n.UpdatedAt)
            .ThenBy(n => n.Id)
            .Page(query)
            .Select(n => new AdminNewsSummaryDto(n.Id, n.Title, n.Status, n.PublishedAt, n.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminNewsSummaryDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// Ordre : not_found → concurrent_update → 400 de valeurs (on ne signale pas une erreur de saisie sur une fiche
    /// périmée, comme pour les événements). Le rowversion d'EF reste le filet entre la lecture et l'UPDATE.
    /// </summary>
    public async Task<AdminNewsDto> UpdateAsync(Guid id, UpdateNewsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var expectedVersion = RowVersions.Decode(request.Version);
        var news = await LoadTrackedAsync(id, cancellationToken);

        ConcurrencyGuard.EnsureSameVersion(news.RowVersion, expectedVersion);
        FieldErrors.ThrowIfAny(NewsRules.Validate(request.Title, request.Content));

        news.Update(request.Title, request.Content, time.GetUtcNow());
        await db.SaveChangesOrConflictAsync(cancellationToken);
        // Construit après l'enregistrement : EF a relu la nouvelle rowversion.
        return ToDto(news);
    }

    /// <summary>Action sans version (ADR 0022 § 4) : la règle de transition suffit, le rowversion arrête une écriture croisée.</summary>
    public Task<AdminNewsDto> PublishAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, (news, now) => news.Publish(now), cancellationToken);

    public Task<AdminNewsDto> UnpublishAsync(Guid id, CancellationToken cancellationToken) =>
        TransitionAsync(id, (news, now) => news.Unpublish(now), cancellationToken);

    /// <summary>
    /// Suppression physique depuis les deux statuts (R18). Sans version : si un autre admin a modifié la fiche entre
    /// la lecture et le DELETE, le rowversion fait échouer la suppression (409 concurrent_update).
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var news = await LoadTrackedAsync(id, cancellationToken);
        db.News.Remove(news);
        await db.SaveChangesOrConflictAsync(cancellationToken);
    }

    private async Task<AdminNewsDto> TransitionAsync(Guid id, Action<NewsArticle, DateTimeOffset> transition, CancellationToken cancellationToken)
    {
        var news = await LoadTrackedAsync(id, cancellationToken);
        transition(news, time.GetUtcNow());
        await db.SaveChangesOrConflictAsync(cancellationToken);
        return ToDto(news);
    }

    private async Task<NewsArticle> LoadTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.News.SingleOrDefaultAsync(n => n.Id == id, cancellationToken)
        ?? throw new NotFoundException(NotFoundMessage);

    private static AdminNewsDto ToDto(NewsArticle news) => new(
        news.Id,
        news.Title,
        news.Content,
        news.Status,
        news.PublishedAt,
        news.CreatedAt,
        news.UpdatedAt,
        RowVersions.Encode(news.RowVersion));
}
