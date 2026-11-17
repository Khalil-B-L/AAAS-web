using System.Net;
using System.Net.Http.Json;
using Aaas.Api.IntegrationTests.Support;
using Aaas.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Aaas.Api.IntegrationTests.Infrastructure;

/// <summary>Protection des données (R24) : aucun mot de passe, e-mail ni téléphone dans les journaux.</summary>
public sealed class LoggingTests(SqlServerFixture sql) : ApiTestBase(sql)
{
    [Fact(DisplayName = "Journaux : inscription, connexion, échecs et doublon ne laissent ni mot de passe, ni e-mail, ni téléphone")]
    public async Task Logs_do_not_contain_personal_data()
    {
        const string password = "Zx9-Uniq-Pass-7391";
        const string wrongPassword = "Wr0ng-Uniq-Pass-5521";
        const string phoneDigits = "98765432";
        var email = $"journal.{Guid.NewGuid():N}@exemple.tn";

        var register = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Prenom-Unique",
            lastName = "Nom-Unique",
            email,
            phoneNumber = $"+216 {phoneDigits[..2]} {phoneDigits[2..5]} {phoneDigits[5..]}",
            password,
            acceptPrivacyPolicy = true,
        });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        await RegisterAsync(RegistrationBody(email, password)); // doublon (409)
        await RegisterAsync(RegistrationBody(NewEmail(), "abc", phone: "+216 11 222 333")); // 400
        await LoginAsync(email, password);
        await LoginAsync(email, wrongPassword);
        await LoginAsync($"inconnu.{Guid.NewGuid():N}@exemple.tn", wrongPassword);
        await GetMeAsync("jeton-invalide");

        var logs = Factory.Logs.Messages;

        Assert.NotEmpty(logs); // le test observe bien quelque chose
        var all = string.Join("\n", logs);
        Assert.DoesNotContain(password, all);
        Assert.DoesNotContain(wrongPassword, all);
        Assert.DoesNotContain(email, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(phoneDigits, all);
        Assert.DoesNotContain("Prenom-Unique", all);
        Assert.DoesNotContain("Nom-Unique", all);
        Assert.DoesNotContain("[Error]", all); // aucun 500 inattendu pendant ces scénarios
    }

    [Fact(DisplayName = "Journaux : un INSERT refusé par l'index unique (provoqué directement, sans course) ne fuit ni e-mail ni Error")]
    public async Task Forced_unique_violation_does_not_leak_email()
    {
        // Le doublon via l'API peut être arrêté par la vérification préalable sans jamais atteindre l'INSERT :
        // ici la violation 2601 est garantie, après un parcours complet inscription / connexion.
        var (email, _, _) = await RegisterMemberAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await RegisterAsync(RegistrationBody(email))).StatusCode);
        await LoginAsync(email, ValidPassword);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            UniqueViolation.AssertIsUniqueIndexViolation(await UniqueViolation.InsertDuplicateEmailAsync(db, email));
        }

        var all = string.Join("\n", Factory.Logs.Messages);
        Assert.DoesNotContain(email, all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValidPassword, all);
        Assert.DoesNotContain("[Error]", all);
    }
}
