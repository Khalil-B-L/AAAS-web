import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { PAGE_RELOAD } from '../browser/page-reload';
import { NotificationService } from '../notifications/notification.service';
import { decodeJwtPayload, normalizeRoles, tokenLifetimeMs } from './jwt';
import { DEFAULT_AFTER_LOGIN } from './return-url';
import {
  SESSION_STORAGE_KEY,
  StoredSession,
  clearStoredSession,
  readStoredSession,
  writeStoredSession,
} from './session-storage';

export interface Session {
  accessToken: string;
  localDeadline: number;
  userId: string;
  firstName: string;
  roles: string[];
}

export const SESSION_EXPIRED_MESSAGE = 'Votre session a expiré. Veuillez vous reconnecter.';
export const ACCOUNT_CHANGED_MESSAGE =
  'Vous êtes maintenant connecté avec un autre compte (depuis un autre onglet). La page a été rechargée.';
/** Valeur du paramètre `raison` de /connexion qui affiche le message de session expirée. */
export const EXPIRED_REASON = 'expiree';
/** Marge retirée à la durée de vie du jeton : on se déconnecte un peu avant que l'API ne refuse le jeton. */
const SAFETY_MARGIN_MS = 60_000;
/** Préfixes des pages qui exigent une session (authGuard / adminGuard). */
const PROTECTED_PREFIXES = ['/mon-compte', '/admin'];
/** setTimeout n'accepte pas plus de 2^31 − 1 ms (environ 24 jours). */
const MAX_TIMEOUT_MS = 2_147_483_647;

/**
 * État de la session, partagé par toute l'application (singleton : providedIn 'root' ≈ AddSingleton).
 * Restauré au démarrage depuis localStorage, SANS appel réseau (ADR 0015) : les pages publiques ne dépendent
 * jamais de l'API d'authentification. L'API reste juge à chaque requête (401 → expire()).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly router = inject(Router);
  private readonly notifications = inject(NotificationService);
  private readonly reloadPage = inject(PAGE_RELOAD);

  // Signal privé modifiable, exposé en lecture seule : seul ce service change la session.
  private readonly state = signal<Session | null>(null);
  readonly session = this.state.asReadonly();
  readonly isLoggedIn = computed(() => this.state() !== null);
  readonly isAdmin = computed(() => this.state()?.roles.includes('Admin') ?? false);
  readonly firstName = computed(() => this.state()?.firstName ?? '');

  private expiryTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.apply(readStoredSession());

    // Synchronisation des onglets : l'événement « storage » n'est reçu que par les AUTRES onglets.
    const onStorage = (event: StorageEvent) => {
      if (event.key === SESSION_STORAGE_KEY || event.key === null) {
        this.onSessionChangedElsewhere();
      }
    };
    window.addEventListener('storage', onStorage);
    inject(DestroyRef).onDestroy(() => {
      window.removeEventListener('storage', onStorage);
      clearTimeout(this.expiryTimer);
    });
  }

  /** Après une connexion ou une inscription réussie. */
  startSession(accessToken: string): void {
    const claims = decodeJwtPayload(accessToken);
    // Échéance relative à l'horloge du poste, jamais `exp` comparé à Date.now() : un téléphone mal réglé
    // de deux heures rendrait sinon toute session « expirée » dès sa création.
    const lifetime = claims ? tokenLifetimeMs(claims) : 60 * 60_000;
    const stored: StoredSession = {
      accessToken,
      localDeadline: Date.now() + lifetime - SAFETY_MARGIN_MS,
    };
    writeStoredSession(stored);
    this.apply(stored);
  }

  /**
   * Déconnexion volontaire (US-A3) : locale, sans appel à l'API (jeton sans état, ADR 0002).
   * On navigue D'ABORD : si la garde de sortie d'un formulaire modifié est refusée (« Rester »), la session
   * reste ouverte et la saisie est conservée. Effacer avant aurait laissé une page admin sans session.
   */
  logout(): void {
    this.router
      // 'reload' : déjà sur /evenements, la navigation est sinon ignorée et la promesse renvoie false.
      .navigateByUrl(DEFAULT_AFTER_LOGIN, { onSameUrlNavigation: 'reload' })
      .then(
        (navigated) => navigated && this.clear(),
        // Erreur de navigation (morceau de code non téléchargé, hors ligne…) : la déconnexion ne doit pas être bloquée.
        () => this.clear(),
      );
  }

  /**
   * Session terminée par l'échéance locale ou par un 401 de l'API.
   * @param redirect true (401 d'une requête authentifiée) : toujours renvoyer vers la connexion ;
   *                 false (minuterie) : seulement si la page courante est réservée, sinon simple message.
   */
  expire(redirect = false): void {
    if (!this.state()) {
      return;
    }
    this.clear();
    const currentUrl = this.router.url;
    if (redirect || isProtectedUrl(currentUrl)) {
      void this.router.navigate(['/connexion'], {
        queryParams: { returnUrl: currentUrl, raison: EXPIRED_REASON },
      });
    } else {
      this.notifications.show(SESSION_EXPIRED_MESSAGE, 'warning');
    }
  }

  /** Jeton à envoyer, ou null si aucune session valide (l'échéance dépassée déclenche l'expiration). */
  accessToken(): string | null {
    const session = this.state();
    if (!session) {
      return null;
    }
    if (Date.now() >= session.localDeadline) {
      this.expire();
      return null;
    }
    return session.accessToken;
  }

  private apply(stored: StoredSession | null): void {
    clearTimeout(this.expiryTimer);
    const session = stored ? toSession(stored) : null;
    if (!session || Date.now() >= session.localDeadline) {
      if (stored) {
        clearStoredSession();
      }
      this.state.set(null);
      return;
    }
    this.state.set(session);
    const delay = Math.min(session.localDeadline - Date.now(), MAX_TIMEOUT_MS);
    this.expiryTimer = setTimeout(() => this.expire(), delay);
  }

  private clear(): void {
    clearTimeout(this.expiryTimer);
    clearStoredSession();
    this.state.set(null);
  }

  private onSessionChangedElsewhere(): void {
    const wasAdmin = this.isAdmin();
    const previousUserId = this.state()?.userId ?? null;
    this.apply(readStoredSession());
    const currentUserId = this.state()?.userId ?? null;
    const url = this.router.url;
    if (!this.isLoggedIn() && isProtectedUrl(url)) {
      // Déconnexion dans un autre onglet : on quitte la page réservée.
      void this.router.navigate(['/connexion'], { queryParams: { returnUrl: url } });
    } else if (previousUserId && currentUserId && previousUserId !== currentUserId) {
      // Connexion avec un AUTRE compte dans un autre onglet : la page affiche encore les données du compte
      // précédent. Rechargement complet (aucun état de l'ancien compte ne survit, gardes rejouées) ;
      // le message est conservé pour être affiché après le rechargement.
      this.notifications.showAfterReload(ACCOUNT_CHANGED_MESSAGE, 'warning');
      this.reloadPage();
    } else if (wasAdmin && !this.isAdmin() && url.startsWith('/admin')) {
      void this.router.navigateByUrl('/acces-refuse');
    }
  }
}

function toSession(stored: StoredSession): Session | null {
  const claims = decodeJwtPayload(stored.accessToken);
  if (!claims?.sub) {
    return null;
  }
  return {
    accessToken: stored.accessToken,
    localDeadline: stored.localDeadline,
    userId: claims.sub,
    firstName: claims.given_name ?? '',
    roles: normalizeRoles(claims.role),
  };
}

export function isProtectedUrl(url: string): boolean {
  return PROTECTED_PREFIXES.some((prefix) => url === prefix || url.startsWith(`${prefix}/`) || url.startsWith(`${prefix}?`));
}
