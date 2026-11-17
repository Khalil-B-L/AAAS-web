using Aaas.Domain.Common;

namespace Aaas.Application.Common.Errors;

/// <summary>Données d'entrée invalides, champ par champ. Traduite en 400 (ValidationProblemDetails).</summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Certains champs sont invalides.")
{
    /// <summary>Clé = nom du champ JSON (camelCase), comme pour la validation automatique des DTO.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Échec d'authentification (identifiants incorrects, compte bloqué). Traduite en 401.</summary>
public sealed class AuthenticationFailedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Ressource absente, ou appartenant à un autre membre (on ne révèle pas son existence). Traduite en 404.</summary>
public sealed class NotFoundException(string message) : Exception(message)
{
    public string Code { get; } = ErrorCodes.NotFound;
}
