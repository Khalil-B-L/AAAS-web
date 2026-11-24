import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';
import { NEWS_PAGE_SIZE, NewsApi } from '../../../core/api/news.api';
import { toApiError } from '../../../core/errors/api-error';
import { DAY_FORMAT } from '../../../core/time/date-formats';
import { Loading } from '../../../shared/ui/loading';
import { Pager, toPageNumber } from '../../../shared/ui/pager';

/** Actualités publiées (US-B1), la plus récente d'abord, 10 par page, page dans l'URL (?page=2). Page d'accueil. */
@Component({
  selector: 'app-news-list',
  imports: [DatePipe, RouterLink, MatButtonModule, Pager, Loading],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './news-list.html',
  styleUrl: './news-list.scss',
})
export class NewsList {
  private readonly api = inject(NewsApi);
  private readonly router = inject(Router);

  readonly page = input(1, { transform: toPageNumber });

  protected readonly news = rxResource({
    params: () => ({ page: this.page() }),
    stream: ({ params }) => this.api.list(params.page),
  });

  protected readonly errorMessage = computed(() => {
    const error = this.news.error();
    return error ? toApiError(error).message : null;
  });

  protected readonly pageSize = NEWS_PAGE_SIZE;
  protected readonly dayFormat = DAY_FORMAT;

  protected goToPage(page: number): void {
    void this.router.navigate([], { queryParams: { page: page > 1 ? page : null } });
  }
}
