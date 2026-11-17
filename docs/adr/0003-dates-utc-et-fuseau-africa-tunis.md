# ADR 0003 — Dates stockées en UTC, saisies et affichées à l'heure de Tunis

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 10/11/2026 — mis à jour le 10/11/2026 après l'implémentation de l'étape 1
- **Mise en œuvre** : étape 1 pour la partie serveur (convention EF, `TimeProvider`, dates du compte : `CreatedAt`, `LastLoginAt`, `PrivacyConsentAt`, et `LockoutEnd` d'Identity) ; partie front en S2
- **Références** : cahier § 6.1 ; mvp R26, US-D1, US-D5, US-D3

## Contexte

Les dates sont stockées en UTC (consigne du projet) et saisies et affichées à l'heure de Tunis (`Africa/Tunis`, UTC+1 sans changement d'heure depuis 2009). Les règles métier sont des **durées** (48 h, 24 h) ou des **comparaisons** avec le début et la fin d'un événement. Un admin peut saisir un événement depuis un navigateur réglé sur un autre fuseau (France par exemple).

Piège classique : un `DateTime` relu par EF Core a `Kind = Unspecified`, il est sérialisé sans `Z` et le navigateur l'interprète comme une heure locale.

## Décision

1. **Type** : `DateTimeOffset` partout dans le code et `datetimeoffset` en base, **toujours avec un décalage nul** (le Domain normalise avec `ToUniversalTime()`). Une convention EF (`ConfigureConventions` de `AppDbContext`, convertisseur `UtcDateTimeOffsetConverter`) ramène à UTC toutes les propriétés `DateTimeOffset` et `DateTimeOffset?`, y compris celles d'Identity (`LockoutEnd`).
2. **Horloge** : `TimeProvider` injecté partout (`TimeProvider.System` en production, `FakeTimeProvider` dans les tests). Aucun `DateTime.Now` ou `DateTimeOffset.UtcNow` dans le code métier.
3. **Le back-end ne fait aucune conversion de fuseau** : toutes les règles (échéance = min(demande + 48 h, début), annulation jusqu'à début − 24 h, événement commencé, terminé) se calculent en UTC, ce qui est exact puisque ce sont des durées et des comparaisons d'instants. Les messages d'erreur ne contiennent pas d'heure ; l'heure affichée (« avant le 12 mars à 10 h ») est formatée par le front à partir du champ `expiresAt` renvoyé.
4. **Contrat d'API** : chaînes ISO 8601 avec décalage. En sortie, toujours en UTC (`2026-03-12T09:00:00+00:00`). En entrée, l'API exige un décalage explicite et refuse une date sans décalage (400).
5. **Front** :
   - affichage : `DatePipe` d'Angular configuré une fois pour toutes via `DATE_PIPE_DEFAULT_OPTIONS` (`timezone: '+0100'`) et `LOCALE_ID = 'fr'` ;
   - saisie : le formulaire admin utilise un champ date-heure dont la valeur est **interprétée comme heure de Tunis**, quelle que soit la configuration du navigateur, puis envoyée avec `+01:00` (fonction utilitaire unique `toTunisIso()`).
   - Le décalage `+01:00` est une constante unique `TUNIS_UTC_OFFSET` dans le front.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| `DateTime` UTC + `ValueConverter` qui force `Kind = Utc` | Fonctionne, mais repose sur une convention facile à oublier (requêtes brutes, projections) ; `TimeProvider.GetUtcNow()` renvoie déjà un `DateTimeOffset`. |
| Stocker l'heure locale de Tunis | Contraire à la consigne ; comparaisons fausses avec l'horloge serveur (UTC sur Azure). |
| Convertir en heure de Tunis côté API (`TimeZoneInfo.FindSystemTimeZoneById("Africa/Tunis")`) | Inutile pour les règles, et dépend de la base de fuseaux de l'OS : souvent absente des images Docker minimales. |
| Utiliser le fuseau du navigateur pour l'affichage | Un membre ou un admin en déplacement verrait de fausses heures ; l'heure de référence est celle de Tunis (R26). |

## Conséquences

- Si la Tunisie réintroduisait l'heure d'été, une seule constante est à modifier côté front (ou passer à `Intl.DateTimeFormat` avec `timeZone: 'Africa/Tunis'`) ; le back-end n'est pas touché.
- Les tests de règles de date utilisent `FakeTimeProvider` et des instants UTC exprimés en commentaire en heure de Tunis (ex. « 10 mars 10 h Tunis = 09:00Z »).
- Tests aux limites exigés : échéance atteinte exactement, annulation à exactement 24 h.
- Le message de blocage de connexion (« réessayez dans N minutes », ADR 0002) contient une **durée**, pas une heure : il respecte le point 3.

## Reste à valider par le porteur

- Les choix ci-dessus (aucune question fonctionnelle en suspens sur cet ADR ; la partie front sera vérifiée en S2).
