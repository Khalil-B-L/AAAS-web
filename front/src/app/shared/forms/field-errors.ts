import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormGroupDirective, NgForm } from '@angular/forms';
import { ErrorStateMatcher } from '@angular/material/core';
import { startWith, switchMap } from 'rxjs';
import { messagesFor } from './validation-messages';

/**
 * Affiche les messages d'erreur d'un champ, à placer dans <mat-error> (ou seul, pour une case à cocher).
 * Les erreurs d'un FormControl ne sont pas des signals : en zoneless + OnPush, le composant ne serait pas
 * réaffiché quand elles changent. On convertit donc le flux `control.events` (valeur, statut, touché…)
 * en signal, que `messages` lit : chaque événement du contrôle recalcule les messages.
 */
@Component({
  selector: 'app-field-errors',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      @for (message of messages(); track message) {
        <span class="field-error">{{ message }}</span>
      }
    }
  `,
  styles: `
    :host {
      display: contents;
    }
    .field-error {
      display: block;
    }
  `,
})
export class FieldErrors {
  readonly control = input.required<AbstractControl>();
  /** Nom du champ (= clé JSON de l'API), pour choisir les messages dans validation-messages.ts. */
  readonly field = input.required<string>();
  /** Messages supplémentaires (erreur portée par le groupe, ex. « La fin doit être après le début. »). */
  readonly extra = input<string[]>([]);

  // toObservable(signal) → Observable ; switchMap : quand le contrôle change, on suit les événements du nouveau.
  private readonly controlEvents = toSignal(
    toObservable(this.control).pipe(switchMap((control) => control.events.pipe(startWith(null)))),
  );

  protected readonly messages = computed(() => {
    this.controlEvents();
    return [...messagesFor(this.field(), this.control().errors), ...this.extra()];
  });

  /** Erreurs affichées après que le champ a été quitté, ou après une tentative d'envoi (markAllAsTouched). */
  protected readonly visible = computed(() => {
    this.controlEvents();
    return this.control().touched;
  });
}

/**
 * ErrorStateMatcher : règle de Material qui décide si un champ s'affiche « en erreur » (rouge + <mat-error>).
 * Celui-ci ajoute une erreur du GROUPE (ex. endBeforeStart) au champ concerné.
 */
export class GroupErrorStateMatcher implements ErrorStateMatcher {
  constructor(private readonly groupErrorKey: string) {}

  isErrorState(control: AbstractControl | null, form: FormGroupDirective | NgForm | null): boolean {
    if (!control) {
      return false;
    }
    const touched = control.touched || !!form?.submitted;
    return touched && (control.invalid || !!control.parent?.hasError(this.groupErrorKey));
  }
}
