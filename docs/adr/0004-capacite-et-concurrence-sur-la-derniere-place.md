# ADR 0004 — Ne jamais dépasser la capacité en cas de demandes simultanées

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 10/11/2026
- **Références** : cahier RG-02, RG-03, RG-06, RG-07, risque § 11 ; mvp US-D1, US-E2, US-E3, R4, R11

## Contexte

Places prises = demandes En attente **non échues** + Confirmées. Si deux membres demandent la dernière place au même instant, deux requêtes peuvent lire « 1 place restante » puis insérer chacune une réservation : la capacité est dépassée. Le même risque existe entre une demande et une réduction de capacité (RG-06), ou une demande et l'annulation de l'événement (RG-07). Le nombre de places prises est **calculé** (pas de compteur stocké), parce que l'expiration (ADR 0005) fait sortir des demandes du décompte sans écriture.

## Décision

**Verrou pessimiste par événement** : toute opération qui peut **augmenter** le nombre de places prises ou qui dépend de ce nombre s'exécute dans une transaction qui commence par verrouiller la ligne de l'événement.

```sql
SELECT * FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = @id
```

Déroulé de la demande de place (US-D1), dans une seule transaction (niveau Read Committed) :
1. verrou `UPDLOCK` sur l'événement (port `IEventLock`, implémenté dans Infrastructure avec `FromSql`) ;
2. passage en Expirée des demandes échues de cet événement (ADR 0005) ;
3. comptage des réservations actives et vérification de l'existence d'une réservation active du membre ;
4. appel de la règle du Domain (`Event.EnsureCanAcceptRequest(now, activeCount)`, `Reservation.Request(...)`) ;
5. insertion et validation de la transaction, qui libère le verrou.

Les opérations concernées prennent le même verrou, **toujours en premier** (ordre constant = pas d'interblocage) : demande de place, modification de la capacité, dépublication, annulation de l'événement. Les opérations qui ne font que **libérer** une place (annulation par le membre, refus, expiration) n'en ont pas besoin.

**Filet de sécurité en base** pour RG-03 : index unique filtré
`UX_Reservations_Event_User_Active ON Reservations(EventId, UserId) WHERE Status IN (1, 2)` (En attente, Confirmée). Une violation est traduite en 409 « Vous avez déjà une demande en cours ou une réservation pour cet événement ».

Points d'implémentation :
- La transaction doit être exécutée via `Database.CreateExecutionStrategy().ExecuteAsync(...)`, sinon `EnableRetryOnFailure` (recommandé pour Azure SQL) lève une exception avec une transaction utilisateur.
- Azure SQL active `READ_COMMITTED_SNAPSHOT` par défaut : `UPDLOCK` bloque quand même les autres `UPDLOCK`, et le comptage, exécuté **après** l'obtention du verrou, voit les insertions validées par la transaction précédente. Les tests d'intégration doivent tourner avec ce réglage activé pour être représentatifs.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Concurrence optimiste : `rowversion` sur `Event` et « toucher » l'événement à chaque demande | Fonctionne, reste dans l'idiome EF. Mais il faut modifier artificiellement l'événement à chaque demande et rejouer la requête en cas de conflit (boucle de retry), ce qui est plus complexe à expliquer et à tester. Alternative de repli si le verrou pose problème. |
| Transaction `Serializable` seule | Le comptage pose des verrous de plage partagés ; deux transactions qui veulent ensuite insérer s'interbloquent et SQL Server en tue une : on se sert d'un interblocage comme mécanisme normal, avec retry obligatoire. |
| Compteur `ReservedSeats` sur `Event` + `UPDATE ... WHERE ReservedSeats < Capacity` | Très efficace, garanti par la base, mais le compteur doit être tenu à jour dans les six transitions et ne voit pas les expirations calculées à la lecture : risque de dérive. |
| `lock` ou `SemaphoreSlim` en mémoire | Ne protège qu'une instance de l'API ; faux dès qu'il y a deux instances ou un redémarrage pendant la requête. |
| Contrainte CHECK avec fonction scalaire comptant les réservations | Fragile (comportement des fonctions dans les CHECK sous concurrence) et performance médiocre. |

## Conséquences

- Les demandes sur **un même événement** sont sérialisées (attente de quelques millisecondes) ; les autres événements ne sont pas affectés. Volume associatif : sans impact.
- Garantie testable : test d'intégration qui lance N demandes en parallèle sur un événement à 1 place (une seule 201, les autres 409 « Événement complet », et en base le nombre de réservations actives ≤ capacité), et test de RG-06 en parallèle avec une demande.
- Un seul endroit (le port `IEventLock`) contient du SQL spécifique à SQL Server.
