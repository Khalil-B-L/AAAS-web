import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { APP_INFO, LOGO_ALT } from '../../core/config/app-info';

/**
 * Logo de l'association (en-tête, pied de page, connexion). N'affiche rien si APP_INFO.logo vaut null.
 * Usage : <app-brand-logo [height]="48" />
 */
@Component({
  selector: 'app-brand-logo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (logo; as l) {
      <!-- width/height en attributs : le navigateur réserve la place AVANT de télécharger l'image, donc
           la page ne « saute » pas à son arrivée (indicateur CLS de Core Web Vitals). -->
      <img [src]="l.url" [alt]="alt" [attr.width]="width()" [attr.height]="height()" />
    }
  `,
  styles: `
    :host {
      display: inline-flex;
    }
    img {
      display: block;
      object-fit: contain;
    }
  `,
})
export class BrandLogo {
  /** Hauteur affichée en pixels ; la largeur suit les proportions du fichier. */
  readonly height = input.required<number>();

  protected readonly logo = APP_INFO.logo;
  protected readonly alt = LOGO_ALT;
  protected readonly width = computed(() =>
    this.logo ? Math.round((this.height() * this.logo.width) / this.logo.height) : 0,
  );
}
