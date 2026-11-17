# API — AAAS

> Tenu par l'agent `expert-technique`. Endpoints, rôle requis, entrées, sorties, codes d'erreur.
> État : **étape 1 (comptes)**.

## Conventions

- Base : `http://localhost:8080` (docker compose) ou `http://localhost:5080` (`dotnet run`). Préfixe `/api`.
- JSON en camelCase ; dates ISO 8601 en UTC avec décalage (`2026-11-10T19:39:20+00:00`).
- Authentification : en-tête `Authorization: Bearer <accessToken>`. **Tout endpoint exige un jeton**, sauf ceux indiqués « anonyme ».
- Erreurs au format `ProblemDetails` (RFC 9457), `Content-Type: application/problem+json` :

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Un compte existe déjà avec cet e-mail.",
  "status": 409,
  "code": "email_taken",
  "traceId": "00-..."
}
```

  - `title` : message en français, affichable tel quel ;
  - `code` : identifiant stable pour le front (liste dans `docs/technique/regles-metier.md` § 3) ;
  - `errors` (400 uniquement) : messages par champ, clés en camelCase :

```json
{
  "title": "Certains champs sont invalides.",
  "status": 400,
  "code": "validation_failed",
  "errors": {
    "phoneNumber": ["Le numéro de téléphone doit être un numéro tunisien au format +216 suivi de 8 chiffres, par exemple +216 20 123 456."],
    "password": ["Le mot de passe doit contenir au moins 8 caractères.", "Le mot de passe doit contenir au moins un chiffre."]
  }
}
```

  - erreur inattendue : `500`, `code` = `internal_error`, message générique. Si le client n'accepte pas `application/json` (en-tête `Accept`), la réponse garde le statut mais n'a pas de corps.

- Documentation OpenAPI : `GET /openapi/v1.json`, en environnement `Development` uniquement.

## Objets communs

**`AccountDto`**

| Champ | Type | Note |
|---|---|---|
| `id` | GUID | |
| `firstName`, `lastName`, `email` | string | |
| `phoneNumber` | string | normalisé : `+21620123456` |
| `roles` | string[] | `["User"]` ou `["Admin", "User"]` |
| `createdAt` | date | création du compte |
| `privacyConsentAt` | date | acceptation de la politique |
| `privacyPolicyVersion` | string | version acceptée (ex. `1.0`) |
| `lastLoginAt` | date ou `null` | dernière connexion réussie |

**`AuthResponse`** : `{ "accessToken": "eyJ...", "expiresAt": "2026-11-10T20:39:20+00:00", "user": AccountDto }`

## Comptes

### `POST /api/auth/register` — créer un compte (US-A1)

- **Accès** : anonyme.
- **Corps** :

```json
{
  "firstName": "Salma",
  "lastName": "Ben Ali",
  "email": "salma@exemple.tn",
  "phoneNumber": "+216 20 123 456",
  "password": "motdepasse1",
  "acceptPrivacyPolicy": true
}
```

- **Réponses** :

| Code | Quand |
|---|---|
| 201 `AuthResponse` | Compte créé avec le rôle `User`, utilisateur connecté. En-tête `Location: /api/account/me`. |
| 400 `validation_failed` | Champ manquant ou trop long, e-mail invalide, téléphone non conforme (RG-11), mot de passe trop faible, case non cochée. |
| 409 `email_taken` | Un compte existe déjà avec cet e-mail (sans tenir compte de la casse). |

### `POST /api/auth/login` — se connecter (US-A2)

- **Accès** : anonyme.
- **Corps** : `{ "email": "salma@exemple.tn", "password": "motdepasse1" }`
- **Réponses** :

| Code | Quand |
|---|---|
| 200 `AuthResponse` | Connexion réussie ; `lastLoginAt` mis à jour. |
| 400 `validation_failed` | E-mail ou mot de passe vide. |
| 401 `invalid_credentials` | E-mail inconnu ou mauvais mot de passe (même message). |
| 401 `account_locked` | 5 échecs consécutifs : compte bloqué 15 minutes (y compris au 5e essai). |

### `GET /api/account/me` — mon compte (US-F3)

- **Accès** : connecté (tout rôle).
- **Réponses** :

| Code | Quand |
|---|---|
| 200 `AccountDto` | |
| 401 `unauthorized` | Jeton absent, expiré, invalide, ou révoqué (compte supprimé, security stamp renouvelé). |

## Supervision

| Endpoint | Accès | Rôle |
|---|---|---|
| `GET /health` | anonyme | *Liveness* : l'API répond (`200 Healthy`). Ne touche pas la base, pour ne pas réveiller Azure SQL serverless à chaque sonde. |
| `GET /health/ready` | anonyme | *Readiness* : l'API **et** la base répondent (`200 Healthy` ou `503 Unhealthy`). |
