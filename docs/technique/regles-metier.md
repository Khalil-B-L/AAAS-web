# Règles métier et cycle de vie — AAAS

> Tenu par l'agent `expert-technique`. Pour chaque règle : ce qui est appliqué, et **où** dans le code.
> Références : `docs/cahier-des-charges.md` (RG-xx), `docs/fonctionnel/mvp.md` (R-xx, US-xx), `docs/adr/`.
> État : **étape 1 (comptes)**. Les règles des réservations, événements et actualités seront ajoutées aux étapes suivantes.

## 1. Comptes et authentification

### 1.1 Inscription (US-A1, R20, RG-11)

| Règle | Comportement | Où dans le code |
|---|---|---|
| Champs obligatoires | Prénom (≤ 100), nom (≤ 100), e-mail valide (≤ 256), téléphone, mot de passe (≤ 128), case de consentement cochée. Sinon **400**, erreur sur le champ concerné. | `RegisterRequest` (attributs de validation) — `src/Aaas.Application/Accounts/AccountDtos.cs` |
| Téléphone tunisien uniquement (RG-11) | Accepté : `+216` suivi d'exactement 8 chiffres (0-9), espaces tolérés (`+216 20 123 456`). Refusé : saisie locale (`20 123 456`), préfixe `00`, autre indicatif, tirets ou points, chiffres non latins. Stocké normalisé : `+21620123456`. Message : « Le numéro de téléphone doit être un numéro tunisien au format +216 suivi de 8 chiffres, par exemple +216 20 123 456. » | `PhoneNumber.TryParse` — `src/Aaas.Domain/Common/PhoneNumber.cs` ; utilisé par `TunisianPhoneNumberAttribute` (400) et revérifié par `AccountService.RegisterAsync` |
| E-mail unique sans tenir compte de la casse | `Salma@Exemple.tn` = `salma@exemple.tn`. Doublon : **409** `email_taken`, « Un compte existe déjà avec cet e-mail. ». Protégé aussi contre deux inscriptions simultanées par l'index unique filtré `UX_AspNetUsers_NormalizedEmail`. Ce cas (erreur SQL 2601) n'est pas journalisé comme erreur et l'e-mail n'apparaît pas dans les journaux (R24) : `SaveChangesFailed` ignoré ; `CommandError` et les autres événements EF Core porteurs d'une exception SQL (`QueryIterationFailed`, `ConnectionError`, `TransactionError`, échecs d'`ExecuteUpdate`/`ExecuteDelete`) rétrogradés en Debug. | `IdentityService.CreateUserAsync` + `AppUserConfiguration` (index) ; `ConfigureWarnings` dans `src/Aaas.Infrastructure/DependencyInjection.cs` |
| Mot de passe (R21) | Au moins 8 caractères, au moins un chiffre, au moins une lettre (minuscule ou majuscule, accents compris). Sinon **400** avec tous les messages sur le champ `password`. | Options Identity dans `src/Aaas.Infrastructure/DependencyInjection.cs` + `LetterRequiredPasswordValidator` ; messages : `FrenchIdentityErrorDescriber` |
| Consentement (R24, § 6.4) | Case obligatoire. On enregistre la **date** (`PrivacyConsentAt`) et la **version** de la politique acceptée (`PrivacyPolicyVersion`, valeur de la configuration `PrivacyPolicy:CurrentVersion`, actuellement `1.0`). | `AccountService.RegisterAsync`, `PrivacyPolicyOptions` |
| Rôle à l'inscription (R22) | Toujours `User`. Aucun champ de rôle n'est lu dans la requête. | `AccountService.RegisterAsync` |
| Connexion automatique | L'inscription renvoie directement un jeton ; `LastLoginAt` = date d'inscription. | `AccountService.RegisterAsync`, `IdentityService.CreateUserAsync` |
| Atomicité | Compte et rôle sont créés dans une même transaction : jamais de compte sans rôle. | `IdentityService.CreateUserAsync` (execution strategy + transaction) |
| Mineurs | Aucun contrôle ni case dédiée (décision du porteur, § 6.4). | — |

Données conservées pour un compte (R20, § 6.4) : prénom, nom, e-mail, téléphone, mot de passe **haché** (PBKDF2, Identity), date de création (`CreatedAt`), date de dernière connexion (`LastLoginAt`), date et version du consentement. Rien d'autre. Toutes les dates sont en UTC (ADR 0003).

### 1.2 Connexion (US-A2, R21)

| Règle | Comportement | Où dans le code |
|---|---|---|
| Identifiants incorrects | E-mail inconnu **ou** mauvais mot de passe : même réponse **401** `invalid_credentials`, « E-mail ou mot de passe incorrect. » | `AccountService.LoginAsync`, `IdentityService.CheckPasswordAsync` |
| Blocage après échecs | Après **5 échecs consécutifs**, le compte est bloqué **15 minutes**. La réponse au 5e échec est déjà **401** `account_locked` ; pendant le blocage, même le bon mot de passe est refusé. Message : « Trop de tentatives de connexion. Votre compte est bloqué temporairement : réessayez dans N minutes. » (N calculé sur la fin réelle du blocage). | `SignInManager.CheckPasswordSignInAsync(lockoutOnFailure: true)` ; options `Lockout` dans `DependencyInjection.cs` |
| Remise à zéro | Une connexion réussie remet le compteur d'échecs à zéro (« 5 fois **de suite** »). | Identity (`CheckPasswordSignInAsync`) |
| Dernière connexion (R25) | `LastLoginAt` mis à jour à chaque connexion réussie. | `IdentityService.RecordLoginAsync` |

### 1.3 Session et jeton (US-A3, ADR 0002)

| Règle | Comportement | Où dans le code |
|---|---|---|
| Durée de session | Jeton valable **60 minutes** (`Jwt:LifetimeMinutes`), sans renouvellement automatique. Ensuite : **401**, le front renvoie vers la connexion. | `TokenService`, `JwtOptions` |
| Révocation immédiate | À chaque requête authentifiée, le *security stamp* du jeton est comparé à celui de la base. Compte supprimé ou stamp renouvelé (changement de rôle) : **401** immédiat. | `JwtAuthenticationExtensions.ValidateSecurityStampAsync` → `IdentityService.IsSecurityStampValidAsync` |
| Sécurisé par défaut | Tout endpoint exige un jeton valide, sauf ceux marqués explicitement anonymes (`/api/auth/*`, `/health`, `/health/ready` ; `/openapi/v1.json` en Development seulement). Un chemin inconnu renvoie **401** `unauthorized` (et non 404) à un anonyme. | Politique de repli (`SetFallbackPolicy`) dans `JwtAuthenticationExtensions` |
| Contenu du jeton | Identifiant, prénom, rôles, security stamp. Ni e-mail ni téléphone. | `TokenService.CreateAccessToken` |

### 1.4 Administrateurs (R22, RG-09, ADR 0008)

| Règle | Comportement | Où dans le code |
|---|---|---|
| Pas d'inscription admin | Le rôle Admin ne s'obtient jamais par l'API publique. | `AccountService.RegisterAsync` |
| Premier admin | Au démarrage, **si aucun admin n'existe** et que `Bootstrap:AdminEmail`, `AdminPassword`, `AdminFirstName`, `AdminLastName`, `AdminPhone` sont renseignés : création du compte avec les rôles `User` + `Admin` (mêmes règles de mot de passe et de téléphone que l'inscription). Consentement daté de la création. | `DatabaseInitializer.EnsureFirstAdminAsync` — `src/Aaas.Infrastructure/Initialization/DatabaseInitializer.cs` |
| Idempotence | S'il existe déjà un admin, rien n'est fait, même si `Bootstrap:*` est présent. Redémarrer ne crée jamais un second admin. | idem |
| E-mail déjà inscrit (par défaut) | Aucun admin n'existe et un compte porte déjà `Bootstrap:AdminEmail` : **pas de promotion automatique**, l'API **refuse de démarrer** (`InvalidOperationException` : « Un compte existe déjà avec Bootstrap:AdminEmail… »). Le message ne contient pas l'e-mail (R24). Raison : n'importe qui aurait pu s'inscrire avec cet e-mail avant le premier démarrage. Solution : promotion manuelle (`exploitation.md` § 6) ou option ci-dessous. | `DatabaseInitializer.HandleExistingAccountAsync` |
| E-mail déjà inscrit, `Bootstrap:PromoteExistingAccount=true` | Le compte existant est **promu** : rôles `User` + `Admin`, mot de passe inchangé (celui du Bootstrap est ignoré), security stamp renouvelé (ancien jeton : 401). Trace d'audit en `Warning` (identifiant du compte, pas l'e-mail). **Une seule instance de l'API doit tourner pendant cette promotion** : les démarrages simultanés ne sont pas gérés sur ce chemin. | idem |
| Deux instances au même démarrage : rôles | Base neuve : si la création d'un rôle échoue (violation d'unicité SQL 2601/2627 sur `RoleNameIndex`, ou refus « rôle déjà existant » d'Identity), l'initialisation vide le suivi EF et revérifie : le rôle existe → on continue ; sinon → l'erreur est relancée. | `DatabaseInitializer.CreateRoleAsync` |
| Deux instances au même démarrage : premier admin | Si la création échoue sur « e-mail déjà utilisé » (index unique), l'initialisation revérifie : un admin existe → rien à faire ; sinon → même règle que « e-mail déjà inscrit ». | `DatabaseInitializer.HandleEmailTakenConcurrentlyAsync` |
| Configuration absente ou incomplète | Avertissement listant les clés manquantes (sans valeur), l'API démarre sans admin. Avec docker compose, un `BOOTSTRAP_ADMIN_EMAIL` ou `BOOTSTRAP_ADMIN_PASSWORD` vide bloque `docker compose up`. | `DatabaseInitializer.EnsureFirstAdminAsync`, `BootstrapOptions.MissingKeys` |
| Échec de l'initialisation | Log `Critical` puis arrêt de l'API. Exception SQL/EF : seuls le type et le numéro SQL sont journalisés, l'exception relancée est neutre (R24). | `src/Aaas.Api/Program.cs` |
| Configuration invalide | Mot de passe trop faible, mot de passe d'exemple public (`ChangeMe2026`) ou téléphone invalide : l'API **refuse de démarrer** avec un message explicite. | idem, `BootstrapOptions.KnownExamplePasswords` |
| Admins suivants | Procédure manuelle (script SQL) : voir `docs/technique/exploitation.md` § 6. | — |
| Au moins un admin (RG-09) | Garanti à la création ; la protection contre la suppression du dernier admin arrive avec US-F2 (ADR 0007). | à venir |

## 2. Dates

Toutes les dates sont des `DateTimeOffset` stockées en UTC (décalage forcé à `+00:00` par la convention `UtcDateTimeOffsetConverter`, `src/Aaas.Infrastructure/Persistence/Conventions/`). L'heure courante vient toujours de `TimeProvider` (jamais `DateTime.Now`). La conversion à l'heure de Tunis est faite par le front (ADR 0003).

## 3. Codes d'erreur (extension `code` des réponses d'erreur)

| Code | HTTP | Sens |
|---|---|---|
| `validation_failed` | 400 | Un ou plusieurs champs invalides (détail dans `errors`) |
| `email_taken` | 409 | Un compte existe déjà avec cet e-mail |
| `invalid_credentials` | 401 | E-mail ou mot de passe incorrect |
| `account_locked` | 401 | Compte bloqué temporairement après 5 échecs |
| `unauthorized` | 401 | Jeton absent, expiré ou révoqué |
| `forbidden` | 403 | Rôle insuffisant |
| `not_found` | 404 | Ressource introuvable |
| `internal_error` | 500 | Erreur inattendue |

Liste de référence : `src/Aaas.Domain/Common/ErrorCodes.cs`. Un code existant ne doit jamais être renommé (le front s'appuie dessus).
