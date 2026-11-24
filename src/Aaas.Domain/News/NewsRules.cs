using System.Text;
using Aaas.Domain.Common;

namespace Aaas.Domain.News;

/// <summary>
/// Règles de valeurs d'une actualité (R18, ADR 0023) : normalisation, limites, extrait. Statiques et pures :
/// l'extrait sert à la liste publique, qui projette quelques colonnes sans charger l'entité.
/// </summary>
public static class NewsRules
{
    public const int TitleMaxLength = 200;
    public const int ContentMaxLength = 8000;
    public const int ExcerptMaxLength = 200;

    /// <summary>
    /// Nombre de caractères lus en base pour calculer l'extrait (SUBSTRING) : assez pour absorber des espaces
    /// multiples et des retours à la ligne, sans transférer un texte de 8 000 caractères par ligne de liste.
    /// </summary>
    public const int ExcerptSourceLength = 400;

    public const string Ellipsis = "…";

    public const string TitleRequiredMessage = "Le titre est obligatoire.";
    public const string TitleTooLongMessage = "Le titre ne doit pas dépasser 200 caractères.";
    public const string ContentRequiredMessage = "Le texte est obligatoire.";
    public const string ContentTooLongMessage = "Le texte ne doit pas dépasser 8 000 caractères.";
    public const string AlreadyPublishedMessage = "Cette actualité est déjà publiée.";
    public const string NotPublishedMessage = "Cette actualité n'est pas publiée.";

    public static string NormalizeTitle(string? title) => (title ?? string.Empty).Trim();

    /// <summary>
    /// \r\n et \r → \n, puis Trim. Le navigateur envoie \n, mais curl ou un copier-coller Windows peuvent envoyer \r\n :
    /// sans cela, le même texte compterait plus de caractères selon sa provenance (ADR 0023 § 2).
    /// Aucun encodage HTML : on encode à l'affichage, pas au stockage.
    /// </summary>
    public static string NormalizeContent(string? content) =>
        (content ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    /// <summary>Toutes les erreurs de valeurs, au plus une par champ, comptées APRÈS normalisation.</summary>
    public static IReadOnlyList<FieldError> Validate(string? title, string? content)
    {
        var errors = new List<FieldError>();

        var normalizedTitle = NormalizeTitle(title);
        if (normalizedTitle.Length == 0)
        {
            errors.Add(new FieldError(NewsFields.Title, TitleRequiredMessage));
        }
        else if (normalizedTitle.Length > TitleMaxLength)
        {
            errors.Add(new FieldError(NewsFields.Title, TitleTooLongMessage));
        }

        var normalizedContent = NormalizeContent(content);
        if (normalizedContent.Length == 0)
        {
            errors.Add(new FieldError(NewsFields.Content, ContentRequiredMessage));
        }
        else if (normalizedContent.Length > ContentMaxLength)
        {
            errors.Add(new FieldError(NewsFields.Content, ContentTooLongMessage));
        }

        return errors;
    }

    /// <summary>
    /// Extrait de la liste publique (US-B1, ADR 0023 § 4) : espaces blancs réduits à une espace, au plus 200 caractères
    /// coupés sur une fin de mot, suivis de « … » s'il manque quelque chose. Un mot plus long que 200 (une URL) est
    /// coupé net. Ne coupe jamais une paire de substitution UTF-16 (émoji).
    /// </summary>
    /// <param name="text">Le texte, ou seulement son début (projection SUBSTRING).</param>
    /// <param name="moreFollows">
    /// Vrai quand <paramref name="text"/> n'est que le début du texte : même court après réduction des espaces,
    /// on coupe alors sur un mot et on ajoute « … ».
    /// </param>
    public static string Excerpt(string? text, bool moreFollows = false)
    {
        text ??= string.Empty;
        var flat = CollapseWhitespace(text);

        if (flat.Length <= ExcerptMaxLength && !moreFollows)
        {
            return flat;
        }

        var cut = Math.Min(ExcerptMaxLength, flat.Length);
        if (cut > 0 && char.IsHighSurrogate(flat[cut - 1]))
        {
            cut--;
        }

        // Le dernier mot est entier si le caractère suivant est une espace (ou, en fin de début de texte, si celui-ci
        // se terminait par un blanc) ; sinon on recule jusqu'à la dernière espace.
        var lastWordIsWhole = cut < flat.Length
            ? flat[cut] == ' '
            : text.Length > 0 && char.IsWhiteSpace(text[^1]);
        var head = flat[..cut];
        if (!lastWordIsWhole)
        {
            var lastSpace = head.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                head = head[..lastSpace];
            }
        }

        return head.TrimEnd() + Ellipsis;
    }

    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
