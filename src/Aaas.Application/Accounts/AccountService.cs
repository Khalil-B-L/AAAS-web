using Aaas.Application.Common.Errors;
using Aaas.Application.Options;
using Aaas.Domain.Common;
using Microsoft.Extensions.Options;

namespace Aaas.Application.Accounts;

/// <summary>Cas d'usage des comptes : inscription (US-A1), connexion (US-A2), consultation de son compte (US-F3).</summary>
public sealed class AccountService(
    IIdentityService identity,
    ITokenService tokens,
    TimeProvider time,
    IOptions<PrivacyPolicyOptions> privacyPolicy)
{
    // Noms des champs JSON, identiques à ceux de la validation automatique des DTO.
    internal const string PhoneNumberField = "phoneNumber";
    internal const string PrivacyField = "acceptPrivacyPolicy";

    internal const string EmailTakenMessage = "Un compte existe déjà avec cet e-mail.";
    internal const string InvalidCredentialsMessage = "E-mail ou mot de passe incorrect.";

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        // Les DTO sont déjà validés par [ApiController] ; on revérifie ce qui protège les données,
        // car le service peut être appelé par un autre point d'entrée.
        if (!PhoneNumber.TryParse(request.PhoneNumber, out var phone))
        {
            throw RequestValidationException.For(PhoneNumberField, PhoneNumber.ExpectedFormatMessage);
        }

        if (!request.AcceptPrivacyPolicy)
        {
            throw RequestValidationException.For(PrivacyField, "Vous devez accepter la politique de confidentialité pour créer un compte.");
        }

        var newUser = new NewUser(
            request.FirstName.Trim(),
            request.LastName.Trim(),
            request.Email.Trim(),
            phone!,
            time.GetUtcNow(),
            privacyPolicy.Value.CurrentVersion);

        // Le rôle n'est jamais lu dans la requête : une inscription donne toujours « User » (R22).
        var result = await identity.CreateUserAsync(newUser, request.Password, [Roles.User], cancellationToken);

        return result switch
        {
            CreateUserResult.Created created => CreateAuthResponse(created.User),
            CreateUserResult.EmailAlreadyUsed => throw new BusinessRuleException(ErrorCodes.EmailTaken, EmailTakenMessage),
            CreateUserResult.Invalid invalid => throw new RequestValidationException(invalid.Errors),
            _ => throw new InvalidOperationException($"Résultat inattendu : {result}"),
        };
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await identity.CheckPasswordAsync(request.Email.Trim(), request.Password, cancellationToken);

        switch (result.Status)
        {
            case PasswordCheckStatus.Succeeded:
                var now = time.GetUtcNow();
                await identity.RecordLoginAsync(result.User!.Id, now, cancellationToken);
                return CreateAuthResponse(result.User with { LastLoginAt = now });

            case PasswordCheckStatus.LockedOut:
                throw new AuthenticationFailedException(ErrorCodes.AccountLocked, LockedOutMessage(result.LockoutEnd));

            default:
                // Même message pour un e-mail inconnu et un mauvais mot de passe (US-A2).
                throw new AuthenticationFailedException(ErrorCodes.InvalidCredentials, InvalidCredentialsMessage);
        }
    }

    public async Task<AccountDto> GetCurrentAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException("Compte introuvable.");
        return AccountDto.From(user);
    }

    private AuthResponse CreateAuthResponse(UserAccount user)
    {
        var token = tokens.CreateAccessToken(user);
        return new AuthResponse(token.Token, token.ExpiresAt, AccountDto.From(user));
    }

    private string LockedOutMessage(DateTimeOffset? lockoutEnd)
    {
        var remaining = lockoutEnd is null ? TimeSpan.Zero : lockoutEnd.Value - time.GetUtcNow();
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return $"Trop de tentatives de connexion. Votre compte est bloqué temporairement : réessayez dans {minutes} minute{(minutes > 1 ? "s" : "")}.";
    }
}
