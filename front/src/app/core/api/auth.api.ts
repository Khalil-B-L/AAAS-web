import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthResponse, LoginRequest, RegisterRequest } from '../models/api-models';
import { apiUrl } from './api-url';

// Les services d'API renvoient l'Observable brut : ni gestion d'erreur ni message ici (c'est l'écran qui décide, ADR 0016).
@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);

  register(body: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(apiUrl('/api/auth/register'), body);
  }

  login(body: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(apiUrl('/api/auth/login'), body);
  }
}
