using System.Net;
using System.Text.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Events;

/// <summary>Numéro de contact (US-E8, RG-10) : enregistrement, retrait avec version, avertissement à la publication.</summary>
public sealed class ContactSettingsTests(SqlServerFixture sql) : AdminApiTestBase(sql)
{
    private async Task<string> VersionAsync(string token) =>
        (await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/settings/contact", token))).GetProperty("version").GetString()!;

    private async Task<JsonElement> SaveAsync(string admin, string phone = "+216 20 123 456", bool whatsApp = true)
    {
        var response = await SendAsync(HttpMethod.Put, "/api/admin/settings/contact", admin,
            new { phoneNumber = phone, whatsApp, version = await VersionAsync(admin) });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    [Fact(DisplayName = "Retrait avec la version courante : 200, numéro NULL et WhatsApp faux en base, nouvelle version")]
    public async Task Remove_with_current_version_clears_number()
    {
        var admin = await AdminTokenAsync();
        var saved = await SaveAsync(admin);

        var response = await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin, new { version = saved.GetProperty("version").GetString() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("phoneNumber").ValueKind);
        Assert.False(json.GetProperty("whatsApp").GetBoolean());
        Assert.False(json.GetProperty("isConfigured").GetBoolean());
        Assert.NotEqual(saved.GetProperty("version").GetString(), json.GetProperty("version").GetString());
        Assert.Null(await Factory.ScalarAsync<string>("SELECT ContactPhone FROM AssociationSettings"));
        Assert.False(await Factory.ScalarAsync<bool>("SELECT ContactWhatsApp FROM AssociationSettings"));
    }

    [Fact(DisplayName = "Retrait avec une version périmée : 409 concurrent_update, le numéro reste")]
    public async Task Remove_with_stale_version_is_refused()
    {
        var admin = await AdminTokenAsync();
        var v0 = await VersionAsync(admin);
        await SaveAsync(admin);

        var response = await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin, new { version = v0 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("concurrent_update", (await ReadJsonAsync(response)).GetProperty("code").GetString());
        Assert.Equal("+21620123456", await Factory.ScalarAsync<string>("SELECT ContactPhone FROM AssociationSettings"));
    }

    [Theory(DisplayName = "Retrait sans version exploitable : 400 sur version, le numéro reste")]
    [InlineData("{}")]
    [InlineData("{\"version\":\"abc\"}")]
    [InlineData("{\"version\":\"\"}")]
    public async Task Remove_without_valid_version_is_400(string json)
    {
        var admin = await AdminTokenAsync();
        await SaveAsync(admin);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/admin/settings/contact")
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("version", out _));
        Assert.Equal("+21620123456", await Factory.ScalarAsync<string>("SELECT ContactPhone FROM AssociationSettings"));
    }

    [Fact(DisplayName = "Après retrait, la lecture membre indique isConfigured faux et la publication avertit à nouveau")]
    public async Task After_removal_publish_warns_again()
    {
        var admin = await AdminTokenAsync();
        var member = (await RegisterMemberAsync()).Token;
        var saved = await SaveAsync(admin);

        var withNumber = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/events/{(await CreateEventAsync(admin)).GetProperty("id").GetGuid()}/publish", admin));
        Assert.Equal(0, withNumber.GetProperty("warnings").GetArrayLength());

        await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin, new { version = saved.GetProperty("version").GetString() });

        var asMember = await ReadJsonAsync(await SendAsync(HttpMethod.Get, "/api/settings/contact", member));
        Assert.False(asMember.GetProperty("isConfigured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, asMember.GetProperty("phoneNumber").ValueKind);

        var second = await ReadJsonAsync(await SendAsync(HttpMethod.Post, $"/api/admin/events/{(await CreateEventAsync(admin)).GetProperty("id").GetGuid()}/publish", admin));
        Assert.Equal("contact_not_configured", second.GetProperty("warnings")[0].GetProperty("code").GetString());
    }

    [Theory(DisplayName = "Numéro : fixe et mobile tunisiens acceptés et normalisés ; format invalide ou étranger → 400 errors.phoneNumber")]
    [InlineData("+216 20 123 456", "+21620123456", HttpStatusCode.OK)]
    [InlineData("+216 71 123 456", "+21671123456", HttpStatusCode.OK)]
    [InlineData("abc", null, HttpStatusCode.BadRequest)]
    [InlineData("0021620123456", null, HttpStatusCode.BadRequest)]
    [InlineData("+33 6 12 34 56 78", null, HttpStatusCode.BadRequest)]
    public async Task Phone_number_validation(string input, string? normalised, HttpStatusCode expected)
    {
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Put, "/api/admin/settings/contact", admin,
            new { phoneNumber = input, whatsApp = false, version = await VersionAsync(admin) });

        Assert.Equal(expected, response.StatusCode);
        var json = await ReadJsonAsync(response);
        if (normalised is not null)
        {
            Assert.Equal(normalised, json.GetProperty("phoneNumber").GetString());
        }
        else
        {
            Assert.True(json.GetProperty("errors").TryGetProperty("phoneNumber", out _));
        }
    }

    [Fact(DisplayName = "Journaux : un 400 et un 409 sur le numéro de contact ne laissent aucun numéro dans les journaux")]
    public async Task Logs_do_not_contain_contact_phone_numbers()
    {
        const string digits = "55 987 321";
        const string compact = "55987321";
        var admin = await AdminTokenAsync();
        var v0 = await VersionAsync(admin);
        var saved = await SaveAsync(admin, $"+216 {digits}");
        await SendAsync(HttpMethod.Put, "/api/admin/settings/contact", admin, new { phoneNumber = $"+216 {digits}", whatsApp = true, version = v0 }); // 409
        await SendAsync(HttpMethod.Put, "/api/admin/settings/contact", admin, new { phoneNumber = "+216 98 765 432x", whatsApp = true, version = saved.GetProperty("version").GetString() }); // 400
        await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin, new { version = v0 }); // 409
        await SendAsync(HttpMethod.Get, "/api/settings/contact", admin);
        await SendAsync(HttpMethod.Delete, "/api/admin/settings/contact", admin, new { version = await VersionAsync(admin) });

        var all = string.Join("\n", Factory.Logs.Messages);

        Assert.NotEmpty(Factory.Logs.Messages);
        Assert.DoesNotContain(compact, all, StringComparison.Ordinal);
        Assert.DoesNotContain(digits, all, StringComparison.Ordinal);
        Assert.DoesNotContain("98 765 432", all, StringComparison.Ordinal);
        Assert.DoesNotContain("98765432", all, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminEmail, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Error]", all, StringComparison.Ordinal);
    }
}
