import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { EventDetailDto, EventSummaryDto, PagedResult } from '../models/api-models';
import { apiUrl } from './api-url';

/** Taille de page des listes d'événements (RG-14, ADR 0011). */
export const EVENTS_PAGE_SIZE = 20;

@Injectable({ providedIn: 'root' })
export class EventsApi {
  private readonly http = inject(HttpClient);

  list(page: number): Observable<PagedResult<EventSummaryDto>> {
    // HttpParams encode les valeurs : jamais de concaténation de chaîne dans l'URL.
    const params = new HttpParams().set('page', page).set('pageSize', EVENTS_PAGE_SIZE);
    return this.http.get<PagedResult<EventSummaryDto>>(apiUrl('/api/events'), { params });
  }

  get(id: string): Observable<EventDetailDto> {
    return this.http.get<EventDetailDto>(apiUrl(`/api/events/${encodeURIComponent(id)}`));
  }
}
