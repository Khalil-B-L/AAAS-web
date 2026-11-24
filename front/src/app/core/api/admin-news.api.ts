import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminNewsDto,
  AdminNewsListQuery,
  AdminNewsSummaryDto,
  NewsInput,
  PagedResult,
  UpdateNewsRequest,
} from '../models/api-models';
import { apiUrl } from './api-url';

/** Taille de page de la liste admin des actualités. */
export const ADMIN_NEWS_PAGE_SIZE = 20;

@Injectable({ providedIn: 'root' })
export class AdminNewsApi {
  private readonly http = inject(HttpClient);

  list(query: AdminNewsListQuery): Observable<PagedResult<AdminNewsSummaryDto>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', ADMIN_NEWS_PAGE_SIZE);
    if (query.status) {
      params = params.set('status', query.status);
    }
    return this.http.get<PagedResult<AdminNewsSummaryDto>>(apiUrl('/api/admin/news'), { params });
  }

  get(id: string): Observable<AdminNewsDto> {
    return this.http.get<AdminNewsDto>(this.url(id));
  }

  create(body: NewsInput): Observable<AdminNewsDto> {
    return this.http.post<AdminNewsDto>(apiUrl('/api/admin/news'), body);
  }

  update(id: string, body: UpdateNewsRequest): Observable<AdminNewsDto> {
    return this.http.put<AdminNewsDto>(this.url(id), body);
  }

  publish(id: string): Observable<AdminNewsDto> {
    return this.http.post<AdminNewsDto>(`${this.url(id)}/publish`, null);
  }

  unpublish(id: string): Observable<AdminNewsDto> {
    return this.http.post<AdminNewsDto>(`${this.url(id)}/unpublish`, null);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(this.url(id));
  }

  private url(id: string): string {
    return apiUrl(`/api/admin/news/${encodeURIComponent(id)}`);
  }
}
