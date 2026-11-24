import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { PendingCountStore } from './reservations/admin-reservation-actions';

/** Cadre de l'espace admin : sous-menu, puis la page admin courante dans un <router-outlet> imbriqué. */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="admin-nav" aria-label="Administration">
      <a mat-button routerLink="evenements" routerLinkActive="active">Événements</a>
      <a mat-button routerLink="demandes" routerLinkActive="active">{{ requestsLabel() }}</a>
      <a mat-button routerLink="parametres" routerLinkActive="active">Numéro de contact</a>
    </nav>
    <router-outlet />
  `,
  styles: `
    .admin-nav {
      display: flex;
      flex-wrap: wrap;
      gap: 0.25rem;
      justify-content: center;
      padding: 0.5rem 1rem;
      border-bottom: 1px solid var(--mat-sys-outline-variant);
    }
    .active {
      text-decoration: underline;
      text-underline-offset: 4px;
    }
  `,
})
export class AdminLayout {
  private readonly pendingCount = inject(PendingCountStore);

  /** « Demandes (3) » ; « Demandes » seul s'il n'y en a aucune ou si le compte n'a pas pu être lu (libellés § 2). */
  protected readonly requestsLabel = computed(() => {
    const count = this.pendingCount.count();
    return count ? `Demandes (${count})` : 'Demandes';
  });

  constructor() {
    // À chaque entrée dans l'espace admin ; les pages le rafraîchissent ensuite après chaque action.
    this.pendingCount.refresh();
  }
}
