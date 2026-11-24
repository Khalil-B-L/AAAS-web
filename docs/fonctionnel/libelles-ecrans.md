# Libellés français des écrans — semaines 2 et réservations

Version 1.2 — 24/11/2026 (voir l'historique en fin de document). Interface en français uniquement. Tutoiement exclu : on **vous** vouvoie. Les textes entre guillemets sont à utiliser tels quels. Les messages de validation (champs) reprennent ceux de l'API (`docs/technique/api.md`) et ne sont pas répétés ici.

Format des dates : détail « samedi 5 décembre 2026 à 10:00 », listes « sam. 5 déc. 2026, 10:00 » (heure de Tunis).

## 1. Général

| Élément | Libellé |
|---|---|
| Nom du site (provisoire, UX-2) | « [NOM DE L'ASSOCIATION] » |
| Chargement | « Chargement… » |
| Erreur de lecture | « Un problème est survenu. Veuillez réessayer. » + bouton « Réessayer » |
| Erreur réseau | « Impossible de joindre le serveur. Vérifiez votre connexion et réessayez. » |
| Boutons communs | « Enregistrer », « Annuler », « Fermer », « Confirmer », « Retour » |
| Quitter un formulaire modifié (UX-8) | Titre « Quitter sans enregistrer ? » ; texte « Vos modifications ne sont pas enregistrées. » ; boutons « Quitter la page » / « Rester » |
| Pagination | « Précédent », « Suivant », « Page 2 sur 5 » |
| Champ obligatoire | « (obligatoire) » |
| Compteur de caractères | « 123 / 4 000 » |

## 2. Menu et pied de page

| Élément | Visiteur | Membre | Administrateur |
|---|---|---|---|
| Liens | « Événements », « Se connecter », « Créer un compte » | « Événements », « Mes réservations », « Mon compte », « Se déconnecter » | idem membre + « Administration » |
| Accueil du menu | « Bonjour {prénom} » (connecté) | | |
| Menu mobile | bouton « Menu » (ouvrir), « Fermer le menu » | | |

Pied de page : « Politique de confidentialité », « © [année] [NOM DE L'ASSOCIATION] ».

Sous-menu Administration : « Événements », « Demandes (N) » (N = nombre de demandes en attente ; « Demandes » seul s'il n'y en a aucune), « Numéro de contact ».

## 3. Événements (public)

| Élément | Libellé |
|---|---|
| Titre de page | « Événements à venir » |
| Message vide | « Aucun événement prévu pour le moment. » |
| Carte d'événement | titre, date, lieu, prix, mention |
| Prix | « Gratuit » ; « 15 TND, à régler sur place » ; « 12,500 TND, à régler sur place » |
| Mentions | « Annulé », « Terminé », « Réservations closes », « Complet », « 5 places restantes », « 1 place restante » |
| Priorité des mentions | Annulé, Terminé, Réservations closes, Complet, places restantes |
| Bouton de détail | « Voir le détail » |
| Détail : sections | « Date et heure », « Lieu », « Prix », « Places », « Description », puis le panneau « Ma réservation » (§ 8) |
| Bouton visiteur | « Demander une place » (mène à la connexion) |
| Bouton membre | « Demander une place » (UX-9 levé : voir § 8) |
| Événement annulé | bandeau « Cet événement a été annulé. » |
| Événement terminé | bandeau « Cet événement est terminé. » |
| Événement introuvable | titre « Contenu introuvable » ; texte « Cette page n'existe pas ou n'est plus disponible. » ; lien « Retour aux événements » |

## 4. Compte

| Écran | Libellés |
|---|---|
| Inscription | Titre « Créer un compte » ; champs « Prénom », « Nom », « Adresse e-mail », « Téléphone » (aide : « Au format +216 20 123 456 »), « Mot de passe » (aide : « Au moins 8 caractères, avec une lettre et un chiffre ») ; case « J'ai lu et j'accepte la politique de confidentialité » (lien ouvert dans un nouvel onglet) ; bouton « Créer mon compte » ; lien « Déjà un compte ? Se connecter » |
| Erreur case non cochée | « Vous devez accepter la politique de confidentialité pour créer un compte. » |
| Connexion | Titre « Se connecter » ; champs « Adresse e-mail », « Mot de passe » ; bouton « Se connecter » ; lien « Pas encore de compte ? Créer un compte » |
| Identifiants faux | « E-mail ou mot de passe incorrect. » |
| Compte verrouillé | « Trop de tentatives. Votre compte est bloqué temporairement. Réessayez plus tard. » |
| Session expirée | « Votre session a expiré. Veuillez vous reconnecter. » |
| Mon compte | Titre « Mon compte » ; « Prénom », « Nom », « Adresse e-mail », « Téléphone », « Compte créé le … » ; lien « Voir mes réservations » (vers « Mes réservations », US-F3) ; pas de bouton de suppression pour l'instant (US-F2 plus tard) |
| Accès réservé | Titre « Accès réservé » ; texte « Cette page est réservée aux administrateurs. » ; lien « Retour aux événements » |

## 5. Administration (événements et numéro de contact)

| Élément | Libellé |
|---|---|
| Titre liste | « Administration — événements » |
| Bouton | « Nouvel événement » |
| Filtres | « Statut » : « Tous », « Brouillon », « Publié », « Annulé » ; « Période » : « À venir », « Terminés », « Tous » |
| Message vide | « Aucun événement ne correspond à ces filtres. » |
| Statuts | « Brouillon », « Publié », « Annulé », « En cours », « Terminé » |
| Formulaire : titres | « Nouvel événement », « Modifier l'événement » |
| Champs | « Titre », « Lieu », « Description », « Début (heure de Tunis) », « Fin (heure de Tunis) », « Capacité (nombre de places) », « Prix en TND (laisser vide si gratuit) » |
| Boutons | « Enregistrer », « Publier », « Dépublier », « Supprimer ce brouillon », « Annuler » ; liens « Inscrits » (depuis la liste et la fiche d'un événement) |
| Enregistré | « Enregistré. » |
| Publié | « L'événement est publié. » ; les avertissements de l'API sont affichés sous ce message |
| Dépublié | « L'événement n'est plus publié. » |
| Suppression : confirmation | Titre « Supprimer ce brouillon ? » ; texte « Cette action est définitive. » ; boutons « Supprimer » / « Annuler » |
| Après suppression | « Le brouillon a été supprimé. » |
| Boutons grisés (UX-7) | Publier, Dépublier, Supprimer désactivés avec l'aide « Enregistrez d'abord vos modifications. » |
| Avertissement inscrits (RG-12) | Titre « Des personnes sont inscrites » ; texte repris de l'API (date, heure ou prix modifiés) ; boutons « Confirmer » / « Annuler ». Si la date change, ajouter : « Les échéances des demandes en attente seront recalculées. » |
| Conflit entre admins (UX-6) | Titre « Événement modifié par un autre administrateur » ; texte « Cet événement vient d'être modifié. Rechargez la page pour voir la dernière version. Vos modifications seront perdues. » ; boutons « Recharger » / « Fermer » (Fermer permet de recopier sa saisie) |
| Capacité sous les places prises (RG-06) | message de l'API |
| Numéro de contact | Titre « Numéro de contact de l'association » ; texte d'aide « Ce numéro est affiché aux membres connectés pour valider leur demande de place. » ; champs « Numéro (+216 …) », case « Ce numéro est aussi sur WhatsApp » ; boutons « Enregistrer », « Retirer le numéro » ; message vide « Aucun numéro n'est défini pour le moment. » ; confirmation du retrait « Retirer le numéro de contact ? Les membres ne verront plus de numéro. » ; après enregistrement « Numéro enregistré. » ; après retrait « Numéro retiré. » |

## 6. Politique de confidentialité

Titre « Politique de confidentialité » ; contenu : `docs/fonctionnel/politique-confidentialite.md` (partie « texte à publier »). Mention provisoire en tête tant que le porteur n'a pas validé : « Version provisoire ».

## 7. Erreurs générales (liées aux codes de l'API)

| Situation | Message |
|---|---|
| 403 (droits insuffisants) | « Vous n'avez pas le droit d'effectuer cette action. » |
| 404 | « Contenu introuvable. » |
| 409 (conflit) | voir « Conflit entre admins » pour les événements, et le § 11 pour les réservations |
| 5xx / inattendu | « Un problème est survenu. Veuillez réessayer plus tard. » |

## 8. Réservation : panneau « Ma réservation » sur le détail d'un événement

Le panneau s'affiche sous la description. Les libellés de statut sont ceux du § 9. Les dates sont à l'heure de Tunis.

| Situation | Libellés |
|---|---|
| Titre du panneau | « Ma réservation » |
| Visiteur, événement réservable | bouton « Demander une place » ; aide « Vous devez être connecté pour demander une place. » (le bouton mène à la connexion) |
| Membre ou administrateur connecté, sans demande, événement réservable | bouton « Demander une place » ; pendant l'envoi : « Envoi en cours… » (bouton désactivé, un seul clic compte) |
| Demande envoyée (En attente) | Titre « Demande en attente de confirmation » ; texte « Votre demande est enregistrée et une place vous est réservée. Pour la faire confirmer, contactez l'association avant le {échéance, heure de Tunis}. Sans confirmation avant cette date, la demande expire et la place est libérée. » ; événement payant : « Prix : 15 TND, à régler sur place. » ; bouton « Annuler ma demande » ; la carte de contact (ci-dessous) |
| Carte de contact | Titre « Contacter l'association » ; numéro affiché (« +216 20 123 456 ») ; liens « Appeler » et « Écrire sur WhatsApp » (ce dernier seulement si le numéro est joignable sur WhatsApp) ; sans numéro : « L'association n'a pas communiqué de numéro pour le moment. » |
| Confirmée, annulation possible | Titre « Réservation confirmée » ; texte « Vous pouvez annuler jusqu'au {date limite d'annulation}. » ; bouton « Annuler ma réservation » |
| Confirmée, délai dépassé | Titre « Réservation confirmée » ; texte « Le délai d'annulation est dépassé. Pour vous désister, contactez l'association. » ; la carte de contact ; pas de bouton d'annulation |
| Mention payée | « Payée » (avec, dans « Mes réservations », la date) ; sinon pour un événement payant « 15 TND, à régler sur place » |
| Confirmation d'annulation | Titre « Annuler cette demande ? » ou « Annuler cette réservation ? » ; texte « Votre place sera libérée. » ; si payée, ajouter « Cette réservation est marquée payée : le remboursement éventuel se règle directement avec l'association. » ; boutons « Oui, annuler » / « Non, garder ma place » |
| Après annulation | « Votre demande est annulée. » / « Votre réservation est annulée. » |
| Dernière demande refusée (QF-3) | Information « Votre demande précédente a été refusée. » ; si un motif existe : « Motif : {motif} » ; puis le bouton « Demander une place » si l'événement est réservable |
| Dernière demande expirée | « Votre demande précédente a expiré sans être confirmée. » ; puis le bouton si réservable |
| Dernière demande annulée par le membre | « Vous avez annulé votre demande précédente. » ; puis le bouton si réservable |
| Dernière réservation annulée par l'association | « Votre réservation précédente a été annulée par l'association. » ; puis le bouton si réservable |
| Événement non réservable, aucune réservation active | pas de bouton ; texte selon le cas : « Cet événement est complet. », « Les réservations sont closes. », bandeaux « Cet événement a été annulé. » ou « Cet événement est terminé. » |

## 9. Mes réservations

| Élément | Libellé |
|---|---|
| Titre de page | « Mes réservations » |
| Sections | « À venir » (demandes en attente et réservations confirmées dont l'événement n'est pas terminé, de la plus proche à la plus lointaine) ; « Historique » (le reste : événements terminés, refusées, expirées, annulées) |
| Messages vides | aucune réservation du tout : « Vous n'avez aucune réservation. » + lien « Voir les événements à venir » ; section vide : « Aucune réservation à venir. » / « Aucune réservation passée. » |
| Ligne | titre de l'événement (lien « Voir l'événement »), date, lieu, statut, prix ou « Payée », « Demandée le {date} » |
| Événement repassé en brouillon | titre sans lien, mention « Événement indisponible » |
| Événement terminé | mention « Terminé » à côté du statut (par exemple « Confirmée — Terminé ») |
| Demande en attente | « À faire confirmer avant le {échéance} » + la carte de contact (§ 8) + bouton « Annuler ma demande » |
| Refusée | « Refusée » + « Motif : {motif} » (ligne omise s'il n'y a pas de motif) |
| Annulée par l'association | « Annulée par l'association » ; si payée : « Cette réservation est marquée payée : le remboursement éventuel se règle directement avec l'association. » |
| Réservation payée annulée | la mention « Payée » reste affichée |

### Statuts (vue du membre)

| Statut | Libellé |
|---|---|
| En attente | « En attente de confirmation » |
| Confirmée | « Confirmée » |
| Refusée | « Refusée » |
| Expirée | « Expirée » |
| Annulée par le membre | « Annulée par vous » |
| Annulée par l'association | « Annulée par l'association » |

Côté administration (§ 10), les statuts s'écrivent « En attente », « Confirmée », « Refusée », « Expirée », « Annulée par le membre », « Annulée par l'association ». Une demande En attente dont l'échéance est passée s'affiche **« Expirée »** partout, même si la mise à jour automatique n'est pas encore passée.

## 10. Administration : demandes à traiter et inscrits

### Demandes à traiter

| Élément | Libellé |
|---|---|
| Titre de page | « Demandes à traiter » |
| Message vide | « Aucune demande à traiter. » |
| Colonnes | « Membre » (prénom, nom, téléphone cliquable, e-mail), « Événement », « Demandée le », « À traiter avant » |
| Contact rapide | à côté du téléphone, le lien « WhatsApp » (mêmes règles que pour les inscrits, voir ci-dessous) |
| Boutons de ligne | « Confirmer », « Refuser » |
| Dialogue de refus | Titre « Refuser cette demande ? » ; champ « Motif (facultatif) » avec compteur « 0 / 500 » ; aide « Le motif sera visible par le membre. Ne mettez pas de données personnelles dans le motif. » ; boutons « Refuser la demande » / « Fermer » |
| Messages de succès | « Demande confirmée. » ; « Demande refusée. » |
| Compte supprimé | « Compte supprimé » à la place des coordonnées |

### Inscrits d'un événement

| Élément | Libellé |
|---|---|
| Titre | « Inscrits — {titre de l'événement} » |
| Total | « 15 / 30 places occupées (12 confirmées, 3 en attente) » |
| Sections | « Demandes en attente » (la plus ancienne d'abord, avec l'échéance) ; « Réservations confirmées » (par nom puis prénom) ; « Autres (refusées, expirées, annulées) » (la plus récente d'abord) |
| Colonnes | « Prénom », « Nom », « Téléphone », « E-mail », « Demandée le », « À traiter avant » (section « En attente » seulement), « Statut » (section « Autres » seulement : « Refusée », « Expirée », « Annulée par le membre », « Annulée par l'association »), « Paiement » (sections « Confirmées » et « Autres », événement payant seulement : « Payée le {date} par {prénom nom de l'admin} » ou « Non payée » ; si cet admin a supprimé son compte : « Payée le {date} par un administrateur supprimé ») |
| Contact | le téléphone est cliquable (appel) ; à côté, un lien « WhatsApp » (s'ouvre dans un nouvel onglet) est proposé **pour tous les inscrits**, sans savoir s'ils ont WhatsApp : le membre a donné son numéro à l'inscription, le libellé reste neutre (pas de « Écrire sur WhatsApp » comme côté membre, où l'on sait si le numéro de l'association y est joignable) ; l'e-mail est cliquable. Aucun lien pour un « Compte supprimé » |
| Motif de refus (section « Autres ») | sous le statut « Refusée » : « Motif : {motif} » (ligne omise s'il n'y a pas de motif) |
| Message vide | « Aucune demande pour le moment. » |
| Rappel | « Ces coordonnées sont réservées à l'organisation de l'événement. » |
| Boutons de ligne | « Confirmer », « Refuser » (en attente) ; « Annuler la réservation » (en attente ou confirmée, avant la fin de l'événement) ; « Marquer comme payée » (confirmée, événement payant) ; « Retirer la mention payée » (si payée) |
| Événement terminé | « Cet événement est terminé : les réservations ne peuvent plus être annulées. » (les boutons « Annuler la réservation » sont absents ; « Marquer comme payée » et « Retirer la mention payée » restent possibles) |
| Annulation par l'admin : confirmation | Titre « Annuler cette réservation ? » ; texte « La place sera libérée. L'association n'envoie pas de message : pensez à prévenir la personne par téléphone. » ; si payée : « Cette réservation est marquée payée : le remboursement éventuel se règle hors de l'application. » ; boutons « Oui, annuler » / « Fermer » |
| Messages de succès | « Réservation annulée. » ; « Mention « Payée » enregistrée. » ; « Mention « Payée » retirée. » |
| Action absente | « Marquer comme payée » n'est pas proposé pour un événement gratuit ni pour une réservation non confirmée |

## 11. Messages d'erreur par code (réservations)

**Côté membre** (panneau « Ma réservation », « Mes réservations ») : le message s'affiche près du bouton concerné, puis le panneau ou la ligne est rechargé avec le statut actuel. **Côté administration** (« Demandes à traiter », « Inscrits ») : le message s'affiche dans un **bandeau en haut de la page**, puis la liste entière est relue (la ligne peut donc disparaître de « Demandes à traiter » si son statut a changé). Choix du MVP : plus simple qu'un message par ligne. Les textes de la colonne « Message » reprennent ceux du plan des réservations (§ 4), à l'identique.

| Code | Où | Message |
|---|---|---|
| `event_full` | demande de place | « Événement complet » |
| `reservations_closed` | demande de place | « Les réservations sont closes » |
| `already_reserved` | demande de place | « Vous avez déjà une demande en cours ou une réservation pour cet événement ». **Cas du double clic** : si la réponse contient la réservation déjà créée, aucun message d'erreur n'est affiché : le panneau montre simplement la demande existante |
| `reservation_state_changed` | annulation par le membre | « Cette demande a déjà été traitée ou annulée. » (proposition ; si la demande a expiré : « Votre demande a expiré. Vous pouvez en faire une nouvelle. ») |
| `reservation_state_changed` | action de l'admin (confirmer, refuser, annuler) | « Cette demande a déjà été traitée ou annulée. » ; si le statut actuel est « Expirée » : « Cette demande a expiré : le membre doit faire une nouvelle demande. » |
| `cancellation_deadline_passed` | annulation par le membre | « Le délai d'annulation est dépassé : contactez l'association » (+ la carte de contact) |
| `reservation_not_confirmed` | « Marquer comme payée » | « Confirmez d'abord la demande avant de la marquer payée » |
| `payment_not_applicable` | « Marquer comme payée » | « Cet événement est gratuit : aucun paiement à enregistrer » |
| `event_cancelled` (ajouté, absent de la liste du plan § 4) | demande de place | « Cet événement a été annulé. » (proposition) |
| `event_ended` (ajouté, absent de la liste du plan § 4) | annulation par l'admin | « Cet événement est terminé : la réservation ne peut plus être annulée. » (proposition) |
| 404 (réservation inconnue ou d'un autre membre ; événement brouillon) | tous | « Contenu introuvable. » |
| 401 | tous | « Votre session a expiré. Veuillez vous reconnecter. » |

## 12. Hors périmètre de ce document (pour mémoire)

Annulation d'un événement par l'admin (US-E3), actualités, suppression de compte : libellés à définir avec l'étape correspondante.

## Historique

- **1.2 (24/11/2026)** : § 10 complété avec ce qui est déjà à l'écran (T-B9, T-B11) : lien « WhatsApp » pour tous les inscrits (libellé neutre), aide du dialogue de refus « Ne mettez pas de données personnelles dans le motif. », colonne « Statut » et « Motif : … » dans « Autres », colonne « Paiement » aussi dans « Autres ». Forme retenue pour un admin supprimé : « Payée le {date} par un administrateur supprimé ». § 11 : côté administration, erreurs en bandeau de page puis liste relue (T-B10), au lieu de « près du bouton ».
- **1.1 (24/11/2026)** : réservations (§ 8 à 11).
- **1.0 (23/11/2026)** : semaine 2.
