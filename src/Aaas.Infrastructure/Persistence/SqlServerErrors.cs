using Microsoft.Data.SqlClient;

namespace Aaas.Infrastructure.Persistence;

/// <summary>Reconnaissance des erreurs SQL Server, sans dépendre du texte du message (localisé, et porteur de données).</summary>
public static class SqlServerErrors
{
    /// <summary>2601 : violation d'index unique ; 2627 : violation de contrainte unique ou de clé primaire.</summary>
    public static bool IsUniqueKeyViolation(Exception? exception) => TryGetUniqueKeyViolationNumber(exception, out _);

    /// <summary>
    /// Parcourt la chaîne des InnerException (DbUpdateException enveloppe la SqlException).
    /// Le message d'une telle SqlException contient la valeur dupliquée (souvent un e-mail, R24) : ne pas le journaliser.
    /// </summary>
    public static bool TryGetUniqueKeyViolationNumber(Exception? exception, out int sqlErrorNumber) =>
        TryGetSqlErrorNumber(exception, out sqlErrorNumber) && sqlErrorNumber is 2601 or 2627;

    /// <summary>Numéro de la première SqlException de la chaîne : journalisable sans risque, contrairement au message.</summary>
    public static bool TryGetSqlErrorNumber(Exception? exception, out int sqlErrorNumber)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                sqlErrorNumber = sql.Number;
                return true;
            }
        }

        sqlErrorNumber = 0;
        return false;
    }
}
