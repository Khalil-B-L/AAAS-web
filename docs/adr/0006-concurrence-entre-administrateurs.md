# ADR 0006 — Concurrence entre administrateurs (et avec le membre)

- **Statut** : Accepté (porteur, 24/11/2026)
- **Date** : 10/11/2026
- **Références** : mvp US-E6 (« déjà traitée par un autre admin »), US-E2, US-E8, US-D3, US-D5

## Contexte

Deux administrateurs (1 à 3 bénévoles, Q11) peuvent traiter la même demande en même temps : l'un confirme pendant que l'autre refuse. Une demande peut aussi être annulée par le membre ou expirer pendant qu'un admin la confirme. Pour les fiches (événement, actualité, numéro de contact), deux admins peuvent modifier la même fiche et le dernier écraserait le travail du premier sans le savoir.

Le scénario attendu : « l'action est refusée et la liste se met à jour avec le statut actuel ».

## Décision

**Concurrence optimiste avec une colonne `rowversion`** sur `Reservation`, `Event`, `News` et `AssociationSettings` (`builder.Property(x => x.RowVersion).IsRowVersion()`).

1. **Transitions de statut** (confirmer, refuser, annuler, marquer payée) : la requête porte une **intention** (« confirmer la réservation X »). Le serveur charge la réservation, la règle du Domain vérifie que la transition est permise depuis le statut **actuel**, puis enregistre. Si une autre écriture est passée entre la lecture et l'écriture, EF lève `DbUpdateConcurrencyException`.
   - Statut déjà changé (lu après coup) → `409` avec `code = "reservation_state_changed"` et le **statut actuel** dans la réponse ;
   - conflit `rowversion` → le serveur relit la réservation et renvoie la même réponse 409.
   Le front rafraîchit la liste à la réception de ce code. Le client n'a pas besoin d'envoyer la version : la règle de transition suffit à exprimer l'intention.
2. **Modification d'une fiche** (événement, actualité, paramètres) : le DTO de lecture expose `version` (le `rowversion` encodé en Base64) ; le DTO de modification le renvoie ; EF compare la valeur d'origine. Conflit → `409` avec `code = "concurrent_update"` et le message « Cette fiche a été modifiée par un autre administrateur. Rechargez-la avant de l'enregistrer. »
3. Les mises à jour en masse (`ExecuteUpdate` de l'expiration et de l'annulation d'événement) filtrent sur le statut (`WHERE Status IN (...)`) et modifient de fait le `rowversion` : une écriture concurrente par entité chargée échouera proprement.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Mise à jour conditionnelle `UPDATE ... WHERE Id = @id AND Status = Pending` et contrôle du nombre de lignes | Atomique et simple, mais la règle de transition quitte le Domain pour du SQL : contraire au principe « règles métier dans le Domain ». |
| Verrou pessimiste sur la réservation | Bloque inutilement pour un conflit rare ; le verrou par événement (ADR 0004) est réservé à la capacité. |
| « Le dernier gagne » | Inacceptable : une demande refusée pourrait repasser Confirmée après coup, et une modification de fiche serait perdue en silence. |
| `ConcurrencyStamp` (Guid géré par l'application) | Équivalent mais à maintenir à la main ; `rowversion` est géré par SQL Server. |
| ETag + `If-Match` en en-tête HTTP | Plus « REST », mais plus lourd à manipuler côté Angular ; le champ `version` dans le DTO suffit et reste lisible. |

## Conséquences

- Aucune action n'est perdue silencieusement ; le second admin est informé et voit l'état réel.
- Le Domain expose des transitions explicites (`Confirm`, `Refuse`, `CancelByUser`, `CancelByAssociation`, `MarkPaid`, `UnmarkPaid`) qui lèvent une `BusinessRuleException` si le statut ne le permet pas (table des transitions du § 2 de `mvp.md`).
- Tests : deux confirmations / un refus parallèles sur la même demande (une seule réussit, l'autre reçoit 409 avec le statut actuel) ; confirmation contre annulation par le membre ; deux modifications d'un événement avec la même `version`.
