using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Aaas.Infrastructure.Persistence.Conventions;

/// <summary>Force le décalage +00:00 à l'écriture et à la lecture, quel que soit l'offset reçu.</summary>
public sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTimeOffset>(
        value => value.ToUniversalTime(),
        value => value.ToUniversalTime());
