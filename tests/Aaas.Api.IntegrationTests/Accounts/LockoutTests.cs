using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Accounts;

/// <summary>R21 : blocage de 15 minutes après 5 échecs consécutifs.</summary>
public sealed class LockoutTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    private const string WrongPassword = "mauvaismdp1";

    private async Task<string> FailAsync(string email)
    {
        var response = await LoginAsync(email, WrongPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("code").GetString()!;
    }

    [Fact(DisplayName = "Les 4 premiers échecs donnent invalid_credentials, le 5e donne account_locked")]
    public async Task Fifth_failure_locks_account()
    {
        var (email, _, _) = await RegisterMemberAsync();

        for (var i = 1; i <= 4; i++)
        {
            Assert.Equal("invalid_credentials", await FailAsync(email));
        }

        var fifth = await LoginAsync(email, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, fifth.StatusCode);
        var problem = await ReadJsonAsync(fifth);
        Assert.Equal("account_locked", problem.GetProperty("code").GetString());
        Assert.Contains("15 minutes", problem.GetProperty("title").GetString());
    }

    [Fact(DisplayName = "Compte bloqué : même le bon mot de passe est refusé (account_locked)")]
    public async Task Locked_account_refuses_correct_password()
    {
        var (email, _, _) = await RegisterMemberAsync();
        for (var i = 0; i < 5; i++)
        {
            await FailAsync(email);
        }

        var response = await LoginAsync(email, ValidPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("account_locked", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact(DisplayName = "Compte bloqué : le message indique les minutes restantes")]
    public async Task Locked_account_message_shows_remaining_minutes()
    {
        var (email, _, _) = await RegisterMemberAsync();
        for (var i = 0; i < 5; i++)
        {
            await FailAsync(email);
        }

        // Heure de l'hôte, comme l'API (TimeProvider.System) : l'horloge du conteneur SQL peut dériver de celle de l'hôte.
        await Factory.ExecuteAsync(
            "UPDATE AspNetUsers SET LockoutEnd = @end WHERE NormalizedEmail = @e",
            ("@end", DateTimeOffset.UtcNow.AddMinutes(3)),
            ("@e", email.ToUpperInvariant()));
        var response = await LoginAsync(email, ValidPassword);

        var title = (await ReadJsonAsync(response)).GetProperty("title").GetString();
        Assert.Contains("réessayez dans 3 minutes.", title);
    }

    [Fact(DisplayName = "Un succès remet le compteur à zéro : 4 échecs, succès, 4 échecs ne bloquent pas")]
    public async Task Success_resets_failure_counter()
    {
        var (email, _, _) = await RegisterMemberAsync();
        for (var i = 0; i < 4; i++)
        {
            await FailAsync(email);
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, ValidPassword)).StatusCode);
        var counter = await Factory.ScalarAsync<int>("SELECT AccessFailedCount FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.Equal(0, counter);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("invalid_credentials", await FailAsync(email));
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, ValidPassword)).StatusCode);
    }

    [Fact(DisplayName = "Fin du blocage : une fois LockoutEnd dépassé, la connexion réussit et le compteur est remis à zéro")]
    public async Task Lockout_ends_after_delay()
    {
        var (email, _, id) = await RegisterMemberAsync();
        for (var i = 0; i < 5; i++)
        {
            await FailAsync(email);
        }

        var lockoutEnd = await Factory.ScalarAsync<DateTimeOffset?>("SELECT LockoutEnd FROM AspNetUsers WHERE Id = @id", ("@id", id));
        Assert.NotNull(lockoutEnd);
        Assert.InRange(lockoutEnd!.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));

        await Factory.ExecuteAsync(
            "UPDATE AspNetUsers SET LockoutEnd = @end WHERE Id = @id",
            ("@end", DateTimeOffset.UtcNow.AddMinutes(-1)),
            ("@id", id));
        var response = await LoginAsync(email, ValidPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counter = await Factory.ScalarAsync<int>("SELECT AccessFailedCount FROM AspNetUsers WHERE Id = @id", ("@id", id));
        Assert.Equal(0, counter);
    }

    [Fact(DisplayName = "Le blocage d'un compte ne touche pas les autres comptes")]
    public async Task Lockout_is_per_account()
    {
        var (locked, _, _) = await RegisterMemberAsync();
        var (other, _, _) = await RegisterMemberAsync();
        for (var i = 0; i < 5; i++)
        {
            await FailAsync(locked);
        }

        var response = await LoginAsync(other, ValidPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
