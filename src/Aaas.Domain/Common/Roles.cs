namespace Aaas.Domain.Common;

/// <summary>Rôles de l'application. Les noms sont stockés en base (AspNetRoles) : ne pas les renommer.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static IReadOnlyList<string> All { get; } = [Admin, User];
}
