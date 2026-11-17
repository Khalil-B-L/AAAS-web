using Aaas.Domain.Common;

namespace Aaas.Domain.Tests.Common;

/// <summary>Compléments R20 / RG-11 : seuls « +216 » + 8 chiffres sont acceptés, les espaces de présentation sont tolérés.</summary>
public class PhoneNumberFormatTests
{
    [Theory(DisplayName = "Les espaces à n'importe quelle position sont tolérés et supprimés")]
    [InlineData("+216 20123456")]
    [InlineData("+216 2 0 1 2 3 4 5 6")]
    [InlineData("+ 216 20 123 456")]
    [InlineData("\t+216 20 123 456\r\n")]
    public void Spaces_are_tolerated(string input)
    {
        Assert.True(PhoneNumber.TryParse(input, out var phone));
        Assert.Equal("+21620123456", phone!.Value);
    }

    [Theory(DisplayName = "Un numéro tunisien avec exactement 8 chiffres est accepté")]
    [InlineData("+21600000000")]
    [InlineData("+21699999999")]
    public void Exactly_eight_digits_are_accepted(string input) =>
        Assert.True(PhoneNumber.TryParse(input, out _));

    [Theory(DisplayName = "Tout autre format est refusé")]
    [InlineData("+2162012345")]      // 7 chiffres
    [InlineData("+216201234567")]    // 9 chiffres
    [InlineData("+21520123456")]     // mauvais indicatif
    [InlineData("+216(20)123456")]   // parenthèses
    [InlineData("+216.20.123.456")]  // points
    [InlineData("++21620123456")]
    [InlineData("+216 2012 345a")]
    public void Other_formats_are_rejected(string input) =>
        Assert.False(PhoneNumber.TryParse(input, out _));

    [Fact(DisplayName = "Parse accepte un numéro valide et renvoie la forme normalisée")]
    public void Parse_valid_number_returns_normalized_value() =>
        Assert.Equal("+21620123456", PhoneNumber.Parse("+216 20 123 456").ToString());
}
