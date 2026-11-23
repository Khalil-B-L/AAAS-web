using System.Globalization;

namespace Aaas.Application.Common;

/// <summary>
/// Dates reçues en chaîne ISO 8601 avec décalage OBLIGATOIRE (ADR 0003, 0009) : 2026-12-05T10:00+01:00, ou ...Z.
/// Sans décalage, l'instant dépendrait du fuseau de qui lit la date : refusé plutôt que deviné.
/// </summary>
public static class IsoDateTimes
{
    // Un seul message pour tout refus (décalage absent, format inconnu, date impossible comme un 31 février) :
    // il rappelle le format attendu, fuseau compris, ce qui couvre les trois cas.
    public const string InvalidFormatMessage =
        "La date est invalide : format attendu AAAA-MM-JJTHH:mm avec le fuseau horaire, par exemple 2026-03-10T10:00+01:00.";

    // Chaque format se termine par un décalage (zzz = ±HH:mm) ou par « Z » littéral : une date sans décalage ne
    // correspond à aucun format. Fractions de seconde tolérées (Date.toISOString() en JavaScript en produit).
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm'Z'",
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
    ];

    /// <summary>Renvoie l'instant converti en UTC.</summary>
    public static bool TryParse(string? value, out DateTimeOffset utc)
    {
        utc = default;
        // AssumeUniversal ne sert qu'aux formats « Z » (sans zzz, le décalage serait sinon celui du serveur).
        if (string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParseExact(value.Trim(), Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return false;
        }

        utc = parsed.ToUniversalTime();
        return true;
    }
}
