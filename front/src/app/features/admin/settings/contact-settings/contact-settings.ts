import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { SettingsApi } from '../../../../core/api/settings.api';
import { toApiError } from '../../../../core/errors/api-error';
import { applyServerErrors } from '../../../../core/errors/apply-server-errors';
import { ContactSettingsDto } from '../../../../core/models/api-models';
import { NotificationService } from '../../../../core/notifications/notification.service';
import { DETAIL_DATE_FORMAT } from '../../../../core/time/date-formats';
import { FORM_FIELD_DEFAULTS } from '../../../../shared/forms/form-field-defaults';
import { FieldErrors } from '../../../../shared/forms/field-errors';
import { HasUnsavedChanges } from '../../../../shared/forms/unsaved-changes.guard';
import { compactPhone, tunisianPhone } from '../../../../shared/forms/validators';
import { formatTunisianPhone, telLink, whatsAppLink } from '../../../../shared/phone';
import { ConfirmDialogService } from '../../../../shared/ui/confirm-dialog';
import { Loading } from '../../../../shared/ui/loading';

const PHONE_PREFIX = '+216 ';

export const CONTACT_CONFLICT_TITLE = 'Numéro modifié par un autre administrateur';

/** Numéro de contact de l'association (US-E8) : enregistrer, changer, retirer, avec `version` (ADR 0013). */
@Component({
  selector: 'app-contact-settings',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    MatButtonModule,
    FieldErrors,
    Loading,
  ],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './contact-settings.html',
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
})
export class ContactSettings implements HasUnsavedChanges {
  private readonly api = inject(SettingsApi);
  private readonly confirmDialog = inject(ConfirmDialogService);
  private readonly notifications = inject(NotificationService);

  protected readonly form = inject(NonNullableFormBuilder).group({
    phoneNumber: [PHONE_PREFIX, [Validators.required, tunisianPhone]],
    whatsApp: [false],
  });

  /** Dernière fiche reçue ; sa `version` est renvoyée à chaque écriture. */
  protected readonly settings = signal<ContactSettingsDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly formatPhone = formatTunisianPhone;
  protected readonly telLink = telLink;
  protected readonly whatsAppLink = whatsAppLink;
  protected readonly detailFormat = DETAIL_DATE_FORMAT;

  constructor() {
    // La case WhatsApp n'a de sens qu'avec un numéro. takeUntilDestroyed : désabonnement automatique
    // quand le composant est détruit (sinon l'abonnement à valueChanges survivrait à la page).
    this.form.controls.phoneNumber.valueChanges
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe(() => this.updateWhatsAppState());
    this.load();
  }

  hasUnsavedChanges(): boolean {
    return this.form.dirty;
  }

  protected onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  protected load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.api.getContact().subscribe({
      next: (settings) => {
        this.apply(settings);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(toApiError(error).message);
        this.loading.set(false);
      },
    });
  }

  protected save(): void {
    const settings = this.settings();
    if (!settings || this.busy()) {
      return;
    }
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    const { phoneNumber, whatsApp } = this.form.getRawValue();
    this.startRequest();
    this.api
      .saveContact({
        phoneNumber,
        whatsApp: this.form.controls.whatsApp.enabled && whatsApp,
        version: settings.version,
      })
      .subscribe({
        next: (saved) => {
          this.apply(saved);
          this.busy.set(false);
          this.notifications.show('Numéro enregistré.');
        },
        error: (error: unknown) => this.handleError(error),
      });
  }

  protected remove(): void {
    const settings = this.settings();
    if (!settings || this.busy()) {
      return;
    }
    this.confirmDialog
      .confirm({
        title: 'Retirer le numéro de contact ?',
        message: 'Les membres ne verront plus de numéro.',
        confirmLabel: 'Retirer le numéro',
        cancelLabel: 'Annuler',
        danger: true,
      })
      .subscribe((confirmed) => {
        if (!confirmed) {
          return;
        }
        this.startRequest();
        this.api.removeContact(settings.version).subscribe({
          next: (removed) => {
            this.apply(removed);
            this.busy.set(false);
            this.notifications.show('Numéro retiré.');
          },
          error: (error: unknown) => this.handleError(error),
        });
      });
  }

  private apply(settings: ContactSettingsDto): void {
    this.settings.set(settings);
    this.form.reset({
      phoneNumber: settings.phoneNumber ? formatTunisianPhone(settings.phoneNumber) : PHONE_PREFIX,
      whatsApp: settings.whatsApp,
    });
    this.updateWhatsAppState();
  }

  private updateWhatsAppState(): void {
    const hasNumber = compactPhone(this.form.controls.phoneNumber.value).length > '+216'.length;
    const whatsApp = this.form.controls.whatsApp;
    // emitEvent: false : pas de boucle valueChanges ; la case reste dans le formulaire mais grisée.
    if (hasNumber && whatsApp.disabled) {
      whatsApp.enable({ emitEvent: false });
    } else if (!hasNumber && whatsApp.enabled) {
      whatsApp.setValue(false, { emitEvent: false });
      whatsApp.disable({ emitEvent: false });
    }
  }

  private startRequest(): void {
    this.busy.set(true);
    this.errorMessage.set(null);
  }

  private handleError(error: unknown): void {
    const apiError = toApiError(error);
    this.busy.set(false);
    if (apiError.code === 'validation_failed') {
      const unmatched = applyServerErrors(this.form, apiError);
      this.errorMessage.set([apiError.message, ...unmatched].join(' '));
    } else if (apiError.code === 'concurrent_update') {
      this.confirmDialog
        .confirm({
          title: CONTACT_CONFLICT_TITLE,
          message: apiError.message,
          confirmLabel: 'Recharger',
          cancelLabel: 'Fermer',
        })
        .subscribe((reload) => (reload ? this.load() : this.errorMessage.set(apiError.message)));
    } else {
      this.errorMessage.set(apiError.message);
    }
  }
}
