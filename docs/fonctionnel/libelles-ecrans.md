# Libellés français des écrans — semaine 2

Version 1.0 — 23/11/2026. Interface en français uniquement. Tutoiement exclu : on **vous** vouvoie. Les textes entre guillemets sont à utiliser tels quels. Les messages de validation (champs) reprennent ceux de l'API (`docs/technique/api.md`) et ne sont pas répétés ici.

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
| Liens | « Événements », « Se connecter », « Créer un compte » | « Événements », « Mon compte », « Se déconnecter » | idem membre + « Administration » |
| Accueil du menu | « Bonjour {prénom} » (connecté) | | |
| Menu mobile | bouton « Menu » (ouvrir), « Fermer le menu » | | |

Pied de page : « Politique de confidentialité », « © [année] [NOM DE L'ASSOCIATION] ».

Sous-menu Administration : « Événements », « Numéro de contact ».

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
| Détail : sections | « Date et heure », « Lieu », « Prix », « Places », « Description » |
| Bouton visiteur | « Demander une place » (mène à la connexion) |
| Bouton membre | masqué tant que les réservations n'existent pas (UX-9) |
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
| Mon compte | Titre « Mon compte » ; « Prénom », « Nom », « Adresse e-mail », « Téléphone », « Compte créé le … » ; pas de bouton de suppression en semaine 2 (US-F2 plus tard) |
| Accès réservé | Titre « Accès réservé » ; texte « Cette page est réservée aux administrateurs. » ; lien « Retour aux événements » |

## 5. Administration

| Élément | Libellé |
|---|---|
| Titre liste | « Administration — événements » |
| Bouton | « Nouvel événement » |
| Filtres | « Statut » : « Tous », « Brouillon », « Publié », « Annulé » ; « Période » : « À venir », « Terminés », « Tous » |
| Message vide | « Aucun événement ne correspond à ces filtres. » |
| Statuts | « Brouillon », « Publié », « Annulé », « En cours », « Terminé » |
| Formulaire : titres | « Nouvel événement », « Modifier l'événement » |
| Champs | « Titre », « Lieu », « Description », « Début (heure de Tunis) », « Fin (heure de Tunis) », « Capacité (nombre de places) », « Prix en TND (laisser vide si gratuit) » |
| Boutons | « Enregistrer », « Publier », « Dépublier », « Supprimer ce brouillon », « Annuler » |
| Enregistré | « Enregistré. » |
| Publié | « L'événement est publié. » ; les avertissements de l'API sont affichés sous ce message |
| Dépublié | « L'événement n'est plus publié. » |
| Suppression : confirmation | Titre « Supprimer ce brouillon ? » ; texte « Cette action est définitive. » ; boutons « Supprimer » / « Annuler » |
| Après suppression | « Le brouillon a été supprimé. » |
| Boutons grisés (UX-7) | Publier, Dépublier, Supprimer désactivés avec l'aide « Enregistrez d'abord vos modifications. » |
| Avertissement inscrits (RG-12) | Titre « Des personnes sont inscrites » ; texte repris de l'API (date, heure ou prix modifiés) ; boutons « Confirmer » / « Annuler » |
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
| 409 (conflit) | voir « Conflit entre admins » |
| 5xx / inattendu | « Un problème est survenu. Veuillez réessayer plus tard. » |

## 8. Hors semaine 2 (pour mémoire, à définir avec l'étape correspondante)

Demander une place, Mes réservations, annulation, demandes à traiter, inscrits, annulation d'événement, actualités, suppression de compte.
