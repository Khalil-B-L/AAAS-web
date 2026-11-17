using System.Net;
using System.Net.Http.Json;
using Aaas.Api.IntegrationTests.Support;

namespace Aaas.Api.IntegrationTests.Accounts;

/// <summary>US-A1 : créer un compte (R20, R21, R24).</summary>
public sealed class RegistrationTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact(DisplayName = "La base de test a bien READ_COMMITTED_SNAPSHOT activé (comme Azure SQL)")]
    public async Task Test_database_uses_read_committed_snapshot()
    {
        var enabled = await Factory.ScalarAsync<bool>("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()");

        Assert.True(enabled);
    }

    [Fact(DisplayName = "Inscription valide : 201, rôle User, consentement et version en base, mot de passe haché")]
    public async Task Register_valid_returns_201_and_persists_user()
    {
        var email = NewEmail();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var response = await RegisterAsync(RegistrationBody(email, phone: "+216 20 123 456"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/account/me", response.Headers.Location?.OriginalString);

        var json = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("accessToken").GetString()));
        var user = json.GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("+21620123456", user.GetProperty("phoneNumber").GetString());
        Assert.Equal(["User"], user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
        Assert.Equal("1.0", user.GetProperty("privacyPolicyVersion").GetString());
        Assert.False(json.ToString().Contains("passwordHash", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.ToString().Contains("securityStamp", StringComparison.OrdinalIgnoreCase));

        var userId = user.GetProperty("id").GetGuid();
        var consentAt = await Factory.ScalarAsync<DateTimeOffset>("SELECT PrivacyConsentAt FROM AspNetUsers WHERE Id = @id", ("@id", userId));
        var version = await Factory.ScalarAsync<string>("SELECT PrivacyPolicyVersion FROM AspNetUsers WHERE Id = @id", ("@id", userId));
        var createdAt = await Factory.ScalarAsync<DateTimeOffset>("SELECT CreatedAt FROM AspNetUsers WHERE Id = @id", ("@id", userId));
        var phone = await Factory.ScalarAsync<string>("SELECT PhoneNumber FROM AspNetUsers WHERE Id = @id", ("@id", userId));
        Assert.Equal("1.0", version);
        Assert.InRange(consentAt, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.InRange(createdAt, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal("+21620123456", phone);

        var roles = await Factory.ScalarAsync<string>(
            "SELECT STRING_AGG(r.Name, ',') FROM AspNetUserRoles ur JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE ur.UserId = @id",
            ("@id", userId));
        Assert.Equal("User", roles);
    }

    [Fact(DisplayName = "Inscription : le mot de passe est stocké haché, jamais en clair")]
    public async Task Register_stores_hashed_password()
    {
        var email = NewEmail();

        var response = await RegisterAsync(RegistrationBody(email, password: "SecretTest1234"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var hash = await Factory.ScalarAsync<string>("SELECT PasswordHash FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.DoesNotContain("SecretTest1234", hash);
        Assert.StartsWith("AQAAAA", hash); // en-tête du format PBKDF2 d'Identity v3
    }

    [Fact(DisplayName = "Inscription : le rôle Admin ne peut pas être demandé dans le corps de la requête")]
    public async Task Register_ignores_role_in_body()
    {
        var email = NewEmail();

        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Mallory",
            lastName = "Test",
            email,
            phoneNumber = ValidPhone,
            password = ValidPassword,
            acceptPrivacyPolicy = true,
            role = "Admin",
            roles = new[] { "Admin" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var roles = (await ReadJsonAsync(response)).GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()!);
        Assert.Equal(["User"], roles);
    }

    [Fact(DisplayName = "Inscription : prénom, nom et e-mail sont enregistrés sans espaces de bord")]
    public async Task Register_trims_names_and_email()
    {
        var email = NewEmail();

        var response = await RegisterAsync(RegistrationBody("  " + email + "  ", firstName: "  Salma ", lastName: " Ben Ali  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = (await ReadJsonAsync(response)).GetProperty("user");
        Assert.Equal("Salma", user.GetProperty("firstName").GetString());
        Assert.Equal("Ben Ali", user.GetProperty("lastName").GetString());
        Assert.Equal(email, user.GetProperty("email").GetString());
    }

    [Fact(DisplayName = "Inscription : l'utilisateur est connecté, le jeton donne accès à /api/account/me")]
    public async Task Register_token_is_usable_immediately()
    {
        var (email, token, _) = await RegisterMemberAsync();

        var me = await GetMeAsync(token);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await ReadJsonAsync(me)).GetProperty("email").GetString());
    }

    [Fact(DisplayName = "Inscription : doublon d'e-mail refusé (409 email_taken), sans tenir compte de la casse")]
    public async Task Register_duplicate_email_is_case_insensitive()
    {
        var email = $"Doublon.{Guid.NewGuid():N}@Exemple.TN";
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(RegistrationBody(email))).StatusCode);

        var duplicate = await RegisterAsync(RegistrationBody(email.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await ReadJsonAsync(duplicate);
        Assert.Equal("email_taken", problem.GetProperty("code").GetString());
        Assert.Equal("Un compte existe déjà avec cet e-mail.", problem.GetProperty("title").GetString());
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        var count = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.Equal(1, count);
    }

    [Fact(DisplayName = "Inscription : doublon d'e-mail avec espaces autour refusé aussi")]
    public async Task Register_duplicate_email_with_padding_is_rejected()
    {
        var email = NewEmail();
        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(RegistrationBody(email))).StatusCode);

        var duplicate = await RegisterAsync(RegistrationBody($" {email} "));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact(DisplayName = "Inscriptions simultanées avec le même e-mail : une seule réussit, les autres reçoivent 409")]
    public async Task Register_concurrently_same_email_only_one_succeeds()
    {
        var email = NewEmail("course");
        const int attempts = 8;

        var responses = await Task.WhenAll(Enumerable.Range(0, attempts)
            .Select(_ => Task.Run(() => RegisterAsync(RegistrationBody(email)))));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(attempts - 1, statuses.Count(s => s == HttpStatusCode.Conflict));
        var count = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.Equal(1, count);
        // Le compte créé a bien son rôle : la transaction (compte + rôle) est atomique.
        var roleCount = await Factory.ScalarAsync<int>(
            "SELECT COUNT(*) FROM AspNetUserRoles ur JOIN AspNetUsers u ON u.Id = ur.UserId WHERE u.NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.Equal(1, roleCount);

        // R24 : la violation d'index unique (2601) est un cas prévu (409). Le message SQL contient la valeur dupliquée :
        // il ne doit apparaître ni comme erreur ni dans les journaux.
        var logs = string.Join("\n", Factory.Logs.Messages);
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[Error]", logs, StringComparison.Ordinal);
    }

    // ---------- 400 : une erreur par champ ----------

    private async Task AssertFieldErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ReadJsonAsync(response);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out var messages), $"Pas d'erreur pour « {field} » : {errors}");
        Assert.True(messages.GetArrayLength() > 0);
    }

    [Theory(DisplayName = "Inscription : téléphone non tunisien, 400 sur phoneNumber")]
    [InlineData("12 34")]
    [InlineData("20 123 456")]
    [InlineData("0021620123456")]
    [InlineData("+33 6 12 34 56 78")]
    [InlineData("+216 2012345")]
    public async Task Register_invalid_phone_returns_400(string phone)
    {
        var response = await RegisterAsync(RegistrationBody(phone: phone));

        await AssertFieldErrorAsync(response, "phoneNumber");
    }

    [Theory(DisplayName = "Inscription : mot de passe faible, 400 sur password (aucun compte créé)")]
    [InlineData("abc")]
    [InlineData("abcdef1")] // 7 caractères : lettre et chiffre présents, mais trop court (limite de R21)
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    public async Task Register_weak_password_returns_400(string password)
    {
        var email = NewEmail();

        var response = await RegisterAsync(RegistrationBody(email, password));

        await AssertFieldErrorAsync(response, "password");
        var count = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.Equal(0, count);
    }

    [Fact(DisplayName = "Inscription : mot de passe de 8 caractères avec lettre et chiffre accepté")]
    public async Task Register_minimal_compliant_password_is_accepted()
    {
        var response = await RegisterAsync(RegistrationBody(password: "abcdefg1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact(DisplayName = "Inscription : consentement absent ou refusé, 400 sur acceptPrivacyPolicy")]
    public async Task Register_without_consent_returns_400()
    {
        var email = NewEmail();

        var refused = await RegisterAsync(RegistrationBody(email, consent: false));
        await AssertFieldErrorAsync(refused, "acceptPrivacyPolicy");

        var absent = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Salma",
            lastName = "Ben Ali",
            email,
            phoneNumber = ValidPhone,
            password = ValidPassword,
        });
        await AssertFieldErrorAsync(absent, "acceptPrivacyPolicy");

        var count = await Factory.ScalarAsync<int>("SELECT COUNT(*) FROM AspNetUsers WHERE NormalizedEmail = @e", ("@e", email.ToUpperInvariant()));
        Assert.Equal(0, count);
    }

    [Fact(DisplayName = "Inscription : nom manquant, 400 sur lastName")]
    public async Task Register_missing_last_name_returns_400()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Salma",
            email = NewEmail(),
            phoneNumber = ValidPhone,
            password = ValidPassword,
            acceptPrivacyPolicy = true,
        });

        await AssertFieldErrorAsync(response, "lastName");
    }

    [Fact(DisplayName = "Inscription : prénom manquant, 400 sur firstName")]
    public async Task Register_missing_first_name_returns_400()
    {
        var response = await RegisterAsync(RegistrationBody(firstName: ""));

        await AssertFieldErrorAsync(response, "firstName");
    }

    [Theory(DisplayName = "Inscription : e-mail invalide, 400 sur email")]
    [InlineData("pas-un-email")]
    [InlineData("salma@")]
    [InlineData("")]
    public async Task Register_invalid_email_returns_400(string email)
    {
        var response = await RegisterAsync(RegistrationBody(email));

        await AssertFieldErrorAsync(response, "email");
    }

    [Fact(DisplayName = "Inscription : corps vide, 400 avec une erreur par champ obligatoire")]
    public async Task Register_empty_body_returns_400_with_all_required_fields()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        foreach (var field in new[] { "firstName", "lastName", "email", "phoneNumber", "password", "acceptPrivacyPolicy" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Erreur attendue sur « {field} »");
        }
    }

    [Fact(DisplayName = "Inscription : JSON mal formé, 400")]
    public async Task Register_malformed_json_returns_400()
    {
        var content = new StringContent("{ \"firstName\": ", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/auth/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
