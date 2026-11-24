import { AdminEventSummaryDto, EventAvailability, EventStatus } from '../../core/models/api-models';

// Le front ne recalcule aucune règle de date avec l'horloge du poste : il lit les booléens de l'API (ADR 0016 § 3).

export type MentionKind = 'cancelled' | 'ended' | 'closed' | 'full' | 'available';

export interface EventMention {
  kind: MentionKind;
  label: string;
}

/** Mention publique, dans l'ordre fixé par api.md : Annulé > Terminé > Réservations closes > Complet > places. */
export function eventMention(event: EventAvailability): EventMention {
  if (event.status === 'Cancelled') {
    return { kind: 'cancelled', label: 'Annulé' };
  }
  if (event.hasEnded) {
    return { kind: 'ended', label: 'Terminé' };
  }
  if (event.hasStarted) {
    return { kind: 'closed', label: 'Réservations closes' };
  }
  if (event.isFull || event.remainingSeats <= 0) {
    return { kind: 'full', label: 'Complet' };
  }
  const seats = event.remainingSeats;
  return { kind: 'available', label: seats === 1 ? '1 place restante' : `${seats} places restantes` };
}

const STATUS_LABELS: Record<EventStatus, string> = {
  Draft: 'Brouillon',
  Published: 'Publié',
  Cancelled: 'Annulé',
};

export function statusLabel(status: EventStatus): string {
  return STATUS_LABELS[status];
}

/** Statuts affichés côté admin : le statut stocké, complété par « En cours » ou « Terminé » (calculés par l'API). */
export function adminStatusLabels(event: Pick<AdminEventSummaryDto, 'status' | 'hasStarted' | 'hasEnded'>): string[] {
  const labels = [statusLabel(event.status)];
  if (event.hasEnded) {
    labels.push('Terminé');
  } else if (event.hasStarted) {
    labels.push('En cours');
  }
  return labels;
}
