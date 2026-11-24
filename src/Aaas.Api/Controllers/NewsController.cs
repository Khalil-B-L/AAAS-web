using Aaas.Application.Common;
using Aaas.Application.Common.Errors;
using Aaas.Application.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>
/// Actualités publiques (US-B1, US-B2), sans compte. Le texte est renvoyé brut, en JSON : aucun encodage HTML côté
/// serveur, c'est le front qui l'affiche par interpolation (ADR 0023).
/// </summary>
[ApiController]
[Route("api/news")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class NewsController(PublicNewsService news) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<NewsSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<NewsSummaryDto>> List([FromQuery] PublicNewsListQuery query, CancellationToken cancellationToken) =>
        news.ListAsync(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<NewsDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NewsDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        news.GetAsync(id, cancellationToken);

    /// <summary>
    /// Identifiant mal formé (« /api/news/abc ») : même 404 qu'un identifiant inconnu, anonyme (sans cette route, la
    /// politique de repli « authentifié par défaut » répondrait 401 à un visiteur). Précédence plus faible que {id:guid}.
    /// </summary>
    [HttpGet("{id}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public void GetMalformedId() => throw new NotFoundException(NotFoundMessages.PublicContent);
}
