using Aaas.Infrastructure.Persistence;
using Aaas.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Infrastructure.Tests.Persistence;

public sealed class SqlServerErrorsTests
{
    [Theory(DisplayName = "Violation d'unicité : 2601 et 2627 reconnus, y compris sous une DbUpdateException")]
    [InlineData(2601)]
    [InlineData(2627)]
    public void UniqueKeyViolation_Recognized_WhenWrapped(int number)
    {
        var ex = new DbUpdateException("save", SqlExceptionFactory.Create(number));

        Assert.True(SqlServerErrors.IsUniqueKeyViolation(ex));
        Assert.True(SqlServerErrors.TryGetUniqueKeyViolationNumber(ex, out var found));
        Assert.Equal(number, found);
    }

    [Fact(DisplayName = "SqlException imbriquée sur deux niveaux : trouvée")]
    public void UniqueKeyViolation_NestedTwoLevels_Recognized()
    {
        var ex = new InvalidOperationException("externe", new DbUpdateException("milieu", SqlExceptionFactory.Create(2601)));

        Assert.True(SqlServerErrors.TryGetUniqueKeyViolationNumber(ex, out var number));
        Assert.Equal(2601, number);
    }

    [Fact(DisplayName = "SqlException nue (sans enveloppe) : reconnue")]
    public void UniqueKeyViolation_Direct_Recognized() =>
        Assert.True(SqlServerErrors.IsUniqueKeyViolation(SqlExceptionFactory.Create(2627)));

    [Fact(DisplayName = "Autre erreur SQL (18456) : numéro connu mais pas une violation d'unicité")]
    public void OtherSqlError_IsNotUniqueKeyViolation_ButNumberIsExposed()
    {
        var ex = new DbUpdateException("save", SqlExceptionFactory.Create(18456));

        Assert.False(SqlServerErrors.IsUniqueKeyViolation(ex));
        Assert.False(SqlServerErrors.TryGetUniqueKeyViolationNumber(ex, out _));
        Assert.True(SqlServerErrors.TryGetSqlErrorNumber(ex, out var number));
        Assert.Equal(18456, number);
    }

    [Theory(DisplayName = "Erreur porteuse d'une valeur saisie : 2601, 2627, 2628 oui ; 18456, 547 non")]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(2628, true)]
    [InlineData(18456, false)]
    [InlineData(547, false)]
    public void ValueRevealingError_Recognized(int number, bool expected)
    {
        var ex = new DbUpdateException("save", SqlExceptionFactory.Create(number));

        Assert.Equal(expected, SqlServerErrors.TryGetValueRevealingErrorNumber(ex, out _));
        Assert.False(number == 2628 && SqlServerErrors.IsUniqueKeyViolation(ex)); // la troncature n'est pas un doublon
    }

    [Fact(DisplayName = "null et exception sans SqlException : faux, numéro 0")]
    public void NullOrNonSql_ReturnsFalse()
    {
        Assert.False(SqlServerErrors.IsUniqueKeyViolation(null));
        Assert.False(SqlServerErrors.TryGetSqlErrorNumber(null, out var n1));
        Assert.Equal(0, n1);
        Assert.False(SqlServerErrors.TryGetSqlErrorNumber(new InvalidOperationException("x", new DbUpdateException("y")), out var n2));
        Assert.Equal(0, n2);
    }
}
