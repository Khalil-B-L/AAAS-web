namespace Aaas.Application.Common;

public static class QueryablePaging
{
    /// <summary>
    /// OFFSET … FETCH NEXT … en SQL. À appeler après un tri TOTAL (complété par Id) : sinon deux lignes de même clé
    /// peuvent changer d'ordre d'une requête à l'autre et apparaître sur deux pages, ou sur aucune.
    /// </summary>
    public static IQueryable<T> Page<T>(this IQueryable<T> source, int page, int pageSize) =>
        source.Skip((page - 1) * pageSize).Take(pageSize);

    public static IQueryable<T> Page<T>(this IQueryable<T> source, PagingQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return source.Page(query.Page, query.PageSize);
    }
}
