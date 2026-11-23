import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { API_PREFIX } from '../api/api-url';
import { AuthService } from './auth.service';

/**
 * Intercepteur fonctionnel (≈ DelegatingHandler / middleware) : appelé pour CHAQUE requête HttpClient.
 * 1. Ajoute « Authorization: Bearer » uniquement vers notre API (jamais vers un autre site).
 * 2. Un 401 sur une requête qui PORTAIT un jeton = session expirée ou révoquée → déconnexion + page de connexion.
 *    Un 401 sans jeton (mauvais mot de passe, compte bloqué) est laissé à l'écran (ADR 0015).
 * Les autres erreurs ne sont pas traitées ici : chaque écran sait où afficher son message (ADR 0016).
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_PREFIX)) {
    return next(req);
  }

  const auth = inject(AuthService);
  const token = auth.accessToken();
  // Les requêtes HttpClient sont immuables : clone() en produit une copie modifiée.
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      if (token && error instanceof HttpErrorResponse && error.status === 401) {
        auth.expire(true);
      }
      // On relance toujours l'erreur : l'appelant (rxResource, subscribe) doit savoir que la requête a échoué.
      return throwError(() => error);
    }),
  );
};
