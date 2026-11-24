import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { Router, RouterLink } from '@angular/router';
import { ADMIN_NEWS_PAGE_SIZE, AdminNewsApi } from '../../../../core/api/admin-news.api';
import { toApiError } from '../../../../core/errors/api-error';
import { NewsStatus } from '../../../../core/models/api-models';
import { LIST_DATE_FORMAT } from '../../../../core/time/date-formats';
import { FORM_FIELD_DEFAULTS } from '../../../../shared/forms/form-field-defaults';
import { NEWS_STATUS_LABELS } from '../../../../shared/news/news-labels';
import { Loading } from '../../../../shared/ui/loading';
import { Pager, toPageNumber } from '../../../../shared/ui/pager';

const STATUSES: readonly NewsStatus[] = ['Draft', 'Published'];

/** Valeur d'URL inconnue → « Tous » (le front n'envoie jamais un statut que l'API refuserait). */
export function toNewsStatusFilter(value: unknown): NewsStatus | null {
  return STATUSES.find((s) => s === value) ?? null;
}

/** Liste admin des actualités (US-E1) : brouillons et publiées, dernière modification d'abord, filtre et page dans l'URL. */
@Component({
  selector: 'app-admin-news-list',
  imports: [DatePipe, RouterLink, MatButtonModule, MatFormFieldModule, MatSelectModule, Pager, Loading],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-news-list.html',
  styleUrl: '../../events/admin-event-list/admin-event-list.scss',
})
export class AdminNewsList {
  private readonly api = inject(AdminNewsApi);
  private readonly router = inject(Router);

  readonly status = input<NewsStatus | null, unknown>(null, { transform: toNewsStatusFilter });
  readonly page = input(1, { transform: toPageNumber });

  protected readonly news = rxResource({
    params: () => ({ status: this.status(), page: this.page() }),
    stream: ({ params }) => this.api.list(params),
  });

  protected readonly errorMessage = computed(() => {
    const error = this.news.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly pageSize = ADMIN_NEWS_PAGE_SIZE;
  protected readonly dateFormat = LIST_DATE_FORMAT;
  protected readonly statusLabels = NEWS_STATUS_LABELS;

  protected onStatusChange(status: NewsStatus | null): void {
    this.updateQuery({ status, page: null });
  }

  protected goToPage(page: number): void {
    this.updateQuery({ page: page > 1 ? page : null });
  }

  private updateQuery(queryParams: Record<string, string | number | null>): void {
    void this.router.navigate([], { queryParams, queryParamsHandling: 'merge' });
  }
}
