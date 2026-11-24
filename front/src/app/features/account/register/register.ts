import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { AuthApi } from '../../../core/api/auth.api';
import { AuthService } from '../../../core/auth/auth.service';
import { safeReturnUrl } from '../../../core/auth/return-url';
import { toApiError } from '../../../core/errors/api-error';
import { applyServerErrors } from '../../../core/errors/apply-server-errors';
import { FORM_FIELD_DEFAULTS } from '../../../shared/forms/form-field-defaults';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { passwordStrength, tunisianPhone } from '../../../shared/forms/validators';

/** Inscription (US-A1) : compte créé avec le rôle User, puis connexion automatique (réponse 201 avec jeton). */
@Component({
  selector: 'app-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatCheckboxModule,
    FieldErrors,
  ],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './register.html',
})
export class Register {
  private readonly authApi = inject(AuthApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string>();

  // Noms des contrôles = clés JSON de l'API : une erreur 400 se recopie sans table de correspondance.
  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    // Préfixe prérempli : l'utilisateur n'a plus qu'à taper ses 8 chiffres (RG-11).
    phoneNumber: ['+216 ', [Validators.required, tunisianPhone]],
    password: [
      '',
      [Validators.required, Validators.minLength(8), Validators.maxLength(128), passwordStrength],
    ],
    // requiredTrue : la case doit être cochée (erreur « required » sinon).
    acceptPrivacyPolicy: [false, [Validators.requiredTrue]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly loginQueryParams = computed(() => (this.returnUrl() ? { returnUrl: this.returnUrl() } : {}));

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    this.submitting.set(true);
    this.errorMessage.set(null);

    this.authApi.register(this.form.getRawValue()).subscribe({
      next: (response) => {
        this.auth.startSession(response.accessToken);
        void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (error: unknown) => {
        const apiError = toApiError(error);
        if (apiError.code === 'email_taken') {
          // 409 sans `errors` : le message va sous le champ e-mail.
          this.form.controls.email.setErrors({ server: [apiError.message] });
          this.form.controls.email.markAsTouched();
        } else {
          const unmatched = applyServerErrors(this.form, apiError);
          this.errorMessage.set(unmatched.length ? unmatched.join(' ') : apiError.message);
        }
        this.submitting.set(false);
      },
    });
  }
}
