import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { AdminReservationAction, AdminReservationsApi } from '../../../../core/api/admin-reservations.api';
import { toApiError } from '../../../../core/errors/api-error';
import { AdminReservationDto } from '../../../../core/models/api-models';
import { NotificationService } from '../../../../core/notifications/notification.service';
import { DAY_FORMAT, LIST_DATE_FORMAT } from '../../../../core/time/date-formats';
import { isGuid } from '../../../../shared/ids';
import { formatTunisianPhone, telLink, whatsAppLink } from '../../../../shared/phone';
import { adminStatusLabel } from '../../../../shared/reservations/reservation-labels';
import { Loading } from '../../../../shared/ui/loading';
import { AdminReservationActions, PendingCountStore } from '../admin-reservation-actions';
import { toggled } from '../pending-requests/pending-requests';
import { rowActionLabel, whatsAppLabel } from '../row-labels';

/** « Admin Association » ou, si cet admin a supprimé son compte, « un administrateur supprimé » (ADR 0021 § 2). */
export function paidByName(r: AdminReservationDto): string {
  return r.paidBy ? `${r.paidBy.firstName} ${r.paidBy.lastName}` : 'un administrateur supprimé';
}

/**
 * « Inscrits — {événement} » (US-E4, E5, E7) : demandes en attente, réservations confirmées, autres.
 * Les coordonnées ne restent qu'en mémoire (signal de la ressource), jamais dans le stockage du navigateur.
 */
@Component({
  selector: 'app-event-registrants',
  imports: [DatePipe, NgTemplateOutlet, RouterLink, MatButtonModule, Loading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './event-registrants.html',
  styleUrl: '../reservations.scss',
})
export class EventRegistrants {
  private readonly api = inject(AdminReservationsApi);
  private readonly actions = inject(AdminReservationActions);
  private readonly counter = inject(PendingCountStore);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  /** :id de l'URL, lié par withComponentInputBinding (≈ un paramètre de route [FromRoute]). */
  readonly id = input<string>();

  protected readonly validId = computed(() => (isGuid(this.id()) ? this.id()! : null));

  // params undefined → la ressource ne charge rien (identifiant mal formé : « Contenu introuvable » sans appel réseau).
  protected readonly registrants = rxResource({
    params: () => (this.validId() ? { id: this.validId()! } : undefined),
    stream: ({ params }) => this.api.registrants(params.id),
  });

  protected readonly busyIds = signal<ReadonlySet<string>>(new Set());
  protected readonly actionError = signal<string | null>(null);

  protected readonly loadError = computed(() => {
    const error = this.registrants.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly isEmpty = computed(() => {
    if (!this.registrants.hasValue()) {
      return false;
    }
    const value = this.registrants.value();
    return value.pending.length + value.confirmed.length + value.others.length === 0;
  });

  protected readonly statusLabel = adminStatusLabel;
  protected readonly dateFormat = LIST_DATE_FORMAT;
  protected readonly formatPhone = formatTunisianPhone;
  protected readonly telLink = telLink;
  protected readonly whatsAppLink = whatsAppLink;
  protected readonly dayFormat = DAY_FORMAT;
  protected readonly paidByName = paidByName;
  protected readonly whatsAppLabel = whatsAppLabel;
  protected readonly rowLabel = rowActionLabel;

  /** Le contexte d'un ng-template n'est pas typé : on redonne son type à la liste pour la vérification du template. */
  protected asRows(items: unknown): AdminReservationDto[] {
    return items as AdminReservationDto[];
  }

  protected act(reservation: AdminReservationDto, action: AdminReservationAction): void {
    if (this.busyIds().has(reservation.id)) {
      return;
    }
    this.busyIds.update((ids) => toggled(ids, reservation.id, true));
    this.actionError.set(null);
    this.actions
      .run(reservation, action)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((outcome) => {
        this.busyIds.update((ids) => toggled(ids, reservation.id, false));
        if (outcome.kind === 'dismissed') {
          return;
        }
        if (outcome.kind === 'done') {
          this.notifications.show(outcome.message);
        } else {
          this.actionError.set(outcome.error.message);
        }
        // Dans les deux cas, la ligne change de groupe ou de statut : on relit (le 409 porte l'état réel en base).
        this.registrants.reload();
        this.counter.refresh();
      });
  }
}
