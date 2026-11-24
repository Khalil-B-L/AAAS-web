# API — AAAS

> Tenu par l'agent `expert-technique`. Endpoints, rôle requis, entrées, sorties, codes d'erreur.
> État : **étape 2** (comptes ; événements côté admin ; numéro de contact ; consultation publique des événements).

## Conventions

- Base : `http://localhost:8080` (docker compose) ou `http://localhost:5080` (`dotnet run`). Préfixe `/api`.
- **Appels du front** : toujours en chemin relatif (`/api/...`, même origine). En local, nginx (conteneur `front`, `http://localhost:4200`) ou le serveur de développement Angular (`proxy.conf.mjs`) relaient vers l'API : pas de CORS. En Azure (S3), le front appellera l'URL d'App Service avec CORS (ADR 0017). Les interfaces TypeScript recopiées de ce document sont dans `front/src/app/core/models/api-models.ts` : **toute modification d'un DTO ici se reporte là-bas dans la même tâche**.
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

  - refus du framework : `405` `method_not_allowed` (« Cette action n'est pas possible sur cette adresse. ») pour une méthode HTTP non prise en charge par la route (sans jeton valide, c'est le 401 qui l'emporte) ; `415` `unsupported_media_type` (« Le corps de la requête doit être envoyé en JSON (en-tête Content-Type: application/json). ») pour un corps attendu sans `Content-Type` JSON ;
  - erreur inattendue : `500`, `code` = `internal_error`, message générique. Si le client n'accepte pas `application/json` (en-tête `Accept`), la réponse garde le statut mais n'a pas de corps.

- Documentation OpenAPI : `GET /openapi/v1.json`, en environnement `Development` uniquement.
- **Dates en entrée** (étape 2) : chaîne ISO 8601 avec décalage **obligatoire** : `2026-12-05T10:00+01:00`, `2026-12-05T10:00:00+01:00`, `2026-12-05T09:00:00Z` (fractions de seconde tolérées). Sans décalage (`2026-12-05T10:00`), format inconnu ou date impossible (`2026-02-30T10:00+01:00`) → 400 sur le champ (`errors.startsAt` ou `errors.endsAt`) : « La date est invalide : format attendu AAAA-MM-JJTHH:mm avec le fuseau horaire, par exemple 2026-03-10T10:00+01:00. » Le serveur ne devine jamais le fuseau (ADR 0003, 0009). En sortie, toujours UTC (`+00:00`).
- **Prix** : en entrée, nombre JSON (`12.5`) ou `null` = gratuit ; `0` est enregistré comme `null`. En sortie, le prix relu en base garde l'échelle de la colonne `decimal(9,3)` : `12.5` saisi est renvoyé `12.500` (même valeur numérique ; le front le formate en dinars).
- **Énumérations** : chaînes (`"Draft"`, `"Published"`, `"Cancelled"`). En paramètre de requête, la casse est ignorée (`status=draft`).
- **Listes paginées** : paramètres `page` (1 à 10 000, défaut 1) et `pageSize` (1 à 50, défaut 20). Réponse :

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 37 }
```

  Une page au-delà de la dernière renvoie `200` avec `items: []`. Paramètre hors bornes ou non numérique → 400 avec la clé du paramètre en camelCase (`errors.pageSize`) et un message français (« La valeur « abc » n'est pas valide. », « Le nombre d'éléments par page doit être compris entre 1 et 50. »).
- **Version d'une fiche** (concurrence entre admins, ADR 0006) : les fiches modifiables (événement admin, numéro de contact) exposent `version` (Base64 de 8 octets, ex. `AAAAAAAAB9E=`). Le client la renvoie telle quelle à la modification ou au retrait ; si un autre admin a enregistré entre-temps → **409 `concurrent_update`** (« Cette fiche a été modifiée par un autre administrateur entre-temps. Rechargez-la, puis refaites vos changements. »). La version change après chaque écriture (modification, publication, dépublication, retrait). Version mal formée → 400 `errors.version`.
- **Limite connue** : une erreur de **type** dans le corps JSON (`"capacity": "trente"`, `"price": "gratuit"`) n'est pas traduite : 400 avec une clé `$.capacity` et un message technique anglais. Le front envoie toujours les bons types.

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

## Événements — consultation publique (US-C1, US-C2)

**`EventSummaryDto`** (ligne de liste) et **`EventDetailDto`** (détail = mêmes champs + `description`)

| Champ | Type | Note |
|---|---|---|
| `id` | GUID | |
| `title`, `location` | string | |
| `description` | string | détail seulement |
| `startsAt`, `endsAt` | date (UTC) | |
| `price` | nombre ou `null` | `null` = gratuit |
| `capacity` | entier | |
| `remainingSeats` | entier | capacité − places occupées (R7), jamais négatif. Étape 2 : égal à `capacity` (aucune réservation possible avant l'étape 4) |
| `status` | `"Published"` ou `"Cancelled"` | jamais `"Draft"` |
| `hasStarted` | booléen | début atteint |
| `hasEnded` | booléen | fin atteinte (détail d'un événement terminé seulement : la liste n'en contient pas) |
| `isFull` | booléen | toutes les places occupées |
| `isBookable` | booléen | publié, pas commencé, pas complet (R2) |

Mention à afficher (choisie par le front, dans cet ordre) : `status = "Cancelled"` → « Annulé » ; `hasEnded` → « Terminé » ; `hasStarted` → « Réservations closes » ; `isFull` → « Complet » ; sinon « N places restantes ».

### `GET /api/events?page=&pageSize=` — événements à venir

- **Accès** : anonyme (un jeton éventuel est ignoré s'il est invalide ou expiré : pas de 401).
- **Contenu** : événements **publiés ou annulés, non terminés** (`endsAt` dans le futur ; un événement en cours reste listé), triés par `startsAt` croissant puis par `id` (ordre stable). Taille par défaut : 20 (RG-14).
- **Réponses** : 200 `PagedResult<EventSummaryDto>` ; 400 `validation_failed` (`page`, `pageSize`).

### `GET /api/events/{id}` — détail d'un événement

- **Accès** : anonyme.
- **Réponses** :

| Code | Quand |
|---|---|
| 200 `EventDetailDto` | Événement publié ou annulé, **même terminé** (historique, RG-14 ; `hasEnded: true`). |
| 404 `not_found` | Brouillon, identifiant inconnu ou mal formé (« Contenu introuvable. », même réponse dans les trois cas, visiteur ou connecté). Un identifiant qui n'est pas un GUID (`/api/events/abc`) est servi par une route de repli `GET /api/events/{id}` sans contrainte, anonyme, qui ne fait que renvoyer ce 404 (`EventsController.GetMalformedId`, absente d'OpenAPI) ; sans elle, aucun endpoint ne correspondait et la politique « authentifié par défaut » répondait 401 au visiteur (écart D1, corrigé le 23/11/2026). Les autres verbes sur ce chemin restent refusés au visiteur (401). |

## Événements — administration (US-E2)

Toutes les routes `/api/admin/...` exigent le rôle **Admin** : sans jeton → 401 `unauthorized` ; membre → 403 `forbidden`. Un identifiant mal formé dans l'URL (`/api/admin/events/abc`, `.../abc/publish`, `.../abc/unpublish`, en GET/PUT/DELETE/POST selon la route) suit le même contrôle de rôle : visiteur 401, membre 403, admin **404 `not_found`** (« Événement introuvable. »). Ces chemins sont servis par `AdminEventsController.MalformedId` (soumis à `AdminOnly`) : sans elle, un membre connecté recevait 404 au lieu de 403.

**`AdminEventSummaryDto`** (ligne de liste) : `id`, `title`, `location`, `startsAt`, `endsAt`, `status` (`Draft`, `Published`, `Cancelled`), `capacity`, `price`, `pendingCount`, `confirmedCount` (0 jusqu'à l'étape 4), `remainingSeats`, `hasStarted`, `hasEnded`, `canBeDeleted`.

**`AdminEventDto`** (fiche) : les mêmes + `description`, `publishedAt` (dernière publication, `null` en brouillon), `firstPublishedAt` (première publication, jamais effacée), `createdAt`, `updatedAt`, `version`.

`canBeDeleted` = brouillon **jamais publié** (`firstPublishedAt` nul) : le front n'affiche le bouton « Supprimer » que dans ce cas.

**Corps de création et de modification**

```json
{
  "title": "Atelier couture",
  "description": "Initiation, matériel fourni.",
  "location": "Local de l'association, Tunis",
  "startsAt": "2026-12-05T10:00+01:00",
  "endsAt": "2026-12-05T12:00+01:00",
  "capacity": 30,
  "price": 12.5
}
```

Modification : mêmes champs, **tous obligatoires** (remplacement complet), plus `"version"` (obligatoire) et `"confirmRegistrantsWarning"` (facultatif, `false` par défaut).

Erreurs de saisie (400 `validation_failed`, `errors` par champ) :

| Champ | Règle | Message |
|---|---|---|
| `title`, `location` | obligatoire, ≤ 200 caractères | « Le titre est obligatoire. » / « Le titre ne doit pas dépasser 200 caractères. » (idem « Le lieu… ») |
| `description` | obligatoire, ≤ 4 000 caractères | « La description est obligatoire. » / « La description ne doit pas dépasser 4 000 caractères. » |
| `startsAt`, `endsAt` | obligatoires, avec décalage | « La date de début est obligatoire. » / « La date est invalide : format attendu AAAA-MM-JJTHH:mm avec le fuseau horaire… » |
| `startsAt` | dans le futur (à la création ; à la modification **seulement s'il change**) | « La date de début doit être dans le futur. » |
| `endsAt` | après le début, et dans le futur (R28) | « La fin doit être après le début. » / « La date de fin doit être dans le futur. » |
| `capacity` | obligatoire, entier de 1 à 10 000 | « La capacité est obligatoire. » / « La capacité doit être comprise entre 1 et 10 000 places. » |
| `price` | ≥ 0, ≤ 99 999,999, au plus 3 décimales | « Le prix ne peut pas être négatif. » / « Le prix est en dinars, avec au plus 3 décimales (millimes). » / « Le prix ne doit pas dépasser 99 999,999 TND. » |

Validation en deux temps : les champs obligatoires, longueurs et formats (dates, version) sont vérifiés d'abord ; les règles de valeurs (dates futures, capacité, prix) ensuite. Une requête avec un titre vide **et** une date passée ne signale d'abord que le titre.

### `GET /api/admin/events?status=&period=&page=&pageSize=` — liste admin

- `status` (facultatif) : `Draft`, `Published` ou `Cancelled`.
- `period` : `upcoming` (défaut : non terminés, par `startsAt` croissant), `past` (terminés, du plus récent au plus ancien), `all` (tous, du plus récent au plus ancien). Valeur inconnue → 400.
- **Réponses** : 200 `PagedResult<AdminEventSummaryDto>` (brouillons compris) ; 400 ; 401 ; 403.

### `GET /api/admin/events/{id}` — fiche admin

200 `AdminEventDto` (tout statut) ; 401 ; 403 ; 404 `not_found` (« Événement introuvable. »).

### `POST /api/admin/events` — créer (en brouillon)

| Code | Quand |
|---|---|
| 201 `AdminEventDto` | Créé avec `status: "Draft"`, invisible du public. En-tête `Location: /api/admin/events/{id}`. |
| 400 `validation_failed` | Voir le tableau des erreurs de saisie. |

### `PUT /api/admin/events/{id}` — modifier

| Code | Quand |
|---|---|
| 200 `AdminEventDto` | Modifié ; nouvelle `version`. |
| 400 `validation_failed` | Saisie invalide (dont `version` absente ou mal formée). |
| 404 `not_found` | Événement inconnu. |
| 409 `concurrent_update` | `version` périmée : un autre admin a modifié, publié ou dépublié la fiche. |
| 409 `event_cancelled` | Événement annulé (« …ne peut plus être modifié ni publié. »). |
| 409 `event_ended` | Événement terminé (« Cet événement est terminé : il ne peut plus être modifié. »). Un événement **commencé** reste modifiable (R28). |
| 409 `capacity_below_reserved` | Capacité inférieure aux places occupées (R11) : « 15 places sont déjà occupées (10 confirmées, 5 en attente). La capacité ne peut pas être inférieure. » *(possible à partir de l'étape 4)* |
| 409 `registrants_warning` | Début, fin ou prix modifié alors que l'événement a des inscrits, sans `confirmRegistrantsWarning: true` (R12) : rien n'est enregistré ; le front affiche le `title` dans une confirmation puis renvoie la **même** requête avec `confirmRegistrantsWarning: true`. Un changement de lieu ou de titre seul n'en déclenche pas. *(possible à partir de l'étape 4)* |

Ordre des contrôles : 404 → `concurrent_update` → `event_cancelled` / `event_ended` → 400 de valeurs → `capacity_below_reserved` → `registrants_warning`.

### `POST /api/admin/events/{id}/publish` — publier

- Pas de corps, pas de `version` (action : la règle de transition suffit).
- **Réponse 200** `PublishEventResponse` :

```json
{
  "event": { "...": "AdminEventDto", "status": "Published" },
  "warnings": [
    { "code": "contact_not_configured", "message": "Aucun numéro de contact n'est configuré : les membres ne sauront pas qui appeler pour faire confirmer leur demande." }
  ]
}
```

  `warnings` est vide si un numéro de contact est configuré. L'avertissement **ne bloque pas** la publication (US-E8).
- **Erreurs** : 404 ; 409 `event_state_changed` (« Cet événement est déjà publié. ») ; 409 `event_cancelled` ; 409 `event_started` (« Cet événement a déjà commencé : il ne peut plus être publié. ») ; 409 `concurrent_update` si un autre admin a écrit sur la fiche au même instant.

### `POST /api/admin/events/{id}/unpublish` — dépublier

- Pas de corps. Repasse en brouillon (`publishedAt: null`, `firstPublishedAt` conservé).
- **Réponses** : 200 `AdminEventDto` ; 404 ; 409 `event_state_changed` (« Cet événement n'est pas publié. ») ; 409 `event_cancelled` ; 409 `event_has_active_reservations` (R14 : « …il ne peut pas être dépublié. Annulez-le à la place. », *possible à partir de l'étape 4*).

### `DELETE /api/admin/events/{id}` — supprimer un brouillon jamais publié

- Pas de corps, pas de `version`. Le front demande une confirmation avant l'appel. Suppression **définitive**.
- **Réponses** :

| Code | Quand |
|---|---|
| 204 | Brouillon jamais publié supprimé. |
| 404 `not_found` | Événement inconnu (ou déjà supprimé). |
| 409 `event_already_published` | L'événement est publié, annulé, ou a été publié puis dépublié : « Cet événement a déjà été publié : il ne peut pas être supprimé. Seul un brouillon jamais publié peut l'être ; dépubliez-le ou annulez-le à la place. » (R15, RG-07). |
| 409 `concurrent_update` | Un autre admin a modifié ou publié l'événement au même instant : rien n'est supprimé, recharger. |

La suppression d'un événement publié qui n'a jamais eu de réservation arrivera à l'étape 7 (US-E3, Q29).

## Numéro de contact (US-E8, RG-10)

**`ContactSettingsDto`**

| Champ | Type | Note |
|---|---|---|
| `phoneNumber` | string ou `null` | E.164 (`+21620123456`) ; `null` = aucun numéro (jamais saisi ou retiré) |
| `whatsApp` | booléen | joignable sur WhatsApp ; toujours `false` sans numéro |
| `isConfigured` | booléen | `phoneNumber` non nul |
| `version` | string | à renvoyer pour modifier ou retirer |
| `updatedAt` | date ou `null` | `null` tant que jamais modifié |

Liens construits par le front : `tel:+21620123456`, `https://wa.me/21620123456`.

### `GET /api/settings/contact` — lire

- **Accès** : connecté (membre ou admin). **Visiteur → 401** : le numéro n'est jamais exposé publiquement (RG-10).
- **Réponses** : 200 `ContactSettingsDto` ; 401.

### `PUT /api/admin/settings/contact` — enregistrer ou changer

- **Accès** : Admin.
- **Corps** : `{ "phoneNumber": "+216 20 123 456", "whatsApp": true, "version": "AAAAAAAAB9E=" }`. Numéro tunisien `+216` + 8 chiffres, **mobile ou fixe** (`+216 71 123 456`), espaces tolérés (RG-11).
- **Réponses** : 200 `ContactSettingsDto` (nouvelle `version`) ; 400 `validation_failed` (`errors.phoneNumber` : message RG-11 ; `errors.version`) ; 401 ; 403 ; 409 `concurrent_update`.

### `DELETE /api/admin/settings/contact` — retirer le numéro

- **Accès** : Admin. Le front demande une confirmation avant l'appel.
- **Corps** (JSON, obligatoire) : `{ "version": "AAAAAAAAB9E=" }`. Contrôle de concurrence comme pour le `PUT` : un retrait ne doit pas effacer en silence un numéro qu'un autre admin vient d'enregistrer.
- **Effet** : plus de numéro, `whatsApp` repasse à `false` (R8). Retirer un numéro déjà absent est accepté (200).
- **Réponses** : 200 `ContactSettingsDto` (`phoneNumber: null`, `isConfigured: false`) ; 400 (`errors.version`, ou corps JSON vide : « Le corps de la requête est obligatoire. ») ; **415 `unsupported_media_type`** sans en-tête `Content-Type: application/json` (refus d'ASP.NET Core avant la lecture du corps) ; 401 ; 403 ; 409 `concurrent_update`.
- Remarque : un `DELETE` avec un corps est permis par HTTP mais sans sémantique standard ; Angular l'envoie avec `http.delete(url, { body })`, curl avec `-X DELETE -d '...'`.

## Supervision

| Endpoint | Accès | Rôle |
|---|---|---|
| `GET /health` | anonyme | *Liveness* : l'API répond (`200 Healthy`). Ne touche pas la base, pour ne pas réveiller Azure SQL serverless à chaque sonde. |
| `GET /health/ready` | anonyme | *Readiness* : l'API **et** la base répondent (`200 Healthy` ou `503 Unhealthy`). |
