using Aaas.Application.Abstractions;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Domain.News;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Application.News;

/// <summary>Consultation publique, sans compte (US-B1, US-B2). Aucune horloge : la visibilité ne dépend que du statut.</summary>
public sealed class PublicNewsService(IAppDbContext db)
{
    /// <summary>Publiées seulement, la plus récente d'abord, puis Id (tri total, ADR 0011).</summary>
    public async Task<PagedResult<NewsSummaryDto>> ListAsync(PublicNewsListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var published = db.News.AsNoTracking().Where(n => n.Status == NewsStatus.Published);

        var total = await published.CountAsync(cancellationToken);
        var rows = await published
            .OrderByDescending(n => n.PublishedAt)
            .ThenBy(n => n.Id)
            .Page(query)
            // SUBSTRING en SQL : la liste ne transfère que le début du texte. Un caractère de plus que nécessaire
            // dit si la suite existe (« … » même quand le début, une fois les espaces réduits, tient en 200).
            .Select(n => new { n.Id, n.Title, n.PublishedAt, Head = n.Content.Substring(0, NewsRules.ExcerptSourceLength + 1) })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r =>
        {
            var moreFollows = r.Head.Length > NewsRules.ExcerptSourceLength;
            var head = moreFollows ? r.Head[..NewsRules.ExcerptSourceLength] : r.Head;
            // PublishedAt n'est jamais nulle pour une publiée (CK_News_PublishedAt).
            return new NewsSummaryDto(r.Id, r.Title, NewsRules.Excerpt(head, moreFollows), r.PublishedAt!.Value);
        }).ToList();

        return new PagedResult<NewsSummaryDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>Publiée seulement ; brouillon (jamais publiée ou dépubliée) ou inconnue → même 404.</summary>
    public async Task<NewsDetailDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var news = await db.News.AsNoTracking()
            .Where(n => n.Id == id && n.Status == NewsStatus.Published)
            .Select(n => new { n.Id, n.Title, n.Content, n.PublishedAt })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(NotFoundMessages.PublicContent);

        return new NewsDetailDto(news.Id, news.Title, news.Content, news.PublishedAt!.Value);
    }
}
