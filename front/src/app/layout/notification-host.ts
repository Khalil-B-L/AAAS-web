import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NotificationService } from '../core/notifications/notification.service';

/** Zone d'annonce (role="status" : lue par les lecteurs d'écran sans voler le focus). */
@Component({
  selector: 'app-notification-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="host" role="status" aria-live="polite">
      @if (notifications.current(); as n) {
        <div [class]="'toast toast--' + n.kind">
          <span>{{ n.message }}</span>
          <button type="button" class="toast__close" (click)="notifications.dismiss()" aria-label="Fermer le message">
            ×
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .host {
      position: fixed;
      bottom: 1rem;
      left: 50%;
      transform: translateX(-50%);
      z-index: 1000;
      width: min(36rem, calc(100% - 2rem));
    }
    .toast {
      display: flex;
      align-items: center;
      gap: 1rem;
      padding: 0.75rem 1rem;
      border-radius: 8px;
      box-shadow: var(--mat-sys-level3);
      background: var(--mat-sys-inverse-surface);
      color: var(--mat-sys-inverse-on-surface);
    }
    .toast--warning {
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .toast span {
      flex: 1;
    }
    .toast__close {
      background: none;
      border: none;
      color: inherit;
      font-size: 1.5rem;
      line-height: 1;
      cursor: pointer;
    }
  `,
})
export class NotificationHost {
  protected readonly notifications = inject(NotificationService);
}
