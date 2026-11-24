import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { AdminEventsApi } from '../../../../core/api/admin-events.api';
import { EVENTS_PAGE_SIZE } from '../../../../core/api/events.api';
import { toApiError } from '../../../../core/errors/api-error';
import { EventPeriod, EventStatus } from '../../../../core/models/api-models';
import { LIST_DATE_FORMAT } from '../../../../core/time/date-formats';
import { adminStatusLabels } from '../../../../shared/events/event-labels';
import { TndPricePipe } from '../../../../shared/pipes/tnd-price.pipe';
import { FORM_FIELD_DEFAULTS } from '../../../../shared/forms/form-field-defaults';
import { Loading } from '../../../../shared/ui/loading';
import { Pager, toPageNumber } from '../../../../shared/ui/pager';

const STATUSES: readonly EventStatus[] = ['Draft', 'Published', 'Cancelled'];
const PERIODS: readonly EventPeriod[] = ['upcoming', 'past', 'all'];

/** Valeur d'URL inconnue → filtre par défaut (le front n'envoie jamais un paramètre que l'API refuserait). */
export function toStatusFilter(value: unknown): EventStatus | null {
  return STATUSES.find((s) => s === value) ?? null;
}

export function toPeriodFilter(value: unknown): EventPeriod {
  return PERIODS.find((p) => p === value) ?? 'upcoming';
}

/** Liste admin des événements (US-E2) : tous statuts, filtres et page dans l'URL. */
@Component({
  selector: 'app-admin-event-list',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    TndPricePipe,
    Pager,
    Loading,
  ],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-event-list.html',
  styleUrl: './admin-event-list.scss',
})
export class AdminEventList {
  private readonly api = inject(AdminEventsApi);
  private readonly router = inject(Router);

  // input<Type, TypeReçu> : la valeur brute de l'URL (unknown) est transformée en valeur typée.
  readonly status = input<EventStatus | null, unknown>(null, { transform: toStatusFilter });
  readonly period = input<EventPeriod, unknown>('upcoming', { transform: toPeriodFilter });
  readonly page = input(1, { transform: toPageNumber });

  protected readonly events = rxResource({
    params: () => ({ status: this.status(), period: this.period(), page: this.page() }),
    stream: ({ params }) => this.api.list(params),
  });

  protected readonly errorMessage = computed(() => {
    const error = this.events.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly pageSize = EVENTS_PAGE_SIZE;
  protected readonly dateFormat = LIST_DATE_FORMAT;
  protected readonly statusLabels = adminStatusLabels;

  protected onStatusChange(status: EventStatus | null): void {
    // Changer un filtre remet la page à 1 (page: null retire le paramètre de l'URL).
    this.updateQuery({ status, page: null });
  }

  protected onPeriodChange(period: EventPeriod): void {
    this.updateQuery({ period: period === 'upcoming' ? null : period, page: null });
  }

  protected goToPage(page: number): void {
    this.updateQuery({ page: page > 1 ? page : null });
  }

  private updateQuery(queryParams: Record<string, string | number | null>): void {
    // 'merge' : on garde les autres paramètres de l'URL (le filtre de statut quand on change de page…).
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge' });
  }
}
