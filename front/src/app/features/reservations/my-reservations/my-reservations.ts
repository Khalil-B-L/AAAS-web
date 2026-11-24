import { DatePipe, NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  WritableSignal,
  computed,
  effect,
  inject,
  signal,
} from '@angular/core';
import { rxResource, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';
import { MY_RESERVATIONS_PAGE_SIZE, ReservationsApi } from '../../../core/api/reservations.api';
import { toApiError } from '../../../core/errors/api-error';
import { MyReservationDto, MyReservationScope, PagedResult } from '../../../core/models/api-models';
import { NotificationService } from '../../../core/notifications/notification.service';
import { DAY_FORMAT, DETAIL_DATE_FORMAT, LIST_DATE_FORMAT } from '../../../core/time/date-formats';
import { TndPricePipe } from '../../../shared/pipes/tnd-price.pipe';
import { memberStatusLabel } from '../../../shared/reservations/reservation-labels';
import { ContactCard } from '../../../shared/ui/contact-card';
import { Loading } from '../../../shared/ui/loading';
import { Pager } from '../../../shared/ui/pager';
import { ReservationCancellation } from '../reservation-cancellation';

/** « Mes réservations » (US-D2, US-D3) : « À venir » et « Historique », chacune paginée (20 par page). */
@Component({
  selector: 'app-my-reservations',
  imports: [DatePipe, NgTemplateOutlet, RouterLink, MatButtonModule, TndPricePipe, ContactCard, Loading, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './my-reservations.html',
  styleUrl: './my-reservations.scss',
})
export class MyReservations {
  private readonly api = inject(ReservationsApi);
  private readonly cancellation = inject(ReservationCancellation);
  private readonly notifications = inject(NotificationService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly currentPage = signal(1);
  protected readonly historyPage = signal(1);

  // Une ressource par section : changer de page dans l'une ne recharge pas l'autre.
  protected readonly current = rxResource({
    params: () => ({ page: this.currentPage() }),
    stream: ({ params }) => this.api.list('current', params.page),
  });
  protected readonly history = rxResource({
    params: () => ({ page: this.historyPage() }),
    stream: ({ params }) => this.api.list('history', params.page),
  });

  /** Identifiant de la ligne en cours d'annulation : ses boutons sont désactivés pendant l'appel. */
  protected readonly busyId = signal<string | null>(null);
  /** Message d'erreur affiché sous la ligne concernée (id → message). */
  protected readonly rowError = signal<{ id: string; message: string; showContact: boolean } | null>(null);

  protected readonly loadError = computed(() => {
    const error = this.current.error() ?? this.history.error();
    return error ? toApiError(error).message : null;
  });
  protected readonly isEmpty = computed(
    () => this.total(this.current) === 0 && this.total(this.history) === 0,
  );

  protected readonly statusLabel = memberStatusLabel;
  protected readonly pageSize = MY_RESERVATIONS_PAGE_SIZE;
  protected readonly listFormat = LIST_DATE_FORMAT;
  protected readonly detailFormat = DETAIL_DATE_FORMAT;
  protected readonly dayFormat = DAY_FORMAT;

  constructor() {
    // Annuler la seule ligne de la dernière page la vide : sans cela, « Aucune réservation à venir. » s'afficherait
    // alors qu'il en reste sur les pages précédentes. effect() relance ce code à chaque nouvelle valeur de la ressource
    // (≈ un abonnement à un événement « PropertyChanged »), et peut écrire dans un signal (le numéro de page).
    effect(() => this.backToLastPage(this.current, this.currentPage));
    effect(() => this.backToLastPage(this.history, this.historyPage));
  }
  protected page(scope: MyReservationScope): PagedResult<MyReservationDto> | null {
    const resource = scope === 'current' ? this.current : this.history;
    return resource.hasValue() ? resource.value() : null;
  }

  protected reload(): void {
    this.current.reload();
    this.history.reload();
  }

  protected cancel(reservation: MyReservationDto): void {
    if (this.busyId()) {
      return;
    }
    this.busyId.set(reservation.id);
    this.rowError.set(null);
    this.cancellation
      .cancel(reservation)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((outcome) => {
        this.busyId.set(null);
        if (outcome.kind === 'cancelled') {
          this.notifications.show(outcome.message);
          this.reload(); // la ligne passe de « À venir » à « Historique »
        } else if (outcome.kind === 'failed') {
          this.rowError.set({
            id: reservation.id,
            message: outcome.error.message,
            showContact: outcome.error.code === 'cancellation_deadline_passed',
          });
          if (outcome.error.code === 'reservation_state_changed') {
            this.reload();
          }
        }
      });
  }

  private backToLastPage(resource: typeof this.current, page: WritableSignal<number>): void {
    if (!resource.hasValue()) {
      return;
    }
    const result = resource.value();
    if (result.items.length === 0 && result.page > 1) {
      // set() d'une valeur identique ne relance rien : pas de boucle si le serveur renvoyait une page incohérente.
      page.set(Math.max(1, Math.ceil(result.totalCount / result.pageSize)));
    }
  }

  private total(resource: typeof this.current): number | null {
    return resource.hasValue() ? resource.value().totalCount : null;
  }
}
