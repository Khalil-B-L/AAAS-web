using Aaas.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>Lecture du numéro de contact : membres connectés uniquement, jamais les visiteurs (RG-10).</summary>
[ApiController]
[Route("api/settings")]
[Authorize]
[Produces("application/json")]
public sealed class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet("contact")]
    [ProducesResponseType<ContactSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<ContactSettingsDto> GetContact(CancellationToken cancellationToken) =>
        settings.GetContactAsync(cancellationToken);
}
