using Aaas.Infrastructure.Jobs;

namespace Aaas.Api.Jobs;

/// <summary>
/// Note qu'une requête /api/ a abouti (ADR 0020 § 4) : la tâche d'expiration ne touche la base qu'après une
/// activité réelle. Ne comptent pas :
/// - /health, /health/ready et /openapi : les sondes Docker ou App Service (une par minute) empêcheraient sinon
///   Azure SQL serverless de se mettre en pause ;
/// - les réponses 4xx/5xx : un robot qui balaie /api/... en anonyme (401, 404) ne doit pas réveiller la base.
/// </summary>
internal static class ApiActivityMiddleware
{
    private static readonly PathString ApiPrefix = new("/api");

    public static IApplicationBuilder UseApiActivityTracking(this IApplicationBuilder app)
    {
        // Singleton résolu une fois au démarrage, pas à chaque requête.
        var activity = app.ApplicationServices.GetRequiredService<ApiActivity>();
        return app.Use(async (context, next) =>
        {
            // Le code n'est connu qu'au retour : placé avant UseExceptionHandler, ce middleware voit aussi le 500
            // que celui-ci a écrit (l'exception ne remonte pas jusqu'ici).
            await next(context);
            if (context.Response.StatusCode < 400
                && context.Request.Path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase))
            {
                activity.RecordRequest();
            }
        });
    }
}
