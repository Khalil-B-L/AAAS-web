using Aaas.Api.IntegrationTests.Support;
using Microsoft.Data.SqlClient;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>Filets en base : les contraintes CHECK refusent ce qu'un script SQL ou un bug contournerait côté Domain.</summary>
public sealed class SchemaConstraintTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    [Fact(DisplayName = "Après migration : une seule ligne AssociationSettings (Id = 1), numéro NULL, WhatsApp faux")]
    public async Task Migration_seeds_exactly_one_settings_row()
    {
        await AdminTokenAsync(); // force le démarrage et la migration

        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM AssociationSettings"));
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT Id FROM AssociationSettings"));
        Assert.Null(await Factory.ScalarAsync<string>("SELECT ContactPhone FROM AssociationSettings"));
        Assert.False(await Factory.ScalarAsync<bool>("SELECT ContactWhatsApp FROM AssociationSettings"));
    }

    [Fact(DisplayName = "CK_AssociationSettings_Singleton : INSERT d'une deuxième ligne (Id = 2) refusé")]
    public async Task Second_settings_row_is_refused()
    {
        await AdminTokenAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            Factory.ExecuteAsync("INSERT INTO AssociationSettings (Id, ContactWhatsApp) VALUES (2, 0)"));

        Assert.Contains("CK_AssociationSettings_Singleton", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM AssociationSettings"));
    }

    [Theory(DisplayName = "CHECK sur Events : une écriture SQL directe hors règles est refusée")]
    [InlineData("Capacity = 0", "CK_Events_Capacity")]
    [InlineData("Capacity = -5", "CK_Events_Capacity")]
    [InlineData("Price = 0", "CK_Events_Price")]
    [InlineData("Price = -1", "CK_Events_Price")]
    [InlineData("EndsAt = StartsAt", "CK_Events_Dates")]
    [InlineData("EndsAt = DATEADD(hour, -1, StartsAt)", "CK_Events_Dates")]
    [InlineData("Status = 0", "CK_Events_Status")]
    [InlineData("Status = 4", "CK_Events_Status")]
    public async Task Events_check_constraints(string assignment, string constraint)
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();

        var error = await Assert.ThrowsAsync<SqlException>(() =>
            Factory.ExecuteAsync($"UPDATE Events SET {assignment} WHERE Id = @id", ("@id", id)));

        Assert.Contains(constraint, error.Message, StringComparison.Ordinal);
        // La ligne est restée intacte.
        Assert.Equal(30, await Factory.ScalarAsync<int>("SELECT Capacity FROM Events WHERE Id = @id", ("@id", id)));
    }

    [Fact(DisplayName = "CHECK sur Events : valeurs limites acceptées (capacité 1, prix NULL, fin = début + 1 s)")]
    public async Task Events_check_constraints_accept_boundaries()
    {
        var admin = await AdminTokenAsync();
        var id = (await CreateEventAsync(admin)).GetProperty("id").GetGuid();

        var changed = await Factory.ExecuteAsync(
            "UPDATE Events SET Capacity = 1, Price = NULL, EndsAt = DATEADD(second, 1, StartsAt), Status = 3 WHERE Id = @id", ("@id", id));

        Assert.Equal(1, changed);
    }
}
