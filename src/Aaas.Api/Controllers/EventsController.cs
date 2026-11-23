using Aaas.Application.Common;
using Aaas.Application.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>
/// Consultation publique (US-C1, US-C2), sans compte. Un jeton éventuel est lu mais pas exigé :
/// un jeton expiré ne donne pas 401, l'appel est traité comme anonyme (servira à l'étape 4).
/// </summary>
[ApiController]
[Route("api/events")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class EventsController(PublicEventService events) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<EventSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<EventSummaryDto>> List([FromQuery] PublicEventListQuery query, CancellationToken cancellationToken) =>
        events.ListAsync(query, cancellationToken);

    /// <summary>{id:guid} : un identifiant mal formé donne 404, comme un identifiant inconnu.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<EventDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EventDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        events.GetAsync(id, cancellationToken);
}
