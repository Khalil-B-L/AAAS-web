namespace Aaas.Infrastructure.Identity;

/// <summary>Noms des claims propres à l'application, partagés par l'émission et la validation du jeton.</summary>
public static class AppClaimTypes
{
    /// <summary>Security stamp Identity au moment de l'émission (révocation, ADR 0002).</summary>
    public const string SecurityStamp = "sst";

    public const string Role = "role";
}
