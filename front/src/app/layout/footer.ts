import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { APP_INFO } from '../core/config/app-info';
import { BrandLogo } from '../shared/ui/brand-logo';

@Component({
  selector: 'app-footer',
  imports: [RouterLink, BrandLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer class="footer">
      <div class="footer__brand">
        @if (appInfo.logo) {
          <span class="footer__badge"><app-brand-logo [height]="40" /></span>
        }
        <span>© {{ year }} {{ appInfo.name }}</span>
      </div>
      <a routerLink="/confidentialite">Politique de confidentialité</a>
    </footer>
  `,
  styles: `
    .footer {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 1rem 1.5rem;
      padding: 1.25rem 1.5rem;
      background: var(--aaas-green);
      color: var(--aaas-white);
      font-weight: 700;
      // Variable redéfinie pour cette zone : le cadre de focus global (styles.scss) devient blanc sur le vert.
      --aaas-focus: var(--aaas-white);
    }
    .footer__brand {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    // Rond blanc de taille fixe autour du logo (fond blanc du JPG fondu dans le rond). Logo de 43 × 40 px :
    // ses coins restent à l'intérieur du cercle de 64 px (demi-diagonale 29 px < rayon 32 px).
    .footer__badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 64px;
      height: 64px;
      border-radius: 50%;
      background: var(--aaas-white);
      flex-shrink: 0;
    }
    a {
      color: var(--aaas-white);
    }
  `,
})
export class Footer {
  protected readonly appInfo = APP_INFO;
  protected readonly year = new Date().getFullYear();
}
