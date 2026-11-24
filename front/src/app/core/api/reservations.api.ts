import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { MyReservationDto, MyReservationScope, PagedResult } from '../models/api-models';
import { apiUrl } from './api-url';

export const MY_RESERVATIONS_PAGE_SIZE = 20;

/**
 * Réservations du membre connecté (/api/me, ADR 0021). Aucun identifiant de membre n'est envoyé :
 * l'API le lit dans le jeton, ajouté par l'intercepteur.
 */
@Injectable({ providedIn: 'root' })
export class ReservationsApi {
  private readonly http = inject(HttpClient);

  request(eventId: string): Observable<MyReservationDto> {
    return this.http.post<MyReservationDto>(apiUrl('/api/me/reservations'), { eventId });
  }

  list(scope: MyReservationScope, page: number): Observable<PagedResult<MyReservationDto>> {
    const params = new HttpParams().set('scope', scope).set('page', page).set('pageSize', MY_RESERVATIONS_PAGE_SIZE);
    return this.http.get<PagedResult<MyReservationDto>>(apiUrl('/api/me/reservations'), { params });
  }

  /**
   * « Ma demande » pour un événement. Réponse 204 (aucune demande) : HttpClient émet alors `null` comme corps,
   * d'où le type `MyReservationDto | null` (pas d'erreur à gérer pour ce cas).
   */
  forEvent(eventId: string): Observable<MyReservationDto | null> {
    return this.http.get<MyReservationDto | null>(apiUrl(`/api/me/events/${encodeURIComponent(eventId)}/reservation`));
  }

  cancel(reservationId: string): Observable<MyReservationDto> {
    return this.http.post<MyReservationDto>(apiUrl(`/api/me/reservations/${encodeURIComponent(reservationId)}/cancel`), null);
  }
}
