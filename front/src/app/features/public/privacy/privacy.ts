import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { APP_INFO } from '../../../core/config/app-info';

/**
 * Politique de confidentialité (US-F1), texte provisoire : docs/fonctionnel/politique-confidentialite.md,
 * partie « texte à publier », version 1.0. Toute modification du texte se fait d'abord dans ce document,
 * puis ici, et s'accompagne d'une nouvelle version (PrivacyPolicy:CurrentVersion côté API).
 */
@Component({
  selector: 'app-privacy',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './privacy.html',
  styleUrl: './privacy.scss',
})
export class Privacy {
  protected readonly info = APP_INFO;
}
