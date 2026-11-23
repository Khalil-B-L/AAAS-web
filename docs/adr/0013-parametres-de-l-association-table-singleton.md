# ADR 0013 — Paramètres de l'association (numéro de contact) : table à une seule ligne

- **Statut** : Proposé (à valider par le porteur)
- **Date** : 18/11/2026
- **Mise en œuvre** : étape 2 (US-E8) ; utilisé à l'étape 4 (US-D4)
- **Références** : cahier RG-10, RG-11 ; mvp US-E8, US-D4, R8, Q9, Q17 ; ADR 0006

## Contexte

L'association a **un seul** numéro de contact (R8, Q9), avec l'indication « joignable sur WhatsApp ». Il est configurable par un admin **sans modifier le code** (RG-10), visible des **membres connectés uniquement**, jamais des visiteurs. Il suit la même règle que le téléphone des membres : « +216 » + 8 chiffres (Q17, valeur par défaut). Un changement de numéro s'applique aussi aux demandes déjà faites (US-E8 « il voit le nouveau numéro ») : le numéro n'est donc **jamais recopié** dans les réservations.

## Décision

1. **Table `AssociationSettings` à une seule ligne** :

   | Colonne | Type | Note |
   |---|---|---|
   | `Id` | `int` PK | toujours 1, `CK_AssociationSettings_Singleton CHECK (Id = 1)` |
   | `ContactPhone` | `nvarchar(12) NULL` | E.164 `+216XXXXXXXX` ; `NULL` = non configuré |
   | `ContactWhatsApp` | `bit NOT NULL` | défaut `0` |
   | `UpdatedAt` | `datetimeoffset NULL` | `NULL` tant que jamais modifié |
   | `RowVersion` | `rowversion` | concurrence entre admins (ADR 0006) |

   La ligne est créée **par la migration** (`HasData`, `Id = 1`, numéro `NULL`) : elle existe toujours, la lecture n'a pas à gérer « ligne absente » et il n'y a jamais d'`INSERT` applicatif. La contrainte `CHECK` empêche une deuxième ligne, même par un script SQL.
2. **Domain** : entité `AssociationSettings` (`Settings/`), `Id` constant `SingletonId = 1`, méthode `UpdateContact(PhoneNumber phone, bool whatsApp, DateTimeOffset now)`. La validation du numéro réutilise `PhoneNumber` (source unique de RG-11). Propriété `IsContactConfigured`.
3. **API** :
   - `GET /api/settings/contact` — **tout membre connecté** (politique par défaut : authentifié) ; anonyme → 401. Réponse `{ phoneNumber, whatsApp, isConfigured, version, updatedAt }`, `phoneNumber` `null` si non configuré.
   - `PUT /api/admin/settings/contact` — politique `AdminOnly`. Corps `{ phoneNumber, whatsApp, version }`. 200 avec la nouvelle valeur ; 400 (format RG-11, via `[TunisianPhoneNumber]`) ; 409 `concurrent_update`.
   - ~~Pas de suppression du numéro au MVP (pas dans US-E8 ; *question E2-7*).~~ Remplacé le 19/11/2026 : retrait possible, `DELETE /api/admin/settings/contact` (voir « Mise à jour » en fin d'ADR).
   - Le **front** décide où l'afficher (après une demande, sur une demande en attente, quand l'annulation n'est plus possible : R8) ; l'API garantit seulement qu'un visiteur ne l'obtient jamais. Liens construits par le front : `tel:+21620123456` et `https://wa.me/21620123456`.
4. **Avertissement à la publication** (US-E8, « numéro non configuré ») : `POST /api/admin/events/{id}/publish` publie quand même et renvoie `warnings: [{ code: "contact_not_configured", message: "Aucun numéro de contact n'est configuré : les membres ne sauront pas qui appeler pour faire confirmer leur demande." }]`. Avertissement **non bloquant**.
5. **Cache** : aucun. Une requête par clé primaire sur une table d'une ligne est négligeable, et un cache mémoire serait faux avec deux instances.

## Alternatives écartées

| Alternative | Raison du rejet |
|---|---|
| Table clé/valeur (`Settings(Key, Value)`) | Extensible sans migration, mais valeurs non typées (`bit` stocké en texte), validation dispersée, pas de contrainte par paramètre. Deux paramètres seulement au MVP. |
| `appsettings.json` / variable d'environnement | Contraire à RG-10 : changer le numéro demanderait un redéploiement ou l'accès au portail Azure. |
| Numéro de l'admin (`AspNetUsers.PhoneNumber`) | Il y a 1 à 3 admins et un seul numéro de contact (R8) ; on mélangerait donnée personnelle d'un compte et paramètre de l'association. |
| Créer la ligne au premier `PUT` (*upsert*) | Plus de cas à gérer (lecture d'une ligne absente, course entre deux premiers `PUT` sur la clé). La ligne de la migration rend tout cela inutile. |
| Exposer le numéro en anonyme et le masquer dans le front | Contraire à RG-10 : le numéro serait lisible dans le réseau du navigateur. |
| Recopier le numéro dans chaque réservation | Contraire à US-E8 (le nouveau numéro doit apparaître sur les demandes anciennes). |

## Conséquences

- Ajouter un paramètre plus tard (délais 48 h / 24 h modifiables par l'admin, adresse e-mail de contact de la politique de confidentialité) = une colonne et une migration : acceptable.
- Le numéro de contact n'est pas une donnée personnelle d'un membre, mais il peut être le numéro personnel d'un bénévole : il n'est affiché qu'aux connectés et n'apparaît jamais dans les journaux.
- Tests : la migration crée exactement une ligne ; un `INSERT` d'une ligne `Id = 2` échoue ; visiteur → 401 ; membre → 200 ; membre en `PUT` → 403 ; format refusé ; deux `PUT` avec la même `version` → un 409.

## Reste à valider

- Par le porteur : la table à une ligne et l'avertissement non bloquant.
- ~~Par l'`expert-fonctionnel` : Q17, E2-7~~ : tranchés, voir ci-dessous.

## Mise à jour du 19/11/2026 (décisions du porteur, cahier v1.2, `mvp.md` v4)

Ajout par l'`expert-technique` lors de l'implémentation, à relire par l'`architecte`.

- **Numéro fixe accepté** (Q17, RG-11) : même règle `+216` + 8 chiffres, mobile ou fixe ; rien à changer (`PhoneNumber` ne distingue pas les deux).
- **Retrait du numéro** (Q24, RG-10, R8) : le point 3 « pas de suppression au MVP » est remplacé par :
  - `DELETE /api/admin/settings/contact`, politique `AdminOnly`, corps JSON `{ "version": "..." }` → 200 `ContactSettingsDto` (`phoneNumber: null`, `whatsApp: false`, `isConfigured: false`) ; 400 si `version` absente ou mal formée ; 409 `concurrent_update` si périmée ;
  - Domain : `AssociationSettings.RemoveContact(now)` (numéro `null`, WhatsApp `false`, `UpdatedAt = now`) ; la ligne `Id = 1` n'est jamais supprimée.
  - **Avec `version`**, contrairement aux actions sur les événements : le retrait efface une donnée qu'un autre admin vient peut-être de saisir (même raisonnement que le `PUT`).
  - Alternatives écartées : `PUT` avec `phoneNumber: null` (un client qui oublie le champ retirerait le numéro ; mélange de deux intentions) ; `version` en paramètre d'URL (le `+` du Base64 est décodé en espace, et `HttpParams` d'Angular ne l'encode pas) ; en-tête `If-Match` (correct en HTTP, mais différent du reste de l'API, qui passe la version dans le corps).
  - Point d'attention : un `DELETE` avec corps est permis mais sans sémantique définie par HTTP (RFC 9110) ; ASP.NET Core, curl et Angular (`http.delete(url, { body })`) le gèrent. À revoir si un proxy de la chaîne de déploiement (S3) supprimait le corps.
