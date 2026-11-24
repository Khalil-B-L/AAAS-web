import { ChangeDetectionStrategy, Component } from '@angular/core';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-loading',
  imports: [MatProgressSpinnerModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="loading" role="status">
      <mat-spinner diameter="40" />
      <span class="visually-hidden">Chargement…</span>
    </div>
  `,
  styles: `
    .loading {
      display: flex;
      justify-content: center;
      padding: 2rem;
    }
  `,
})
export class Loading {}
