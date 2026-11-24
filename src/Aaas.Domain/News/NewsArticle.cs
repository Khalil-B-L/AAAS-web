using Aaas.Domain.Common;

namespace Aaas.Domain.News;

/// <summary>
/// Actualité de l'association (US-E1, US-B1, US-B2, R18 ; ADR 0022). Nommée <c>NewsArticle</c> et non <c>News</c> :
/// dans l'espace de noms <c>Aaas.Domain.News</c>, <c>News</c> désignerait à la fois l'espace de noms et le type (CS0118).
/// Aucune méthode ne lit l'horloge : <c>now</c> est passé en paramètre (ADR 0003).
/// </summary>
public sealed class NewsArticle
{
    private NewsArticle()
    {
        // Pour EF Core.
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Texte brut normalisé (\n seulement), jamais du HTML : affiché par interpolation (ADR 0023).</summary>
    public string Content { get; private set; } = string.Empty;

    public NewsStatus Status { get; private set; }

    /// <summary>
    /// Date de publication affichée : posée à la PREMIÈRE publication, conservée par la dépublication et la
    /// republication, modifiée par aucune modification (ADR 0022 § 3). Ne dit rien de la visibilité : seul
    /// <see cref="Status"/> compte.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>rowversion SQL Server (concurrence entre admins, ADR 0006).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public static NewsArticle Create(string? title, string? content, DateTimeOffset now)
    {
        ThrowIfInvalid(NewsRules.Validate(title, content));
        var utcNow = now.ToUniversalTime();
        return new NewsArticle
        {
            Title = NewsRules.NormalizeTitle(title),
            Content = NewsRules.NormalizeContent(content),
            Status = NewsStatus.Draft,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    /// <summary>Brouillon ou publiée : titre, texte et UpdatedAt changent, PublishedAt jamais (US-E1).</summary>
    public void Update(string? title, string? content, DateTimeOffset now)
    {
        ThrowIfInvalid(NewsRules.Validate(title, content));
        Title = NewsRules.NormalizeTitle(title);
        Content = NewsRules.NormalizeContent(content);
        UpdatedAt = now.ToUniversalTime();
    }

    public void Publish(DateTimeOffset now)
    {
        if (Status != NewsStatus.Draft)
        {
            throw new BusinessRuleException(ErrorCodes.NewsStateChanged, NewsRules.AlreadyPublishedMessage);
        }

        var utcNow = now.ToUniversalTime();
        Status = NewsStatus.Published;
        // « ??= » : une republication garde la date d'origine (QN-2). Pour « republier = nouvelle date », remplacer par « = ».
        PublishedAt ??= utcNow;
        UpdatedAt = utcNow;
    }

    public void Unpublish(DateTimeOffset now)
    {
        if (Status != NewsStatus.Published)
        {
            throw new BusinessRuleException(ErrorCodes.NewsStateChanged, NewsRules.NotPublishedMessage);
        }

        Status = NewsStatus.Draft; // PublishedAt conservée
        UpdatedAt = now.ToUniversalTime();
    }

    private static void ThrowIfInvalid(IReadOnlyList<FieldError> errors)
    {
        // Filet : Application valide d'abord (400 par champ, avec la même NewsRules.Validate). Arriver ici signale un
        // appel sans validation préalable, donc un bug de code : 409 validation_failed plutôt que le 400 par champ
        // (le Domain ne connaît pas la validation de requête d'Application). Même choix que Event.
        if (errors.Count > 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.ValidationFailed,
                "Données d'actualité invalides : " + string.Join(" ", errors.Select(e => $"{e.Field} : {e.Message}")));
        }
    }
}
