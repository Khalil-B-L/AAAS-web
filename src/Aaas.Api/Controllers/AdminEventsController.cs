using Aaas.Api.Auth;
using Aaas.Application.Common;
using Aaas.Application.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>
/// Gestion des événements (US-E2). Publier et dépublier sont des actions (POST .../publish) et non un PUT du statut :
/// l'intention est explicite et la règle de transition reste dans le Domain (ADR 0006).
/// </summary>
[ApiController]
[Route("api/admin/events")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminEventsController(AdminEventService events) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminEventSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<AdminEventSummaryDto>> List([FromQuery] AdminEventListQuery query, CancellationToken cancellationToken) =>
        events.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminEventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminEventDto> Get(Guid id, CancellationToken cancellationToken) =>
        events.GetAsync(id, cancellationToken);

    /// <summary>Création en brouillon.</summary>
    [HttpPost]
    [ProducesResponseType<AdminEventDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminEventDto>> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        var created = await events.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/events/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdminEventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminEventDto> Update(Guid id, UpdateEventRequest request, CancellationToken cancellationToken) =>
        events.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<PublishEventResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<PublishEventResponse> Publish(Guid id, CancellationToken cancellationToken) =>
        events.PublishAsync(id, cancellationToken);

    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType<AdminEventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminEventDto> Unpublish(Guid id, CancellationToken cancellationToken) =>
        events.UnpublishAsync(id, cancellationToken);

    /// <summary>Suppression définitive d'un brouillon jamais publié (R15 étape 2, RG-07) ; sinon 409 event_already_published.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await events.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
