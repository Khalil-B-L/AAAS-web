using System.ComponentModel.DataAnnotations;
using Aaas.Application.Common;
using Aaas.Application.Common.Validation;
using Aaas.Domain.News;

namespace Aaas.Application.News;

// ---------- Entrées ----------

/// <summary>
/// Champs communs à la création et à la modification. Aucune limite de longueur ici : elles sont comptées par le
/// Domain APRÈS normalisation (Trim, \r\n → \n), sinon un texte valide envoyé avec des \r\n serait refusé (ADR 0023 § 3).
/// </summary>
public abstract class NewsInput
{
    [Required(ErrorMessage = NewsRules.TitleRequiredMessage)]
    public string Title { get; init; } = string.Empty;

    [Required(ErrorMessage = NewsRules.ContentRequiredMessage)]
    public string Content { get; init; } = string.Empty;
}

public sealed class CreateNewsRequest : NewsInput;

public sealed class UpdateNewsRequest : NewsInput
{
    [Required(ErrorMessage = "La version de la fiche est obligatoire. Rechargez-la.")]
    [RowVersion]
    public string Version { get; init; } = string.Empty;
}

/// <summary>Liste publique : 10 par page par défaut (US-B1).</summary>
public sealed class PublicNewsListQuery() : PagingQuery(DefaultPublicPageSize)
{
    public const int DefaultPublicPageSize = 10;
}

/// <summary>Liste admin : 20 par page, filtre facultatif par statut.</summary>
public sealed class AdminNewsListQuery() : PagingQuery
{
    public NewsStatus? Status { get; init; }
}

// ---------- Sorties ----------

/// <summary>Carte de la liste publique (US-B1). Excerpt : 200 caractères au plus, calculé (ADR 0023 § 4).</summary>
public sealed record NewsSummaryDto(Guid Id, string Title, string Excerpt, DateTimeOffset PublishedAt);

/// <summary>Détail public (US-B2) : texte brut complet, paragraphes séparés par des lignes vides (\n\n).</summary>
public sealed record NewsDetailDto(Guid Id, string Title, string Content, DateTimeOffset PublishedAt);

public sealed record AdminNewsSummaryDto(Guid Id, string Title, NewsStatus Status, DateTimeOffset? PublishedAt, DateTimeOffset UpdatedAt);

/// <summary>Fiche admin, avec la version à renvoyer lors de la modification (ADR 0006).</summary>
public sealed record AdminNewsDto(
    Guid Id,
    string Title,
    string Content,
    NewsStatus Status,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string Version);
