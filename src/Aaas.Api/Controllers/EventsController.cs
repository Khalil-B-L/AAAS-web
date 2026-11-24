using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
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

    [HttpGet("{id:guid}")]
    [ProducesResponseType<EventDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EventDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        events.GetAsync(id, cancellationToken);

    /// <summary>
    /// Identifiant mal formé (« /api/events/abc ») : même 404 qu'un identifiant inconnu. Sans cette route, aucun
    /// endpoint ne correspond, la politique de repli (« authentifié par défaut ») s'applique et un visiteur reçoit 401.
    /// « {id} » sans contrainte a une précédence plus faible que « {id:guid} » : un GUID valide va toujours à Get.
    /// </summary>
    [HttpGet("{id}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public void GetMalformedId() => throw new NotFoundException(PublicEventService.NotFoundMessage);
}
