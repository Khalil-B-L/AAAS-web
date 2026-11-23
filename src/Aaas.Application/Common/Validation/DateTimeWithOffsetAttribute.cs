using System.ComponentModel.DataAnnotations;

namespace Aaas.Application.Common.Validation;

/// <summary>
/// Date ISO 8601 avec décalage obligatoire (ADR 0009). Le DTO garde une chaîne plutôt qu'un DateTimeOffset :
/// une date mal formée donne ainsi une erreur française sur le bon champ, au lieu d'une erreur de désérialisation
/// sous la clé « $.startsAt » qui interromprait la lecture de tout le corps.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class DateTimeWithOffsetAttribute : ValidationAttribute
{
    public DateTimeWithOffsetAttribute()
        : base(IsoDateTimes.InvalidFormatMessage)
    {
    }

    // Vide : laissé à [Required], pour n'afficher qu'un message.
    public override bool IsValid(object? value) =>
        value is not string text || string.IsNullOrWhiteSpace(text) || IsoDateTimes.TryParse(text, out _);
}
