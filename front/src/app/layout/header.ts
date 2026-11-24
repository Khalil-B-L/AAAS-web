import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { APP_INFO } from '../core/config/app-info';
import { BrandLogo } from '../shared/ui/brand-logo';

/** En-tête : logo et nom de l'association, menu selon la session (visiteur, membre, admin), menu repliable sur téléphone. */
@Component({
  selector: 'app-header',
  imports: [RouterLink, RouterLinkActive, MatButtonModule, BrandLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './header.html',
  styleUrl: './header.scss',
})
export class Header {
  // inject() ≈ injection par constructeur : on récupère le singleton AuthService.
  protected readonly auth = inject(AuthService);
  protected readonly appInfo = APP_INFO;
  protected readonly menuOpen = signal(false);

  protected toggleMenu(): void {
    this.menuOpen.update((open) => !open);
  }

  protected closeMenu(): void {
    this.menuOpen.set(false);
  }

  protected logout(): void {
    this.closeMenu();
    this.auth.logout();
  }
}
