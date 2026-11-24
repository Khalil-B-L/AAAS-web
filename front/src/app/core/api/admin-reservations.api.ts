import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AdminReservationDto, EventRegistrantsDto, PagedResult } from '../models/api-models';
import { apiUrl } from './api-url';

export const PENDING_PAGE_SIZE = 20;

export type AdminReservationAction = 'confirm' | 'refuse' | 'cancel' | 'mark-paid' | 'unmark-paid';

/**
 * Traitement des réservations par un administrateur (/api/admin, ADR 0021). Les réponses contiennent des coordonnées
 * de membres : elles ne vivent que dans les signaux des écrans (jamais de localStorage) et l'API les sert en no-store.
 */
@Injectable({ providedIn: 'root' })
export class AdminReservationsApi {
  private readonly http = inject(HttpClient);

  pending(page: number, pageSize = PENDING_PAGE_SIZE): Observable<PagedResult<AdminReservationDto>> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<PagedResult<AdminReservationDto>>(apiUrl('/api/admin/reservations/pending'), { params });
  }

  registrants(eventId: string): Observable<EventRegistrantsDto> {
    return this.http.get<EventRegistrantsDto>(apiUrl(`/api/admin/events/${encodeURIComponent(eventId)}/reservations`));
  }

  /** Une action = un POST sans version : la rowversion côté serveur détecte qu'un autre est passé avant (409). */
  act(reservationId: string, action: AdminReservationAction, reason?: string | null): Observable<AdminReservationDto> {
    const body = action === 'refuse' ? { reason: reason ?? null } : null;
    return this.http.post<AdminReservationDto>(
      apiUrl(`/api/admin/reservations/${encodeURIComponent(reservationId)}/${action}`),
      body,
    );
  }
}
