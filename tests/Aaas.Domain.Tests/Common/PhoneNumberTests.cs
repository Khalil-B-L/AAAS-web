using Aaas.Domain.Common;

namespace Aaas.Domain.Tests.Common;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+21620123456")]
    [InlineData("+216 20 123 456")]
    [InlineData("  +216 20123456 ")]
    [InlineData("+216 20 123 456")] // espaces insécables (copier-coller depuis un document)
    public void Tunisian_number_with_or_without_spaces_is_normalized(string input)
    {
        Assert.True(PhoneNumber.TryParse(input, out var phone));
        Assert.Equal("+21620123456", phone!.Value);
        Assert.Equal("+216 20 123 456", phone.ToDisplayString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12 34")]                 // scénario US-A1
    [InlineData("20 123 456")]            // saisie locale sans préfixe : refusée
    [InlineData("0021620123456")]         // préfixe 00 : refusé
    [InlineData("21620123456")]           // sans le +
    [InlineData("+216 2012345")]          // 7 chiffres
    [InlineData("+216 201234567")]        // 9 chiffres
    [InlineData("+33 6 12 34 56 78")]     // international : refusé
    [InlineData("+216-20-123-456")]       // seuls les espaces sont tolérés
    [InlineData("+216 2O 123 456")]       // lettre O
    [InlineData("+216 ٢٠١٢٣٤٥٦")]          // chiffres arabes orientaux
    public void Invalid_numbers_are_rejected(string? input)
    {
        Assert.False(PhoneNumber.TryParse(input, out var phone));
        Assert.Null(phone);
    }

    [Fact]
    public void Parse_throws_business_rule_exception_with_stable_code()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => PhoneNumber.Parse("abc"));
        Assert.Equal(ErrorCodes.InvalidPhoneNumber, ex.Code);
    }
}
