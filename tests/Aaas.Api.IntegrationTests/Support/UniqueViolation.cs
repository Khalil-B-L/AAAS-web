using Aaas.Infrastructure.Identity;
using Aaas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>Provoque, sans dépendre d'une course, une violation de l'index unique UX_AspNetUsers_NormalizedEmail (SQL 2601).</summary>
public static class UniqueViolation
{
    public static async Task<DbUpdateException> InsertDuplicateEmailAsync(AppDbContext db, string existingEmail)
    {
        // UserName différent : seul l'index sur NormalizedEmail peut refuser l'INSERT (pas UserNameIndex).
        var id = Guid.NewGuid();
        db.Users.Add(new AppUser
        {
            Id = id,
            UserName = $"doublon.{id:N}",
            NormalizedUserName = $"DOUBLON.{id:N}".ToUpperInvariant(),
            Email = existingEmail,
            NormalizedEmail = existingEmail.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = "Doublon",
            LastName = "Test",
            CreatedAt = DateTimeOffset.UtcNow,
            PrivacyConsentAt = DateTimeOffset.UtcNow,
            PrivacyPolicyVersion = "1.0",
        });

        return await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    public static void AssertIsUniqueIndexViolation(DbUpdateException exception)
    {
        Assert.True(SqlServerErrors.TryGetUniqueKeyViolationNumber(exception, out var number));
        Assert.Equal(2601, number);
    }
}
