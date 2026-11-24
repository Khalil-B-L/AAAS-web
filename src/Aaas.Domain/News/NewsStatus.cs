namespace Aaas.Domain.News;

/// <summary>
/// Statut stocké d'une actualité (tinyint, ADR 0022). Valeurs FIGÉES : elles sont en base et dans la contrainte
/// CK_News_Status (« Status IN (1, 2) ») ; changer une valeur casserait les données sans erreur de compilation.
/// Le statut est la seule source de vérité de la visibilité publique (jamais PublishedAt, conservée à la dépublication).
/// </summary>
public enum NewsStatus : byte
{
    Draft = 1,
    Published = 2,
}
