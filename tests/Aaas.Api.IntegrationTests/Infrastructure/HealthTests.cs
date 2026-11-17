using System.Net;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Infrastructure;

public sealed class HealthTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact(DisplayName = "/health : anonyme, 200 Healthy")]
    public async Task Liveness_is_anonymous_and_healthy()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "/health/ready : anonyme, 200 Healthy quand la base répond")]
    public async Task Readiness_is_healthy_with_database()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "Quand la base disparaît : /health reste 200 (sans accès base), /health/ready passe à 503")]
    public async Task Readiness_is_unhealthy_without_database_but_liveness_stays_healthy()
    {
        await Sql.DropDatabaseAsync(Factory.ConnectionString);

        var liveness = await Client.GetAsync("/health");
        var readiness = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        Assert.Equal("Unhealthy", await readiness.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "Un chemin inconnu renvoie 401 unauthorized sans jeton (politique par défaut : tout exige un jeton)")]
    public async Task Unknown_route_without_token_is_not_exposed()
    {
        var response = await Client.GetAsync("/api/inexistant");

        // 401 et non 404 : la politique de repli s'applique aussi quand aucune route ne correspond, donc un anonyme n'apprend pas quelles routes existent.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("unauthorized", problem.GetProperty("code").GetString());
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
    }

    [Fact(DisplayName = "OpenAPI n'est pas exposé hors développement")]
    public async Task OpenApi_is_not_exposed_outside_development()
    {
        var response = await Client.GetAsync("/openapi/v1.json");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
