using Microsoft.Extensions.Options;

namespace Aaas.Application.Options;

/// <summary>
/// Délais des réservations (section « Reservations » d'appsettings.json, ADR 0005, 0020 ; P-R4 : pas modifiables
/// depuis l'interface au MVP). Validés au démarrage : une valeur absurde arrête l'API au lieu de fausser les échéances.
/// </summary>
public sealed class ReservationOptions
{
    public const string SectionName = "Reservations";

    public static readonly TimeSpan MinPendingLifetime = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxPendingLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaxCancellationDeadline = TimeSpan.FromDays(30);
    public static readonly TimeSpan MinJobInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxJobInterval = TimeSpan.FromHours(1);

    /// <summary>RG-04 : durée de vie d'une demande En attente (48 h ; quelques minutes pour une démonstration locale).</summary>
    public TimeSpan PendingLifetime { get; set; } = TimeSpan.FromHours(48);

    /// <summary>RG-05 : une réservation confirmée s'annule jusqu'à « début − ce délai » (24 h).</summary>
    public TimeSpan ConfirmedCancellationDeadline { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Tâche de fond qui écrit « Expirée » (ADR 0020).</summary>
    public ExpirationJobOptions ExpirationJob { get; set; } = new();

    /// <summary>Erreurs de configuration, avec le nom de la clé (jamais de valeur secrète : il n'y en a pas ici).</summary>
    public IReadOnlyList<string> Errors()
    {
        var errors = new List<string>();
        if (ExpirationJob.Interval < MinJobInterval || ExpirationJob.Interval > MaxJobInterval)
        {
            errors.Add($"{SectionName}:{nameof(ExpirationJob)}:{nameof(ExpirationJobOptions.Interval)} doit être compris entre 00:01:00 et 01:00:00.");
        }

        if (PendingLifetime < MinPendingLifetime || PendingLifetime > MaxPendingLifetime)
        {
            errors.Add($"{SectionName}:{nameof(PendingLifetime)} doit être compris entre 00:01:00 et 30.00:00:00.");
        }

        if (ConfirmedCancellationDeadline < TimeSpan.Zero || ConfirmedCancellationDeadline > MaxCancellationDeadline)
        {
            errors.Add($"{SectionName}:{nameof(ConfirmedCancellationDeadline)} doit être compris entre 00:00:00 et 30.00:00:00.");
        }

        return errors;
    }
}

/// <summary>Sous-section « Reservations:ExpirationJob » (ADR 0020 § 4 et 6).</summary>
public sealed class ExpirationJobOptions
{
    /// <summary>Faux : la tâche ne fait rien (l'exactitude reste assurée par le filtre à la lecture, ADR 0005).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Temps entre deux passages (5 min ; bornes 1 min à 1 h).</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Vrai : un passage ne touche la base que si l'API a reçu une requête /api/ depuis le passage précédent, pour
    /// laisser Azure SQL serverless se mettre en pause. Faux : pour observer la tâche en local sans naviguer.
    /// </summary>
    public bool OnlyWhenActive { get; set; } = true;
}

/// <summary>Branché par AddOptions().ValidateOnStart() : IValidateOptions donne un message par clé invalide.</summary>
public sealed class ReservationOptionsValidator : IValidateOptions<ReservationOptions>
{
    public ValidateOptionsResult Validate(string? name, ReservationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = options.Errors();
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
