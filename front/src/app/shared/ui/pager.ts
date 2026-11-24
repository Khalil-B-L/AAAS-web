import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

/**
 * « Précédent — Page 2 sur 3 — Suivant » (ADR 0016 § 5). Masqué s'il n'y a qu'une page.
 * Composant « bête » : il ne navigue pas lui-même, il émet le numéro choisi (output) et le parent met à jour l'URL.
 */
@Component({
  selector: 'app-pager',
  imports: [MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (totalPages() > 1) {
      <nav class="pager" aria-label="Pagination">
        <button mat-stroked-button type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">
          Précédent
        </button>
        <span aria-live="polite">Page {{ page() }} sur {{ totalPages() }}</span>
        <button mat-stroked-button type="button" [disabled]="page() >= totalPages()" (click)="go(page() + 1)">
          Suivant
        </button>
      </nav>
    }
  `,
  styles: `
    .pager {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 1rem;
      margin: 1.5rem 0;
    }
  `,
})
export class Pager {
  // input() : entrée du composant sous forme de signal (≈ [Parameter] de Blazor). required = obligatoire.
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  // output() : événement émis vers le parent, qui l'écoute avec (pageChange)="...".
  readonly pageChange = output<number>();

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));

  protected go(page: number): void {
    this.pageChange.emit(page);
  }
}

/**
 * Transformation d'un paramètre d'URL en numéro de page : absent, non numérique ou < 1 → 1.
 * Le front n'envoie ainsi jamais un paramètre qui provoquerait un 400 de l'API.
 */
export function toPageNumber(value: unknown): number {
  const page = typeof value === 'number' ? value : Number.parseInt(String(value ?? ''), 10);
  return Number.isInteger(page) && page >= 1 && page <= 10_000 ? page : 1;
}
