using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Aaas.Api.IntegrationTests.Support;

/// <summary>
/// Base des classes de tests : une base SQL Server neuve (RCSI) et une API démarrée par <b>test</b>.
/// xUnit crée une instance de la classe par test : InitializeAsync/DisposeAsync s'exécutent donc à chaque test.
/// Choix assumé plutôt qu'une base par classe (IClassFixture) : HealthTests supprime la base et LoggingTests lit
/// tous les journaux de l'API, ce qui rendrait les tests d'une même classe dépendants de leur ordre.
/// </summary>
public abstract class ApiTestBase(SqlServerFixture sql) : IAsyncLifetime
{
    protected const string ValidPassword = "motdepasse1";
    protected const string ValidPhone = "+216 20 123 456";

    protected SqlServerFixture Sql { get; } = sql;

    protected ApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected virtual Dictionary<string, string?>? ConfigurationOverrides => null;

    /// <summary>Horloge simulée à injecter dans l'API (null = horloge réelle).</summary>
    protected virtual TimeProvider? ClockOverride => null;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await Sql.CreateDatabaseAsync();
        Factory = new ApiFactory(connectionString, ConfigurationOverrides, ClockOverride);
        Client = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await Sql.DropDatabaseAsync(Factory.ConnectionString);
    }

    /// <summary>E-mail unique : pas de collision quand un test crée plusieurs comptes.</summary>
    protected static string NewEmail(string prefix = "membre") => $"{prefix}.{Guid.NewGuid():N}@exemple.tn";

    protected static object RegistrationBody(
        string? email = null,
        string? password = ValidPassword,
        string phone = ValidPhone,
        bool consent = true,
        string firstName = "Salma",
        string lastName = "Ben Ali") => new
    {
        firstName,
        lastName,
        email = email ?? NewEmail(),
        phoneNumber = phone,
        password,
        acceptPrivacyPolicy = consent,
    };

    protected Task<HttpResponseMessage> RegisterAsync(object body) => Client.PostAsJsonAsync("/api/auth/register", body);

    protected Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        Client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>Inscrit un membre et renvoie son e-mail, son jeton et son identifiant.</summary>
    protected async Task<(string Email, string Token, Guid Id)> RegisterMemberAsync(string? email = null)
    {
        email ??= NewEmail();
        var response = await RegisterAsync(RegistrationBody(email));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJsonAsync(response);
        return (email, json.GetProperty("accessToken").GetString()!, json.GetProperty("user").GetProperty("id").GetGuid());
    }

    protected Task<HttpResponseMessage> GetMeAsync(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/me");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return Client.SendAsync(request);
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
