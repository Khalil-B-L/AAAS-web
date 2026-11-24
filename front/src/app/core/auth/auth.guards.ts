import { inject } from '@angular/core';
import { CanActivateFn, CanMatchFn, Router, UrlSegment, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { DEFAULT_AFTER_LOGIN } from './return-url';

// Guards = CONFORT (masquer des pages, rediriger au bon endroit). La sécurité réelle est côté API (401/403),
// comme un [Authorize] qui serait vérifié par le serveur et seulement « mimé » par le client.
// Un guard renvoie true (navigation autorisée) ou un UrlTree (redirection), jamais de navigate() + false.

/** Pages membres (/mon-compte, /mes-reservations) : non connecté → connexion avec retour. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isLoggedIn() || loginTree(inject(Router), state.url);
};

/**
 * Espace admin, en canMatch : si le guard refuse, la route ne « correspond » pas et le code de l'espace admin
 * (chargé par loadChildren) n'est même pas téléchargé par le navigateur.
 */
export const adminGuard: CanMatchFn = (_route, segments) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isLoggedIn()) {
    return loginTree(router, attemptedUrl(router, segments));
  }
  return auth.isAdmin() || router.parseUrl('/acces-refuse');
};

/** /connexion et /inscription : déjà connecté → liste des événements. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return !auth.isLoggedIn() || inject(Router).parseUrl(DEFAULT_AFTER_LOGIN);
};

function loginTree(router: Router, returnUrl: string): UrlTree {
  return router.createUrlTree(['/connexion'], { queryParams: { returnUrl } });
}

/** En canMatch, on ne dispose que des segments : l'URL complète (avec ?page=…) vient de la navigation en cours. */
function attemptedUrl(router: Router, segments: UrlSegment[]): string {
  const navigation = router.currentNavigation();
  return navigation
    ? router.serializeUrl(navigation.extractedUrl)
    : `/${segments.map((s) => s.path).join('/')}`;
}
