import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { AuthApi } from '../../../core/api/auth.api';
import { AuthService, EXPIRED_REASON, SESSION_EXPIRED_MESSAGE } from '../../../core/auth/auth.service';
import { safeReturnUrl } from '../../../core/auth/return-url';
import { toApiError } from '../../../core/errors/api-error';
import { applyServerErrors } from '../../../core/errors/apply-server-errors';
import { FORM_FIELD_DEFAULTS } from '../../../shared/forms/form-field-defaults';
import { FieldErrors } from '../../../shared/forms/field-errors';
import { BrandLogo } from '../../../shared/ui/brand-logo';

/** Connexion (US-A2). Après succès : retour à `returnUrl` (validé) ou aux événements (UX-10). */
@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    FieldErrors,
    BrandLogo,
  ],
  providers: [FORM_FIELD_DEFAULTS],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.html',
  styles: `
    .login-logo {
      display: flex;
      justify-content: center;
      margin-bottom: 1rem;
    }
  `,
})
export class Login {
  private readonly authApi = inject(AuthApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  // Paramètres de requête (?returnUrl=…&raison=expiree) reçus grâce à withComponentInputBinding().
  readonly returnUrl = input<string>();
  readonly raison = input<string>();

  // Reactive form typé : form.controls.email est un FormControl<string> (NonNullable : reset() remet '' et non null).
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });

  protected readonly submitting = signal(false);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly expiredMessage = computed(() =>
    this.raison() === EXPIRED_REASON && !this.errorMessage() ? SESSION_EXPIRED_MESSAGE : null,
  );
  protected readonly registerQueryParams = computed(() =>
    this.returnUrl() ? { returnUrl: this.returnUrl() } : {},
  );

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

    // Observable « froid » : la requête ne part qu'au subscribe(). HttpClient émet une fois puis se termine.
    this.authApi.login(this.form.getRawValue()).subscribe({
      next: (response) => {
        this.auth.startSession(response.accessToken);
        void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (error: unknown) => {
        const apiError = toApiError(error);
        applyServerErrors(this.form, apiError);
        // invalid_credentials, account_locked : message de l'API (avec la durée de blocage) dans le bandeau.
        this.errorMessage.set(apiError.message);
        if (apiError.code === 'invalid_credentials' || apiError.code === 'account_locked') {
          this.form.controls.password.reset();
        }
        // État affiché = signal : le template se met à jour même en zoneless.
        this.submitting.set(false);
      },
    });
  }
}
