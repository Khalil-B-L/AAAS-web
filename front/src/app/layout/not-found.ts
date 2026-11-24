import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

/**
 * « Contenu introuvable » (US-B2, US-C2). Sert de route `**` ET de bloc affiché par une page de détail
 * qui reçoit 404 : l'URL demandée reste dans la barre d'adresse.
 */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <h1>Contenu introuvable</h1>
      <p>Cette page n'existe pas ou n'est plus disponible.</p>
      <a mat-flat-button routerLink="/evenements">Retour aux événements</a>
    </section>
  `,
})
export class NotFound {}
