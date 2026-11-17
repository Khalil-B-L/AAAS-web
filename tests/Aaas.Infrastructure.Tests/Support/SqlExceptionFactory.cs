using System.Reflection;
using Microsoft.Data.SqlClient;

namespace Aaas.Infrastructure.Tests.Support;

/// <summary>
/// SqlException n'a aucun constructeur public : on la fabrique par réflexion (constructeurs internes de
/// SqlError / SqlErrorCollection / SqlException), pour tester la reconnaissance des numéros d'erreur sans serveur.
/// </summary>
public static class SqlExceptionFactory
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static SqlException Create(int number, string message = "Cannot insert duplicate key row ... The duplicate key value is (secret@exemple.tn).")
    {
        var errorCtor = typeof(SqlError).GetConstructors(All).OrderByDescending(c => c.GetParameters().Length).First();
        var errorArgs = errorCtor.GetParameters().Select((p, i) => i == 0 ? number : DefaultFor(p.ParameterType, message)).ToArray();
        var error = (SqlError)errorCtor.Invoke(errorArgs);

        var collection = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), true)!;
        typeof(SqlErrorCollection).GetMethod("Add", All)!.Invoke(collection, [error]);

        var create = typeof(SqlException).GetMethods(All)
            .Where(m => m.Name == "CreateException" && m.GetParameters().Length >= 2
                && m.GetParameters()[0].ParameterType == typeof(SqlErrorCollection) && m.GetParameters()[1].ParameterType == typeof(string))
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault();
        if (create is not null)
        {
            var args = create.GetParameters().Select((p, i) => i == 0 ? collection : i == 1 ? "11.0.0" : DefaultFor(p.ParameterType, message)).ToArray();
            return (SqlException)create.Invoke(null, args)!;
        }

        throw new InvalidOperationException("SqlException.CreateException introuvable : adapter SqlExceptionFactory à la version de Microsoft.Data.SqlClient.");
    }

    private static object? DefaultFor(Type type, string message)
    {
        if (type == typeof(string))
        {
            return message;
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
