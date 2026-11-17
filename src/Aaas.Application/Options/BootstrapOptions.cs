namespace Aaas.Application.Options;

/// <summary>
/// Premier administrateur créé au démarrage s'il n'existe aucun admin (ADR 0008, R22).
/// Valeurs secrètes : user-secrets en local, .env pour docker compose, Key Vault en Azure. Jamais dans le dépôt.
/// </summary>
public sealed class BootstrapOptions
{
    public const string SectionName = "Bootstrap";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    public string? AdminFirstName { get; set; }
    public string? AdminLastName { get; set; }
    public string? AdminPhone { get; set; }

    /// <summary>
    /// false par défaut : si un compte porte déjà AdminEmail, le démarrage échoue au lieu de le promouvoir.
    /// Sinon, quiconque s'inscrit avec cet e-mail avant le premier démarrage deviendrait admin.
    /// </summary>
    public bool PromoteExistingAccount { get; set; }

    /// <summary>Mots de passe d'exemple déjà publiés (docs, anciens .env.example) : refusés pour créer l'admin.</summary>
    public static IReadOnlySet<string> KnownExamplePasswords { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ChangeMe2026" };

    public bool IsComplete => MissingKeys.Count == 0;

    /// <summary>Clés vides ou absentes (noms seuls, jamais les valeurs) : pour un message de démarrage précis.</summary>
    public IReadOnlyList<string> MissingKeys =>
    [
        .. new (string Key, string? Value)[]
        {
            ($"{SectionName}:{nameof(AdminEmail)}", AdminEmail),
            ($"{SectionName}:{nameof(AdminPassword)}", AdminPassword),
            ($"{SectionName}:{nameof(AdminFirstName)}", AdminFirstName),
            ($"{SectionName}:{nameof(AdminLastName)}", AdminLastName),
            ($"{SectionName}:{nameof(AdminPhone)}", AdminPhone),
        }
        .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
        .Select(entry => entry.Key),
    ];
}
