using Aaas.Application.Common.Errors;
using Aaas.Domain.Common;
using Aaas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Errors;

/// <summary>
/// Traduit les exceptions connues en ProblemDetails (RFC 9457) avec l'extension « code » (ADR 0001).
/// Le « title » est en français et affichable tel quel par le front.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Le client a fermé la connexion : ni erreur serveur, ni corps à écrire (personne ne le lira).
            // Le middleware d'ASP.NET Core le fait déjà en amont ; ce cas protège si l'ordre du pipeline change.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Requête {Method} {Path} annulée par le client.", httpContext.Request.Method, httpContext.Request.Path);
            }
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        var (problem, code) = exception switch
        {
            RequestValidationException validation => (
                new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = validation.Message,
                },
                ErrorCodes.ValidationFailed),
            AuthenticationFailedException auth => (Problem(StatusCodes.Status401Unauthorized, auth.Message), auth.Code),
            NotFoundException notFound => (Problem(StatusCodes.Status404NotFound, notFound.Message), notFound.Code),
            BusinessRuleException rule => (Problem(StatusCodes.Status409Conflict, rule.Message), rule.Code),
            _ => (Problem(StatusCodes.Status500InternalServerError, "Une erreur inattendue est survenue. Veuillez réessayer plus tard."),
                ErrorCodes.InternalError),
        };

        if (problem.Status == StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(httpContext, exception);
        }

        problem.Extensions[ProblemDetailsSetup.CodeExtension] = code;
        httpContext.Response.StatusCode = problem.Status!.Value;
        await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });

        // Toujours « géré », même si aucun writer n'accepte l'en-tête Accept (statut seul, sans corps) :
        // renvoyer false rendrait la main au middleware, qui journaliserait l'exception brute (et son message SQL).
        return true;
    }

    private void LogUnhandled(HttpContext httpContext, Exception exception)
    {
        if (SqlServerErrors.TryGetValueRevealingErrorNumber(exception, out var sqlErrorNumber))
        {
            // R24 : le message SQL d'une violation d'unicité (2601/2627) contient la valeur dupliquée (souvent un
            // e-mail), celui d'une troncature (2628) la valeur tronquée. On journalise sans l'exception : type,
            // numéro SQL et requête suffisent à localiser le code fautif.
            logger.LogError(
                "Erreur SQL {SqlErrorNumber} non interceptée, journalisée sans son message ({ExceptionType}) sur {Method} {Path}",
                sqlErrorNumber, exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path);
            return;
        }

        logger.LogError(exception, "Erreur non gérée sur {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
    }

    private static ProblemDetails Problem(int status, string title) => new() { Status = status, Title = title };
}
