namespace Aaas.Domain.Common;

/// <summary>
/// Numéro de téléphone tunisien (RG-11) : « +216 » suivi d'exactement 8 chiffres.
/// Les espaces sont tolérés à la saisie ; la valeur stockée est normalisée (E.164, ex. +21620123456).
/// Saisie locale sans préfixe, préfixe « 00 » et numéros d'autres pays sont refusés (décision du porteur).
/// </summary>
public sealed record PhoneNumber
{
    public const string CountryPrefix = "+216";
    public const int SubscriberDigits = 8;
    public const int NormalizedLength = 12; // "+216" + 8 chiffres
    public const string ExpectedFormatMessage =
        "Le numéro de téléphone doit être un numéro tunisien au format +216 suivi de 8 chiffres, par exemple +216 20 123 456.";

    private PhoneNumber(string value) => Value = value;

    /// <summary>Forme normalisée, sans espace : +21620123456.</summary>
    public string Value { get; }

    public static bool TryParse(string? input, out PhoneNumber? phoneNumber)
    {
        phoneNumber = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var compact = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
        if (compact.Length != NormalizedLength || !compact.StartsWith(CountryPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // char.IsDigit accepterait aussi les chiffres arabes orientaux (٠-٩) : on veut uniquement 0-9.
        if (compact.AsSpan(CountryPrefix.Length).ContainsAnyExceptInRange('0', '9'))
        {
            return false;
        }

        phoneNumber = new PhoneNumber(compact);
        return true;
    }

    public static PhoneNumber Parse(string? input) =>
        TryParse(input, out var phoneNumber)
            ? phoneNumber!
            : throw new BusinessRuleException(ErrorCodes.InvalidPhoneNumber, ExpectedFormatMessage);

    /// <summary>Forme lisible : +216 20 123 456.</summary>
    public string ToDisplayString() =>
        $"{CountryPrefix} {Value.AsSpan(4, 2)} {Value.AsSpan(6, 3)} {Value.AsSpan(9, 3)}";

    public override string ToString() => Value;
}
