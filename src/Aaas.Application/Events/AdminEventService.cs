using Aaas.Application.Abstractions;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.Options;
using Aaas.Application.Settings;
using Aaas.Domain.Common;
using Aaas.Domain.Events;
using Aaas.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aaas.Application.Events;

/// <summary>Gestion des événements par un admin (US-E2) : créer, lire, lister, modifier, publier, dépublier, supprimer un brouillon.</summary>
public sealed class AdminEventService(
    IAppDbContext db,
    IEventLock eventLock,
    EventOccupancyReader occupancyReader,
    SettingsService settings,
    IOptions<ReservationOptions> reservationOptions,
    TimeProvider time)
{
    public const string NotFoundMessage = "Événement introuvable.";
    internal const string ContactNotConfiguredMessage =
        "Aucun numéro de contact n'est configuré : les membres ne sauront pas qui appeler pour faire confirmer leur demande.";

    public async Task<AdminEventDto> CreateAsync(CreateEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = time.GetUtcNow();
        var details = ToDetails(request);
        FieldErrors.ThrowIfAny(EventRules.Validate(details, previousStartsAt: null, now));

        var created = Event.Create(details, now);
        db.Events.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return ToAdminDto(created, EventOccupancy.None, now);
    }

    public async Task<AdminEventDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var found = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(NotFoundMessage);
        return ToAdminDto(found, await occupancyReader.GetAsync(id, now, cancellationToken), now);
    }

    public async Task<PagedResult<AdminEventSummaryDto>> ListAsync(AdminEventListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = time.GetUtcNow();

        var filtered = db.Events.AsNoTracking().InPeriod(query.Period, now);
        if (query.Status is { } status)
        {
            filtered = filtered.Where(e => e.Status == status);
        }

        var total = await filtered.CountAsync(cancellationToken);
        var rows = await filtered
            .OrderFor(query.Period)
            .Page(query.Page, query.PageSize)
            .Select(EventRow.Projection)
            .ToListAsync(cancellationToken);
        var occupancies = await occupancyReader.GetManyAsync([.. rows.Select(r => r.Id)], now, cancellationToken);

        var items = rows.Select(r =>
        {
            var occupancy = occupancies.GetValueOrDefault(r.Id, EventOccupancy.None);
            return new AdminEventSummaryDto(
                r.Id, r.Title, r.Location, r.StartsAt, r.EndsAt, r.Status, r.Capacity, r.Price,
                occupancy.Pending, occupancy.Confirmed, EventRules.RemainingSeats(r.Capacity, occupancy),
                r.StartsAt <= now, r.EndsAt <= now, EventRules.CanBeDeleted(r.Status, r.FirstPublishedAt));
        }).ToList();

        return new PagedResult<AdminEventSummaryDto>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// Sous verrou (ADR 0012) : la capacité (R11) et l'avertissement (R12) dépendent des places occupées, qu'une
    /// demande de place (étape 4) ne doit pas changer pendant la vérification.
    /// Ordre : not_found → concurrent_update → event_cancelled / event_ended → 400 de valeurs → R11 → R12.
    /// On ne signale pas une erreur de saisie sur une fiche périmée ou devenue non modifiable.
    /// </summary>
    public async Task<AdminEventDto> UpdateAsync(Guid id, UpdateEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var details = ToDetails(request);
        var expectedVersion = RowVersions.Decode(request.Version);

        async Task<(Event Event, EventOccupancy Occupancy, DateTimeOffset Now)> Work(Event ev, CancellationToken ct)
        {
            // Après le verrou : lu avant, « now » pourrait précéder de quelques secondes l'échéance d'une demande échue
            // pendant l'attente ; RescheduleDeadlinesAsync la croirait vivante (ranimée) ou poserait une échéance
            // antérieure à la demande (CK_Reservations_Deadline). Renvoyé pour que le DTO utilise la même heure.
            var now = time.GetUtcNow();
            ConcurrencyGuard.EnsureSameVersion(ev.RowVersion, expectedVersion);
            ev.EnsureCanBeEdited(now);
            FieldErrors.ThrowIfAny(EventRules.Validate(details, previousStartsAt: ev.StartsAt, now));
            var current = await occupancyReader.GetAsync(id, now, ct);
            var previousStartsAt = ev.StartsAt;
            ev.Update(details, now, current, request.ConfirmRegistrantsWarning);
            if (ev.StartsAt != previousStartsAt)
            {
                await RescheduleDeadlinesAsync(ev, now, ct);
            }

            return (ev, current, now);
        }

        (Event Event, EventOccupancy Occupancy, DateTimeOffset Now) result;
        try
        {
            result = await eventLock.ExecuteWithLockedEventAsync(id, Work, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Count > 0 && ex.Entries.All(e => e.Entity is Reservation))
        {
            // Seules des demandes ont bougé entre la lecture des demandes et l'enregistrement, par un écrivain sans
            // verrou d'événement : un membre qui annule, ou la tâche d'expiration (ExpireDueAsync, tous événements,
            // ADR 0020) qui écrit Expirée. La fiche de l'admin n'est pas périmée, concurrent_update serait faux. La transaction
            // est annulée ; on rejoue UNE fois sur des données relues (un second conflit → concurrent_update).
            db.ChangeTracker.Clear();
            result = await RunLockedAsync(id, Work, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyGuard.Conflict();
        }

        // Construit après l'enregistrement : la nouvelle version (rowversion) a été relue par EF.
        return ToAdminDto(result.Event, result.Occupancy, result.Now);
    }

    /// <summary>
    /// Sans verrou ni version (ADR 0012 § 1) : la règle de transition du Domain suffit, le rowversion d'EF arrête
    /// une écriture concurrente (deux publications simultanées : la seconde reçoit concurrent_update).
    /// Avertissement non bloquant si aucun numéro de contact (US-E8).
    /// </summary>
    public async Task<PublishEventResponse> PublishAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var ev = await LoadTrackedAsync(id, cancellationToken);
        ev.Publish(now);
        await db.SaveChangesOrConflictAsync(cancellationToken);

        var warnings = await settings.IsContactConfiguredAsync(cancellationToken)
            ? []
            : new List<OperationWarning> { new(ErrorCodes.ContactNotConfigured, ContactNotConfiguredMessage) };
        var occupancy = await occupancyReader.GetAsync(id, now, cancellationToken);
        return new PublishEventResponse(ToAdminDto(ev, occupancy, now), warnings);
    }

    /// <summary>Sous verrou : R14 dépend des places occupées.</summary>
    public async Task<AdminEventDto> UnpublishAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var (unpublished, occupancy) = await RunLockedAsync(id, async (ev, ct) =>
        {
            var current = await occupancyReader.GetAsync(id, now, ct);
            ev.Unpublish(now, current);
            return (ev, current);
        }, cancellationToken);
        return ToAdminDto(unpublished, occupancy, now);
    }

    /// <summary>
    /// R15 (étape 2) : seul un brouillon jamais publié est supprimable ; il n'a jamais pu recevoir de réservation,
    /// donc pas besoin du verrou. Sans version (action) : si un autre admin a publié entre-temps, le rowversion
    /// fait échouer le DELETE (409 concurrent_update) au lieu de supprimer un événement publié.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var ev = await LoadTrackedAsync(id, cancellationToken);
        ev.EnsureCanBeDeleted();
        db.Events.Remove(ev);
        await db.SaveChangesOrConflictAsync(cancellationToken);
    }

    /// <summary>
    /// ADR 0018 § 5 : le début a changé → les demandes en attente NON échues reprennent l'échéance
    /// min(demande + 48 h, nouveau début). Sous le verrou de l'événement : aucune demande ne peut s'insérer entre-temps,
    /// mais un membre peut en annuler une (sans verrou) : UpdateAsync rejoue alors une fois.
    /// Les lignes suivies sont enregistrées par IEventLock avec l'événement (même transaction).
    /// </summary>
    private async Task RescheduleDeadlinesAsync(Event ev, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await db.Reservations
            .Where(r => r.EventId == ev.Id && r.Status == ReservationStatus.Pending && r.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var reservation in pending)
        {
            reservation.RescheduleDeadline(ev.StartsAt, reservationOptions.Value.PendingLifetime, now);
        }
    }

    private async Task<TResult> RunLockedAsync<TResult>(
        Guid id, Func<Event, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
    {
        try
        {
            return await eventLock.ExecuteWithLockedEventAsync(id, work, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Le verrou protège la ligne de l'événement, pas celles des réservations : une annulation de membre
            // (sans verrou, ADR 0019 § 1) peut modifier une demande lue dans le bloc. Ici, aucun rejeu : 409.
            // UpdateAsync, qui touche des demandes (échéances), rejoue une fois avant d'arriver là.
            throw ConcurrencyGuard.Conflict();
        }
    }

    private async Task<Event> LoadTrackedAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Events.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
        ?? throw new NotFoundException(NotFoundMessage);

    /// <summary>
    /// Les chaînes ont été validées par [DateTimeWithOffset] et [Required] ; on revérifie quand même (le service
    /// peut être appelé par un autre point d'entrée) pour garder un 400 sur le bon champ.
    /// </summary>
    internal static EventDetails ToDetails(EventInput input)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!IsoDateTimes.TryParse(input.StartsAt, out var startsAt))
        {
            errors[EventFields.StartsAt] = [IsoDateTimes.InvalidFormatMessage];
        }

        if (!IsoDateTimes.TryParse(input.EndsAt, out var endsAt))
        {
            errors[EventFields.EndsAt] = [IsoDateTimes.InvalidFormatMessage];
        }

        if (input.Capacity is null)
        {
            errors[EventFields.Capacity] = ["La capacité est obligatoire."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        return new EventDetails(
            input.Title ?? string.Empty,
            input.Description ?? string.Empty,
            input.Location ?? string.Empty,
            startsAt,
            endsAt,
            input.Capacity!.Value,
            input.Price);
    }

    internal static AdminEventDto ToAdminDto(Event ev, EventOccupancy occupancy, DateTimeOffset now) => new(
        ev.Id,
        ev.Title,
        ev.Description,
        ev.Location,
        ev.StartsAt,
        ev.EndsAt,
        ev.Status,
        ev.Capacity,
        ev.Price,
        occupancy.Pending,
        occupancy.Confirmed,
        ev.RemainingSeats(occupancy),
        ev.HasStarted(now),
        ev.HasEnded(now),
        ev.CanBeDeleted,
        ev.PublishedAt,
        ev.FirstPublishedAt,
        ev.CreatedAt,
        ev.UpdatedAt,
        RowVersions.Encode(ev.RowVersion));
}
