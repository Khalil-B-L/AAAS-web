import { Injectable, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { SettingsApi } from '../api/settings.api';
import { AuthService } from '../auth/auth.service';
import { ContactSettingsDto } from '../models/api-models';

/**
 * Numéro de contact de l'association (US-D4, RG-10), lu UNE fois par session et partagé par tous les écrans
 * (panneau de réservation, « Mes réservations »). Singleton (providedIn 'root' ≈ AddSingleton).
 * Jamais recopié dans les réponses de réservation (ADR 0021) : un changement de numéro vaut pour les anciennes demandes.
 */
@Injectable({ providedIn: 'root' })
export class ContactStore {
  private readonly api = inject(SettingsApi);
  private readonly auth = inject(AuthService);

  /** Chargement paresseux : rien n'est demandé tant qu'aucun écran n'en a besoin. */
  private readonly wanted = signal(false);

  // params dépend de l'utilisateur : un autre compte (ou une déconnexion) relance ou met au repos la ressource.
  // undefined = ressource au repos : un visiteur n'appelle jamais l'API (il recevrait 401).
  private readonly resource = rxResource({
    params: () => (this.wanted() && this.auth.isLoggedIn() ? { userId: this.auth.session()?.userId } : undefined),
    stream: () => this.api.getContact(),
  });

  readonly contact = computed<ContactSettingsDto | null>(() => (this.resource.hasValue() ? this.resource.value() : null));
  readonly isLoading = this.resource.isLoading;
  readonly hasError = computed(() => this.resource.error() !== undefined);

  /** À appeler par un écran qui affiche le numéro ; sans effet après le premier appel. */
  ensureLoaded(): void {
    this.wanted.set(true);
  }

  reload(): void {
    this.resource.reload();
  }
}
