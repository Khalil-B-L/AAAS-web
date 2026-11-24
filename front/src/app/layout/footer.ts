import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { APP_INFO } from '../core/config/app-info';

@Component({
  selector: 'app-footer',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer class="footer">
      <a routerLink="/confidentialite">Politique de confidentialité</a>
      <span>© {{ year }} {{ appInfo.name }}</span>
    </footer>
  `,
  styles: `
    .footer {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: 1.5rem;
      padding: 1.5rem 1rem;
      background: var(--mat-sys-surface-container);
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class Footer {
  protected readonly appInfo = APP_INFO;
  protected readonly year = new Date().getFullYear();
}
