namespace Aaas.Domain.Common;

/// <summary>
/// Violation d'une règle métier. Traduite par l'API en ProblemDetails 409 :
/// <see cref="Exception.Message"/> devient le <c>title</c> (français, affichable tel quel),
/// <see cref="Code"/> devient l'extension <c>code</c> (stable, utilisée par le front),
/// et chaque entrée de <see cref="Details"/> devient une extension de plus (ADR 0018 § 4).
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message)
        : this(code, message, details: null)
    {
    }

    public BusinessRuleException(string code, string message, IReadOnlyDictionary<string, object?>? details)
        : base(message)
    {
        Code = code;
        Details = details ?? new Dictionary<string, object?>();
    }

    public string Code { get; }

    /// <summary>
    /// État réel qui voyage avec le 409 (<see cref="ErrorDetailKeys"/>) : jamais de donnée personnelle,
    /// seulement des identifiants et des statuts (R24).
    /// </summary>
    public IReadOnlyDictionary<string, object?> Details { get; }
}
