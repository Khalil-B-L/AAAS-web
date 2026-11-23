import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ContactSettingsDto, UpdateContactSettingsRequest } from '../models/api-models';
import { apiUrl } from './api-url';

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  /** Lecture réservée aux connectés (un visiteur reçoit 401, RG-10). */
  getContact(): Observable<ContactSettingsDto> {
    return this.http.get<ContactSettingsDto>(apiUrl('/api/settings/contact'));
  }

  saveContact(body: UpdateContactSettingsRequest): Observable<ContactSettingsDto> {
    return this.http.put<ContactSettingsDto>(apiUrl('/api/admin/settings/contact'), body);
  }

  /**
   * DELETE avec un corps JSON { version } : passer par l'option `body` pour que HttpClient sérialise en JSON
   * et ajoute Content-Type: application/json (sans lui, l'API répond 415).
   */
  removeContact(version: string): Observable<ContactSettingsDto> {
    return this.http.delete<ContactSettingsDto>(apiUrl('/api/admin/settings/contact'), { body: { version } });
  }
}
