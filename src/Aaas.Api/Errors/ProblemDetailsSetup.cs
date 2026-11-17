using Aaas.Domain.Common;

namespace Aaas.Api.Errors;

/// <summary>
/// Complète toutes les ProblemDetails, y compris celles produites par le framework
/// (400 de validation automatique, 401/403/404 sans corps) : code stable + titre français.
/// </summary>
internal static class ProblemDetailsSetup
{
    public const string CodeExtension = "code";

    private static readonly Dictionary<int, (string Code, string Title)> Defaults = new()
    {
        [StatusCodes.Status400BadRequest] = (ErrorCodes.ValidationFailed, "Certains champs sont invalides."),
        [StatusCodes.Status401Unauthorized] = (ErrorCodes.Unauthorized, "Vous devez être connecté. Votre session a peut-être expiré."),
        [StatusCodes.Status403Forbidden] = (ErrorCodes.Forbidden, "Vous n'avez pas les droits nécessaires pour cette action."),
        [StatusCodes.Status404NotFound] = (ErrorCodes.NotFound, "Contenu introuvable."),
        [StatusCodes.Status500InternalServerError] = (ErrorCodes.InternalError, "Une erreur inattendue est survenue. Veuillez réessayer plus tard."),
    };

    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            // Une ProblemDetails qui a déjà un code vient de ApiExceptionHandler : son titre est déjà le bon.
            if (problem.Extensions.ContainsKey(CodeExtension)
                || problem.Status is not { } status
                || !Defaults.TryGetValue(status, out var defaults))
            {
                return;
            }

            problem.Extensions[CodeExtension] = defaults.Code;
            problem.Title = defaults.Title;
        });
}
