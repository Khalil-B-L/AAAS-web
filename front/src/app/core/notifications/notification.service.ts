import { Injectable, signal } from '@angular/core';

export interface Notification {
  /** Identifiant croissant : deux messages identiques successifs restent deux notifications distinctes. */
  id: number;
  message: string;
  kind: 'info' | 'success' | 'warning';
}

const DISPLAY_MS = 6000;
/** Message à afficher au prochain démarrage de CET onglet (sessionStorage : propre à l'onglet, vidé à sa fermeture). */
export const PENDING_NOTIFICATION_KEY = 'aaas.notification';

/**
 * Message court affiché en bas de l'écran, centré (position fixe de <app-notification-host>), après une action
 * (« Enregistré. », « Votre session a expiré… »).
 * Un simple signal partagé, lu par <app-notification-host> dans la coquille : pas de MatSnackBar,
 * dont la couche « overlay » alourdirait le bundle initial (chargé par toutes les pages).
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly state = signal<Notification | null>(null);
  readonly current = this.state.asReadonly();
  private nextId = 1;
  private timer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    const pending = takePendingNotification();
    if (pending) {
      this.show(pending.message, pending.kind);
    }
  }

  show(message: string, kind: Notification['kind'] = 'success'): void {
    clearTimeout(this.timer);
    this.state.set({ id: this.nextId++, message, kind });
    this.timer = setTimeout(() => this.state.set(null), DISPLAY_MS);
  }

  /** Message « flash » (≈ TempData d'ASP.NET MVC) : survit à un rechargement complet de la page, affiché une fois. */
  showAfterReload(message: string, kind: Notification['kind'] = 'info'): void {
    try {
      sessionStorage.setItem(PENDING_NOTIFICATION_KEY, JSON.stringify({ message, kind }));
    } catch {
      // Stockage indisponible (navigation privée stricte) : le message est perdu, le rechargement a lieu quand même.
    }
  }

  dismiss(): void {
    clearTimeout(this.timer);
    this.state.set(null);
  }
}

function takePendingNotification(): Pick<Notification, 'message' | 'kind'> | null {
  try {
    const raw = sessionStorage.getItem(PENDING_NOTIFICATION_KEY);
    sessionStorage.removeItem(PENDING_NOTIFICATION_KEY);
    const parsed: unknown = raw ? JSON.parse(raw) : null;
    if (parsed && typeof parsed === 'object' && 'message' in parsed && typeof parsed.message === 'string') {
      const kind = 'kind' in parsed && (parsed.kind === 'success' || parsed.kind === 'warning') ? parsed.kind : 'info';
      return { message: parsed.message, kind };
    }
  } catch {
    // Contenu illisible ou stockage indisponible : rien à afficher.
  }
  return null;
}
