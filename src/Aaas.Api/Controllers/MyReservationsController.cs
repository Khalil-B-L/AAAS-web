using Aaas.Api.Auth;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>
/// Réservations du membre connecté (US-D1 à D4, ADR 0021). Aucune route ne prend un userId : il vient du jeton,
/// ce qui rend impossible par construction l'accès aux réservations d'un autre membre (IDOR).
/// Un admin a aussi le rôle membre : il peut demander une place pour lui-même (QF-1).
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
[Produces("application/json")]
public sealed class MyReservationsController(MemberReservationService reservations) : ControllerBase
{
    [HttpPost("reservations")]
    [ProducesResponseType<MyReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MyReservationDto>> Create(CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var created = await reservations.RequestAsync(User.GetUserId(), request, cancellationToken);
        return Created($"/api/me/reservations/{created.Id}", created);
    }

    [HttpGet("reservations")]
    [ProducesResponseType<PagedResult<MyReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<MyReservationDto>> List([FromQuery] MyReservationListQuery query, CancellationToken cancellationToken) =>
        reservations.ListAsync(User.GetUserId(), query, cancellationToken);

    [HttpGet("reservations/{id:guid}")]
    [ProducesResponseType<MyReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<MyReservationDto> Get(Guid id, CancellationToken cancellationToken) =>
        reservations.GetAsync(User.GetUserId(), id, cancellationToken);

    /// <summary>« Ma demande » sur le détail d'un événement : 200 (la plus récente) ou 204 si aucune.</summary>
    [HttpGet("events/{eventId:guid}/reservation")]
    [ProducesResponseType<MyReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MyReservationDto>> GetForEvent(Guid eventId, CancellationToken cancellationToken)
    {
        var latest = await reservations.GetForEventAsync(User.GetUserId(), eventId, cancellationToken);
        return latest is null ? NoContent() : latest;
    }

    /// <summary>Annulation par le membre : une action (POST .../cancel), pas un DELETE, l'historique est conservé.</summary>
    [HttpPost("reservations/{id:guid}/cancel")]
    [ProducesResponseType<MyReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<MyReservationDto> Cancel(Guid id, CancellationToken cancellationToken) =>
        reservations.CancelAsync(User.GetUserId(), id, cancellationToken);

    /// <summary>
    /// Identifiant mal formé : 404 comme un identifiant inconnu, pour un connecté ([Authorize] du contrôleur :
    /// un visiteur reçoit 401). « {id} » sans contrainte a une précédence plus faible que « {id:guid} ».
    /// </summary>
    [HttpGet("reservations/{id}")]
    [HttpPost("reservations/{id}/cancel")]
    [HttpGet("events/{eventId}/reservation")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public void MalformedId() => throw new NotFoundException(MemberReservationService.NotFoundMessage);
}
