using Aaas.Application.Common;
using Aaas.Application.Common.Validation;

namespace Aaas.Application.Tests.Common;

/// <summary>[DateTimeWithOffset] (ADR 0009) et [RowVersion] (ADR 0006).</summary>
public class ValidationAttributesTests
{
    private static readonly DateTimeWithOffsetAttribute DateAttribute = new();
    private static readonly RowVersionAttribute VersionAttribute = new();

    [Theory(DisplayName = "Date avec décalage ou Z : acceptée et convertie en UTC")]
    [InlineData("2026-12-05T10:00+01:00")]
    [InlineData("2026-12-05T10:00:00+01:00")]
    [InlineData("2026-12-05T09:00:00Z")]
    [InlineData("2026-12-05T09:00Z")]
    [InlineData("2026-12-05T09:00:00.000Z")] // Date.toISOString() en JavaScript
    [InlineData("2026-12-05T12:00+03:00")]
    public void Date_with_offset_is_accepted(string value)
    {
        Assert.True(DateAttribute.IsValid(value));
        Assert.True(IsoDateTimes.TryParse(value, out var utc));
        Assert.Equal(new DateTimeOffset(2026, 12, 5, 9, 0, 0, TimeSpan.Zero), utc); // 10 h à Tunis = 09:00Z
        Assert.Equal(TimeSpan.Zero, utc.Offset);
    }

    [Theory(DisplayName = "Date sans décalage, mal formée ou impossible : refusée avec le message général (format + fuseau)")]
    [InlineData("2026-12-05T10:00")]
    [InlineData("2026-12-05T10:00:00")]
    [InlineData("05/12/2026 10:00")]
    [InlineData("demain")]
    [InlineData("2026-12-05")]
    [InlineData("2026-02-30T10:00+01:00")]
    public void Date_without_offset_is_rejected(string value)
    {
        Assert.False(DateAttribute.IsValid(value));
        Assert.Equal(
            "La date est invalide : format attendu AAAA-MM-JJTHH:mm avec le fuseau horaire, par exemple 2026-03-10T10:00+01:00.",
            DateAttribute.FormatErrorMessage("startsAt"));
    }

    [Theory(DisplayName = "Date vide : laissée à [Required]")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_date_is_left_to_required(string? value) => Assert.True(DateAttribute.IsValid(value));

    [Fact(DisplayName = "Version : Base64 de 8 octets accepté, aller-retour exact")]
    public void Valid_row_version()
    {
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 7, 209 };
        var encoded = RowVersions.Encode(bytes);

        Assert.Equal("AAAAAAAAB9E=", encoded);
        Assert.True(VersionAttribute.IsValid(encoded));
        Assert.Equal(bytes, RowVersions.Decode(encoded));
    }

    [Theory(DisplayName = "Version invalide : pas du Base64, ou pas 8 octets")]
    [InlineData("abc")]
    [InlineData("AAAAAA==")]          // 4 octets
    [InlineData("AAAAAAAAAAAAAA==")]  // 10 octets
    [InlineData("pas de la base64 !")]
    public void Invalid_row_version(string value)
    {
        Assert.False(VersionAttribute.IsValid(value));
        Assert.Equal(RowVersionAttribute.InvalidMessage, VersionAttribute.FormatErrorMessage("version"));
    }
}
