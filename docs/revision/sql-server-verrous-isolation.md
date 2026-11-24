# Fiche de révision — SQL Server : verrous, isolation, index filtré (la « dernière place »)

> À relire la veille d'un entretien. Exemples tirés de la tranche « Réservations » (tâche A) du projet AAAS : 20 membres demandent la dernière place en même temps, un seul doit l'obtenir (RG-02). Complète `ef-core-concurrence.md` (rowversion, `UPDLOCK` côté EF, stratégie de reprise).

## 1. Le problème : « compter puis insérer »

```
T1 : SELECT COUNT(*) actives → 19 (capacité 20)        T2 : SELECT COUNT(*) actives → 19
T1 : INSERT (20e place)                                 T2 : INSERT (21e place !)
```

Deux lectures **avant** les deux écritures : chacune croit qu'il reste une place. Une contrainte `CHECK` ne peut pas l'empêcher (elle ne voit qu'une ligne, pas un `COUNT`). Il faut **sérialiser** les demandes d'un même événement.

## 2. Les verrous en 5 minutes

| Verrou | Pris par | Compatible avec | Image |
|---|---|---|---|
| **S** (partagé) | une lecture en Read Committed **sans** RCSI | S, U | « je lis, ne modifiez pas pendant ma lecture » |
| **U** (mise à jour) | `SELECT … WITH (UPDLOCK)`, ou la recherche de lignes d'un `UPDATE` | S seulement | « je lis **avec l'intention** d'écrire ; un seul à la fois » |
| **X** (exclusif) | `INSERT`, `UPDATE`, `DELETE` | rien | « j'écris » |

- **U n'est pas compatible avec U** : deux transactions qui demandent `UPDLOCK` sur la même ligne **font la queue**. C'est exactement ce qu'il faut : la seconde demande attend que la première ait inséré et validé.
- **`ROWLOCK`** : verrou de **ligne** plutôt que de page, pour ne pas bloquer les événements voisins.
- Le verrou dure **jusqu'au `COMMIT`** : il faut donc une **transaction explicite** autour de « verrouiller → compter → insérer ».
- **Interblocage** (*deadlock*, erreur **1205**) : A attend B qui attend A ; SQL Server tue l'une des deux. Parade : prendre les verrous **toujours dans le même ordre** (d'abord l'événement). S'il arrive quand même, EF Core le considère comme transitoire et rejoue le bloc (`EnableRetryOnFailure`).

Dans le projet (`src/Aaas.Infrastructure/Persistence/EventLock.cs`) :

```csharp
await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
var ev = await db.Events
    .FromSql($"SELECT * FROM [Events] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {eventId}")   // paramètre SQL, pas de concaténation
    .AsTracking().SingleOrDefaultAsync(ct);
var result = await work(ev, ct);    // lit l'heure, compte les places, insère la demande
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);           // libère le verrou : la demande suivante peut compter
```

Et dans `MemberReservationService.RequestAsync`, **l'heure est lue après le verrou** : une demande qui a attendu 2 s ne décide pas avec une heure périmée.

## 3. Niveaux d'isolation (question classique)

| Niveau | Empêche | Coût |
|---|---|---|
| Read Uncommitted | rien (lectures « sales ») | — |
| **Read Committed** (défaut) | lecture de données non validées | lecteurs et écrivains se bloquent (verrous S) |
| **RCSI** (`READ_COMMITTED_SNAPSHOT ON`, défaut d'**Azure SQL**) | idem, mais un lecteur lit la **dernière version validée au début de l'instruction**, sans verrou S | versions dans tempdb ; les lecteurs ne bloquent jamais |
| Repeatable Read | une ligne relue qui change | verrous S gardés jusqu'à la fin |
| **Serializable** | lignes « fantômes » (un `COUNT` qui change) | verrous de **plage** ; interblocages fréquents sur « compter puis insérer » |
| **Snapshot** | tout, par versions, à l'échelle de la **transaction** | conflits d'écriture levés en erreur |

**Notre choix** : Read Committed (RCSI activé) **+ `UPDLOCK` sur la ligne de l'événement**.

- Le comptage s'exécute **après** l'obtention du verrou. Sous RCSI, il lit la dernière version validée **au début de l'instruction** : il voit donc l'insertion de la demande précédente, validée juste avant que le verrou soit rendu.
- **Piège** : en isolation `SNAPSHOT`, l'instantané daterait du **début de la transaction** (avant l'attente du verrou) : le comptage ne verrait pas l'insertion concurrente. Ne jamais passer ce bloc en `SNAPSHOT`.
- Pourquoi pas `Serializable` ? Il protège le `COUNT` par des verrous de plage, mais deux transactions prennent chacune un verrou S de plage puis veulent insérer dans cette plage : interblocage **comme fonctionnement normal**.
- Les tests d'intégration activent RCSI sur la base de test (`SqlServerFixture`) pour être dans les conditions d'Azure SQL.

## 4. Index unique filtré : le filet en base

```sql
CREATE UNIQUE INDEX UX_Reservations_Event_User_Active
  ON Reservations (EventId, UserId)
  WHERE [Status] IN (1, 2) AND [UserId] IS NOT NULL;
```

- **C'est quoi** : un index qui ne contient que les lignes qui passent le filtre ; `UNIQUE` ne s'applique qu'à elles. Un membre peut avoir dix demandes refusées pour un événement, mais **une seule** en attente ou confirmée (RG-03).
- **EF Core** : `HasIndex(r => new { r.EventId, r.UserId }).IsUnique().HasFilter("[Status] IN (1, 2) AND [UserId] IS NOT NULL")`.
- **Valeurs figées** : changer la valeur numérique de « Confirmée » casserait l'index **sans erreur de compilation** (d'où le commentaire sur `ReservationStatus`).
- **`QUOTED_IDENTIFIER ON` obligatoire** pour écrire dans une table qui a un index filtré : EF et SSMS oui ; `sqlcmd` **seulement avec `-I`** (sinon erreur 1934).
- **Filet, pas règle** : la règle est d'abord vérifiée par le Domain sous verrou. L'index arrête un script manuel ou un bug futur. Sa violation (erreur **2601**) est traduite en `already_reserved` par `EventLock`, **sans** le message SQL (il contient les valeurs dupliquées).
- **Conséquence** : une demande En attente échue mais pas encore écrite « Expirée » occupe toujours l'index. D'où, sous le verrou et **avant** l'insertion, un `UPDATE … SET Status = 4 WHERE EventId = @e AND Status = 1 AND ExpiresAt <= @now` (`ExecuteUpdateAsync`).

**Index couvrant** : `IX_Reservations_EventId_Status (EventId, Status) INCLUDE (ExpiresAt)` : le décompte (`WHERE EventId IN (…) AND (Status = 2 OR (Status = 1 AND ExpiresAt > @now))`) se lit entièrement dans l'index, sans aller chercher la ligne.

## 5. Pessimiste ou optimiste ?

| | Pessimiste (`UPDLOCK`) | Optimiste (`rowversion`) |
|---|---|---|
| Principe | on **empêche** le conflit en faisant attendre | on **détecte** le conflit à l'écriture (0 ligne mise à jour → `DbUpdateConcurrencyException`) |
| Quand | conflit **probable et coûteux** | conflit **rare** |
| Dans AAAS | demande de place, modification et dépublication d'un événement (tout ce qui fait monter le compteur ou en dépend) | annuler, confirmer, refuser (ne font que libérer une place ou changer un statut) |

Règle mnémotechnique (ADR 0019) : **on verrouille ce qui peut faire monter le compteur ou ce qui en dépend ; le reste se contente de `rowversion`.**

## 6. Prouver que le verrou est réellement posé

Un test « 20 requêtes en parallèle » ne prouve la garantie que **statistiquement**. Le test déterministe (`ReservationConcurrencyTests.Request_waits_for_the_event_row_lock`) :

1. une `SqlConnection` de test ouvre une transaction et exécute `SELECT Id FROM Events WITH (UPDLOCK, ROWLOCK) WHERE Id = @id` ;
2. on lance `POST /api/me/reservations` ;
3. au bout d'1 s, la requête **n'est pas terminée** (elle attend notre verrou) ;
4. `COMMIT` de la connexion de test → la requête se termine en 201.

Exercice de révision : retirer `UPDLOCK` de `EventLock` en local et constater que ce test **et** « 20 membres sur la dernière place » échouent.

## 7. Commandes utiles

```sql
-- Qui attend qui (pendant un blocage) :
SELECT session_id, blocking_session_id, wait_type, wait_time, command FROM sys.dm_exec_requests WHERE blocking_session_id <> 0;
-- Verrous posés :
SELECT request_session_id, resource_type, request_mode, request_status FROM sys.dm_tran_locks WHERE resource_database_id = DB_ID();
-- RCSI activé ?
SELECT name, is_read_committed_snapshot_on FROM sys.databases;
-- Index filtrés d'une table :
SELECT name, has_filter, filter_definition FROM sys.indexes WHERE object_id = OBJECT_ID('Reservations');
```

## Questions d'entretien

1. **Comment garantir qu'on ne vend pas plus de places que la capacité ?** — Sérialiser « compter puis insérer » par événement : transaction + `SELECT … WITH (UPDLOCK, ROWLOCK)` sur la ligne de l'événement, comptage après le verrou, puis insertion et `COMMIT`. Une contrainte `CHECK` ne peut pas porter sur un `COUNT`.
2. **Différence entre RCSI et Snapshot ?** — Les deux lisent des versions sans verrou S. RCSI : version validée **au début de chaque instruction** (le niveau reste Read Committed). Snapshot : version **au début de la transaction**, pour toutes les instructions ; un conflit d'écriture lève une erreur (3960).
3. **À quoi sert un index unique filtré ?** — À imposer l'unicité sur un sous-ensemble de lignes (ex. une seule réservation **active** par membre et événement, l'historique restant libre ; ou un e-mail unique seulement s'il n'est pas `NULL`).
4. **Pourquoi pas `Serializable` ?** — Verrous de plage : deux transactions qui comptent puis insèrent dans la même plage s'interbloquent ; on obtient des erreurs 1205 comme fonctionnement normal.

## Exercice (facultatif)

Écrire en SQL pur, dans deux fenêtres SSMS, le scénario du § 1 sans `UPDLOCK` (deux `BEGIN TRAN; SELECT COUNT(*)…;` puis deux `INSERT`) et constater le dépassement ; recommencer avec `WITH (UPDLOCK)` sur la ligne de l'événement et observer la seconde fenêtre qui attend.
