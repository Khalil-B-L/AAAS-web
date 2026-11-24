import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ContactStore } from '../../core/contact/contact.store';
import { formatTunisianPhone, telLink, whatsAppLink } from '../phone';

/** Compteur de module : partagé par toutes les instances (≈ un champ static), pour des id uniques dans la page. */
let nextId = 0;

/**
 * « Contacter l'association » (US-D4, R8, libelles-ecrans.md § 8) : numéro, lien d'appel, lien WhatsApp si le numéro
 * y est joignable. Lit le ContactStore : le numéro n'est demandé qu'une fois, même si plusieurs cartes sont affichées.
 */
@Component({
  selector: 'app-contact-card',
  imports: [MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="contact-card" [attr.aria-labelledby]="titleId">
      <h3 [id]="titleId">Contacter l'association</h3>
      @if (store.contact(); as contact) {
        @if (contact.isConfigured && contact.phoneNumber) {
          <p class="number">{{ formatPhone(contact.phoneNumber) }}</p>
          <div class="actions">
            <a mat-flat-button [href]="telLink(contact.phoneNumber)">Appeler</a>
            @if (contact.whatsApp) {
              <!-- noopener : la page WhatsApp ouverte ne peut pas piloter cet onglet (window.opener). -->
              <a mat-stroked-button [href]="whatsAppLink(contact.phoneNumber)" target="_blank" rel="noopener noreferrer">
                Écrire sur WhatsApp
              </a>
            }
          </div>
        } @else {
          <p>L'association n'a pas communiqué de numéro pour le moment.</p>
        }
      } @else if (store.hasError()) {
        <p>Le numéro de l'association n'a pas pu être chargé.</p>
        <button mat-stroked-button type="button" (click)="store.reload()">Réessayer</button>
      } @else {
        <p class="muted">Chargement…</p>
      }
    </section>
  `,
  styles: `
    .contact-card {
      margin: 1rem 0;
      padding: 1rem;
      border-radius: 8px;
      border: 1px solid var(--mat-sys-outline-variant);
    }

    h3 {
      margin: 0 0 0.5rem;
    }

    .number {
      font: var(--mat-sys-title-large);
      margin: 0;
    }
  `,
})
export class ContactCard {
  // Plusieurs cartes peuvent s'afficher dans la même page (une par demande en attente) : un id fixe serait
  // dupliqué et aria-labelledby désignerait le mauvais titre.
  protected readonly titleId = `contact-card-title-${++nextId}`;
  protected readonly store = inject(ContactStore);
  protected readonly formatPhone = formatTunisianPhone;
  protected readonly telLink = telLink;
  protected readonly whatsAppLink = whatsAppLink;

  constructor() {
    this.store.ensureLoaded();
  }
}
