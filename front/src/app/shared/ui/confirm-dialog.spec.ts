import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { ConfirmDialogData, ConfirmDialogService } from './confirm-dialog';

describe('ConfirmDialogService (dialogue Material réel)', () => {
  const data: ConfirmDialogData = {
    title: 'Supprimer ?',
    message: 'Définitif.',
    confirmLabel: 'Supprimer',
    cancelLabel: 'Garder',
    danger: true,
  };

  const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;

  function button(label: string): HTMLButtonElement {
    const found = Array.from(overlay().querySelectorAll('button')).find((b) => b.textContent?.trim() === label);
    if (!found) {
      throw new Error(`Bouton introuvable : ${label}`);
    }
    return found;
  }

  beforeEach(() => {
    // Sans animations, ouverture et fermeture ne dépendent plus d'une durée : on attend une condition, pas un délai.
    TestBed.configureTestingModule({ providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }] });
  });

  async function open(d: ConfirmDialogData = data) {
    const results: boolean[] = [];
    TestBed.inject(ConfirmDialogService).confirm(d).subscribe((r) => results.push(r));
    // Le conteneur apparaît avant le contenu (rendu au cycle suivant, en zoneless) : on attend le titre.
    await vi.waitFor(() => expect(overlay()?.textContent).toContain(d.title));
    return results;
  }

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  it('affiche titre, message et libellés, et émet true sur confirmation', async () => {
    const results = await open();
    expect(overlay().textContent).toContain('Supprimer ?');
    expect(overlay().textContent).toContain('Définitif.');
    expect(button('Supprimer').classList.contains('danger')).toBe(true);
    button('Supprimer').click();
    await vi.waitFor(() => expect(results).toEqual([true]));
  });

  it('émet false sur annulation', async () => {
    const results = await open();
    button('Garder').click();
    await vi.waitFor(() => expect(results).toEqual([false]));
  });

  it('émet false (et non undefined) quand le dialogue est fermé sans choix', async () => {
    const results = await open();
    TestBed.inject(MatDialog).closeAll();
    await vi.waitFor(() => expect(results).toEqual([false]));
  });

  it('utilise « Annuler » par défaut et pas de style danger', async () => {
    await open({ title: 't', message: 'm', confirmLabel: 'OK' });
    expect(button('Annuler')).toBeTruthy();
    expect(button('OK').classList.contains('danger')).toBe(false);
  });
});
