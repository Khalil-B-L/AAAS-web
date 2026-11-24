using Aaas.Api.Auth;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.Events;
using Aaas.Application.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aaas.Api.Controllers;

/// <summary>
/// Traitement des réservations par un administrateur (US-E4 à E7, ADR 0006, 0021). Seul endroit de l'API où
/// apparaissent les coordonnées d'un autre compte : AdminOnly, et Cache-Control: no-store (NoStoreMiddleware,
/// préfixe /api/admin). Les actions sont des intentions (POST .../confirm) sans version : la rowversion de la
/// réservation suffit à détecter qu'un autre acteur est passé avant (409 reservation_state_changed).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminReservationsController(AdminReservationService reservations) : ControllerBase
{
    /// <summary>Demandes à traiter, tous événements, échéance la plus proche d'abord ; totalCount = compteur du menu.</summary>
    [HttpGet("reservations/pending")]
    [ProducesResponseType<PagedResult<AdminReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<AdminReservationDto>> ListPending(
        [FromQuery] PendingReservationListQuery query, CancellationToken cancellationToken) =>
        reservations.ListPendingAsync(query, cancellationToken);

    /// <summary>Inscrits d'un événement (en attente, confirmées, autres), non paginé.</summary>
    [HttpGet("events/{eventId:guid}/reservations")]
    [ProducesResponseType<EventRegistrantsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EventRegistrantsDto> Registrants(Guid eventId, CancellationToken cancellationToken) =>
        reservations.GetEventRegistrantsAsync(eventId, cancellationToken);

    [HttpPost("reservations/{id:guid}/confirm")]
    [ProducesResponseType<AdminReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminReservationDto> Confirm(Guid id, CancellationToken cancellationToken) =>
        reservations.ConfirmAsync(id, cancellationToken);

    /// <summary>Corps facultatif : { "reason": "…" } ; un POST sans corps vaut un refus sans motif.</summary>
    [HttpPost("reservations/{id:guid}/refuse")]
    [ProducesResponseType<AdminReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminReservationDto> Refuse(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefuseReservationRequest? request,
        CancellationToken cancellationToken) =>
        reservations.RefuseAsync(id, request, cancellationToken);

    [HttpPost("reservations/{id:guid}/cancel")]
    [ProducesResponseType<AdminReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminReservationDto> Cancel(Guid id, CancellationToken cancellationToken) =>
        reservations.CancelAsync(id, cancellationToken);

    /// <summary>L'admin enregistré (R13) est celui du jeton, jamais une valeur du corps.</summary>
    [HttpPost("reservations/{id:guid}/mark-paid")]
    [ProducesResponseType<AdminReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminReservationDto> MarkPaid(Guid id, CancellationToken cancellationToken) =>
        reservations.MarkPaidAsync(User.GetUserId(), id, cancellationToken);

    [HttpPost("reservations/{id:guid}/unmark-paid")]
    [ProducesResponseType<AdminReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminReservationDto> UnmarkPaid(Guid id, CancellationToken cancellationToken) =>
        reservations.UnmarkPaidAsync(id, cancellationToken);

    /// <summary>
    /// Identifiant mal formé : 404 pour un admin, comme un identifiant inconnu. Soumises à AdminOnly (attribut du
    /// contrôleur) : un visiteur reçoit 401 et un membre 403, sans apprendre que l'identifiant était mal formé.
    /// </summary>
    [HttpPost("reservations/{id}/confirm")]
    [HttpPost("reservations/{id}/refuse")]
    [HttpPost("reservations/{id}/cancel")]
    [HttpPost("reservations/{id}/mark-paid")]
    [HttpPost("reservations/{id}/unmark-paid")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public void MalformedId() => throw new NotFoundException(AdminReservationService.NotFoundMessage);

    /// <summary>Même repli pour l'événement, avec le message d'un événement inconnu.</summary>
    [HttpGet("events/{eventId}/reservations")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public void MalformedEventId() => throw new NotFoundException(AdminEventService.NotFoundMessage);
}
