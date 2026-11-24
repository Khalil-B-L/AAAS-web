# ADR 0007 — Suppression de compte et anonymisation de l'historique

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 10/11/2026 — mis à jour le 10/11/2026 avec les données du compte décidées par le porteur (cahier v1.1)
- **Références** : cahier § 6.4, RG-09 ; mvp US-F2, R13, R20, R23, R24, R25, Q16
- **Mise en œuvre** : prévue à l'étape 9 (protection des données). Les colonnes concernées existent depuis l'étape 1 (migration `InitialIdentity`).

## Contexte

La suppression du compte est immédiate et définitive. Les réservations à venir (En attente ou Confirmées) passent à « Annulée par l'utilisateur » ; l'historique est conservé de façon anonyme (statistiques de fréquentation) ; la mention « Payée » reste visible pour un éventuel remboursement ; l'e-mail peut être réutilisé pour un nouveau compte vierge ; le dernier administrateur ne peut pas supprimer son compte. Cadre légal : loi organique tunisienne n° 2004-63.

Depuis le cahier v1.1, le compte contient aussi la **date de création**, la **date de dernière connexion** et la **version de la politique de confidentialité acceptée** (avec la date du consentement). US-F2 exige que ces données soient effacées comme les autres.

## Décision

**Suppression physique de l'utilisateur, après détachement de ses réservations** (`Reservation.UserId` devient `NULL`).

Dans une seule transaction (`AccountService.DeleteMyAccount`, exécutée dans la *execution strategy* d'EF) :
1. refuser si l'utilisateur est le dernier membre du rôle `Admin` (RG-09) → 409 ;
2. passer en Expirée ses demandes échues (ADR 0005), puis passer en « Annulée par l'utilisateur » ses réservations actives d'événements non commencés (`ExecuteUpdate` filtré sur le statut) ;
3. mettre à `NULL` `Reservations.UserId` de toutes ses réservations, et `Reservations.PaidByAdminId` s'il était admin ;
4. supprimer l'utilisateur via `UserManager.DeleteAsync` (les tables Identity liées — rôles, claims, jetons — sont supprimées en cascade).

Le front supprime le jeton ; de toute façon, l'API rejette tout jeton existant puisque l'utilisateur n'existe plus (ADR 0002).

### Périmètre de l'effacement

Toutes les données du compte sont des colonnes de la ligne `AspNetUsers` : la suppression de la ligne les efface **toutes d'un coup**, sans liste à tenir à jour.

| Donnée | Colonne | Effacée par |
|---|---|---|
| Prénom, nom | `FirstName`, `LastName` | suppression de la ligne |
| E-mail | `Email`, `NormalizedEmail`, `UserName`, `NormalizedUserName` | idem |
| Téléphone | `PhoneNumber` | idem |
| Mot de passe haché, security stamp, blocage | `PasswordHash`, `SecurityStamp`, `LockoutEnd`, `AccessFailedCount` | idem |
| **Date de création du compte** | `CreatedAt` | idem |
| **Date de dernière connexion** | `LastLoginAt` | idem |
| **Consentement : date et version de la politique** | `PrivacyConsentAt`, `PrivacyPolicyVersion` | idem |
| Rôles | `AspNetUserRoles` | cascade |
| Lien avec les réservations | `Reservations.UserId`, `Reservations.PaidByAdminId` | mis à `NULL` (étape 3) |

Règle de conception qui en découle : **aucune de ces données ne doit être recopiée ailleurs** (pas de nom ou de téléphone dénormalisé dans `Reservations`, pas de table d'audit des connexions, pas de journal contenant e-mail, téléphone ou nom). Une nouvelle donnée personnelle ajoutée plus tard doit l'être sur `AppUser`, ou bien prévoir explicitement son effacement ici.

Schéma : `Reservations.UserId` et `Reservations.PaidByAdminId` sont des clés étrangères **nullables** avec `ON DELETE NO ACTION` ; le détachement est fait explicitement par l'application. (SQL Server refuse deux `ON DELETE SET NULL` d'une même table vers `AspNetUsers` : « multiple cascade paths ».) Une réservation sans utilisateur s'affiche « Compte supprimé » ; une mention payée sans admin s'affiche « Payée le {date} par un administrateur supprimé » (libellé retenu dans `docs/fonctionnel/libelles-ecrans.md`).

Une réservation anonymisée ne contient plus aucune donnée personnelle : il reste l'événement, le statut, les dates de la réservation, le motif de refus et la mention payée.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Suppression logique (`IsDeleted`) et effacement des champs du compte | La ligne Identity subsiste (hachage du mot de passe, security stamp, `UserName` obligatoire à remplacer par une valeur factice), chaque requête doit filtrer les comptes supprimés, et il faut penser à vider chaque colonne (dont les nouvelles `CreatedAt`, `LastLoginAt`, `PrivacyPolicyVersion`, non nullables pour deux d'entre elles) : risque élevé d'oublier une donnée. |
| Remplacer `UserId` par un utilisateur fictif « Compte supprimé » | Évite une colonne nullable, mais crée un faux compte à protéger (pas de connexion, pas de rôle) et à exclure partout. |
| Supprimer aussi les réservations | Perd les statistiques de fréquentation et le suivi des réservations payées, contraire à US-F2 et R13. |
| Pseudonymisation (hachage de l'Id) | Permettrait de relier les réservations d'une même personne disparue : ce n'est plus une anonymisation, et ce n'est pas demandé. |
| Conserver une trace du consentement après suppression (preuve) | Contraire à US-F2, qui demande l'effacement du consentement ; une fois le compte supprimé, il n'y a plus de traitement à justifier. |

## Conséquences

- Le motif de refus est saisi librement par un admin : il ne doit pas contenir de données personnelles (rappel dans l'interface et dans le guide admin). Risque résiduel accepté.
- Les journaux applicatifs ne doivent jamais contenir d'e-mail, de téléphone ou de nom (on journalise les `Guid`) : sinon la suppression serait incomplète. L'étape 1 respecte cette règle (`DatabaseInitializer` ne journalise pas l'e-mail du premier admin).
- Les sauvegardes automatiques d'Azure SQL conservent les données supprimées jusqu'à leur expiration (7 jours par défaut) : à mentionner dans la politique de confidentialité.
- Réinscription avec le même e-mail : possible puisque la ligne a disparu (index unique sur `NormalizedEmail`). Le nouveau compte reçoit une nouvelle `CreatedAt` et la version **en vigueur** de la politique.
- Le même service servira à la purge des comptes inactifs (R25, « plus tard »), qui sélectionnera les comptes dont `LastLoginAt` (à défaut `CreatedAt`) a plus de 3 ans.

## Reste à valider par le porteur

- Le principe de suppression physique et le périmètre de l'effacement ci-dessus.
- La mention des sauvegardes Azure (7 jours) dans la politique de confidentialité (texte à rédiger par l'association, Q13).
