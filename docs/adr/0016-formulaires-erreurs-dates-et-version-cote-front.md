# ADR 0016 — Front : formulaires, erreurs ProblemDetails, dates de Tunis et version (409)

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 20/11/2026
- **Mise en œuvre** : semaine 2, tâches T2, T4, T8, T9 du plan [`plan-front-angular.md`](../technique/plan-front-angular.md)
- **Références** : api.md (conventions, `ProblemDetails`, `code`, `errors`, `version`) ; ADR 0003 (UTC, `toTunisIso`, `DATE_PIPE_DEFAULT_OPTIONS`), ADR 0006 et 0012 (version, `registrants_warning`), ADR 0009 (date avec décalage obligatoire), ADR 0010 (prix TND), ADR 0011 (pagination) ; mvp R12, R20, R21, R26

## Contexte

L'API renvoie des erreurs normalisées : `title` en français affichable tel quel, `code` stable, `errors` par champ (clés camelCase) pour les 400, parfois `$.champ` pour une erreur de type JSON. Les fiches modifiables (événement admin, numéro de contact) exposent une `version` à renvoyer ; deux 409 demandent une réaction précise (`concurrent_update`, `registrants_warning`). Les dates circulent en UTC avec décalage et doivent être saisies et affichées à l'heure de Tunis quel que soit le fuseau du navigateur. Le public est peu à l'aise avec l'informatique (cahier § 6.1).

## Décision

### 1. Formulaires
- **Reactive forms typés** (`NonNullableFormBuilder`), un `FormGroup` par écran, noms des contrôles **identiques** aux clés JSON de l'API (`firstName`, `phoneNumber`, `startsAt`…) : une erreur serveur se recopie sans table de correspondance.
- **Validation côté front = confort**, reprise des règles serveur avec les **mêmes messages** que l'API : obligatoire, longueurs (prénom/nom 100, e-mail 256, mot de passe 8 à 128 avec une lettre et un chiffre, titre/lieu 200, description 4 000), téléphone RG-11 (espaces retirés, puis `+216` suivi de exactement 8 chiffres), capacité entière 1 à 10 000, prix ≥ 0, ≤ 99 999,999, au plus 3 décimales, fin après début. Le serveur reste la référence (dates futures, règles d'état).
- Messages centralisés dans `shared/forms/validation-messages.ts` et affichés par un composant unique `<app-field-errors [control]>` sous chaque champ, **après** que le champ a été quitté ou après une tentative d'envoi (`markAllAsTouched()`). Le bouton d'envoi reste actif (un bouton grisé sans explication déroute) mais est désactivé **pendant** l'envoi (pas de double soumission).
- Saisie du **prix** : champ texte `inputmode="decimal"` (la virgule française est acceptée et convertie en point ; `type="number"` se comporte différemment selon les navigateurs), vide → `null`. **Capacité** : `inputmode="numeric"`.

### 2. Erreurs de l'API
- Une fonction unique `toApiError(HttpErrorResponse): ApiError` (`core/errors/api-error.ts`) produit `{ status, code, message, fieldErrors }` :
  - corps `ProblemDetails` → `message = title`, `code`, `fieldErrors = errors` avec les clés `$.capacity` ramenées à `capacity` ;
  - `status = 0` (réseau) → « Impossible de joindre le serveur. Vérifiez votre connexion puis réessayez. » ;
  - 502, 503, 504 → « Le service est momentanément indisponible. Réessayez dans un instant. » ;
  - réponse sans corps exploitable (415, etc.) → message générique « Une erreur est survenue. » et `code = 'unknown'`.
- `applyServerErrors(form, apiError)` : chaque clé de `fieldErrors` qui correspond à un contrôle reçoit `setErrors({ server: messages })` (effacé automatiquement dès que l'utilisateur modifie le champ) ; les autres messages et le `title` s'affichent dans un bandeau en haut du formulaire.
- Traitements par `code` (le front ne lit **jamais** le texte pour décider) :

| `code` | Réaction |
|---|---|
| `validation_failed` | erreurs par champ + bandeau |
| `email_taken` | erreur sur le champ `email` |
| `invalid_credentials`, `account_locked` | bandeau du formulaire de connexion |
| `not_found` (page de détail) | page « Contenu introuvable » |
| `forbidden` | message « Accès réservé aux administrateurs » |
| `concurrent_update` | dialogue (texte = `title`) avec « Recharger la fiche » ; voir § 4 |
| `registrants_warning` | dialogue de confirmation, puis renvoi ; voir § 4 |
| `event_cancelled`, `event_ended`, `event_started`, `event_state_changed`, `event_already_published`, `event_has_active_reservations`, `capacity_below_reserved` | bandeau avec `title`, puis rechargement de la fiche (l'état a changé côté serveur) |
| `unauthorized` | géré par l'intercepteur (ADR 0015) |
| autre / `internal_error` | bandeau avec `title` |

- Pas d'intercepteur global d'erreurs qui afficherait des *snackbars* partout : chaque écran sait mieux où placer le message. Seul le 401 est global (ADR 0015).

### 3. Dates (ADR 0003, précisé)
- **Affichage** : `DatePipe` avec `DATE_PIPE_DEFAULT_OPTIONS = { timezone: '+0100' }` et `LOCALE_ID = 'fr'`, configurés une fois dans `app.config.ts`. Deux formats nommés dans `core/time/date-formats.ts` : liste `EEE d MMM y, HH:mm` (« sam. 5 déc. 2026, 10:00 ») et détail `EEEE d MMMM y 'à' HH:mm` (« samedi 5 décembre 2026 à 10:00 »). Formats à confirmer (UX-4).
- **Saisie** : `<input type="datetime-local">` (sélecteur natif, adapté au mobile). Deux fonctions pures dans `core/time/tunis-time.ts`, qui n'utilisent **jamais** le fuseau du navigateur :
  - `toTunisIso('2026-12-05T10:00')` → `'2026-12-05T10:00+01:00'` (envoi) ;
  - `toTunisLocalInput('2026-12-05T09:00:00+00:00')` → `'2026-12-05T10:00'` (préremplissage : instant + 1 h, formaté avec les getters UTC).
  La constante `TUNIS_UTC_OFFSET` (`+01:00`, 60 min) est unique. Libellé des champs : « Début (heure de Tunis) ».
- Le front **ne recalcule aucune règle de date** (commencé, terminé, réservable) avec l'horloge du poste : il affiche les booléens fournis par l'API (`hasStarted`, `hasEnded`, `isBookable`, `isFull`).

### 4. Version et flux 409
- La `version` d'une fiche est gardée dans un **signal du composant**, pas dans un contrôle du formulaire (l'utilisateur ne la modifie pas). Elle est remplacée par celle de **chaque** réponse qui renvoie la fiche : `PUT` (200), publication (`event` de la réponse), dépublication, `PUT`/`DELETE` du numéro de contact. Oublier ce remplacement provoquerait un faux `concurrent_update` à l'enregistrement suivant.
- `registrants_warning` : dialogue « Confirmer » / « Annuler » avec le `title` de l'API ; sur « Confirmer », renvoi de la **même** requête (même `version`) avec `confirmRegistrantsWarning: true`. Le drapeau n'est jamais envoyé d'office.
- `concurrent_update` : dialogue avec le `title`, bouton « Recharger la fiche » qui relit `GET` et remplace le formulaire (la saisie en cours est perdue, ce que dit le message de l'API) ; bouton « Fermer » qui laisse la saisie affichée pour la recopier. *Choix par défaut, question UX-6.*
- Actions (Publier, Dépublier, Supprimer) : désactivées tant que le formulaire contient des modifications non enregistrées (`form.dirty`), avec l'indication « Enregistrez d'abord vos modifications ». Supprimer et retirer le numéro demandent une confirmation (dialogue).
- Avertissement non bloquant `contact_not_configured` (réponse 200 de publication) : message persistant avec un lien vers `/admin/parametres`.

### 5. Prix, mentions, pagination
- Pipe pur `tndPrice` : `null` → « Gratuit » ; entier → « 15 TND, à régler sur place » ; sinon 3 décimales « 12,500 TND, à régler sur place » (`formatNumber` avec la locale `fr` : séparateur de milliers « 99 999,999 »).
- Fonction pure `eventMention(dto)` dans l'ordre fixé par api.md : Annulé > Terminé > Réservations closes > Complet > « N places restantes » (« 1 place restante » au singulier).
- Pagination (ADR 0011) : numéro de page **dans l'URL** (`/evenements?page=2`), lu par `input()` ; valeur absente, non numérique ou < 1 → page 1 (le front n'envoie jamais un paramètre qui provoquerait un 400). Composant `<app-pager>` maison : « Précédent », « Page 2 sur 3 », « Suivant », masqué s'il n'y a qu'une page ; page au-delà de la dernière (`items` vide, `totalCount` > 0) → message et lien vers la page 1.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Signal Forms (stables en v22) | Voir ADR 0014 : la stack impose les reactive forms. |
| Template-driven forms (`ngModel`) | Moins testables, validation dispersée dans le HTML. |
| Bibliothèque de dates (date-fns-tz, Luxon) | Un décalage fixe d'une heure se traite en deux fonctions ; `Intl.DateTimeFormat` avec `timeZone: 'Africa/Tunis'` reste le plan B si la Tunisie réintroduisait l'heure d'été (ADR 0003). |
| `MatDatepicker` + champ heure séparé | Deux champs à recombiner, plus de code ; le champ natif suffit et s'adapte au mobile. |
| Version dans un contrôle caché du formulaire | Serait réinitialisée par `form.reset()` ou écrasée par erreur ; un signal dédié est explicite. |
| Envoyer d'office `confirmRegistrantsWarning: true` | Contournerait la règle R12 (l'admin doit voir l'avertissement). |
| `mat-paginator` | Sélecteur de taille de page et textes anglais à traduire, inutiles ici ; trois boutons suffisent. |

## Conséquences

- Les messages du front et de l'API doivent rester identiques : la liste des messages de `regles-metier.md` et `api.md` est la source ; une modification d'un côté se reporte de l'autre dans la même tâche.
- Les secondes d'une date ne sont pas éditables (champ à la minute) : un événement créé hors du front avec des secondes verrait son début « modifié » au premier enregistrement (et refusé s'il a commencé). Risque faible, à tester.
- Tous les comportements ci-dessus sont des fonctions pures ou de petits composants : testables sans navigateur avec Vitest.

## Reste à valider par le porteur

- Formats de date (UX-4), comportement sur conflit (UX-6), blocage des actions quand le formulaire est modifié (UX-7).
