using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Aaas.Infrastructure.Identity;

/// <summary>Configuration des jetons (section « Jwt »), validée au démarrage : l'API refuse de démarrer sans clé.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32; // HS256 : clé d'au moins 256 bits

    [Required(ErrorMessage = "Jwt:Issuer est obligatoire.")]
    public string Issuer { get; set; } = string.Empty;

    [Required(ErrorMessage = "Jwt:Audience est obligatoire.")]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Secret : user-secrets en local, variable Jwt__SigningKey dans Docker, Key Vault en Azure.</summary>
    [Required(ErrorMessage = "Jwt:SigningKey est obligatoire (user-secrets, variable d'environnement Jwt__SigningKey ou Key Vault).")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(5, 24 * 60, ErrorMessage = "Jwt:LifetimeMinutes doit être compris entre 5 et 1440.")]
    public int LifetimeMinutes { get; set; } = 60;

    public byte[] SigningKeyBytes => Encoding.UTF8.GetBytes(SigningKey);

    public bool HasStrongEnoughKey => Encoding.UTF8.GetByteCount(SigningKey) >= MinimumKeyBytes;

    /// <summary>Valeurs d'exemple déjà publiées (ancien .env.example) : assez longues, mais connues de tous.</summary>
    public static IReadOnlySet<string> KnownExampleKeys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "remplacer-par-une-cle-aleatoire-d-au-moins-32-caracteres",
    };

    public bool IsKnownExampleKey => KnownExampleKeys.Contains(SigningKey.Trim());
}
