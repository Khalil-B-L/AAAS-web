using System.ComponentModel.DataAnnotations;

namespace Aaas.Application.Common;

/// <summary>
/// Paramètres de pagination par décalage (ADR 0011), lus dans la query string (?page=&amp;pageSize=).
/// La taille par défaut est un paramètre du constructeur : un initialiseur de propriété ne se redéfinit pas dans une
/// classe dérivée, et les actualités publiques en veulent 10 quand les autres listes en veulent 20.
/// </summary>
public abstract class PagingQuery
{
    public const int MaxPage = 10_000;
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 20;

    protected PagingQuery(int defaultPageSize = DefaultPageSize)
    {
        PageSize = defaultPageSize;
    }

    // Borne haute : sans elle, (page - 1) * pageSize déborde pour une page énorme (Skip négatif → 500).
    [Range(1, MaxPage, ErrorMessage = "Le numéro de page doit être compris entre 1 et 10 000.")]
    public int Page { get; init; } = 1;

    [Range(1, MaxPageSize, ErrorMessage = "Le nombre d'éléments par page doit être compris entre 1 et 50.")]
    public int PageSize { get; init; }
}
