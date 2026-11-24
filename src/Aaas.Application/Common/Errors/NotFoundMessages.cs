namespace Aaas.Application.Common.Errors;

public static class NotFoundMessages
{
    /// <summary>
    /// Contenu public absent : brouillon, dépublié, inconnu ou identifiant mal formé (événements et actualités).
    /// Un seul message pour ne jamais révéler l'existence d'un brouillon.
    /// </summary>
    public const string PublicContent = "Contenu introuvable.";
}
