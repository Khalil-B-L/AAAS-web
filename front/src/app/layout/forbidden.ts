import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink } from '@angular/router';

/** Membre connecté qui ouvre une adresse de l'espace admin (adminGuard). */
@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <h1>Accès réservé</h1>
      <p>Cette page est réservée aux administrateurs.</p>
      <a mat-flat-button routerLink="/evenements">Retour aux événements</a>
    </section>
  `,
})
export class Forbidden {}
