namespace Aaas.Application.Common.Errors;

/// <summary>
/// Violation d'un index unique, traduite par Infrastructure (seul endroit qui connaît SqlClient, ADR 0019 § 3).
/// Volontairement SANS InnerException ni détail : le message SQL d'origine contient les valeurs dupliquées (R24).
/// Le service appelant la traduit en règle métier (insertion de Reservation → 409 already_reserved).
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException()
        : base("Violation d'un index unique (détail non conservé : il peut contenir des données).")
    {
    }

    // Pas de constructeur (message, innerException) : c'est précisément ce qu'on refuse de transporter.
}
