using Aaas.Api.Auth;
using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>
/// Gestion des actualités (US-E1). Publier et dépublier sont des actions (POST .../publish), comme pour les événements.
/// Cache-Control: no-store posé par NoStoreMiddleware (préfixe /api/admin), erreurs comprises.
/// </summary>
[ApiController]
[Route("api/admin/news")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminNewsController(AdminNewsService news) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminNewsSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<AdminNewsSummaryDto>> List([FromQuery] AdminNewsListQuery query, CancellationToken cancellationToken) =>
        news.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminNewsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminNewsDto> Get(Guid id, CancellationToken cancellationToken) =>
        news.GetAsync(id, cancellationToken);

    /// <summary>Création en brouillon.</summary>
    [HttpPost]
    [ProducesResponseType<AdminNewsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminNewsDto>> Create(CreateNewsRequest request, CancellationToken cancellationToken)
    {
        var created = await news.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/news/{created.Id}", created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdminNewsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminNewsDto> Update(Guid id, UpdateNewsRequest request, CancellationToken cancellationToken) =>
        news.UpdateAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType<AdminNewsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminNewsDto> Publish(Guid id, CancellationToken cancellationToken) =>
        news.PublishAsync(id, cancellationToken);

    [HttpPost("{id:guid}/unpublish")]
    [ProducesResponseType<AdminNewsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AdminNewsDto> Unpublish(Guid id, CancellationToken cancellationToken) =>
        news.UnpublishAsync(id, cancellationToken);

    /// <summary>Suppression définitive, brouillon ou publiée (R18).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await news.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Identifiant mal formé : 404 pour un admin ; ces routes héritent de AdminOnly, donc visiteur 401 et membre 403
    /// (sans elles, un membre passerait la politique de repli et apprendrait l'erreur avant d'être refusé).
    /// </summary>
    [AcceptVerbs("GET", "PUT", "DELETE", Route = "{id}")]
    [HttpPost("{id}/publish")]
    [HttpPost("{id}/unpublish")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public void MalformedId() => throw new NotFoundException(AdminNewsService.NotFoundMessage);
}
