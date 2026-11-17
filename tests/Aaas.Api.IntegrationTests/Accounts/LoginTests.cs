using System.Net;
using System.Net.Http.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Accounts;

/// <summary>US-A2 : se connecter.</summary>
public sealed class LoginTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact(DisplayName = "Connexion valide : 200, jeton, compte, lastLoginAt mis à jour en base")]
    public async Task Login_valid_returns_200_and_updates_last_login()
    {
        var (email, _, id) = await RegisterMemberAsync();
        var registeredLoginAt = await Factory.ScalarAsync<DateTimeOffset>("SELECT LastLoginAt FROM AspNetUsers WHERE Id = @id", ("@id", id));
        await Task.Delay(1100); // laisse l'horloge avancer d'au moins une seconde
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await LoginAsync(email, ValidPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("accessToken").GetString()));
        Assert.True(json.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(30));
        var user = json.GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal(["User"], user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!));

        var dbLoginAt = await Factory.ScalarAsync<DateTimeOffset>("SELECT LastLoginAt FROM AspNetUsers WHERE Id = @id", ("@id", id));
        Assert.True(dbLoginAt > registeredLoginAt, "LastLoginAt doit avancer à la connexion.");
        Assert.True(dbLoginAt >= before);
        Assert.Equal(dbLoginAt, user.GetProperty("lastLoginAt").GetDateTimeOffset());
    }

    [Fact(DisplayName = "Connexion : le jeton émis ouvre /api/account/me")]
    public async Task Login_token_is_accepted_by_me()
    {
        var (email, _, _) = await RegisterMemberAsync();
        var token = (await ReadJsonAsync(await LoginAsync(email, ValidPassword))).GetProperty("accessToken").GetString();

        var me = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact(DisplayName = "Connexion : l'e-mail est insensible à la casse")]
    public async Task Login_email_is_case_insensitive()
    {
        var (email, _, _) = await RegisterMemberAsync();

        var response = await LoginAsync(email.ToUpperInvariant(), ValidPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Connexion : mauvais mot de passe et e-mail inconnu donnent exactement la même réponse")]
    public async Task Login_wrong_password_and_unknown_email_are_indistinguishable()
    {
        var (email, _, _) = await RegisterMemberAsync();

        var wrongPassword = await LoginAsync(email, "mauvaismdp1");
        var unknownEmail = await LoginAsync(NewEmail("inconnu"), "mauvaismdp1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        var a = await ReadJsonAsync(wrongPassword);
        var b = await ReadJsonAsync(unknownEmail);
        Assert.Equal("invalid_credentials", a.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("code").GetString(), b.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("title").GetString(), b.GetProperty("title").GetString());
        Assert.Equal("E-mail ou mot de passe incorrect.", a.GetProperty("title").GetString());
        Assert.Equal(wrongPassword.Content.Headers.ContentType?.MediaType, unknownEmail.Content.Headers.ContentType?.MediaType);
    }

    [Fact(DisplayName = "Connexion : un échec ne modifie pas LastLoginAt")]
    public async Task Login_failure_keeps_last_login()
    {
        var (email, _, id) = await RegisterMemberAsync();
        var before = await Factory.ScalarAsync<DateTimeOffset>("SELECT LastLoginAt FROM AspNetUsers WHERE Id = @id", ("@id", id));

        await LoginAsync(email, "mauvaismdp1");

        var after = await Factory.ScalarAsync<DateTimeOffset>("SELECT LastLoginAt FROM AspNetUsers WHERE Id = @id", ("@id", id));
        Assert.Equal(before, after);
    }

    [Theory(DisplayName = "Connexion : e-mail ou mot de passe vide, 400 sur le champ")]
    [InlineData("", "motdepasse1", "email")]
    [InlineData("salma@exemple.tn", "", "password")]
    public async Task Login_empty_field_returns_400(string email, string password, string field)
    {
        var response = await LoginAsync(email, password);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact(DisplayName = "Connexion : corps vide, 400")]
    public async Task Login_empty_body_returns_400()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
