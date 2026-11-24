import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Observable } from 'rxjs';
import { FORM_FIELD_DEFAULTS } from '../../../shared/forms/form-field-defaults';

export const REFUSAL_REASON_MAX_LENGTH = 500;

/** Résultat du dialogue : `undefined` si l'admin ferme sans refuser. */
export interface RefuseDialogResult {
  reason: string | null;
}

/**
 * « Refuser cette demande ? » (US-E6, libelles-ecrans.md § 10) : motif facultatif, 500 caractères au plus.
 * Le dialogue ne fait pas l'appel HTTP : il rend le motif à l'écran (MatDialogRef.close(résultat)),
 * comme une boîte modale qui renvoie un DialogResult.
 */
@Component({
  selector: 'app-refuse-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>Refuser cette demande ?</h2>
    <mat-dialog-content>
      <mat-form-field class="reason">
        <mat-label>Motif (facultatif)</mat-label>
        <!-- maxlength natif : le navigateur bloque la saisie au-delà ; Validators.maxLength reste le filet (collage). -->
        <textarea matInput [formControl]="reason" rows="4" [attr.maxlength]="max"></textarea>
        <mat-hint>Le motif sera visible par le membre. Ne mettez pas de données personnelles dans le motif.</mat-hint>
        <mat-hint align="end">{{ reason.value.length }} / {{ max }}</mat-hint>
        @if (reason.hasError('maxlength')) {
          <mat-error>Le motif ne doit pas dépasser 500 caractères.</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" [mat-dialog-close]="undefined">Fermer</button>
      <button mat-flat-button type="button" class="danger" [disabled]="reason.invalid" (click)="refuse()">
        Refuser la demande
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    .reason {
      width: 100%;
      min-width: min(28rem, 80vw);
    }
    .danger {
      --mat-button-filled-container-color: var(--mat-sys-error);
      --mat-button-filled-label-text-color: var(--mat-sys-on-error);
      --mat-button-filled-state-layer-color: var(--mat-sys-on-error);
    }
  `,
})
export class RefuseDialog {
  private readonly ref = inject<MatDialogRef<RefuseDialog, RefuseDialogResult>>(MatDialogRef);

  protected readonly max = REFUSAL_REASON_MAX_LENGTH;
  // FormControl typé : nonNullable → la valeur est toujours une string (reset() remet '' et non null).
  protected readonly reason = new FormControl('', {
    nonNullable: true,
    validators: [Validators.maxLength(REFUSAL_REASON_MAX_LENGTH)],
  });

  protected refuse(): void {
    if (this.reason.invalid) {
      return;
    }
    const trimmed = this.reason.value.trim();
    this.ref.close({ reason: trimmed === '' ? null : trimmed });
  }
}

/** Ouvre le dialogue de refus ; émet le motif choisi, ou `undefined` si l'admin ferme. */
export function openRefuseDialog(dialog: MatDialog): Observable<RefuseDialogResult | undefined> {
  return dialog.open<RefuseDialog, void, RefuseDialogResult>(RefuseDialog, { maxWidth: '36rem' }).afterClosed();
}
