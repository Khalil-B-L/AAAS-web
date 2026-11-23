# ADR 0011 — Listes : pagination, tri et filtres (événements, puis actualités)

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 18/11/2026
- **Mise en œuvre** : étape 2 (événements publics et admin) ; réutilisée à l'étape 8 (actualités, R18)
- **Références** : mvp US-C1, US-B1 (10 par page), R7, R18, R19 ; ADR 0004, ADR 0005, ADR 0009

## Contexte

La liste publique des événements montre les événements à venir, du plus proche au plus lointain, avec les places restantes (R7). La liste admin doit aussi montrer les brouillons et les événements passés. Les actualités (étape 8) exigent 10 éléments par page avec « page suivante ». Volume attendu : quelques dizaines d'événements par an, quelques centaines d'actualités à terme.

Les places restantes dépendent du nombre de réservations actives, **calculé** (pas de compteur stocké, ADR 0004) et qui doit exclure les demandes échues (ADR 0005).

## Décision

1. **Pagination par numéro de page** (offset) : paramètres de requête `page` (≥ 1, défaut 1) et `pageSize` (1 à 50, défaut 20 pour les événements ; 10 pour les actualités). Hors bornes → 400 `validation_failed` sur le paramètre. Une page au-delà de la dernière renvoie `200` avec `items: []`.
2. **Enveloppe commune** `PagedResult<T>` (Application, `Common/`) :
   ```json
   { "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 37 }
   ```
   Le front en déduit le nombre de pages (`Math.ceil(totalCount / pageSize)`).
3. **Tri imposé par le serveur**, toujours complété par `Id` pour un ordre stable entre deux pages :
   - public : `StartsAt` croissant, puis `Id` ;
   - admin, `period=upcoming` : `StartsAt` croissant ; `period=past` ou `all` : `StartsAt` décroissant.
   Pas de paramètre de tri libre (pas de besoin, et un tri libre exige une liste blanche de colonnes).
4. **Filtres** :
   - public : aucun paramètre ; le filtre « visible et à venir » est fixe (`EventQueries.IsListedPublicly(now)`, ADR 0009) ;
   - admin (`GET /api/admin/events`) : `status` (`Draft`, `Published`, `Cancelled`, facultatif) et `period` (`upcoming` = `EndsAt > now`, défaut ; `past` = `EndsAt <= now` ; `all`).
5. **Places restantes sans N+1** : la requête de la page charge les événements (projection), puis **une seule** requête de décompte pour les identifiants de la page (`WHERE EventId IN (...) GROUP BY EventId, Status`), via le service unique `EventOccupancyReader` (Application). Le calcul `remainingSeats`, `isFull`, `isBookable` est fait en C# par le Domain (`Event.RemainingSeats(occupancy)`, `Event.IsBookable(now, occupancy)`) : la règle R7 / R2 reste à un seul endroit. À l'étape 2, la table `Reservations` n'existe pas encore : `EventOccupancyReader` renvoie une occupation nulle pour chaque événement (ADR 0012, § 4).
6. **Index** : `IX_Events_Status_StartsAt (Status, StartsAt)` couvre le filtre par statut et le tri ; le filtre `EndsAt > now` est résiduel, ce qui est sans conséquence au volume attendu.
7. **Lecture sans suivi** : les requêtes de liste et de détail utilisent `AsNoTracking()` et une projection vers le DTO (pas de chargement d'entités complètes).

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Pas de pagination (tout renvoyer) | Suffirait pour les événements à venir, mais la réponse n'est pas bornée (liste admin `all`) et les actualités en ont besoin : autant un seul mécanisme. |
| Pagination par curseur (*keyset*, « après tel `StartsAt`/`Id` ») | Plus performante sur de gros volumes et stable si des lignes sont insérées pendant la navigation, mais pas de « page 3 sur 5 » et un curseur opaque à encoder : sur-ingénierie ici. |
| `totalCount` dans un en-tête HTTP (`X-Total-Count`) | Moins visible, à exposer explicitement dans CORS ; l'enveloppe JSON est plus simple à lire et à tester. |
| Sous-requête corrélée dans la projection (`Capacity - Reservations.Count(...)`) | Une seule requête SQL, mais la règle « active » serait dupliquée dans la projection et le calcul des places quitterait le Domain. La deuxième requête groupée est bornée à la taille de la page. Repli possible si un jour le volume l'exige. |
| Compteur de places stocké sur `Event` | Écarté par l'ADR 0004 (dérive avec l'expiration calculée). |
| OData / bibliothèque de pagination | Dépendance pour deux listes. |

## Conséquences

- Deux requêtes SQL par page (page + décompte) à partir de l'étape 4, une seule à l'étape 2 ; le `totalCount` ajoute un `COUNT(*)` : trois requêtes au plus, toutes indexées.
- Le front Angular (S2) a un seul modèle `PagedResult<T>` et un seul composant de pagination réutilisable pour événements et actualités.
- Tests : tri (deux événements au même `StartsAt` gardent un ordre stable par `Id`), page au-delà de la fin, `pageSize = 51` refusé, filtres admin, brouillon absent de la liste publique, événement terminé absent, événement annulé présent.

## Reste à valider

- Par le porteur : pagination par numéro de page, taille par défaut 20 pour les événements.
