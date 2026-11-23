using Aaas.Application.Common.Errors;
using Aaas.Domain.Common;

namespace Aaas.Application.Common;

public static class FieldErrors
{
    /// <summary>Erreurs de valeurs du Domain → 400 validation_failed, regroupées par champ (comme la validation des DTO).</summary>
    public static void ThrowIfAny(IReadOnlyList<FieldError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            return;
        }

        throw new RequestValidationException(errors
            .GroupBy(e => e.Field, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray(), StringComparer.Ordinal));
    }
}
