namespace Aaas.Application.Common;

/// <summary>Enveloppe des listes paginées (ADR 0011). Le front calcule le nombre de pages : ceil(totalCount / pageSize).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
