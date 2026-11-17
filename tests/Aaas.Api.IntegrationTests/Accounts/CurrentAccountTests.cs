using System.Net;
using System.Security.Claims;
using Aaas.Api.IntegrationTests.Support;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aaas.Api.IntegrationTests.Accounts;

/// <summary>GET /api/account/me et validité des jetons (ADR 0002).</summary>
public sealed class CurrentAccountTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    private static string CraftToken(
        Guid userId,
        string securityStamp,
        string signingKey = ApiFactory.SigningKey,
        DateTime? expires = null,
        string audience = "aaas-front",
        string issuer = "aaas-api",
        string role = "User")
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", userId.ToString()),
                new Claim("sst", securityStamp),
                new Claim("role", role),
            ]),
            IssuedAt = now.AddHours(-3),
            NotBefore = now.AddHours(-3),
            Expires = expires ?? now.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private Task<string> StampOfAsync(Guid id) =>
        Factory.ScalarAsync<string>("SELECT SecurityStamp FROM AspNetUsers WHERE Id = @id", ("@id", id))!;

    private static async Task AssertUnauthorizedProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("unauthorized", problem.GetProperty("code").GetString());
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
    }

    [Fact(DisplayName = "me avec un jeton valide : 200 et les données du compte (consentement inclus)")]
    public async Task Me_with_valid_token_returns_account()
    {
        var (email, token, id) = await RegisterMemberAsync();

        var response = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(id, json.GetProperty("id").GetGuid());
        Assert.Equal(email, json.GetProperty("email").GetString());
        Assert.Equal("+21620123456", json.GetProperty("phoneNumber").GetString());
        Assert.Equal("1.0", json.GetProperty("privacyPolicyVersion").GetString());
        Assert.True(json.TryGetProperty("createdAt", out _));
        Assert.True(json.TryGetProperty("privacyConsentAt", out _));
        Assert.True(json.TryGetProperty("lastLoginAt", out _));
        Assert.False(json.TryGetProperty("passwordHash", out _));
        Assert.False(json.TryGetProperty("securityStamp", out _));
    }

    [Fact(DisplayName = "me sans jeton : 401 unauthorized au format ProblemDetails")]
    public async Task Me_without_token_returns_401()
    {
        var response = await GetMeAsync(null);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Theory(DisplayName = "me avec un jeton illisible : 401")]
    [InlineData("n-importe-quoi")]
    [InlineData("a.b.c")]
    [InlineData("")]
    public async Task Me_with_garbage_token_returns_401(string token)
    {
        var response = await GetMeAsync(token);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact(DisplayName = "me avec un jeton falsifié (charge modifiée, signature d'origine) : 401")]
    public async Task Me_with_tampered_token_returns_401()
    {
        var (_, token, _) = await RegisterMemberAsync();
        var parts = token.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        var forged = payload.Replace("\"User\"", "\"Admin\"");
        Assert.NotEqual(payload, forged);
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(forged)}.{parts[2]}";

        var response = await GetMeAsync(tampered);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact(DisplayName = "me avec un jeton signé par une autre clé : 401")]
    public async Task Me_with_token_signed_by_other_key_returns_401()
    {
        var (_, _, id) = await RegisterMemberAsync();
        var token = CraftToken(id, await StampOfAsync(id), signingKey: "une-autre-cle-secrete-de-plus-de-32-caracteres!");

        var response = await GetMeAsync(token);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact(DisplayName = "me avec un jeton non signé (alg none) : 401")]
    public async Task Me_with_unsigned_token_returns_401()
    {
        var (_, _, id) = await RegisterMemberAsync();
        var header = Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncoder.Encode(
            $"{{\"sub\":\"{id}\",\"sst\":\"{await StampOfAsync(id)}\",\"role\":\"Admin\",\"iss\":\"aaas-api\",\"aud\":\"aaas-front\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}");

        var response = await GetMeAsync($"{header}.{payload}.");

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact(DisplayName = "Contrôle : un jeton fabriqué avec la bonne clé et le bon security stamp est accepté")]
    public async Task Me_with_correctly_crafted_token_returns_200()
    {
        var (_, _, id) = await RegisterMemberAsync();

        var response = await GetMeAsync(CraftToken(id, await StampOfAsync(id)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "me avec un jeton expiré : 401")]
    public async Task Me_with_expired_token_returns_401()
    {
        var (_, _, id) = await RegisterMemberAsync();
        var token = CraftToken(id, await StampOfAsync(id), expires: DateTime.UtcNow.AddMinutes(-10));

        var response = await GetMeAsync(token);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Theory(DisplayName = "me avec un jeton d'un autre émetteur ou d'une autre audience : 401")]
    [InlineData("autre-emetteur", "aaas-front")]
    [InlineData("aaas-api", "autre-audience")]
    public async Task Me_with_wrong_issuer_or_audience_returns_401(string issuer, string audience)
    {
        var (_, _, id) = await RegisterMemberAsync();
        var token = CraftToken(id, await StampOfAsync(id), issuer: issuer, audience: audience);

        var response = await GetMeAsync(token);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact(DisplayName = "me après renouvellement du security stamp : l'ancien jeton est refusé (401), la reconnexion fonctionne")]
    public async Task Me_after_security_stamp_change_returns_401()
    {
        var (email, token, id) = await RegisterMemberAsync();
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(token)).StatusCode);

        await Factory.ExecuteAsync("UPDATE AspNetUsers SET SecurityStamp = CONVERT(nvarchar(36), NEWID()) WHERE Id = @id", ("@id", id));

        await AssertUnauthorizedProblemAsync(await GetMeAsync(token));
        var newToken = (await ReadJsonAsync(await LoginAsync(email, ValidPassword))).GetProperty("accessToken").GetString();
        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(newToken)).StatusCode);
    }

    [Fact(DisplayName = "me après suppression du compte : le jeton est refusé (401)")]
    public async Task Me_after_account_deletion_returns_401()
    {
        var (_, token, id) = await RegisterMemberAsync();
        await Factory.ExecuteAsync("DELETE FROM AspNetUserRoles WHERE UserId = @id; DELETE FROM AspNetUsers WHERE Id = @id", ("@id", id));

        var response = await GetMeAsync(token);

        await AssertUnauthorizedProblemAsync(response);
    }

    [Fact(DisplayName = "Chaque membre ne voit que son propre compte")]
    public async Task Me_returns_only_own_account()
    {
        var (emailA, tokenA, _) = await RegisterMemberAsync();
        var (emailB, tokenB, _) = await RegisterMemberAsync();

        var a = await ReadJsonAsync(await GetMeAsync(tokenA));
        var b = await ReadJsonAsync(await GetMeAsync(tokenB));

        Assert.Equal(emailA, a.GetProperty("email").GetString());
        Assert.Equal(emailB, b.GetProperty("email").GetString());
    }
}
