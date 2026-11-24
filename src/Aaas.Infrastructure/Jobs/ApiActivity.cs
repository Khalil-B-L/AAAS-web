namespace Aaas.Infrastructure.Jobs;

/// <summary>
/// « L'API a-t-elle reçu une requête /api/ depuis le dernier passage de la tâche ? » (ADR 0020 § 4).
/// Singleton : le middleware de l'Api l'écrit à chaque requête, ExpirePendingReservationsJob le lit et le remet à zéro.
/// Un drapeau plutôt qu'une heure : la question est « depuis le passage précédent », pas « depuis quand »,
/// et un drapeau ne dépend d'aucune horloge (réelle ou simulée).
/// </summary>
public sealed class ApiActivity
{
    // int et non bool : Interlocked.Exchange n'existe pas pour bool. 1 = activité non encore consommée.
    private int _pending;

    /// <summary>Appelé par le middleware : une simple écriture, sans verrou (coût négligeable par requête).</summary>
    public void RecordRequest() => Volatile.Write(ref _pending, 1);

    /// <summary>Lit et remet à zéro en une seule opération atomique : une requête arrivée pendant le passage n'est pas perdue.</summary>
    public bool TryConsume() => Interlocked.Exchange(ref _pending, 0) == 1;

    /// <summary>Pour les tests et le diagnostic.</summary>
    public bool HasPendingActivity => Volatile.Read(ref _pending) == 1;
}
