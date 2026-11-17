# ADR 0002 — Authentification : ASP.NET Core Identity + JWT, hachage et blocage

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 10/11/2026 — mis à jour le 10/11/2026 avec les décisions du porteur (cahier v1.1), l'implémentation de l'étape 1, puis la revue de code de l'étape 1 (journaux, secrets d'exemple, énumération via `account_locked`, actions avant production) ; mis à jour le 13/11/2026 (journalisation des violations d'unicité par `ApiExceptionHandler`, `CommandError` en Debug), puis le même jour après relecture du code final (`ApiExceptionHandler` renvoie toujours `true`, échec de démarrage relancé sans l'exception d'origine, variables obligatoires dans `docker-compose.yml`, ports publiés sur `127.0.0.1`), puis le même jour après la correction d'une fuite par `CoreEventId.QueryIterationFailed` (règle « EF Core ne journalise aucune exception SQL au-dessus de Debug », liste complète des événements traités, choix assumé sur `ExecutionStrategyRetrying`, point de décision ouvert sur les autres erreurs 500 et sur `/health/ready`)
- **Références** : cahier § 6.2, § 6.4, RG-11 ; mvp US-A1, US-A2, US-A3, US-F1, US-F2, US-F3 ; R20, R21, R24, R25 ; Q16
- **Mise en œuvre** : étape 1 (`src/Aaas.Infrastructure/Identity/`, `src/Aaas.Infrastructure/DependencyInjection.cs`, `src/Aaas.Api/Auth/`, `src/Aaas.Api/Errors/ApiExceptionHandler.cs`, `src/Aaas.Api/Program.cs`, `AccountService`)

## Contexte

Le cahier impose des mots de passe hachés par ASP.NET Core Identity et une authentification JWT. Le front (Azure Static Web Apps) et l'API (App Service) sont sur deux domaines différents. Exigences : 8 caractères minimum dont une lettre et un chiffre, e-mail unique sans tenir compte de la casse, message identique pour e-mail inconnu et mauvais mot de passe, blocage 15 min après 5 échecs consécutifs, session qui expire (US-A3), compte supprimé inutilisable immédiatement (US-F2).

Décisions du porteur (cahier v1.1) qui touchent le compte : on conserve la **date de création** et la **date de dernière connexion** ; le téléphone est **uniquement** « +216 » suivi de 8 chiffres ; **aucun contrôle d'âge** ; le consentement est conservé avec sa **date** et la **version de la politique** acceptée.

## Décision

### Données du compte (`AppUser`)
- `AppUser : IdentityUser<Guid>` ajoute exactement ce que liste R20, rien d'autre :

  | Propriété | Colonne | Règle |
  |---|---|---|
  | `FirstName`, `LastName` | `nvarchar(100)`, obligatoires | — |
  | `Email` / `UserName` | colonnes Identity | `UserName` = e-mail |
  | `PhoneNumber` | colonne Identity, `nvarchar(12)` | stockée normalisée au format **E.164** (`+21620123456`) |
  | `CreatedAt` | `datetimeoffset` | fixée à l'inscription (affichée en US-F3) |
  | `LastLoginAt` | `datetimeoffset NULL` | mise à jour à **chaque connexion réussie** (US-A2, R25) |
  | `PrivacyConsentAt` | `datetimeoffset` | = date de l'inscription (la case est obligatoire) |
  | `PrivacyPolicyVersion` | `nvarchar(20)`, obligatoire | **fixée côté serveur** (voir plus bas) |

- **Pas de date de naissance ni de case « majeur »** : décision du porteur, le site est accessible à tous (§ 6.4, US-F1).
- **`LastLoginAt`** : écrite par un `ExecuteUpdate` ciblé (`IdentityService.RecordLoginAsync`), qui ne touche ni au `ConcurrencyStamp` ni aux autres colonnes d'Identity. Elle n'est **jamais** modifiée par un échec de connexion. L'inscription connectant immédiatement l'utilisateur (US-A1), elle est initialisée à la date de création. R25 (inactivité de 3 ans) se mesure sur `LastLoginAt`, à défaut `CreatedAt`.
- **Version de la politique** : la valeur vient de la configuration `PrivacyPolicy:CurrentVersion` (`"1.0"` dans `appsettings.json`, validée au démarrage), **jamais de la requête** : le client ne peut pas prétendre avoir accepté une autre version. On l'incrémente à chaque modification du texte de la politique. Redemander l'accord quand la version change n'est **pas** au MVP (Q16) ; la donnée est là pour le faire plus tard (comparer `PrivacyPolicyVersion` à `CurrentVersion` à la connexion).
- **Téléphone (RG-11)** : la règle vit dans l'objet valeur `PhoneNumber` du Domain (« +216 » + exactement 8 chiffres ASCII, espaces tolérés à la saisie). Refusés : saisie locale (`20 123 456`), préfixe `00`, autres indicatifs. L'attribut de DTO `[TunisianPhoneNumber]` délègue à `PhoneNumber.TryParse` : une seule source de vérité. `AccountService` revérifie (défense en profondeur si un autre point d'entrée appelle le service).
- **Deux comptes peuvent avoir le même téléphone** : pas d'index unique sur `PhoneNumber` (décision par défaut : autorisé, par exemple un parent qui inscrit deux membres de sa famille avec son numéro). Seul l'e-mail identifie un compte. À confirmer par le porteur.

### Stockage et hachage
- `AddIdentityCore<AppUser>()` + `AddRoles<IdentityRole<Guid>>()` + `AddEntityFrameworkStores<AppDbContext>()` + `AddSignInManager()`. On n'utilise **pas** `AddIdentity` (qui installe l'authentification par cookie) ni `MapIdentityApi`.
- Hachage : `PasswordHasher` par défaut d'Identity (format V3 : PBKDF2 avec HMAC-SHA512, sel aléatoire, 100 000 itérations depuis .NET 7). Aucun algorithme maison. Le mot de passe est borné à 128 caractères dans le DTO pour borner le coût du hachage.
- E-mail unique : `RequireUniqueEmail = true` **et** index unique filtré `UX_AspNetUsers_NormalizedEmail` (Identity ne crée qu'un index non unique ; l'index unique protège contre deux inscriptions simultanées, la violation SQL 2601/2627 est traduite en « Un compte existe déjà avec cet e-mail », 409). La comparaison sans casse est assurée par `NormalizedEmail` (majuscules).
- La création du compte et l'ajout des rôles se font dans **une transaction**, exécutée dans la *execution strategy* d'EF (obligatoire avec `EnableRetryOnFailure`).

### Règles de mot de passe et blocage
- `RequiredLength = 8`, `RequireDigit = true`, `RequireLowercase = RequireUppercase = RequireNonAlphanumeric = false`, plus un `IPasswordValidator` personnalisé « au moins une lettre » (`LetterRequiredPasswordValidator` : Identity ne sait exiger que des minuscules ou des majuscules).
- `FrenchIdentityErrorDescriber` traduit les messages d'Identity en français (sans reprendre l'e-mail saisi dans les messages).
- **Ordre de validation** : le DTO ne vérifie que la présence et la longueur maximale du mot de passe ; les règles de complexité sont vérifiées **par Identity**, donc **après** la validation du DTO. Les erreurs d'Identity sont renvoyées en `400 ValidationProblemDetails` sur le champ `password`, comme les autres. Conséquence : si le téléphone **et** le mot de passe sont invalides, la première réponse ne signale que le téléphone ; le mot de passe est signalé au second envoi. Choix assumé : une seule source de vérité (la configuration d'Identity) plutôt qu'une copie des règles dans un attribut. Le front reproduira les mêmes règles en validation de formulaire, pour le confort uniquement.
- `Lockout.MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 min`, `AllowedForNewUsers = true`. Connexion par `SignInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)` : le compteur est remis à zéro après un succès (« 5 fois **de suite** »).
- E-mail inconnu et mauvais mot de passe renvoient la même réponse `401`, `code = "invalid_credentials"`, « E-mail ou mot de passe incorrect. ». Compte bloqué : `401` avec `code = "account_locked"` et le nombre de minutes restantes (ce qui révèle l'existence du compte : risque accepté, voir plus bas).

### Jeton
- JWT signé en **HS256** avec une clé symétrique d'au moins 256 bits, lue dans la configuration `Jwt:SigningKey` (user-secrets en local, variable d'environnement dans Docker, Key Vault en Azure). `Issuer`, `Audience`, durée de vie dans `JwtOptions` validées au démarrage : l'API refuse de démarrer sans clé ou avec une clé trop courte. La validation n'accepte que HS256 (`ValidAlgorithms`), ce qui exclut `alg: none`.
- Durée de vie : **60 minutes**, **sans refresh token** au MVP. À expiration, l'API répond 401 et le front renvoie vers la connexion (c'est le scénario « Session expirée » de US-A3). `ClockSkew` ramené à 1 minute.
- Claims : `sub` (Id), `given_name` (prénom, pour l'affichage), `jti`, `sst` (security stamp) et **un claim `role` par rôle**. Pas d'e-mail ni de téléphone dans le jeton. `MapInboundClaims = false` pour garder les noms courts.
- **Révocation** : dans `JwtBearerEvents.OnTokenValidated`, une requête vérifie que l'utilisateur existe encore et que son `SecurityStamp` correspond au claim `sst`. Un compte supprimé, ou dont le rôle a changé (`UpdateSecurityStampAsync`), est rejeté immédiatement. Coût : une lecture indexée par requête authentifiée, acceptable vu le volume.
- **Sécurisé par défaut** : politique de repli (`FallbackPolicy`) « utilisateur authentifié » ; les endpoints publics sont marqués `[AllowAnonymous]` explicitement (`/api/auth/*`, `/health`, `/health/ready`, OpenAPI en développement).
- Déconnexion (US-A3) : côté front, suppression du jeton. Pas de liste noire côté serveur.
- Côté front, le jeton est conservé dans `localStorage` (pour survivre au rafraîchissement de la page) et envoyé par un interceptor HTTP dans l'en-tête `Authorization: Bearer`.

### Rôles
- Deux rôles, `Admin` et `User`, créés au démarrage (ADR 0008). L'inscription attribue toujours `User` ; aucun champ de rôle n'est accepté en entrée. Politique d'autorisation `AdminOnly` (`RequireRole("Admin")`).
- **Un administrateur a les deux rôles, `User` et `Admin`** : il garde les droits d'un membre (réserver pour lui-même), et un membre promu admin conserve naturellement `User`. Le premier admin est donc créé avec `[User, Admin]` (ADR 0008).
- Le jeton contient autant de claims `role` que de rôles, et le DTO de compte (`AccountDto`, renvoyé par `/api/auth/register`, `/api/auth/login` et `/api/account/me`) expose **`roles` sous forme de tableau** (`["Admin", "User"]` ; l'ordre n'est pas garanti). Le front calcule `isAdmin = roles.includes('Admin')` ; il ne doit pas supposer un rôle unique.

### Journaux et secrets d'exemple
- **Aucun e-mail dans les journaux** (ni mot de passe, ni téléphone, ni jeton). Toute journalisation qui touche un compte se fait par `UserId`, jamais par e-mail.
- **Point de vigilance relevé en revue** : lors d'une violation d'unicité SQL Server (2601 index unique, 2627 contrainte unique), le **message de l'exception** contient la **valeur de la clé dupliquée**, c'est-à-dire l'e-mail normalisé dans le cas `email_taken`. Plus généralement, le message d'une `SqlException` peut porter une donnée saisie (valeur dupliquée, valeur non convertible, valeur tronquée).
- **Fuite corrigée (13/11/2026)** : la première version ne traitait que `SaveChangesFailed` et `CommandError`. Or une **lecture** en échec (`FirstOrDefaultAsync`, `ToListAsync`, `RoleExistsAsync`…) émet `CoreEventId.QueryIterationFailed` (catégorie `Microsoft.EntityFrameworkCore.Query`, niveau Error, `SqlException` jointe). Constaté au démarrage sur une base sans schéma : le message SQL brut sortait **avant** le `catch` de `Program.cs`. D'où une règle générale plutôt qu'une liste au cas par cas.
- **Règle** : **EF Core ne journalise aucune exception SQL au-dessus de `Debug`.** L'exception remonte toujours ; c'est **l'appelant** qui journalise, en filtrant (`ApiExceptionHandler` pour les 500, le `catch` de `Program.cs` en `Critical` au démarrage, la future tâche d'expiration). Toutes les sources sont traitées ainsi :

  | Source | Traitement | Où |
  |---|---|---|
  | `CoreEventId.SaveChangesFailed` (Error, exception jointe) | **ignoré** : doublon de `CommandError` | `AddInfrastructure`, `ConfigureWarnings(w => w.Ignore(CoreEventId.SaveChangesFailed) ...)` |
  | `RelationalEventId.CommandError` (texte SQL, paramètres masqués, exception jointe) | **rétrogradé en `Debug`** : un 409 prévu n'est pas une erreur | `ConfigureWarnings(w => w.Log((..., LogLevel.Debug), ...))` |
  | `CoreEventId.QueryIterationFailed` (lecture en échec) | **rétrogradé en `Debug`** (source de la fuite corrigée) | idem |
  | `RelationalEventId.NonQueryOperationFailed`, `ExecuteUpdateFailed`, `ExecuteDeleteFailed` (`ExecuteUpdate`/`ExecuteDelete`, dont `RecordLoginAsync`) | **rétrogradés en `Debug`** | idem |
  | `RelationalEventId.ConnectionError`, `TransactionError` | **rétrogradés en `Debug`** | idem |
  | `CoreEventId.ExecutionStrategyRetrying` (Information, exception jointe) | **laissé tel quel, volontairement** (voir ci-dessous) | — |
  | `ApiExceptionHandler` (500 pour une exception non gérée) | une violation d'unicité SQL 2601/2627 qui remonterait jusqu'à lui est journalisée **sans l'exception** : numéro SQL, type d'exception, méthode HTTP et chemin seulement ; les **autres** exceptions restent journalisées **avec l'exception complète** (niveau Error) : point de décision ouvert ci-dessous. Le gestionnaire **renvoie toujours `true`** (voir ci-dessous) | `src/Aaas.Api/Errors/ApiExceptionHandler.cs` |
  | Middleware `UseExceptionHandler` d'ASP.NET Core | n'intervient jamais, parce que `ApiExceptionHandler` déclare toujours l'exception gérée | idem |
  | Échec de l'initialisation au démarrage (hors requête) | journalisé par `Program.cs` en `Critical`, sans l'exception quand elle peut contenir un message SQL ; exception relancée **neutre** (voir ci-dessous) | `src/Aaas.Api/Program.cs` |
  | Sonde `/health/ready` (`AddDbContextCheck`) | **à vérifier** : point ouvert ci-dessous | `AddInfrastructure`, `Program.cs` |

- **Pourquoi `Debug` et pas `Ignore` pour ces événements** : `Ignore` supprime l'événement à tout niveau ; `Debug` le garde disponible pour un diagnostic local ponctuel. On n'ignore que le vrai doublon (`SaveChangesFailed`, qui répète `CommandError`).
- **Pourquoi `ExecutionStrategyRetrying` reste en Information** : il n'est émis que pour des erreurs que la stratégie de réessai de SQL Server classe comme **transitoires** (délai dépassé, deadlock 1205, base Azure SQL en pause ou en cours de reprise, coupure réseau) ; leurs messages ne contiennent pas de donnée saisie. Il est utile en exploitation pour **repérer les réveils d'Azure SQL serverless** (premier appel lent après une pause). **Condition** : si l'on ajoute un jour des numéros d'erreur à la liste des erreurs transitoires (`EnableRetryOnFailure(..., errorNumbersToAdd)`), revérifier que leurs messages ne portent pas de donnée ; sinon rétrograder cet événement à son tour.
- **Liste exhaustive** : pour la reconstituer après une montée de version d'EF Core, énumérer par réflexion les définitions d'événements de `CoreResources`/`RelationalResources` et garder celles de niveau ≥ Warning (ou Information) qui portent une exception (`docs/revision/dotnet-erreurs-journalisation.md` § 2).

- **`ApiExceptionHandler` renvoie toujours `true`** : il fixe le statut, écrit le `ProblemDetails` par `IProblemDetailsService.TryWriteAsync`, puis déclare l'exception gérée **même si aucun writer n'a accepté la réponse**. Raison : en .NET 10, si le gestionnaire renvoie `false` (ce que ferait un `return await TryWriteAsync(...)`), le middleware `UseExceptionHandler` reprend la main et **journalise l'exception brute**, ce qui annulerait le filtre des erreurs 2601/2627 ci-dessus. Contrepartie : une requête dont l'en-tête `Accept` refuse le JSON (par exemple `Accept: text/html` seul) reçoit le **statut (500, 409…) sans corps**. Acceptable : le front envoie toujours `Accept: application/json`, et un client qui refuse le JSON ne saurait de toute façon pas lire un `ProblemDetails`. Même règle pour une requête annulée par le client (statut 499, rien d'écrit, trace `Debug`).
- **Limite connue du niveau `Debug`** : les paramètres SQL sont masqués (`EnableSensitiveDataLogging` n'est jamais activé), mais l'exception jointe aux événements rétrogradés (`CommandError`, `QueryIterationFailed`, etc.) porte toujours le message SQL, donc l'e-mail possible. **Activer le niveau `Debug` sur les catégories EF Core** (`Microsoft.EntityFrameworkCore.Database.Command`, `Microsoft.EntityFrameworkCore.Query`, ou `Microsoft.EntityFrameworkCore` en entier) fait donc **réapparaître des données personnelles** dans les journaux. À réserver à un diagnostic local ponctuel, jamais en Azure ; rappelé dans `exploitation.md` § 8.
- **Code hors requête HTTP** : comme les événements d'échec d'EF Core ne dépassent plus `Debug` et que `ApiExceptionHandler` ne voit que les requêtes HTTP, une erreur SQL survenue **hors requête** n'est journalisée par personne d'autre que le code appelant. Conséquences :
  - le **démarrage** (`DatabaseInitializer`, appelé depuis `Program.cs`) intercepte toute exception de l'initialisation (sauf `OperationCanceledException`), la journalise en **`Critical`** dans la catégorie `Aaas.Startup`, puis arrête le processus en relançant une exception. Deux cas :
    - **`InvalidOperationException` de configuration** (type exact `InvalidOperationException`, sans `InnerException`) : ce sont les erreurs levées par `DatabaseInitializer` (téléphone invalide, compte existant, mot de passe d'exemple…) ou par la configuration d'EF ; leurs messages sont rédigés sans donnée personnelle. Journalisée **avec** l'exception, puis relancée par **`throw;`** : message et pile d'origine conservés ;
    - **toute autre exception** (`SqlException`, `DbUpdateException`, `RetryLimitExceededException`, une `InvalidOperationException` qui enveloppe une erreur SQL…) : son message, ou celui d'une exception interne, peut contenir un e-mail. Sont journalisés un **message fixe**, le **type** de l'exception et le **numéro d'erreur SQL** s'il y en a un (`SqlServerErrors.TryGetSqlErrorNumber`), **sans l'exception**. Puis `Program.cs` relance une **`InvalidOperationException` neutre, sans `InnerException`**, dont le message ne contient que le texte fixe et le nom du type.
  - **Pourquoi pas `throw;` dans le second cas** : une exception non gérée qui sort de `Main` est écrite **par le runtime .NET** sur la sortie d'erreur (message, exceptions internes, pile), hors de tout filtre de journalisation ; dans un conteneur, cette sortie finit dans `docker compose logs api` (et dans le flux de journaux d'App Service). Relancer l'exception d'origine y ferait apparaître le message SQL, donc l'e-mail.
  - **Écart assumé** : dans ce second cas, la **pile d'appels d'origine est perdue** (ni dans les journaux, ni sur la sortie d'erreur). Le diagnostic s'appuie sur le type d'exception et le numéro SQL (2601/2627 unicité, 4060/18456 base ou connexion, 1934 `QUOTED_IDENTIFIER`, 208 objet introuvable si le schéma manque…), puis, si besoin, sur une reproduction en local avec un niveau de journalisation plus détaillé. C'est un compromis acceptable pour un échec qui survient au démarrage, dans un code court (`DatabaseInitializer`) et sans charge.
  - la future **tâche d'expiration** (ADR 0005, étape 5) devra journaliser elle-même ses erreurs, selon la même règle (pas de message SQL brut).
- **Aucun secret d'exemple utilisable dans le dépôt** (il est public) : dans `.env.example`, `MSSQL_SA_PASSWORD`, `JWT_SIGNING_KEY` et `BOOTSTRAP_ADMIN_PASSWORD` (ainsi que `BOOTSTRAP_ADMIN_EMAIL`, ADR 0008) restent **vides**, avec en commentaire la façon de générer une valeur aléatoire. `docker-compose.yml` les rend **obligatoires** avec la syntaxe `${VAR:?message}` : `docker compose up` refuse de démarrer tant qu'elles ne sont pas renseignées dans `.env`. Une valeur d'exemple assez longue passerait la validation au démarrage : une installation qui l'oublierait tournerait avec une clé de signature connue de tous (n'importe qui pourrait fabriquer un jeton admin) ou un mot de passe admin public. Hors compose (`dotnet run`, Azure), une clé JWT vide fait échouer `ValidateOnStart`, et une configuration `Bootstrap:*` incomplète empêche la création de l'admin (avertissement). Même principe pour toute variable secrète ajoutée plus tard.

### Point de décision ouvert — message SQL brut dans les autres erreurs 500 (Proposé, à valider par le porteur)

**Constat.** R24 n'est aujourd'hui appliqué par `ApiExceptionHandler` qu'aux violations d'unicité (2601/2627). Toute **autre** exception non gérée est journalisée avec l'exception complète (`LogError(exception, ...)`), donc avec le message SQL brut si une `SqlException` est dans la chaîne. Or d'autres erreurs SQL Server recopient une valeur saisie dans leur message, par exemple :
- **2628 / 8152** (troncature) : depuis SQL Server 2019, le message cite la **valeur tronquée** (« Truncated value: '...' ») ;
- **245 / 8114** (conversion) : le message cite la valeur non convertible ;
- une contrainte `CHECK` ou une clé étrangère (547) ne cite en principe que la table et la colonne, sans valeur.

Ces erreurs sont **peu probables** (DTO validés, longueurs alignées sur le schéma), mais un défaut de validation suffit à en produire une, avec l'e-mail ou le nom dans le message.

**Options.**

| Option | Pour | Contre |
|---|---|---|
| A. Garder l'exception complète pour les autres 500 (état actuel) | Diagnostic maximal (message, pile, exceptions internes) sur un incident imprévu, là où on en a le plus besoin | Une donnée personnelle peut atteindre les journaux (et Application Insights / flux App Service en S3) : R24 n'est tenu que pour les doublons |
| B. Filtrer **toute** exception dont la chaîne contient une `SqlException` : journaliser le **type**, le **numéro SQL** (et éventuellement la classe / l'état SQL), la méthode et le chemin, **sans le message** ; garder l'exception complète pour les exceptions non SQL | Règle uniforme et simple (« jamais de message SQL brut dans les journaux », comme au démarrage) ; réutilise `SqlServerErrors.TryGetSqlErrorNumber` ; le numéro suffit à classer l'erreur | Perte du message SQL (nom de colonne d'une troncature, par exemple) et de la pile si on ne la journalise pas à part |

**Avis de l'architecte (non imposé).** Je penche pour **B**, pour trois raisons : la règle devient la même partout (EF, `ApiExceptionHandler`, `Program.cs`), ce qui est plus facile à expliquer et à tester ; le numéro SQL identifie la nature de l'erreur sans ambiguïté ; et la perte de diagnostic peut être limitée en journalisant aussi la **pile d'appels** de l'exception la plus externe (la propriété `StackTrace` ne contient pas le message), ce qui localise le code fautif. Le message manquant se retrouve en reproduisant en local. L'option A reste défendable pour un mini-projet sans trafic, à condition d'accepter explicitement qu'une erreur SQL imprévue puisse journaliser une donnée personnelle ; dans ce cas, l'écrire comme risque accepté au § 9 de `architecture.md`. **À trancher par le porteur** ; si B est retenu, la modification de `ApiExceptionHandler.LogUnhandled` revient à l'`expert-technique`, les tests au `testeur`.

**À vérifier — sonde `/health/ready`.** Le service de health checks d'ASP.NET Core (`DefaultHealthCheckService`) journalise une vérification en échec (`Unhealthy`) au niveau Error, **avec l'exception** si la vérification en a produit une. `AddDbContextCheck` s'appuie sur `CanConnectAsync`, qui en principe renvoie `false` plutôt que de lever (l'événement `ConnectionError` d'EF étant, lui, désormais en `Debug`). Il faut **le constater** : base arrêtée, base inexistante, mot de passe faux, puis appel de `/health/ready` et lecture des journaux capturés. Les messages de connexion (4060, 18456) ne contiennent a priori pas de donnée personnelle (nom de base, nom de login technique), mais une exception jointe à ce niveau contredirait la règle « pas de message SQL brut ». Si c'est le cas : relever le niveau minimal de la catégorie `Microsoft.Extensions.Diagnostics.HealthChecks` ou fournir à `AddDbContextCheck` une vérification qui ne propage pas l'exception. La réponse HTTP de la sonde (texte `Healthy`/`Unhealthy`) ne divulgue rien.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Cookie d'authentification HttpOnly | Meilleure protection contre le vol de jeton par XSS, mais front et API sont sur deux domaines : cookie tiers `SameSite=None`, CORS avec credentials et protection CSRF à gérer. Contraire au cahier (JWT). |
| `MapIdentityApi` (.NET 8+) | Jetons opaques propriétaires (pas des JWT), endpoints imposés (en anglais, messages non traduits), peu de contrôle sur le blocage et les messages. |
| Entra External ID / Azure AD B2C | Hors cahier, configuration lourde, dépendance cloud même en local. |
| RS256 (clé asymétrique) | Utile quand plusieurs services valident les jetons ; ici l'émetteur et le validateur sont la même API. |
| Refresh token | Rotation, stockage, révocation : complexité non justifiée pour le MVP. Réévaluable si 60 minutes gênent les bénévoles. |
| Jeton en mémoire seulement | Plus sûr contre le XSS, mais déconnexion à chaque rafraîchissement de page : mauvaise ergonomie pour un public peu technophile. |
| Rôle unique par compte (claim `role` scalaire) | Plus simple à lire, mais oblige à retirer `User` lors d'une promotion et à le remettre lors d'un retrait : source d'erreurs. |
| Règles de mot de passe dupliquées dans un attribut de DTO | Toutes les erreurs en une réponse, mais deux sources de vérité à garder synchronisées avec Identity. |
| Version de la politique envoyée par le front | Le client pourrait enregistrer n'importe quelle version ; le serveur sait quelle version il sert. |
| Renvoyer `invalid_credentials` même pour un compte bloqué | Supprime l'énumération via `account_locked`, mais un membre bloqué ne comprend plus pourquoi son bon mot de passe est refusé, alors que le scénario de blocage (R21) prévoit de l'informer. Gain faible puisque l'inscription révèle déjà l'existence d'un e-mail. |
| Exemples de secrets « à remplacer » dans `.env.example` | Plus pratique pour démarrer, mais une valeur publique qui passe la validation finit tôt ou tard en production. |
| Filtre de journalisation qui réécrit les messages SQL (masquage de l'e-mail par expression régulière) | Couvre aussi le niveau Debug, mais fragile (format du message SQL Server, langue) et plus complexe que de ne pas journaliser l'exception dans les cas connus. |
| Ne traiter que les événements EF d'écriture (`SaveChangesFailed`, `CommandError`) | Version initiale : laissait passer les lectures en échec (`QueryIterationFailed`), d'où la fuite corrigée. Remplacée par la règle générale « aucune exception SQL au-dessus de `Debug` ». |
| **Ignorer** (`Ignore`) tous les événements d'échec d'EF Core | Aussi sûr au niveau habituel, et même au niveau `Debug` ; mais supprime toute trace EF utilisable pour un diagnostic local ponctuel. Le niveau `Debug` n'étant jamais activé hors poste de développement, `Debug` offre le même résultat en exploitation tout en gardant cet outil. Seul le doublon `SaveChangesFailed` est ignoré. |
| Rétrograder aussi `ExecutionStrategyRetrying` | Gain nul (erreurs transitoires sans donnée), perte d'un signal utile : les réveils d'Azure SQL serverless et les deadlocks deviendraient invisibles. |
| `ApiExceptionHandler` qui renvoie le résultat de `TryWriteAsync` (`false` si aucun writer n'accepte l'en-tête `Accept`) | Laisserait le middleware écrire sa propre réponse, mais celui-ci journalise alors l'exception brute (message SQL compris) : le filtre 2601/2627 serait contourné par un simple en-tête `Accept`. |
| Au démarrage, **`throw;` dans tous les cas** après la journalisation (pile d'origine conservée) | Simple et meilleur pour le diagnostic, mais le runtime écrit l'exception non gérée sur la sortie d'erreur : le message SQL (et l'e-mail possible) finit dans `docker compose logs`, hors de tout filtre. Retenu uniquement pour les `InvalidOperationException` de configuration, dont les messages sont maîtrisés. |
| Au démarrage, journaliser puis terminer par `Environment.Exit(1)` sans relancer | Pas de sortie d'erreur du runtime, mais contourne l'arrêt normal de l'hôte (vidage des journaux non garanti) et rend le comportement plus difficile à tester avec `WebApplicationFactory`. |

## Conséquences

- Risque XSS sur `localStorage` : atténué par la durée de 60 minutes, l'échappement natif d'Angular (pas de `innerHTML` non assaini) et une Content-Security-Policy servie par nginx et Static Web Apps.
- Identity bloque le compte **au 5e échec** : la réponse au 5e essai est déjà « compte bloqué », la 6e tentative (même avec le bon mot de passe) est refusée, ce qui satisfait le scénario.
- La vérification du security stamp rend la suppression de compte et le retrait du rôle Admin effectifs sans attendre l'expiration du jeton.
- Les données ajoutées au compte (`CreatedAt`, `LastLoginAt`, `PrivacyConsentAt`, `PrivacyPolicyVersion`) sont des données personnelles : elles sont visibles par leur titulaire (US-F3) et effacées avec le compte (ADR 0007).
- Ignorer `SaveChangesFailed` et rétrograder en `Debug` les autres événements d'échec d'EF Core (`CommandError`, `QueryIterationFailed`, `NonQueryOperationFailed`, `ExecuteUpdateFailed`, `ExecuteDeleteFailed`, `ConnectionError`, `TransactionError`) retire **toute** trace d'échec SQL d'EF Core au niveau habituel (Information et au-delà) ; seul `ExecutionStrategyRetrying` reste visible. Une erreur inattendue pendant une requête reste journalisée par `ApiExceptionHandler` (500) ; une violation d'unicité non traduite l'est aussi, mais sans l'exception (numéro SQL, type, méthode, chemin), ce qui suffit à la localiser ; les autres 500 gardent l'exception complète tant que le point de décision ci-dessus n'est pas tranché. Hors requête HTTP, chaque appelant journalise ses propres erreurs (démarrage en `Critical`, tâche d'expiration à l'étape 5).
- Tout **nouveau** code qui accède à la base hors requête HTTP doit intercepter et journaliser lui-même ses erreurs, sinon elles ne laissent aucune trace au niveau habituel.
- Une erreur SQL ou EF au démarrage se diagnostique avec son type et son numéro SQL, sans pile d'appels (écart assumé ci-dessus).
- Un client qui n'accepte pas le JSON reçoit les erreurs sous forme de statut seul, sans corps.
- Le niveau `Debug` sur EF Core n'est **pas** sûr au regard des données personnelles (voir ci-dessus) : ne jamais l'activer dans un environnement partagé ou en production.
- Points à tester (agent `testeur`) :
  - inscription avec un e-mail déjà pris → 409 `email_taken` et **aucune** occurrence de l'e-mail dans les journaux capturés (niveau Information et au-delà) ;
  - **lecture en échec** (par exemple démarrage sur une base sans schéma, ou requête sur une table absente) → **aucune** entrée de niveau ≥ Information dans les catégories `Microsoft.EntityFrameworkCore.*` portant une exception, hormis `ExecutionStrategyRetrying` ; l'entrée `Critical` de `Aaas.Startup` reste présente (type + numéro SQL, ici 208) ;
  - garde-fou de configuration : pour chaque identifiant d'événement de la liste ci-dessus, le niveau effectif configuré est `Debug` (ou ignoré pour `SaveChangesFailed`) ; test qui échoue si l'un disparaît de `ConfigureWarnings` ;
  - une violation 2601/2627 non traduite qui atteint `ApiExceptionHandler` → 500, entrée de journal avec le numéro SQL, sans exception ni e-mail ;
  - même cas avec `Accept: text/html` (aucun writer JSON accepté) → 500 **sans corps**, gestionnaire qui renvoie `true`, **aucune** entrée du middleware `ExceptionHandlerMiddleware` contenant l'exception ;
  - échec d'initialisation au démarrage par une `InvalidOperationException` de configuration → entrée `Critical` avec l'exception, sans e-mail, exception relancée identique ;
  - échec d'initialisation par une exception SQL ou EF (dont une `InvalidOperationException` avec `InnerException`) → entrée `Critical` avec type et numéro SQL, sans exception jointe ni message SQL ; exception relancée de type `InvalidOperationException`, `InnerException` nulle, message sans e-mail ;
  - **`/health/ready` avec base indisponible** (conteneur SQL arrêté, mauvais mot de passe, base inexistante) → réponse 503 `Unhealthy` ; relever si une entrée Error porte une exception SQL (point « à vérifier » ci-dessus).

### Risques de sécurité connus — décision différée (pas de limitation de débit à l'étape 1)

Ces risques sont **acceptés temporairement** et consignés ici pour qu'ils ne soient pas oubliés. Aucun n'empêche l'étape 1 d'être « terminée ». Ils seront réexaminés au plus tard avant la mise en ligne Azure (S3).

| Risque | Explication | Parade envisagée (non implémentée) |
|---|---|---|
| **Pas de limitation de débit sur `/api/auth/*`** | Rien n'empêche un robot d'enchaîner les essais : *password spraying* (un mot de passe courant essayé sur beaucoup de comptes, que le blocage par compte n'arrête pas) et création de comptes en masse sur `register`. | Limiteur intégré d'ASP.NET Core (`AddRateLimiter`, fenêtre fixe par adresse IP, par exemple 10 requêtes par minute) appliqué aux seuls endpoints `/api/auth/*`, réponse 429. Derrière App Service, l'IP du client se lit dans `X-Forwarded-For` (middleware `ForwardedHeaders` à configurer). |
| **Énumération des comptes par le temps de réponse** | Pour un e-mail inconnu, la connexion répond immédiatement ; pour un e-mail existant, elle calcule un hachage PBKDF2 (quelques dizaines de millisecondes). Le message est identique, mais la durée trahit l'existence du compte. Impact limité : l'inscription révèle déjà qu'un e-mail est pris (exigé par US-A1). | Hacher un mot de passe factice quand l'e-mail est inconnu, pour égaliser les temps ; et la limitation de débit ci-dessus. |
| **Énumération des comptes via `account_locked`** (risque **accepté**, sans parade prévue) | Un e-mail inconnu ne renvoie **jamais** `account_locked` (il n'y a pas de compte à bloquer) ; un e-mail existant le renvoie après 5 mauvais mots de passe. En 5 essais, un tiers sait donc si une adresse a un compte, et il bloque ce compte au passage. Impact **faible** : l'inscription révèle déjà qu'un e-mail est pris (409 `email_taken`, exigé par US-A1), plus simplement et sans bruit. | Aucune au MVP (voir alternative écartée « renvoyer `invalid_credentials` même pour un compte bloqué »). La limitation de débit réduit le volume d'adresses testables. |
| **Déni de service par blocage ciblé** | Connaissant l'e-mail d'un admin, un tiers peut envoyer 5 mauvais mots de passe toutes les 15 minutes et l'empêcher de se connecter. | Limitation de débit par IP (l'attaquant ne bloque plus que depuis une seule adresse) ; en dernier recours, déblocage manuel en SQL (`LockoutEnd = NULL`, `AccessFailedCount = 0`, avec `sqlcmd -I` ; procédure à ajouter dans `exploitation.md` par l'`expert-technique`). Ne pas exempter les admins du blocage. |

### À faire avant la mise en production (S3)

Liste à reprendre dans l'ADR de déploiement de S3 ; aucune de ces actions n'est nécessaire pour l'usage local de l'étape 1.

| Action | Pourquoi | Remarque |
|---|---|---|
| `UseForwardedHeaders` (`X-Forwarded-For`, `X-Forwarded-Proto`) en tête du pipeline | Derrière le proxy d'App Service, l'API voit l'IP du proxy et le schéma `http` : sans ce middleware, la limitation de débit par IP limiterait tout le monde d'un coup, et la redirection HTTPS bouclerait ou ne marcherait pas. | Vider `KnownNetworks`/`KnownProxies` (l'adresse du proxy d'App Service n'est pas fixe) ; vérifier en S3 si la variable `ASPNETCORE_FORWARDEDHEADERS_ENABLED` posée par App Service suffit. |
| HTTPS obligatoire et HSTS | Le jeton et le mot de passe ne doivent jamais circuler en clair. | Réglage « HTTPS uniquement » d'App Service (TLS terminé par Azure) ; `UseHsts()` hors développement ; `UseHttpsRedirection()` après `UseForwardedHeaders`. Static Web Apps sert déjà en HTTPS. |
| Limitation de débit sur `/api/auth/*` | Voir le premier risque ci-dessus. | `AddRateLimiter`, partition par IP (donc après `UseForwardedHeaders`), réponse 429 en `ProblemDetails` avec un `code` stable (à ajouter à `ErrorCodes`). |
| Trancher le point de décision « message SQL brut dans les autres 500 » et le point « `/health/ready` » | Les journaux partent alors vers le flux d'App Service (et Application Insights s'il est retenu), hors du poste local. | Voir « Point de décision ouvert » ci-dessus. |
| ~~Ports `docker compose` publiés sur `127.0.0.1`~~ **Fait** | SQL Server (compte SA) et l'API ne doivent pas être joignables depuis le réseau local du poste. | `docker-compose.yml` publie `"127.0.0.1:1433:1433"` et `"127.0.0.1:8080:8080"` ; le front (S2) suivra la même règle (`127.0.0.1:4200:80`). |

## Reste à valider par le porteur

1. Durée de session de **60 minutes sans « rester connecté »** : acceptable pour des bénévoles qui traitent des demandes ?
2. Blocage effectif **au 5e échec** (le scénario parle de la 6e tentative) : comportement compatible, à confirmer.
3. **Téléphone partagé** entre plusieurs comptes autorisé par défaut : à confirmer (avec l'`expert-fonctionnel` si besoin).
4. Les **risques de sécurité différés** ci-dessus : accepter le report de la limitation de débit jusqu'à S3, ou l'avancer ; accepter l'énumération des comptes via `account_locked`.
5. **Autres erreurs 500** : garder l'exception complète dans le journal (option A, diagnostic) ou filtrer toute exception SQL en ne gardant que type + numéro SQL (+ pile) (option B, recommandée par l'architecte) ? Et, après vérification, traitement éventuel du journal de `/health/ready`.
