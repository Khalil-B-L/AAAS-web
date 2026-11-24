import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';
import { Observable, firstValueFrom, of } from 'rxjs';
import { ConfirmDialogService } from '../ui/confirm-dialog';
import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard (UX-8)', () => {
  let confirm: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({ providers: [{ provide: ConfirmDialogService, useValue: { confirm } }] });
  });

  function run(component: HasUnsavedChanges) {
    const route = {} as ActivatedRouteSnapshot;
    const state = {} as RouterStateSnapshot;
    return TestBed.runInInjectionContext(() => unsavedChangesGuard(component, route, state, state));
  }

  it('laisse partir sans dialogue quand rien n’est modifié', () => {
    expect(run({ hasUnsavedChanges: () => false })).toBe(true);
    expect(confirm).not.toHaveBeenCalled();
  });

  it('demande confirmation quand le formulaire est modifié, et laisse partir si on confirme', async () => {
    const result = run({ hasUnsavedChanges: () => true });
    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ confirmLabel: 'Quitter la page', cancelLabel: 'Rester' }));
    expect(await firstValueFrom(result as Observable<boolean>)).toBe(true);
  });

  it('bloque la navigation quand on choisit « Rester »', async () => {
    confirm.mockReturnValue(of(false));
    const result = run({ hasUnsavedChanges: () => true });
    expect(await firstValueFrom(result as Observable<boolean>)).toBe(false);
  });
});
