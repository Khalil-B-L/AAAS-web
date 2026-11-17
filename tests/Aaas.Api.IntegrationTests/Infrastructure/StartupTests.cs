using System.Net;
using System.Net.Http.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Infrastructure;

/// <summary>ADR 0008 et R22 : initialisation au démarrage (rôles, premier administrateur) et validation de la configuration.</summary>
public sealed class StartupTests(SqlServerFixture sql)
{
    private const string AdminPassword = "AdminTest2026";

    private static Dictionary<string, string?> Bootstrap(string email, string password = AdminPassword, string phone = "+216 71 000 000") => new()
    {
        ["Bootstrap:AdminEmail"] = email,
        ["Bootstrap:AdminPassword"] = password,
        ["Bootstrap:AdminFirstName"] = "Admin",
        ["Bootstrap:AdminLastName"] = "Association",
        ["Bootstrap:AdminPhone"] = phone,
    };

    private static string NewAdminEmail() => $"admin.{Guid.NewGuid():N}@association.tn";

    /// <summary>Démarre l'API (ce qui exécute l'initialisation), puis l'arrête.</summary>
    private static async Task StartAndStopAsync(string connectionString, Dictionary<string, string?>? overrides = null)
    {
        await using var factory = new ApiFactory(connectionString, overrides);
        using var client = factory.CreateClient(); // force le démarrage
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    private static async Task<int> CountAdminsAsync(ApiFactory factory) =>
        await factory.ScalarAsync<int>(
            "SELECT COUNT(DISTINCT ur.UserId) FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE r.NormalizedName = 'ADMIN'");

    private static string[] RolesOf(System.Text.Json.JsonElement json) =>
        [.. json.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).Order()];

    [Fact(DisplayName = "Démarrage : les rôles Admin et User sont créés, une seule fois même après plusieurs démarrages")]
    public async Task Startup_creates_roles_idempotently()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await StartAndStopAsync(cs);
            await StartAndStopAsync(cs);

            await using var factory = new ApiFactory(cs);
            var roles = await factory.ScalarAsync<string>("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM AspNetRoles");
            Assert.Equal("Admin,User", roles);
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Premier admin : créé avec les rôles Admin et User, peut se connecter ; un second démarrage ne crée rien de plus")]
    public async Task First_admin_is_created_once()
    {
        var cs = await sql.CreateDatabaseAsync();
        var email = NewAdminEmail();
        try
        {
            await StartAndStopAsync(cs, Bootstrap(email));
            await StartAndStopAsync(cs, Bootstrap(email));
            await StartAndStopAsync(cs, Bootstrap(email));

            await using var factory = new ApiFactory(cs, Bootstrap(email));
            using var client = factory.CreateClient();
            Assert.Equal(1, await CountAdminsAsync(factory));
            Assert.Equal(1, await factory.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers"));

            var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = AdminPassword });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var json = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(["Admin", "User"], RolesOf(json));
            Assert.Equal("+21671000000", json.GetProperty("user").GetProperty("phoneNumber").GetString());
            // Consentement enregistré pour le compte créé par l'installation (ADR 0008).
            Assert.NotNull(await factory.ScalarAsync<string>("SELECT PrivacyPolicyVersion FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant())));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    private const string MemberPassword = "MembrePass1";

    /// <summary>Inscrit un membre via l'API publique (base sans admin) et renvoie son jeton.</summary>
    private static async Task<string> RegisterMemberAsync(string connectionString, string email)
    {
        await using var factory = new ApiFactory(connectionString);
        using var client = factory.CreateClient();
        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Membre",
            lastName = "Existant",
            email,
            phoneNumber = "+216 20 123 456",
            password = MemberPassword,
            acceptPrivacyPolicy = true,
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        return System.Text.Json.JsonDocument.Parse(await register.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
    }

    [Fact(DisplayName = "Premier admin : par défaut, un compte existant avec cet e-mail n'est pas promu et l'API refuse de démarrer")]
    public async Task Existing_account_is_not_promoted_by_default()
    {
        var cs = await sql.CreateDatabaseAsync();
        var email = NewAdminEmail();
        try
        {
            await RegisterMemberAsync(cs, email);

            await using var factory = new ApiFactory(cs, Bootstrap(email));
            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            var message = Flatten(ex);
            Assert.Contains("Un compte existe déjà avec Bootstrap:AdminEmail", message, StringComparison.Ordinal);
            Assert.Contains("Bootstrap:PromoteExistingAccount=true", message, StringComparison.Ordinal);
            Assert.DoesNotContain(email, message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await CountAdminsAsync(factory));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Premier admin : avec PromoteExistingAccount=true, le compte existant est promu, sans doublon ni changement de mot de passe, ancien jeton révoqué")]
    public async Task Existing_account_is_promoted_when_explicitly_allowed()
    {
        var cs = await sql.CreateDatabaseAsync();
        var email = NewAdminEmail();
        const string memberPassword = MemberPassword;
        try
        {
            var oldToken = await RegisterMemberAsync(cs, email);

            var overrides = Bootstrap(email, password: "AutreMotDePasse9");
            overrides["Bootstrap:PromoteExistingAccount"] = "true";
            await using var second = new ApiFactory(cs, overrides);
            using var secondClient = second.CreateClient();

            Assert.Equal(1, await second.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant())));
            Assert.Equal(1, await CountAdminsAsync(second));

            // Le mot de passe d'origine est conservé (celui du bootstrap est ignoré pour un compte existant).
            var login = await secondClient.PostAsJsonAsync("/api/auth/login", new { email, password = memberPassword });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var json = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(["Admin", "User"], RolesOf(json));
            Assert.Equal("Membre", json.GetProperty("user").GetProperty("firstName").GetString());
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await secondClient.PostAsJsonAsync("/api/auth/login", new { email, password = "AutreMotDePasse9" })).StatusCode);

            // Le jeton émis avant la promotion (sans rôle Admin) est révoqué par le nouveau security stamp.
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", oldToken);
            Assert.Equal(HttpStatusCode.Unauthorized, (await secondClient.SendAsync(request)).StatusCode);
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Un admin existe déjà : la configuration Bootstrap est ignorée, aucun second admin n'est créé")]
    public async Task No_admin_is_created_when_one_exists()
    {
        var cs = await sql.CreateDatabaseAsync();
        var firstAdmin = NewAdminEmail();
        var otherAdmin = NewAdminEmail();
        try
        {
            await StartAndStopAsync(cs, Bootstrap(firstAdmin));
            await StartAndStopAsync(cs, Bootstrap(otherAdmin, phone: "+216 71 111 111"));

            await using var factory = new ApiFactory(cs);
            using var client = factory.CreateClient();
            Assert.Equal(1, await CountAdminsAsync(factory));
            Assert.Equal(0, await factory.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", otherAdmin.ToUpperInvariant())));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Un admin existe déjà : un membre portant l'e-mail du Bootstrap n'est pas promu")]
    public async Task Existing_member_is_not_promoted_when_an_admin_exists()
    {
        var cs = await sql.CreateDatabaseAsync();
        var admin = NewAdminEmail();
        var member = $"membre.{Guid.NewGuid():N}@exemple.tn";
        try
        {
            await StartAndStopAsync(cs, Bootstrap(admin));
            await using (var factory = new ApiFactory(cs))
            {
                using var client = factory.CreateClient();
                var register = await client.PostAsJsonAsync("/api/auth/register", new
                {
                    firstName = "M", lastName = "N", email = member, phoneNumber = "+216 20 123 456", password = "MembrePass1", acceptPrivacyPolicy = true,
                });
                Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            }

            await using var restarted = new ApiFactory(cs, Bootstrap(member));
            using var restartedClient = restarted.CreateClient();

            Assert.Equal(1, await CountAdminsAsync(restarted));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Sans admin et sans configuration Bootstrap : l'API démarre quand même et avertit dans les journaux")]
    public async Task Startup_without_bootstrap_warns_but_starts()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await using var factory = new ApiFactory(cs);
            using var client = factory.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            Assert.Equal(0, await CountAdminsAsync(factory));
            Assert.Contains(factory.Logs.Messages, m =>
                m.StartsWith("[Warning]", StringComparison.Ordinal) && m.Contains("configuration Bootstrap:* est incomplète", StringComparison.Ordinal));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Journaux du démarrage : le mot de passe et l'e-mail de l'admin n'apparaissent pas")]
    public async Task Startup_logs_do_not_leak_bootstrap_secrets()
    {
        var cs = await sql.CreateDatabaseAsync();
        var email = NewAdminEmail();
        try
        {
            await using var factory = new ApiFactory(cs, Bootstrap(email, password: "Secret-Bootstrap-4411"));
            using var client = factory.CreateClient();

            var all = string.Join("\n", factory.Logs.Messages);
            Assert.Contains("Premier administrateur créé", all);
            Assert.DoesNotContain("Secret-Bootstrap-4411", all);
            Assert.DoesNotContain(email, all, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Theory(DisplayName = "Bootstrap invalide (mot de passe faible ou d'exemple, téléphone non tunisien) : l'API refuse de démarrer")]
    [InlineData("abc", "+216 71 000 000", "Configuration Bootstrap invalide")]
    [InlineData("ChangeMe2026", "+216 71 000 000", "Bootstrap:AdminPassword reprend une valeur d'exemple")]
    [InlineData("AdminTest2026", "71 000 000", "Bootstrap:AdminPhone invalide")]
    public async Task Invalid_bootstrap_prevents_startup(string password, string phone, string expectedMessage)
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await using var factory = new ApiFactory(cs, Bootstrap(NewAdminEmail(), password, phone));

            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            Assert.Contains(expectedMessage, Flatten(ex), StringComparison.Ordinal);
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Theory(DisplayName = "Jwt:SigningKey absente, vide, trop courte ou d'exemple : l'API refuse de démarrer avec un message explicite")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("trop-courte")]
    [InlineData("remplacer-par-une-cle-aleatoire-d-au-moins-32-caracteres")]
    public async Task Missing_or_weak_signing_key_prevents_startup(string? signingKey)
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            var overrides = new Dictionary<string, string?> { ["Jwt:SigningKey"] = signingKey };
            await using var factory = new ApiFactory(cs, overrides);

            var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

            Assert.Contains("Jwt:SigningKey", Flatten(ex));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "Jwt:SigningKey de 32 octets exactement : l'API démarre")]
    public async Task Signing_key_of_minimum_length_is_accepted()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await StartAndStopAsync(cs, new Dictionary<string, string?> { ["Jwt:SigningKey"] = new string('k', 32) });
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    [Fact(DisplayName = "PrivacyPolicy:CurrentVersion est enregistrée avec le consentement")]
    public async Task Privacy_policy_version_comes_from_configuration()
    {
        var cs = await sql.CreateDatabaseAsync();
        try
        {
            await using var factory = new ApiFactory(cs, new Dictionary<string, string?> { ["PrivacyPolicy:CurrentVersion"] = "2.7" });
            using var client = factory.CreateClient();
            var email = $"v.{Guid.NewGuid():N}@exemple.tn";

            var register = await client.PostAsJsonAsync("/api/auth/register", new
            {
                firstName = "A", lastName = "B", email, phoneNumber = "+216 20 123 456", password = "MembrePass1", acceptPrivacyPolicy = true,
            });

            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            Assert.Equal("2.7", await factory.ScalarAsync<string>("SELECT PrivacyPolicyVersion FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant())));
        }
        finally
        {
            await sql.DropDatabaseAsync(cs);
        }
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException!)
        {
            parts.Add(current.Message);
            if (current is AggregateException aggregate)
            {
                parts.AddRange(aggregate.InnerExceptions.Select(Flatten));
            }

            if (current.InnerException is null)
            {
                break;
            }
        }

        return string.Join(" | ", parts);
    }
}
