# Fiche de révision — ASP.NET Core Identity et JWT

> À relire la veille d'un entretien. Exemples tirés du projet AAAS (étape 1). Décisions : ADR 0002 et 0008.

## 1. Identity : ce qu'on utilise et pourquoi

| Élément | Rôle | Dans le projet |
|---|---|---|
| `AddIdentityCore<TUser>()` | Services de gestion des comptes **sans** authentification par cookie | `src/Aaas.Infrastructure/DependencyInjection.cs` |
| `AddIdentity<TUser, TRole>()` | Idem **+ cookies** (appli MVC/Razor) | Écarté : l'API utilise des JWT |
| `UserManager<T>` | Créer, trouver, rôles, security stamp, blocage | `IdentityService`, `DatabaseInitializer` |
| `SignInManager<T>.CheckPasswordSignInAsync(user, pwd, lockoutOnFailure: true)` | Vérifie le mot de passe **et** gère le compteur d'échecs, sans créer de cookie | `IdentityService.CheckPasswordAsync` |
| `RoleManager<T>` | Créer les rôles | `DatabaseInitializer` |
| `IdentityDbContext<TUser, TRole, TKey>` | Tables `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`... | `AppDbContext` (clé `Guid`) |
| `IdentityErrorDescriber` | Textes des erreurs | `FrenchIdentityErrorDescriber` |
| `IPasswordValidator<T>` | Règle de mot de passe supplémentaire | `LetterRequiredPasswordValidator` (« au moins une lettre ») |

**Hachage** : `PasswordHasher` par défaut = PBKDF2 HMAC-SHA512, sel aléatoire de 128 bits, 100 000 itérations (format V3, .NET 7+). Le format est versionné : Identity sait réhacher un ancien format à la connexion. Ne jamais écrire son propre hachage.

**Normalisation** : `NormalizedEmail` / `NormalizedUserName` (majuscules) servent aux recherches sans casse. `FindByEmailAsync("Salma@Exemple.tn")` cherche `SALMA@EXEMPLE.TN`.

### Pièges
- `RequireUniqueEmail = true` ne fait qu'un `SELECT` avant l'`INSERT` : deux inscriptions simultanées passent. Il faut un **index unique** (ici `UX_AspNetUsers_NormalizedEmail ... WHERE [NormalizedEmail] IS NOT NULL`) et traduire l'erreur SQL 2601/2627 en « e-mail déjà utilisé ».
- `AllowedUserNameCharacters` par défaut refuse `'` ou les lettres accentuées : comme `UserName` = e-mail, on le vide.
- Identity ne sait pas exiger « une lettre » : seulement une minuscule ou une majuscule → validateur personnalisé.
- **Blocage** : avec `MaxFailedAccessAttempts = 5`, le compte est bloqué **au 5e échec** (la réponse au 5e essai est déjà « bloqué »). Le compteur repasse à 0 au succès, et à 0 au moment du blocage.
- `UserManager` n'accepte pas de `CancellationToken`.
- Avec `EnableRetryOnFailure`, une transaction explicite doit être dans `Database.CreateExecutionStrategy().ExecuteAsync(...)`, sinon EF lève une exception.

## 2. JWT : structure et validation

Un JWT = `base64url(en-tête).base64url(charge utile).signature`. La charge utile est **lisible par tous** (encodée, pas chiffrée) : n'y mettre aucune donnée sensible. Exemple réel du projet :

```json
{ "iss": "aaas-api", "aud": "aaas-front", "iat": 1794339975, "nbf": 1794339975, "exp": 1794343575,
  "sub": "987cf2d9-...", "given_name": "Salma", "jti": "363f...", "sst": "ZMBJ...", "role": "User" }
```

| Claim | Sens |
|---|---|
| `iss` / `aud` | Émetteur / destinataire attendu : un jeton émis pour une autre appli est refusé |
| `iat` / `nbf` / `exp` | Émis le / valable à partir de / expire le (secondes Unix) |
| `sub` | Identifiant du compte |
| `role` | Un claim par rôle (tableau s'il y en a plusieurs) |
| `jti` | Identifiant unique du jeton |
| `sst` | Security stamp Identity (claim propre au projet) |

**HS256** : signature HMAC-SHA256 avec une clé symétrique (≥ 256 bits) partagée par l'émetteur et le validateur, ici la même API. **RS256** : clé privée pour signer, clé publique pour vérifier, utile quand plusieurs services valident (ou avec un fournisseur d'identité externe).

### Émission (TokenService)

```csharp
var descriptor = new SecurityTokenDescriptor
{
    Issuer = jwt.Issuer, Audience = jwt.Audience, Subject = new ClaimsIdentity(claims),
    IssuedAt = now.UtcDateTime, NotBefore = now.UtcDateTime, Expires = expiresAt.UtcDateTime,
    SigningCredentials = new(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256),
};
string token = new JsonWebTokenHandler().CreateToken(descriptor);
```

`JsonWebTokenHandler` (Microsoft.IdentityModel.JsonWebTokens) remplace `JwtSecurityTokenHandler`, plus ancien et plus lent.

### Validation (AddJwtBearer)

```csharp
bearer.MapInboundClaims = false;            // garder « sub », « role » au lieu des URI WS-Federation
bearer.TokenValidationParameters = new()
{
    ValidIssuer = ..., ValidAudience = ..., IssuerSigningKey = ...,
    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],   // refuse « alg: none » et la confusion d'algorithmes
    NameClaimType = "sub", RoleClaimType = "role",       // pour User.Identity.Name et [Authorize(Roles = ...)]
    ClockSkew = TimeSpan.FromMinutes(1),                 // 5 min par défaut : un jeton « expiré » resterait valable 5 min
};
bearer.Events = new() { OnTokenValidated = ValidateSecurityStampAsync };
```

### Révocation par security stamp
Un JWT est valable jusqu'à `exp` : le serveur ne peut pas « l'oublier ». Parade : `OnTokenValidated` compare le claim `sst` au `SecurityStamp` en base (une lecture indexée par requête). Renouveler le stamp (`UpdateSecurityStampAsync`, ou un `UPDATE` SQL) révoque **tous** les jetons du compte : suppression de compte, changement de rôle, changement de mot de passe. Vérifié dans le projet : après `UPDATE ... SET SecurityStamp = NEWID()`, l'ancien jeton reçoit 401.

### Autorisation
- `[Authorize]` : connecté ; `[Authorize(Policy = "AdminOnly")]` : politique `RequireRole("Admin")`.
- **Politique de repli** (`SetFallbackPolicy(RequireAuthenticatedUser)`) : tout endpoint sans attribut exige un jeton. On ouvre explicitement avec `[AllowAnonymous]` / `.AllowAnonymous()` (auth, `/health`). « Sécurisé par défaut » : oublier un attribut ferme l'accès au lieu de l'ouvrir.
- 401 = qui es-tu ? (pas ou mauvais jeton). 403 = je sais qui tu es, mais tu n'as pas le droit.

## 3. Configuration et secrets

- `JwtOptions` liées à la section `Jwt`, validées par `ValidateDataAnnotations()` + `.Validate(clé ≥ 32 octets)` + `ValidateOnStart()`.
- `ValidateOnStart` ne s'exécute qu'au démarrage de l'hôte (`app.Run`). Si on fait du travail avant (migrations), appeler `app.Services.GetRequiredService<IStartupValidator>().Validate()` pour échouer d'abord sur la configuration.
- Configurer `JwtBearerOptions` à partir des options validées : `services.AddOptions<JwtBearerOptions>(scheme).Configure<IOptions<JwtOptions>>((bearer, jwt) => ...)`.
- Clé : user-secrets en local, variable `Jwt__SigningKey` dans compose, Key Vault en Azure. Changer la clé invalide tous les jetons en cours.

## 4. Commandes utiles

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/Aaas.Api
dotnet user-secrets list --project src/Aaas.Api
# Décoder la charge utile d'un jeton (bash) :
echo "$TOKEN" | cut -d. -f2 | tr '_-' '/+' | base64 -d
# Tester :
curl -s -H "Content-Type: application/json" -d '{"email":"...","password":"..."}' http://localhost:8080/api/auth/login
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/account/me
```

## 5. Questions d'entretien

- **Pourquoi un JWT plutôt qu'un cookie ?** Front et API sur deux domaines (Static Web Apps / App Service) : pas de cookie tiers `SameSite=None` ni de protection CSRF à gérer. En échange, le jeton est exposé au XSS s'il est dans `localStorage` (durée courte, CSP, échappement d'Angular).
- **Comment révoquer un JWT ?** Durée courte + vérification d'un état serveur (security stamp ici), ou liste noire de `jti`, ou refresh tokens révocables.
- **Pourquoi `ValidAlgorithms` ?** Pour refuser `alg: none` et les attaques par confusion d'algorithme (un jeton RS256 « re-signé » en HS256 avec la clé publique).
- **Comment Identity stocke-t-il le mot de passe ?** PBKDF2 avec sel et 100 000 itérations, dans un format versionné ; jamais le mot de passe en clair ni réversible.
- **Même message pour e-mail inconnu et mauvais mot de passe : pourquoi ?** Pour ne pas révéler quels e-mails ont un compte (énumération).
- **Le blocage après 5 échecs ouvre-t-il un risque ?** Un attaquant peut bloquer le compte de quelqu'un d'autre (déni de service ciblé) : on limite à 15 minutes ; un rate limiting par IP est un complément.
