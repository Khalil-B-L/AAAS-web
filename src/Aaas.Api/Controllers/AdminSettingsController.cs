using Aaas.Api.Auth;
using Aaas.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

/// <summary>Numéro de contact de l'association : enregistrer, changer, retirer (US-E8, RG-10).</summary>
[ApiController]
[Route("api/admin/settings")]
[Authorize(Policy = Policies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminSettingsController(SettingsService settings) : ControllerBase
{
    [HttpPut("contact")]
    [ProducesResponseType<ContactSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContactSettingsDto> UpdateContact(UpdateContactSettingsRequest request, CancellationToken cancellationToken) =>
        settings.UpdateContactAsync(request, cancellationToken);

    /// <summary>
    /// Retire le numéro (WhatsApp repasse à false). La version voyage dans le corps, comme pour le PUT :
    /// un retrait ne doit pas effacer en silence un numéro qu'un autre admin vient d'enregistrer.
    /// </summary>
    [HttpDelete("contact")]
    [ProducesResponseType<ContactSettingsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContactSettingsDto> RemoveContact([FromBody] RemoveContactSettingsRequest request, CancellationToken cancellationToken) =>
        settings.RemoveContactAsync(request, cancellationToken);
}
