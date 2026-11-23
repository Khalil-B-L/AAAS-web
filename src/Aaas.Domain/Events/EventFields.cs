namespace Aaas.Domain.Events;

/// <summary>Noms JSON des champs saisissables : clés des erreurs 400, identiques à celles de la validation des DTO.</summary>
public static class EventFields
{
    public const string Title = "title";
    public const string Description = "description";
    public const string Location = "location";
    public const string StartsAt = "startsAt";
    public const string EndsAt = "endsAt";
    public const string Capacity = "capacity";
    public const string Price = "price";
}
