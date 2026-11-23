using Aaas.Domain.Common;

namespace Aaas.Domain.Events;

/// <summary>
/// Règles de valeurs d'un événement (RG-13, R11, R12, R28, R29 ; ADR 0009, 0010) et calculs d'affichage (R2, R7).
/// Les calculs sont statiques pour servir aussi aux listes, qui projettent quelques colonnes sans charger l'entité :
/// la règle reste écrite une seule fois.
/// </summary>
public static class EventRules
{
    public const int TitleMaxLength = 200;
    public const int LocationMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 10_000;
    public const decimal MaxPrice = 99_999.999m;
    public const int PriceDecimals = 3;

    // Messages partagés avec les attributs des DTO (mêmes textes, que l'erreur vienne du DTO ou du Domain).
    public const string TitleRequiredMessage = "Le titre est obligatoire.";
    public const string TitleTooLongMessage = "Le titre ne doit pas dépasser 200 caractères.";
    public const string DescriptionRequiredMessage = "La description est obligatoire.";
    public const string DescriptionTooLongMessage = "La description ne doit pas dépasser 4 000 caractères.";
    public const string LocationRequiredMessage = "Le lieu est obligatoire.";
    public const string LocationTooLongMessage = "Le lieu ne doit pas dépasser 200 caractères.";
    public const string StartsInPastMessage = "La date de début doit être dans le futur.";
    public const string EndsBeforeStartMessage = "La fin doit être après le début.";
    public const string EndsInPastMessage = "La date de fin doit être dans le futur.";
    public const string CapacityRangeMessage = "La capacité doit être comprise entre 1 et 10 000 places.";
    public const string NegativePriceMessage = "Le prix ne peut pas être négatif.";
    public const string PriceDecimalsMessage = "Le prix est en dinars, avec au plus 3 décimales (millimes).";
    public const string PriceTooHighMessage = "Le prix ne doit pas dépasser 99 999,999 TND.";

    /// <summary>Une seule représentation de « gratuit » : 0 devient null (ADR 0010).</summary>
    public static decimal? NormalizePrice(decimal? price) => price == 0m ? null : price;

    /// <summary>
    /// Toutes les erreurs de valeurs, au plus une par champ. <paramref name="previousStartsAt"/> : début actuel
    /// (modification) ou null (création). Le début doit être dans le futur à la création, et à la modification
    /// seulement s'il change (R28 : un événement commencé reste modifiable sans déplacer son début).
    /// La fin doit toujours être dans le futur (R28, Q28) : à la création c'est déjà impliqué par « début futur
    /// et fin après début » ; à la modification d'un événement commencé, c'est la seule protection.
    /// </summary>
    public static IReadOnlyList<FieldError> Validate(EventDetails details, DateTimeOffset? previousStartsAt, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        var errors = new List<FieldError>();

        ValidateText(errors, EventFields.Title, details.Title, TitleMaxLength, TitleRequiredMessage, TitleTooLongMessage);
        ValidateText(errors, EventFields.Description, details.Description, DescriptionMaxLength, DescriptionRequiredMessage, DescriptionTooLongMessage);
        ValidateText(errors, EventFields.Location, details.Location, LocationMaxLength, LocationRequiredMessage, LocationTooLongMessage);

        var startChanges = previousStartsAt is null || previousStartsAt.Value != details.StartsAt;
        if (startChanges && details.StartsAt <= now)
        {
            errors.Add(new FieldError(EventFields.StartsAt, StartsInPastMessage));
        }

        if (details.EndsAt <= details.StartsAt)
        {
            errors.Add(new FieldError(EventFields.EndsAt, EndsBeforeStartMessage));
        }
        else if (details.EndsAt <= now)
        {
            errors.Add(new FieldError(EventFields.EndsAt, EndsInPastMessage));
        }

        if (details.Capacity is < MinCapacity or > MaxCapacity)
        {
            errors.Add(new FieldError(EventFields.Capacity, CapacityRangeMessage));
        }

        if (PriceError(details.Price) is { } priceError)
        {
            errors.Add(new FieldError(EventFields.Price, priceError));
        }

        return errors;
    }

    /// <summary>R7 : places restantes = capacité − places occupées, jamais négatif.</summary>
    public static int RemainingSeats(int capacity, EventOccupancy occupancy) => Math.Max(0, capacity - occupancy.Active);

    public static bool IsFull(int capacity, EventOccupancy occupancy) => occupancy.Active >= capacity;

    /// <summary>R2 : publié (donc ni brouillon ni annulé), pas commencé, pas complet.</summary>
    public static bool IsBookable(EventStatus status, DateTimeOffset startsAt, int capacity, DateTimeOffset now, EventOccupancy occupancy) =>
        status == EventStatus.Published && startsAt > now && !IsFull(capacity, occupancy);

    /// <summary>R15 à l'étape 2 (RG-07) : seul un brouillon jamais publié peut être supprimé.</summary>
    public static bool CanBeDeleted(EventStatus status, DateTimeOffset? firstPublishedAt) =>
        status == EventStatus.Draft && firstPublishedAt is null;

    private static void ValidateText(List<FieldError> errors, string field, string? value, int maxLength, string requiredMessage, string tooLongMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new FieldError(field, requiredMessage));
        }
        else if (value.Trim().Length > maxLength)
        {
            errors.Add(new FieldError(field, tooLongMessage));
        }
    }

    private static string? PriceError(decimal? price) => price switch
    {
        null => null,
        < 0m => NegativePriceMessage,
        // Refus plutôt qu'arrondi silencieux : 12.5001 n'est pas « presque » 12.500 pour celui qui l'a saisi.
        { } p when decimal.Round(p, PriceDecimals) != p => PriceDecimalsMessage,
        > MaxPrice => PriceTooHighMessage,
        _ => null,
    };
}
