namespace Aaas.Domain.Common;

/// <summary>
/// Codes d'erreur stables renvoyés dans l'extension <c>code</c> des ProblemDetails.
/// Le front s'appuie dessus : ne jamais renommer un code existant.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidPhoneNumber = "invalid_phone_number";
    public const string EmailTaken = "email_taken";
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountLocked = "account_locked";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string InternalError = "internal_error";
}
