import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { Router, RouterLink } from '@angular/router';
import { EVENTS_PAGE_SIZE, EventsApi } from '../../../core/api/events.api';
import { toApiError } from '../../../core/errors/api-error';
import { LIST_DATE_FORMAT } from '../../../core/time/date-formats';
import { eventMention } from '../../../shared/events/event-labels';
import { TndPricePipe } from '../../../shared/pipes/tnd-price.pipe';
import { Loading } from '../../../shared/ui/loading';
import { Pager, toPageNumber } from '../../../shared/ui/pager';

/** Événements à venir (US-C1), 20 par page, numéro de page dans l'URL (?page=2). */
@Component({
  selector: 'app-event-list',
  imports: [DatePipe, RouterLink, MatCardModule, MatButtonModule, TndPricePipe, Pager, Loading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './event-list.html',
  styleUrl: './event-list.scss',
})
export class EventList {
  private readonly api = inject(EventsApi);
  private readonly router = inject(Router);

  /** ?page= de l'URL ; `transform` ramène toute valeur invalide à 1 (jamais de 400 provoqué par le front). */
  readonly page = input(1, { transform: toPageNumber });

  // params lit le signal page() : quand l'URL change (?page=3), rxResource relance l'appel tout seul
  // (et annule l'ancien s'il n'est pas fini, comme un switchMap).
  protected readonly events = rxResource({
    params: () => ({ page: this.page() }),
    stream: ({ params }) => this.api.list(params.page),
  });

  protected readonly errorMessage = computed(() => {
    const error = this.events.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly pageSize = EVENTS_PAGE_SIZE;
  protected readonly dateFormat = LIST_DATE_FORMAT;
  protected readonly mention = eventMention;

  protected goToPage(page: number): void {
    // Navigation vers la même page avec un autre ?page= : le bouton « Précédent » du navigateur fonctionne.
    void this.router.navigate([], { queryParams: { page: page > 1 ? page : null } });
  }
}
