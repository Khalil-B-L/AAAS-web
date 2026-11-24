import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';
import {
  AdminReservationAction,
  AdminReservationsApi,
  PENDING_PAGE_SIZE,
} from '../../../../core/api/admin-reservations.api';
import { toApiError } from '../../../../core/errors/api-error';
import { AdminReservationDto } from '../../../../core/models/api-models';
import { NotificationService } from '../../../../core/notifications/notification.service';
import { LIST_DATE_FORMAT } from '../../../../core/time/date-formats';
import { formatTunisianPhone, telLink, whatsAppLink } from '../../../../shared/phone';
import { Loading } from '../../../../shared/ui/loading';
import { Pager, toPageNumber } from '../../../../shared/ui/pager';
import { AdminReservationActions, PendingCountStore } from '../admin-reservation-actions';
import { rowActionLabel, whatsAppLabel } from '../row-labels';

/** Ajoute ou retire un identifiant d'un ensemble immuable (un signal ne voit un changement que si la référence change). */
export function toggled(ids: ReadonlySet<string>, id: string, present: boolean): ReadonlySet<string> {
  const next = new Set(ids);
  if (present) {
    next.add(id);
  } else {
    next.delete(id);
  }
  return next;
}

/** « Demandes à traiter » (US-E6) : demandes en attente de tous les événements, échéance la plus proche d'abord. */
@Component({
  selector: 'app-pending-requests',
  imports: [DatePipe, RouterLink, MatButtonModule, Loading, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './pending-requests.html',
  styleUrl: '../reservations.scss',
})
export class PendingRequests {
  private readonly api = inject(AdminReservationsApi);
  private readonly actions = inject(AdminReservationActions);
  private readonly counter = inject(PendingCountStore);
  private readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** ?page= de l'URL (withComponentInputBinding), ramené à un entier valide. */
  readonly page = input(1, { transform: toPageNumber });

  protected readonly requests = rxResource({
    params: () => ({ page: this.page() }),
    stream: ({ params }) => this.api.pending(params.page),
  });

  /**
   * Lignes dont une action est en cours : leurs boutons sont désactivés (un double clic n'envoie qu'un appel).
   * signal<Set> : on remplace l'ensemble à chaque changement (toggled) ; muter le Set existant ne préviendrait personne.
   */
  protected readonly busyIds = signal<ReadonlySet<string>>(new Set());
  protected readonly actionError = signal<string | null>(null);

  protected readonly loadError = computed(() => {
    const error = this.requests.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly pageSize = PENDING_PAGE_SIZE;
  protected readonly dateFormat = LIST_DATE_FORMAT;
  protected readonly formatPhone = formatTunisianPhone;
  protected readonly telLink = telLink;
  protected readonly whatsAppLink = whatsAppLink;
  protected readonly whatsAppLabel = whatsAppLabel;
  protected readonly rowLabel = rowActionLabel;

  /**
   * Dernière page existante quand la page affichée est vide alors qu'il reste des demandes (on vient de traiter la
   * dernière ligne de la dernière page, ou ?page= trop grand dans l'URL) ; null sinon.
   */
  protected readonly lastPageIfOutOfRange = computed(() => {
    if (!this.requests.hasValue()) {
      return null;
    }
    const { items, page, totalCount } = this.requests.value();
    const lastPage = Math.ceil(totalCount / this.pageSize);
    // lastPage < page : si les lignes ont disparu entre le COUNT et la lecture, on ne boucle pas sur la même page.
    return items.length === 0 && page > 1 && totalCount > 0 && lastPage < page ? lastPage : null;
  });

  constructor() {
    // effect : rejoué à chaque changement des signaux lus (≈ un abonnement). Réservé aux effets de bord comme une
    // navigation ; la valeur elle-même reste un computed. replaceUrl : « Précédent » ne ramène pas à la page vide.
    effect(() => {
      const lastPage = this.lastPageIfOutOfRange();
      if (lastPage !== null) {
        this.goToPage(lastPage, true);
      }
    });
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
          // 409 reservation_state_changed (autre admin, membre, expiration) : message de l'API, puis liste relue.
          this.actionError.set(outcome.error.message);
        }
        this.requests.reload();
        this.counter.refresh();
      });
  }

  protected goToPage(page: number, replaceUrl = false): void {
    void this.router.navigate([], {
      queryParams: { page: page > 1 ? page : null },
      queryParamsHandling: 'merge',
      replaceUrl,
    });
  }
}
