# ADR 0021 — Réservations : routes de l'API et données personnelles des inscrits

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 24/11/2026
- **Mise en œuvre** : tranche « Réservations », tâches A et B (`plan-reservations.md`)
- **Références** : cahier § 6.2 (« un membre n'accède jamais aux réservations d'un autre »), § 6.4, RG-10 ; mvp US-C2, US-D2, US-D4, US-E4, US-E6, US-E7, R8, R24 ; ADR 0001 (ports), 0007 (suppression de compte), 0011 (listes), 0013 (numéro de contact)

## Contexte

La tranche expose pour la première fois des **données personnelles d'un membre à un autre utilisateur** : l'admin voit le prénom, le nom, le téléphone et l'e-mail des inscrits (US-E4). Ces données vivent dans `AspNetUsers` (`AppUser`, Infrastructure), qu'Application ne voit pas (ADR 0001). Il faut aussi choisir des routes qui rendent impossible, par construction, l'accès d'un membre aux réservations d'un autre, et savoir comment le détail **public** d'un événement (anonyme, ADR 0009) affiche « ma demande ».

## Décision

### 1. Deux familles de routes, deux politiques

- **`/api/me/...`** (politique par défaut : connecté, membre ou admin) : l'identifiant du membre vient **toujours du jeton** (`sub`), jamais de l'URL ni du corps. Une réservation d'un autre membre → 404 `not_found`.
- **`/api/admin/...`** (`AdminOnly`) : seul endroit où apparaissent les coordonnées d'un autre compte.

Le détail public `GET /api/events/{id}` reste **anonyme et identique pour tous** ; « ma demande » passe par un appel séparé `GET /api/me/events/{eventId}/reservation` (200 avec la réservation la plus récente du membre pour cet événement, ou **204** s'il n'en a aucune). Ainsi la réponse publique ne dépend jamais de l'appelant (pas de risque de cache partagé, pas de jeton expiré qui change la réponse).

Le numéro de contact n'est **pas recopié** dans les réponses de réservation : le front le lit avec `GET /api/settings/contact` (ADR 0013 : réservé aux connectés, un changement de numéro vaut pour les demandes anciennes).

### 2. Ce que voit qui

| Donnée | Membre (sa réservation) | Admin (liste des inscrits, demandes à traiter) | Visiteur |
|---|---|---|---|
| Statut effectif, dates (demande, échéance, décision, annulation), motif de refus | oui | oui | non |
| Mention « Payée », date | oui | oui | non |
| Admin qui a marqué « payée » | **non** | prénom et nom (R13) ; « administrateur supprimé » si `NULL` | non |
| Prénom, nom, téléphone, e-mail du membre | (les siens, via « Mon compte ») | **oui** (US-E4 : finalité = joindre la personne avant de confirmer) ; « Compte supprimé » si `UserId` est `NULL` | non |
| Numéro de contact de l'association | oui | oui | non (RG-10) |
| Nombre de places restantes | oui | oui | oui (R7) |

Rien d'autre : pas de date de création ni de dernière connexion du membre, pas de consentement, pas d'historique de ses autres réservations dans la vue admin d'un événement.

### 3. Lecture des coordonnées : port `IIdentityService` élargi

`IIdentityService.GetContactsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken)` → `IReadOnlyDictionary<Guid, MemberContact>` (`Id`, `FirstName`, `LastName`, `Email`, `PhoneNumber`). **Une requête** par écran (`WHERE Id IN (...)`), après la requête des réservations : pas de N+1, même schéma que `EventOccupancyReader.GetManyAsync` (ADR 0011). Implémentation dans `IdentityService` (Infrastructure), projection `AsNoTracking`.

### 4. Hygiène des réponses et des journaux

- En-tête **`Cache-Control: no-store`** sur toutes les réponses de `/api/me/...` et `/api/admin/...` (filtre MVC appliqué aux contrôleurs concernés) : sur un ordinateur partagé (local associatif), le navigateur ne garde pas la liste des inscrits sur disque.
- Le front ne stocke **aucune** de ces données hors mémoire (pas de `localStorage`, ADR 0015).
- Journaux : aucun e-mail, téléphone, nom ni motif de refus (texte libre) ; seuls des GUID et des nombres (ADR 0002, 0007). Le motif de refus est saisi avec le rappel « Ne mettez pas de données personnelles dans le motif » (risque résiduel accepté par l'ADR 0007).
- Pas d'export (CSV, impression dédiée) au MVP : la liste à l'écran suffit (hors périmètre).

### 5. Pagination

- `GET /api/me/reservations?scope=current|history&page=&pageSize=` : `PagedResult<MyReservationDto>` (ADR 0011, 20 par défaut). `current` = active et événement non terminé, triée par début croissant ; `history` = tout le reste, par début décroissant. Le front affiche deux sections (US-D2).
- `GET /api/admin/reservations/pending?page=&pageSize=` : demandes non échues de tous les événements, par échéance croissante (US-E6) ; `totalCount` sert de compteur dans le menu admin.
- `GET /api/admin/events/{eventId}/reservations` : **non paginée** (en attente, confirmées, autres), bornée en pratique par la capacité ; repli vers la pagination de l'ADR 0011 si un événement dépassait quelques centaines de lignes.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| `GET /api/reservations?userId=` | L'identifiant dans l'URL invite à l'erreur d'autorisation (IDOR) ; `/api/me` la rend impossible. |
| Ajouter « ma réservation » au détail public quand un jeton est présent | Réponse variable selon l'appelant sur une route anonyme ; complexifie le cache et le cas « jeton expiré ». |
| Dénormaliser nom et téléphone dans `Reservations` | Interdit par l'ADR 0007 (effacement incomplet à la suppression du compte) et périmé si le membre change de numéro. |
| Mapper `AppUser` dans `IAppDbContext` | Application dépendrait d'un type d'Identity (Infrastructure) ; le port existe déjà. |
| Vue SQL des inscrits | Une vue de plus à migrer pour une jointure triviale. |
| Masquer partiellement le téléphone (`+216 20 *** 456`) | L'admin doit appeler la personne (R5) : la donnée entière est nécessaire à la finalité. |

## Conséquences

- La règle « un membre ne voit que ses réservations » est testée par construction (aucune route ne prend un `userId`) et par un test d'intégration (membre B sur la réservation de A → 404).
- `IIdentityService` gagne une méthode (doublure à compléter dans `Aaas.Application.Tests`).
- La politique de confidentialité (`fonctionnel/politique-confidentialite.md`) doit mentionner que les administrateurs voient ces coordonnées pour traiter les demandes (à confirmer par l'`expert-fonctionnel`).
