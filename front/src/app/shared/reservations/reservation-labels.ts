import { ReservationStatus } from '../../core/models/api-models';

// Libellés : docs/fonctionnel/libelles-ecrans.md § 8 et § 9 (v1.1). Fonctions pures, testées sans composant.

const MEMBER_LABELS: Record<ReservationStatus, string> = {
  Pending: 'En attente de confirmation',
  Confirmed: 'Confirmée',
  Refused: 'Refusée',
  Expired: 'Expirée',
  CancelledByUser: 'Annulée par vous',
  CancelledByAssociation: "Annulée par l'association",
};

const ADMIN_LABELS: Record<ReservationStatus, string> = {
  ...MEMBER_LABELS,
  Pending: 'En attente',
  CancelledByUser: 'Annulée par le membre',
};

/** Statut tel que le membre le lit (« Annulée par vous »). */
export function memberStatusLabel(status: ReservationStatus): string {
  return MEMBER_LABELS[status];
}

/** Statut tel que l'admin le lit (« Annulée par le membre ») — écrans de la tâche B. */
export function adminStatusLabel(status: ReservationStatus): string {
  return ADMIN_LABELS[status];
}

/** Statuts qui occupent une place (le membre ne peut pas refaire de demande). */
export function isActiveStatus(status: ReservationStatus): boolean {
  return status === 'Pending' || status === 'Confirmed';
}

/** Ligne d'information sur le détail quand la dernière demande est terminée (QF-3, libellés § 8). */
export function previousReservationMessage(status: ReservationStatus): string | null {
  switch (status) {
    case 'Refused':
      return 'Votre demande précédente a été refusée.';
    case 'Expired':
      return 'Votre demande précédente a expiré sans être confirmée.';
    case 'CancelledByUser':
      return 'Vous avez annulé votre demande précédente.';
    case 'CancelledByAssociation':
      return "Votre réservation précédente a été annulée par l'association.";
    default:
      return null;
  }
}
