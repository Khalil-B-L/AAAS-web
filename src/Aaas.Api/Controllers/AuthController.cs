using Aaas.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aaas.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class AuthController(AccountService accounts) : ControllerBase
{
    /// <summary>US-A1 : création d'un compte « User », connecté immédiatement.</summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await accounts.RegisterAsync(request, cancellationToken);
        return Created("/api/account/me", response);
    }

    /// <summary>US-A2 : connexion par e-mail et mot de passe ; blocage 15 min après 5 échecs.</summary>
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        accounts.LoginAsync(request, cancellationToken);
}
