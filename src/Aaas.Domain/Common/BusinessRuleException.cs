namespace Aaas.Domain.Common;

/// <summary>
/// Violation d'une règle métier. Traduite par l'API en ProblemDetails 409 :
/// <see cref="Exception.Message"/> devient le <c>title</c> (français, affichable tel quel),
/// <see cref="Code"/> devient l'extension <c>code</c> (stable, utilisée par le front).
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
