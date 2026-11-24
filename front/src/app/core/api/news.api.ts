import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { NewsDetailDto, NewsSummaryDto, PagedResult } from '../models/api-models';
import { apiUrl } from './api-url';

/** Taille de page de la liste publique des actualités (US-B1). */
export const NEWS_PAGE_SIZE = 10;

@Injectable({ providedIn: 'root' })
export class NewsApi {
  private readonly http = inject(HttpClient);

  list(page: number): Observable<PagedResult<NewsSummaryDto>> {
    const params = new HttpParams().set('page', page).set('pageSize', NEWS_PAGE_SIZE);
    return this.http.get<PagedResult<NewsSummaryDto>>(apiUrl('/api/news'), { params });
  }

  get(id: string): Observable<NewsDetailDto> {
    return this.http.get<NewsDetailDto>(apiUrl(`/api/news/${encodeURIComponent(id)}`));
  }
}
