# API — AAAS

> Tenu par l'agent `expert-technique`. Endpoints, rôle requis, entrées, sorties, codes d'erreur.
> État : **réservations, tâche B** (24/11/2026) : comptes ; événements côté admin ; numéro de contact ; consultation publique ; demande, suivi et annulation d'une place par le membre ; traitement par l'admin (demandes à traiter, inscrits, confirmer, refuser, annuler, « Payée »). Tâche C (expiration automatique) : à venir.

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
- **Extensions des 409** (ADR 0018 § 4) : certaines erreurs portent l'état réel en plus de `code` : `reservationId` (avec `already_reserved` : la demande existante) et `currentStatus` (avec `reservation_state_changed` : statut effectif relu, ex. `"Expired"`). Jamais de donnée personnelle.

```json
{ "title": "Vous avez déjà une demande en cours ou une réservation pour cet événement", "status": 409,
  "code": "already_reserved", "reservationId": "5f0c2a3e-…" }
```

- **Réponse sans corps** : `204 No Content` signifie « rien à renvoyer » (ex. aucune demande pour cet événement) ; ce n'est pas une erreur.
- **Cache** : toutes les réponses de `/api/me/...` et `/api/admin/...`, erreurs comprises, portent `Cache-Control: no-store` (ADR 0021 § 4) : le navigateur ne les garde pas sur disque.
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
| `remainingSeats` | entier | capacité − places occupées (R7), jamais négatif. Places occupées = demandes en attente non échues + réservations confirmées (une demande échue ne compte plus, même avant d'être écrite « Expirée ») |
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

**`AdminEventSummaryDto`** (ligne de liste) : `id`, `title`, `location`, `startsAt`, `endsAt`, `status` (`Draft`, `Published`, `Cancelled`), `capacity`, `price`, `pendingCount`, `confirmedCount` (`pendingCount` : demandes en attente non échues), `remainingSeats`, `hasStarted`, `hasEnded`, `canBeDeleted`.

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
| 409 `concurrent_update` | `version` périmée : un autre admin a modifié, publié ou dépublié la fiche. Une annulation de membre pendant le recalcul des échéances **ne** le déclenche **pas** : la modification est rejouée une fois (seul un second conflit donne ce code). |
| 409 `event_cancelled` | Événement annulé (« …ne peut plus être modifié ni publié. »). |
| 409 `event_ended` | Événement terminé (« Cet événement est terminé : il ne peut plus être modifié. »). Un événement **commencé** reste modifiable (R28). |
| 409 `capacity_below_reserved` | Capacité inférieure aux places occupées (R11) : « 15 places sont déjà occupées (10 confirmées, 5 en attente). La capacité ne peut pas être inférieure. » |
| 409 `registrants_warning` | Début, fin ou prix modifié alors que l'événement a des inscrits, sans `confirmRegistrantsWarning: true` (R12) : rien n'est enregistré ; le front affiche le `title` dans une confirmation puis renvoie la **même** requête avec `confirmRegistrantsWarning: true`. Un changement de lieu ou de titre seul n'en déclenche pas. Si le **début** change, l'échéance des demandes en attente non échues est recalculée dans la même transaction : `min(demande + 48 h, nouveau début)` (ADR 0018 § 5). |

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
- **Réponses** : 200 `AdminEventDto` ; 404 ; 409 `event_state_changed` (« Cet événement n'est pas publié. ») ; 409 `event_cancelled` ; 409 `event_has_active_reservations` (R14 : « …il ne peut pas être dépublié. Annulez-le à la place. »).

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

## Réservations — membre (US-D1 à D4, tâche A)

Rôle : **connecté** (membre ; un admin a aussi le rôle membre et peut demander une place pour lui-même, QF-1). L'identité vient **toujours du jeton** : aucune route ne prend un identifiant de membre (ADR 0021). Une réservation d'un autre membre répond **404**, comme un contenu introuvable. Visiteur : 401. Identifiant mal formé : 404 (connecté) ou 401 (visiteur). Toutes les réponses : `Cache-Control: no-store`.

**`MyReservationDto`**

| Champ | Type | Note |
|---|---|---|
| `id` | GUID | |
| `status` | chaîne | statut **effectif** : `Pending`, `Confirmed`, `Refused`, `Expired`, `CancelledByUser`, `CancelledByAssociation`. Une demande en attente dont l'échéance est atteinte est déjà `Expired`, même si la base n'a pas encore été mise à jour |
| `requestedAt` | date | |
| `expiresAt` | date ou `null` | seulement si `Pending` : date limite pour faire confirmer la demande (48 h après la demande, ou le début de l'événement si c'est plus tôt) |
| `decidedAt`, `cancelledAt` | date ou `null` | confirmation ou refus ; annulation |
| `refusalReason` | chaîne ou `null` | motif de refus saisi par l'admin (facultatif) |
| `isPaid`, `paidAt` | booléen, date ou `null` | mention « Payée » posée par l'admin ; conservée après une annulation. Le membre ne voit pas quel admin l'a posée |
| `canCancel` | booléen | le membre peut annuler maintenant : en attente non échue, ou confirmée jusqu'à 24 h avant le début (borne incluse) |
| `cancellableUntil` | date ou `null` | échéance (en attente) ou début − 24 h (confirmée) ; `null` sinon |
| `event` | objet | `id`, `title`, `location`, `startsAt`, `endsAt`, `price`, `status`, `hasStarted`, `hasEnded`, `isVisible` (faux si l'événement est repassé en brouillon : pas de lien) |

### `POST /api/me/reservations` — demander une place

- **Entrée** : `{ "eventId": "<GUID>" }`.
- **Réponse** : **201** `MyReservationDto` (statut `Pending`), en-tête `Location: /api/me/reservations/{id}`.

| Erreur | Quand | `title` |
|---|---|---|
| 400 `errors.eventId` | champ absent | « L'événement est obligatoire. » |
| 404 `not_found` | événement inconnu **ou brouillon** (indiscernables) | « Contenu introuvable. » |
| 409 `event_cancelled` | événement annulé | « Cet événement a été annulé. » |
| 409 `reservations_closed` | événement commencé ou terminé | « Les réservations sont closes » |
| 409 `already_reserved` + `reservationId` | le membre a déjà une demande en attente ou une réservation confirmée pour cet événement (contrôlé **avant** « complet ») | « Vous avez déjà une demande en cours ou une réservation pour cet événement » |
| 409 `event_full` | plus aucune place | « Événement complet » |

Garantie (RG-02, ADR 0004, 0019) : la demande est traitée **sous verrou** de la ligne de l'événement ; N demandes simultanées sur la dernière place donnent exactement un 201. Double clic : le second appel reçoit `already_reserved` avec l'identifiant de la première demande (le front l'affiche sans message d'erreur).

### `GET /api/me/reservations?scope=&page=&pageSize=` — mes réservations (US-D2)

- `scope` : `current` (défaut) = en attente non échues et confirmées dont l'événement **n'est pas terminé** (un événement en cours reste « À venir »), du début le plus proche au plus lointain ; `history` = tout le reste (terminés, refusées, expirées, annulées), du début le plus récent au plus ancien.
- **Réponse** : 200 `PagedResult<MyReservationDto>` (20 par page par défaut). 400 si `scope` est inconnu ou si la pagination est hors bornes.

### `GET /api/me/reservations/{id}` — une réservation

200 `MyReservationDto` ; 404 si inconnue ou d'un autre membre.

### `GET /api/me/events/{eventId}/reservation` — « ma demande » sur le détail

- **200** `MyReservationDto` : la demande **la plus récente** du membre pour cet événement (active ou terminée) ;
- **204** : aucune demande ;
- **404** : événement inconnu ou brouillon (même règle que `GET /api/events/{id}`).

Le détail public `GET /api/events/{id}` reste identique pour tous (anonyme) : « ma demande » passe par cet appel séparé (ADR 0021 § 1). Le numéro à contacter est lu par `GET /api/settings/contact` (jamais recopié dans les réservations).

### `POST /api/me/reservations/{id}/cancel` — annuler (US-D3)

- **Réponse** : 200 `MyReservationDto` (`CancelledByUser`) ; 404.

| Erreur | Quand | `title` |
|---|---|---|
| 409 `cancellation_deadline_passed` | réservation **confirmée**, moins de 24 h avant le début | « Le délai d'annulation est dépassé : contactez l'association » |
| 409 `reservation_state_changed` + `currentStatus` | déjà annulée, refusée, ou demande expirée (y compris si l'échéance vient de passer, ou si un admin a changé son statut au même moment). Une écriture concurrente qui ne change pas le statut (échéance recalculée après un changement de date) ne le déclenche pas : l'annulation est réappliquée une fois → 200 | « Cette demande a déjà été traitée ou annulée. » ; si `currentStatus` = `Expired` : « Votre demande a expiré. Vous pouvez en faire une nouvelle. » |

Annuler ne prend pas de verrou (une place ne peut que se libérer) : un conflit d'écriture est détecté par la `rowversion` (ADR 0006, 0019 § 5).

## Réservations — administration (US-E4 à E7, tâche B)

Rôle : **Admin** (`AdminOnly`). Visiteur : 401 ; membre : 403 (avant toute lecture : il n'apprend rien, pas même qu'un identifiant est mal formé). Identifiant mal formé : 404 `not_found` pour un admin (routes de repli soumises à `AdminOnly`, comme `/api/admin/events/{id}`). Toutes les réponses, erreurs comprises : `Cache-Control: no-store` (coordonnées des membres, ADR 0021 § 4). Aucune de ces données n'est journalisée.

Les actions sont des **intentions** (`POST .../confirm`, ADR 0006) **sans `version`** : la `rowversion` de la réservation détecte qu'un autre acteur (autre admin, membre, expiration) est passé entre la lecture et l'écriture → **409 `reservation_state_changed`** avec `currentStatus` (statut effectif relu). Si l'écriture concurrente n'a pas changé le statut (échéance recalculée après un déplacement de date, « Payée » posée au même moment), l'action est réappliquée une fois → 200 ; un **second** conflit sans changement de statut → **409 `concurrent_update`** (rare : la ligne change sans cesse ; recharger). « Retirer Payée », valable dans tous les statuts, est réappliqué même si le statut a changé. Aucune action admin ne prend le verrou de l'événement (ADR 0019 § 1 : confirmer ne change pas le nombre de places prises, refuser et annuler en libèrent).

**`AdminReservationDto`**

| Champ | Type | Note |
|---|---|---|
| `id`, `status`, `requestedAt`, `expiresAt`, `decidedAt`, `refusalReason`, `cancelledAt`, `isPaid`, `paidAt` | | comme `MyReservationDto` (statut **effectif** ; `expiresAt` seulement si `Pending`) |
| `paidBy` | `{ firstName, lastName }` ou `null` | admin qui a posé « Payée » (R13). `null` avec `isPaid: true` : cet admin a supprimé son compte (« un administrateur supprimé ») |
| `member` | `{ id, firstName, lastName, email, phoneNumber }` ou `null` | coordonnées (RG-15). `null` = « Compte supprimé » (ADR 0007) |
| `event` | `{ id, title, startsAt, endsAt, price, status }` | |
| `actions` | `{ canConfirm, canRefuse, canCancel, canMarkPaid, canUnmarkPaid }` | boutons à proposer, calculés par le Domain (`Reservation.AdminActions`) ; le serveur revérifie à chaque appel |

Pas de `canCancel` / `cancellableUntil` du membre : l'admin a ses propres règles (`actions`).

### `GET /api/admin/reservations/pending?page=&pageSize=` — demandes à traiter (US-E6)

- Demandes **en attente non échues** de tous les événements, **échéance la plus proche d'abord** (puis date de demande).
- **Réponse** : 200 `PagedResult<AdminReservationDto>` (20 par page). `totalCount` sert au compteur « Demandes (N) » du menu (le front appelle avec `pageSize=1`). 400 si la pagination est hors bornes.

### `GET /api/admin/events/{eventId}/reservations` — inscrits d'un événement (US-E4)

- **Non paginée** (P-R7). Brouillon compris (historique d'un événement dépublié).
- **Réponse** : 200 `EventRegistrantsDto` :

| Champ | Contenu |
|---|---|
| `event` | `id`, `title`, `startsAt`, `endsAt`, `capacity`, `price`, `status`, `hasStarted`, `hasEnded` |
| `occupancy` | `pending`, `confirmed`, `active` (= pending + confirmed), `remaining` (≥ 0) : « 15 / 30 places occupées (12 confirmées, 3 en attente) ». Calculé sur le statut **effectif** (une demande échue ne compte pas) |
| `pending` | en attente non échues, **de la plus ancienne à la plus récente** |
| `confirmed` | confirmées, **par nom puis prénom** (insensible à la casse), comptes supprimés à la fin |
| `others` | refusées, expirées (y compris échues non encore écrites), annulées : **la plus récente demande d'abord** |

- **Erreurs** : 404 `not_found` (« Événement introuvable. ») si l'événement n'existe pas.
- Coordonnées : **une seule requête** sur `AspNetUsers` pour toute la page (`IIdentityService.GetContactsAsync`, ADR 0021 § 3), membres et admins « Payée » confondus.

### `POST /api/admin/reservations/{id}/confirm` — confirmer (US-E6)

- En attente non échue → `Confirmed` (`decidedAt` = maintenant). Places inchangées. L'admin qui confirme n'est pas enregistré (P-R6).
- **Réponses** : 200 `AdminReservationDto` ; 404 ; 409 `reservation_state_changed` + `currentStatus` (déjà confirmée, refusée, annulée, ou échue : un événement commencé a forcément des demandes échues, l'échéance ne dépassant jamais le début). Comme toutes les actions ci-dessous : 409 `concurrent_update` au second conflit d'écriture d'affilée.

### `POST /api/admin/reservations/{id}/refuse` — refuser (US-E6)

- **Corps facultatif** : `{ "reason": "Réservé aux adhérents" }`. Sans corps, `{}`, `null` ou motif blanc → refus **sans motif** (`refusalReason: null`). Espaces de début et de fin retirés ; **500 caractères au plus après retrait** (sinon 400 `errors.reason` : « Le motif ne doit pas dépasser 500 caractères. »). Le motif est visible par le membre.
- En attente non échue → `Refused` ; la place est libérée ; le membre peut refaire une demande (RG-03).
- **Réponses** : 200 ; 400 ; 404 ; 409 `reservation_state_changed`.

### `POST /api/admin/reservations/{id}/cancel` — annuler (US-E5)

- En attente non échue ou confirmée → `CancelledByAssociation` (`cancelledAt`), **sans délai** tant que l'événement n'est pas terminé (y compris pendant l'événement). La mention « Payée » est conservée.
- **Réponses** : 200 ; 404 ; 409 `reservation_state_changed` ; 409 **`event_ended`** (« Cet événement est terminé : la réservation ne peut plus être annulée. ») si `maintenant >= fin`.

### `POST /api/admin/reservations/{id}/mark-paid` — marquer « Payée » (US-E7)

- Sur une **confirmée** d'un événement **payant**, à tout moment, y compris après la fin (QF-4). Enregistre `paidAt` et l'admin **du jeton** (`paidBy`). Déjà payée : sans effet, 200 avec la date et l'admin d'origine (double clic).
- **Erreurs** (dans cet ordre) : 409 `reservation_state_changed` + `currentStatus` (refusée, expirée, annulée : la ligne affichée est périmée) ; 409 **`payment_not_applicable`** (« Cet événement est gratuit : aucun paiement à enregistrer ») ; 409 **`reservation_not_confirmed`** + `currentStatus: "Pending"` (« Confirmez d'abord la demande avant de la marquer payée ») ; 404.

### `POST /api/admin/reservations/{id}/unmark-paid` — retirer « Payée » (US-E7)

- Quel que soit le statut, **y compris annulée** (QF-5, correction d'erreur) : `isPaid: false`, `paidAt` et `paidBy` effacés. Non payée : sans effet, 200. Si le membre annule au même instant, le retrait est réappliqué sur la ligne relue → 200 avec `status: "CancelledByUser"` (pas de `reservation_state_changed`).
- **Réponses** : 200 ; 404 ; 409 `concurrent_update` (second conflit d'écriture d'affilée, très rare).

Effets sur les événements (deviennent atteignables avec les vraies confirmations) : `PUT /api/admin/events/{id}` → `capacity_below_reserved` (R11) et `registrants_warning` (R12) ; `POST .../unpublish` → `event_has_active_reservations` (R14), levé de nouveau dès que l'admin annule les réservations actives.

## Supervision

| Endpoint | Accès | Rôle |
|---|---|---|
| `GET /health` | anonyme | *Liveness* : l'API répond (`200 Healthy`). Ne touche pas la base, pour ne pas réveiller Azure SQL serverless à chaque sonde. |
| `GET /health/ready` | anonyme | *Readiness* : l'API **et** la base répondent (`200 Healthy` ou `503 Unhealthy`). |
