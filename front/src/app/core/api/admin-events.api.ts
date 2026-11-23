import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AdminEventDto,
  AdminEventListQuery,
  AdminEventSummaryDto,
  EventInput,
  PagedResult,
  PublishEventResponse,
  UpdateEventRequest,
} from '../models/api-models';
import { apiUrl } from './api-url';
import { EVENTS_PAGE_SIZE } from './events.api';

@Injectable({ providedIn: 'root' })
export class AdminEventsApi {
  private readonly http = inject(HttpClient);

  list(query: AdminEventListQuery): Observable<PagedResult<AdminEventSummaryDto>> {
    let params = new HttpParams()
      .set('period', query.period)
      .set('page', query.page)
      .set('pageSize', EVENTS_PAGE_SIZE);
    if (query.status) {
      params = params.set('status', query.status);
    }
    return this.http.get<PagedResult<AdminEventSummaryDto>>(apiUrl('/api/admin/events'), { params });
  }

  get(id: string): Observable<AdminEventDto> {
    return this.http.get<AdminEventDto>(this.url(id));
  }

  create(body: EventInput): Observable<AdminEventDto> {
    return this.http.post<AdminEventDto>(apiUrl('/api/admin/events'), body);
  }

  update(id: string, body: UpdateEventRequest): Observable<AdminEventDto> {
    return this.http.put<AdminEventDto>(this.url(id), body);
  }

  publish(id: string): Observable<PublishEventResponse> {
    return this.http.post<PublishEventResponse>(`${this.url(id)}/publish`, null);
  }

  unpublish(id: string): Observable<AdminEventDto> {
    return this.http.post<AdminEventDto>(`${this.url(id)}/unpublish`, null);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(this.url(id));
  }

  private url(id: string): string {
    return apiUrl(`/api/admin/events/${encodeURIComponent(id)}`);
  }
}
