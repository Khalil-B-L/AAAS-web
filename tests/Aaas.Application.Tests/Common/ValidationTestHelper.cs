using System.ComponentModel.DataAnnotations;

namespace Aaas.Application.Tests.Common;

internal static class ValidationTestHelper
{
    /// <summary>Validation déclarative, comme [ApiController] avant l'appel du service. Clé = nom C# de la propriété.</summary>
    public static Dictionary<string, string> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results
            .SelectMany(r => r.MemberNames.Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => string.Join(" | ", g.Select(x => x.ErrorMessage)));
    }
}
