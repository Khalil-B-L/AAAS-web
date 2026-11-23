namespace Aaas.Application.Common;

/// <summary>
/// Conversion du rowversion SQL Server (8 octets) en chaîne Base64 pour le champ « version » des DTO (ADR 0006).
/// Le client ne l'interprète pas : il la renvoie telle quelle à la modification.
/// </summary>
public static class RowVersions
{
    public const int Length = 8;

    public static string Encode(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    /// <summary>La valeur a déjà été validée par [RowVersion] ; une valeur invalide ici est une erreur de programmation.</summary>
    public static byte[] Decode(string version) => TryDecode(version, out var bytes)
        ? bytes
        : throw new FormatException("Version invalide (Base64 de 8 octets attendu).");

    public static bool TryDecode(string? version, out byte[] bytes)
    {
        bytes = new byte[Length];
        // Tampon de 8 octets exactement : une valeur plus longue fait échouer TryFromBase64String.
        return !string.IsNullOrWhiteSpace(version)
            && Convert.TryFromBase64String(version, bytes, out var written)
            && written == Length;
    }
}
