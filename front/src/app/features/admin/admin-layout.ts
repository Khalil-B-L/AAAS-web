import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

/** Cadre de l'espace admin : sous-menu, puis la page admin courante dans un <router-outlet> imbriqué. */
@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="admin-nav" aria-label="Administration">
      <a mat-button routerLink="evenements" routerLinkActive="active">Événements</a>
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
export class AdminLayout {}
