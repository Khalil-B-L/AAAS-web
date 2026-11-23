# Politique de confidentialité — texte provisoire (Q13)

| | |
|---|---|
| **Version du texte** | 1.0-provisoire — 23/11/2026 |
| **Statut** | Provisoire : à relire et à compléter par le porteur et l'association avant la mise en ligne |

> **À compléter par le porteur / l'association** (champs entre crochets) :
> - `[NOM OFFICIEL DE L'ASSOCIATION]` : nom exact figurant dans les statuts ;
> - `[ADRESSE POSTALE]` et `[E-MAIL DE CONTACT]` : coordonnées pour les demandes sur les données (Q13 : par défaut, l'e-mail de l'association) ;
> - `[DATE DE MISE EN LIGNE]` ;
> - vérifier auprès de l'INPDP si une déclaration est nécessaire (cahier des charges § 6.4) ; relecture par une personne compétente conseillée.
>
> La « version » ci-dessus est celle enregistrée avec le consentement de chaque membre (cahier § 6.4). **Toute modification du texte incrémente la version.** Tout ce qui est sous la ligne « Début du texte à publier » est le texte affiché à la page `/confidentialite`.

---

## Début du texte à publier

# Politique de confidentialité

*Version 1.0 — en vigueur depuis le [DATE DE MISE EN LIGNE]*

## 1. Qui est responsable de vos données ?

Le responsable du traitement est **[NOM OFFICIEL DE L'ASSOCIATION]**, association établie en Tunisie, joignable à l'adresse **[ADRESSE POSTALE]** ou par e-mail à **[E-MAIL DE CONTACT]**.

Ce site est géré par des bénévoles. Nous traitons vos données conformément à la **loi organique n° 2004-63 du 27 juillet 2004 portant sur la protection des données à caractère personnel** (autorité de contrôle : l'Instance nationale de protection des données personnelles, INPDP).

## 2. Quelles données collectons-nous ?

Uniquement ce qui est nécessaire au fonctionnement du site :

| Donnée | Pourquoi |
|---|---|
| Prénom et nom | Vous identifier dans vos réservations et sur les listes d'inscrits tenues par les administrateurs |
| Adresse e-mail | Vous connecter à votre compte |
| Numéro de téléphone (« +216 » et 8 chiffres) | Permettre à un administrateur de vous joindre pour valider votre demande de place et, le cas échéant, régler le prix en main propre |
| Mot de passe | Protéger votre compte. Il n'est **jamais conservé en clair** : seule une empreinte irréversible est enregistrée |
| Date de création du compte et date de dernière connexion | Vous les montrer dans « Mon compte » et repérer les comptes inactifs à supprimer |
| Date de votre accord et version de cette politique acceptée | Prouver que vous avez donné votre consentement |
| Vos demandes de réservation (événement, statut, mention « payé » le cas échéant) | Gérer les inscriptions aux événements |

Nous ne collectons **aucune donnée de paiement** : il n'y a pas de paiement en ligne. Nous n'utilisons **aucun traceur publicitaire** et ne vendons ni ne cédons vos données.

## 3. Pour quelles finalités ?

- Créer et gérer votre compte ;
- traiter vos demandes de réservation (demande, confirmation, refus, annulation, suivi du paiement remis en main propre) ;
- permettre aux administrateurs de vous contacter au sujet de ces demandes ;
- assurer la sécurité du site.

Vos données ne sont pas utilisées à d'autres fins sans votre accord.

## 4. Qui peut voir vos données ?

- **Vous** : dans « Mon compte » et « Mes réservations ».
- **Les administrateurs de l'association** : vos nom, prénom, e-mail et téléphone, pour traiter vos réservations.
- **Les prestataires techniques** qui hébergent le site (hébergement et base de données Azure, région France Central), uniquement pour le faire fonctionner. Vos données sont donc **hébergées hors de Tunisie**, en France.
- Les autres membres et les visiteurs **ne voient jamais** vos données.

## 5. Combien de temps les conservons-nous ?

- Tant que votre compte existe.
- Un compte **inactif depuis 3 ans** (sans connexion) est supprimé.
- Si vous **supprimez votre compte** : la suppression est immédiate et définitive ; vos réservations à venir sont annulées (les places sont libérées) et les autres réservations sont **rendues anonymes** (elles ne peuvent plus être reliées à vous). Les statistiques d'un événement restent ainsi cohérentes.

## 6. Sauvegardes

Pour se protéger d'une panne, la base de données est sauvegardée automatiquement par l'hébergeur. Les données que vous avez supprimées peuvent donc rester **dans ces sauvegardes pendant 7 jours au plus**, puis disparaissent définitivement. Ces sauvegardes ne servent qu'à restaurer le site en cas d'incident.

## 7. Vos droits

Conformément à la loi organique n° 2004-63, vous pouvez :
- **accéder** à vos données (page « Mon compte ») ;
- **faire rectifier** des données inexactes ;
- **vous opposer** au traitement de vos données pour des motifs légitimes ;
- **supprimer votre compte** à tout moment depuis « Mon compte » ;
- retirer votre consentement (la suppression du compte met fin au traitement).

Pour exercer un droit qui n'est pas possible directement sur le site, écrivez à **[E-MAIL DE CONTACT]** ou à **[ADRESSE POSTALE]**. Nous répondons dans un délai raisonnable.

Vous pouvez aussi saisir l'INPDP si vous estimez que vos droits ne sont pas respectés.

## 8. Mineurs

Le site est accessible à tous. Si vous êtes mineur, nous vous conseillons de vous inscrire avec l'accord d'un parent ou tuteur.

## 9. Sécurité

Les échanges avec le site sont chiffrés (HTTPS), les mots de passe sont protégés et l'accès aux données est réservé aux administrateurs. Aucun système n'étant infaillible, nous vous invitons à choisir un mot de passe que vous n'utilisez pas ailleurs.

## 10. Modifications de cette politique

Si ce texte change, sa version change : il vous sera demandé de l'accepter de nouveau lors d'une prochaine étape prévue par l'association *(à confirmer, hors MVP)*. La version acceptée par chaque membre est conservée avec la date de son accord.

## 11. Nous contacter

**[NOM OFFICIEL DE L'ASSOCIATION]** — **[ADRESSE POSTALE]** — **[E-MAIL DE CONTACT]**

---

## Notes pour le porteur (ne pas publier)

- Les affirmations « 7 jours de sauvegardes » et « hébergement en France » viennent de l'ADR 0007 et du cahier (§ 6.5) ; à revérifier avec la configuration réelle d'Azure SQL au déploiement (S3).
- Le point 8 (mineurs) respecte la décision du porteur (aucun contrôle) ; c'est un simple conseil, non une obligation. À supprimer si vous préférez ne rien dire.
- Le point 10 suppose qu'aucune redemande de consentement n'existe au MVP : à valider.
- Le droit d'opposition et le délai de réponse sont formulés de façon générale ; faire vérifier les formulations exactes par une personne connaissant la loi 2004-63.
