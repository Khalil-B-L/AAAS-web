# ADR 0009 — Événement : cycle de vie, dates et validation des saisies

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 18/11/2026
- **Mise en œuvre** : étape 2 (événements côté admin, consultation publique)
- **Références** : cahier RG-01, § 4.1, § 6.1 ; mvp US-E2, US-C1, US-C2, R2, R14, R19, R26 ; ADR 0003 (dates UTC), ADR 0001 (erreurs)

## Contexte

L'étape 2 crée la première entité métier, `Event`. Il faut fixer :
- les statuts stockés et ceux qui sont **calculés** (commencé, terminé) ;
- ce que veut dire « à venir » pour la liste publique (US-C1, R19) et qui peut voir le détail (US-C2) ;
- comment l'API reçoit les dates saisies en heure de Tunis par un admin dont le navigateur peut être dans un autre fuseau (ADR 0003 pose le principe : décalage explicite obligatoire, sinon 400) ;
- où vivent les validations qui dépendent de l'heure courante (« date de début dans le passé ») tout en renvoyant un 400 par champ, lisible par le formulaire.

## Décision

### 1. Statuts

| Valeur stockée (`tinyint`) | Enum `EventStatus` | Sens |
|---|---|---|
| 1 | `Draft` | Brouillon, invisible du public |
| 2 | `Published` | Publié, visible |
| 3 | `Cancelled` | Annulé, visible avec la mention « Annulé », définitif (étape 7, US-E3) |

Valeurs figées (comme pour les réservations). « Commencé » (`StartsAt <= now`) et « terminé » (`EndsAt <= now`) sont **calculés**, jamais stockés. L'enum contient `Cancelled` dès l'étape 2 parce que les DTO publics doivent savoir l'afficher (US-C1) ; la transition vers `Cancelled` n'arrive qu'à l'étape 7.

Transitions de l'étape 2 (méthodes du Domain qui reçoivent `now`) :

| Méthode | De → vers | Refus (409) |
|---|---|---|
| `Event.Create(...)` | → `Draft` | données invalides : voir § 4 (400) |
| `Publish(now)` | `Draft` → `Published` | `event_state_changed` si pas en brouillon ; `event_cancelled` si annulé ; `event_started` si `StartsAt <= now` |
| `Unpublish(occupancy)` | `Published` → `Draft` | `event_state_changed` si pas publié ; `event_has_active_reservations` si `occupancy.Active > 0` (R14) |
| `Update(..., now, occupancy, confirmWarning)` | inchangé | `event_cancelled`, `event_ended` (`EndsAt <= now`), `capacity_below_reserved` (R11), `registrants_warning` (R12) : détail dans l'ADR 0012 |

### 2. « À venir » et visibilité publique

- **Liste publique** : `Status IN (Published, Cancelled) AND EndsAt > now`, triée par `StartsAt` croissant. Un événement **en cours** reste listé (avec `hasStarted = true`, non réservable) jusqu'à sa fin : un visiteur peut encore y trouver le lieu et l'heure. *Valeur par défaut, à confirmer (question ouverte E2-1).*
- **Détail public** (`GET /api/events/{id}`) : visible si `Status IN (Published, Cancelled)`, **même terminé** ; brouillon ou inexistant → 404 « Contenu introuvable ». Raison : « Mes réservations » (US-D2, étape 6) doit pouvoir pointer vers un événement passé ; R19 (« pas d'archive ») ne vise que la liste. *À confirmer (E2-2).*

### 3. Dates en entrée

- Les DTO d'entrée reçoivent `startsAt` et `endsAt` en **chaîne**, validée par un attribut `[DateTimeWithOffset]` (Application, `Common/Validation`) : format ISO 8601 `yyyy-MM-ddTHH:mm[:ss]` suivi **obligatoirement** de `Z` ou `±HH:mm`. Sans décalage → 400 sur le champ (`errors.startsAt`), message français : « La date doit indiquer son fuseau horaire, par exemple 2026-03-10T10:00+01:00. »
- Le service convertit avec `DateTimeOffset.ParseExact(..., CultureInfo.InvariantCulture)` puis `ToUniversalTime()` ; tout décalage explicite est accepté (le front envoie `+01:00`, un client de test peut envoyer `Z`).
- En sortie, rien ne change (ADR 0003) : `DateTimeOffset` UTC, `2026-03-10T09:00:00+00:00`.
- Aucune conversion de fuseau côté serveur. Le front interprète la saisie du champ `datetime-local` comme heure de Tunis et ajoute `+01:00` (`toTunisIso()`, S2).

### 4. Validation : forme dans le DTO, règles dans le Domain

| Vérification | Où | Résultat |
|---|---|---|
| obligatoire, longueur (titre ≤ 200, lieu ≤ 200, description ≤ 4 000), format de date avec décalage, format de `version` | attributs du DTO (validation automatique `[ApiController]`) | 400 `validation_failed`, erreurs par champ |
| `capacity` entre 1 et 10 000 ; `price` ≥ 0, ≤ 99 999,999, au plus 3 décimales (ADR 0010) ; `endsAt > startsAt` ; `startsAt > now` (à la création, et à la modification **seulement si `startsAt` change**) | **Domain** : `EventRules.Validate(...)` renvoie une liste d'erreurs `(Field, Message)` | Application la convertit en `RequestValidationException` → 400 par champ |
| invariants | Domain : `Create` et `Update` appellent la même `EventRules.Validate` et lèvent `BusinessRuleException(validation_failed)` si on les appelle sans validation préalable | filet ; jamais atteint par l'API |
| règles d'état et d'occupation | méthodes de `Event` | 409 avec `code` |

Les noms de champs (`"startsAt"`, `"capacity"`…) sont des constantes du Domain (`EventFields`), pour que la clé d'erreur corresponde au nom JSON. La règle « début dans le futur » utilise `TimeProvider` (passé par Application sous forme de `now`). Comme pour l'inscription, la validation se fait en deux temps : une requête avec un titre vide **et** une date passée ne signale d'abord que le titre.

### 5. Modification d'un événement commencé

- Terminé (`EndsAt <= now`) → toute modification refusée (`event_ended`).
- Commencé mais pas terminé → modification permise (corriger une description, augmenter la capacité) ; si `startsAt` est modifié, la nouvelle valeur doit être dans le futur.
- Annulé → toute modification refusée (`event_cancelled`) ; cohérent avec US-E3 « un événement annulé ne se réactive pas ».

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| `DateTimeOffset` dans le DTO avec un `JsonConverter` strict | Une `JsonException` interrompt la lecture de tout le corps : erreur sous la clé `$.startsAt`, message non maîtrisé, plus une erreur anglaise « The request field is required », et les autres champs ne sont pas validés. La chaîne + attribut donne une erreur française sur `startsAt`, comme `[TunisianPhoneNumber]` pour le téléphone. |
| Accepter une date sans décalage et la supposer en heure de Tunis | Le serveur ferait une conversion de fuseau (contraire à l'ADR 0003) et une erreur d'outil (date UTC sans `Z`) passerait inaperçue avec une heure d'écart. |
| Toutes les règles dans des attributs de DTO | La règle « début dans le futur » a besoin de l'heure courante (`TimeProvider`) et la règle « fin après début » porte sur deux champs : elles sortiraient du Domain et seraient difficiles à tester avec une horloge simulée. |
| Règles de valeurs en 409 (`BusinessRuleException`) | Le formulaire doit savoir quel champ est en erreur ; une erreur de saisie reste un 400. Le 409 est réservé aux conflits d'état (statut, occupation, concurrence). |
| Liste publique limitée à `StartsAt > now` | Plus littéral (« à venir »), mais l'événement disparaît dès qu'il commence ; gardé comme repli si l'association le préfère (E2-1 : une seule ligne à changer). |
| Stocker un statut `Finished` | Il faudrait une tâche pour l'écrire ; c'est une comparaison d'instants, toujours exacte si on la calcule. |

## Conséquences

- Tests Domain avec des instants fixes (pas d'horloge) : début = maintenant (refusé), début = maintenant + 1 min (accepté), fin = début (refusé), modification d'un événement commencé sans changer le début (acceptée), événement terminé (refusé).
- Les DTO d'entrée ont des dates en `string` : à expliquer en revue (compromis assumé pour des messages d'erreur maîtrisés).
- Le front doit toujours envoyer un décalage ; le formulaire admin d'Angular (S2) concentre la conversion dans `toTunisIso()`.
- Changer la définition de « à venir » ne touche qu'une expression de requête (`EventQueries.IsListedPublicly(now)`).

## Reste à valider

- Par le porteur : le principe (chaîne + attribut, règles de valeurs dans le Domain en 400).
- ~~Par l'`expert-fonctionnel` : E2-1, E2-2, E2-5, E2-6~~ : tranchés, voir ci-dessous.

## Mise à jour du 19/11/2026 (décisions du porteur, cahier v1.2, `mvp.md` v4)

Ajout par l'`expert-technique` lors de l'implémentation, à relire par l'`architecte`.

- **E2-1, E2-2, E2-5, E2-6 confirmés** (Q18, Q19, Q22, Q23 ; RG-12, RG-13, RG-14) : les § 2, 4 et 5 s'appliquent tels quels.
- **Fin toujours dans le futur** (R28, Q28) : `EventRules.Validate` refuse une `endsAt` atteinte ou passée (« La date de fin doit être dans le futur. »), à la création comme à la modification. Sur un événement commencé, c'est la seule protection contre « le terminer » en avançant sa fin.
- **`hasEnded` dans les DTO publics** (liste et détail) : le détail d'un événement terminé reste accessible (§ 2) et le front doit pouvoir afficher « Terminé ». Priorité des mentions : Annulé > Terminé > Réservations closes > Complet > N places restantes.
- **Suppression d'un brouillon jamais publié** (Q26, RG-07, R15) : nouvelle action `Event.EnsureCanBeDeleted()` (`Draft` **et** jamais publié), `DELETE /api/admin/events/{id}` → 204, sinon 409 `event_already_published`. Comme `Unpublish` remet `PublishedAt` à `null`, un marqueur persistant est nécessaire : **colonne `FirstPublishedAt datetimeoffset NULL`**, posée à la première publication et jamais effacée (migration `AddEventsAndAssociationSettings`). Alternatives écartées : ne plus remettre `PublishedAt` à `null` (un brouillon aurait une date de publication, sens trouble pour le front) ; booléen `HasBeenPublished` (la date coûte autant et renseigne davantage). Pas de verrou ni de `version` (aucune réservation possible sur un brouillon jamais publié) ; le `rowversion` arrête la suppression si un autre admin publie au même instant (409 `concurrent_update`). L'étape 7 élargira la règle à « publié sans aucune réservation » (Q29).
- **Dépublier un événement annulé** : `event_cancelled` (plutôt que `event_state_changed`), cohérent avec « action impossible sur un événement annulé ».
