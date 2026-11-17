using Aaas.Api.Auth;
using Aaas.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
[Produces("application/json")]
public sealed class AccountController(AccountService accounts) : ControllerBase
{
    /// <summary>US-F3 et confirmation de session au démarrage du front.</summary>
    [HttpGet("me")]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AccountDto> Me(CancellationToken cancellationToken) =>
        accounts.GetCurrentAccountAsync(User.GetUserId(), cancellationToken);
}
