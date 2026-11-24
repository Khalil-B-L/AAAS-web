namespace Aaas.Api.Errors;

/// <summary>
/// ADR 0021 § 4 : « Cache-Control: no-store » sur toutes les réponses de /api/me et /api/admin (réservations,
/// coordonnées des inscrits) : sur un ordinateur partagé, le navigateur ne les garde pas sur disque.
/// Middleware par préfixe plutôt que filtre MVC : l'en-tête est posé au démarrage de la réponse (OnStarting), donc
/// aussi sur les 401/403/404/409 écrits par l'authentification et par ApiExceptionHandler (qui vide les en-têtes).
/// </summary>
internal static class NoStoreMiddleware
{
    private static readonly PathString[] Prefixes = [new("/api/me"), new("/api/admin")];

    public static IApplicationBuilder UseNoStoreForPersonalData(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (Prefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.OnStarting(static state =>
                {
                    var response = (HttpResponse)state;
                    response.Headers.CacheControl = "no-store";
                    return Task.CompletedTask;
                }, context.Response);
            }

            await next(context);
        });
}
