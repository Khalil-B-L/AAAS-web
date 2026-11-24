using Aaas.Application.Common.Errors;
using Aaas.Infrastructure.Persistence;
using Aaas.Infrastructure.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Infrastructure.Tests.Persistence;

/// <summary>
/// ADR 0019 § 3 : EventLock traduit une violation d'index unique en exception neutre d'Application, SANS
/// InnerException (le message SQL contient la valeur dupliquée, R24). Sans base : le DbContext lève lui-même.
/// </summary>
public sealed class EventLockUniqueViolationTests
{
    private sealed class FailingContext(Exception toThrow) : DbContext
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromException<int>(toThrow);
    }

    [Theory(DisplayName = "DbUpdateException 2601 / 2627 → UniqueConstraintViolationException sans InnerException ni donnée")]
    [InlineData(2601)]
    [InlineData(2627)]
    public async Task Unique_violation_is_translated(int number)
    {
        using var db = new FailingContext(new DbUpdateException("échec", SqlExceptionFactory.Create(number)));

        var error = await Assert.ThrowsAsync<UniqueConstraintViolationException>(
            () => EventLock.SaveChangesTranslatingUniqueViolationAsync(db, CancellationToken.None));

        Assert.Null(error.InnerException);
        Assert.DoesNotContain("@", error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Autre erreur SQL (547, CHECK) : remonte telle quelle")]
    public async Task Other_errors_are_not_translated()
    {
        using var db = new FailingContext(new DbUpdateException("échec", SqlExceptionFactory.Create(547)));

        await Assert.ThrowsAsync<DbUpdateException>(() => EventLock.SaveChangesTranslatingUniqueViolationAsync(db, CancellationToken.None));
    }

    [Fact(DisplayName = "Conflit de rowversion : DbUpdateConcurrencyException remonte telle quelle")]
    public async Task Concurrency_conflicts_are_not_translated()
    {
        using var db = new FailingContext(new DbUpdateConcurrencyException("conflit"));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => EventLock.SaveChangesTranslatingUniqueViolationAsync(db, CancellationToken.None));
    }
}
