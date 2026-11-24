import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { buttonByText, problem } from '../../../../../testing/dom';
import { AdminEventsApi } from '../../../../core/api/admin-events.api';
import { FORMATTING_PROVIDERS } from '../../../../core/config/formatting.providers';
import { AdminEventSummaryDto, PagedResult } from '../../../../core/models/api-models';
import { AdminEventList, toPeriodFilter, toStatusFilter } from './admin-event-list';

function item(overrides: Partial<AdminEventSummaryDto> = {}): AdminEventSummaryDto {
  return {
    id: '1',
    title: 'Atelier couture',
    location: 'Tunis',
    startsAt: '2026-12-05T09:00:00+00:00',
    endsAt: '2026-12-05T11:00:00+00:00',
    status: 'Draft',
    capacity: 30,
    price: null,
    pendingCount: 1,
    confirmedCount: 2,
    remainingSeats: 27,
    hasStarted: false,
    hasEnded: false,
    canBeDeleted: true,
    ...overrides,
  };
}

function result(items: AdminEventSummaryDto[], totalCount = items.length, page = 1): PagedResult<AdminEventSummaryDto> {
  return { items, page, pageSize: 20, totalCount };
}

describe('filtres de la liste admin (valeurs d’URL)', () => {
  it.each([
    ['Draft', 'Draft'],
    ['Published', 'Published'],
    ['Cancelled', 'Cancelled'],
    ['draft', null],
    ['', null],
    [undefined, null],
    ['1; DROP TABLE', null],
  ])('statut %j -> %j', (value, expected) => {
    expect(toStatusFilter(value)).toBe(expected);
  });

  it.each([
    ['upcoming', 'upcoming'],
    ['past', 'past'],
    ['all', 'all'],
    ['futur', 'upcoming'],
    [undefined, 'upcoming'],
  ])('période %j -> %j', (value, expected) => {
    expect(toPeriodFilter(value)).toBe(expected);
  });
});

describe('AdminEventList (US-E2)', () => {
  let api: { list: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    harness = undefined;
    api = { list: vi.fn((query: { page: number }) => of(result([item()], 60, query.page))) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'admin/evenements', component: AdminEventList }], withComponentInputBinding()),
        ...FORMATTING_PROVIDERS,
        { provide: AdminEventsApi, useValue: api },
        // Panneau du mat-select sans animation : son ouverture ne dépend plus d'une durée.
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  let harness: RouterTestingHarness | undefined;
  async function open(url: string) {
    harness ??= await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, AdminEventList);
    await harness.fixture.whenStable();
    return { harness: harness!, component, router: TestBed.inject(Router) };
  }

  async function changeFilter(root: HTMLElement, label: string, option: string) {
    const field = Array.from(root.querySelectorAll('mat-form-field')).find((f) => f.textContent?.includes(label))!;
    field.querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
    // Le panneau d'options s'ouvre dans l'overlay (hors du composant) : on attend qu'il contienne l'option.
    const found = await vi.waitFor(() => {
      const match = Array.from(document.querySelectorAll<HTMLElement>('mat-option')).find((o) => o.textContent?.trim() === option);
      if (!match) throw new Error(`Option introuvable : ${option}`);
      return match;
    });
    found.click();
  }

  it('lit statut, période et page dans l’URL et les envoie à l’API', async () => {
    await open('/admin/evenements?status=Published&period=past&page=3');
    expect(api.list).toHaveBeenCalledWith({ status: 'Published', period: 'past', page: 3 });
  });

  it('valeurs par défaut sans paramètre : tous statuts, à venir, page 1', async () => {
    await open('/admin/evenements');
    expect(api.list).toHaveBeenCalledWith({ status: null, period: 'upcoming', page: 1 });
  });

  it('paramètres invalides : repli sur les valeurs par défaut', async () => {
    await open('/admin/evenements?status=zzz&period=zzz&page=-4');
    expect(api.list).toHaveBeenCalledWith({ status: null, period: 'upcoming', page: 1 });
  });

  it('affiche les lignes, les compteurs et le message de liste vide', async () => {
    const { harness } = await open('/admin/evenements');
    const content = (harness.routeNativeElement as HTMLElement).textContent!;
    expect(content).toContain('Atelier couture');
    expect(content).toContain('27 / 30 libres');
    expect(content).toContain('1 en attente, 2 confirmées');

    api.list.mockReturnValue(of(result([])));
    await open('/admin/evenements?status=Cancelled');
    expect(document.body.textContent).toContain('Aucun événement ne correspond à ces filtres.');
  });

  it('changer le statut met l’URL à jour et remet la page à 1', async () => {
    const { harness, router } = await open('/admin/evenements?page=3&period=past');
    await changeFilter(harness.routeNativeElement as HTMLElement, 'Statut', 'Publié');
    await harness.fixture.whenStable();

    const tree = router.parseUrl(router.url);
    expect(tree.queryParams['status']).toBe('Published');
    expect(tree.queryParams['page']).toBeUndefined();
    expect(tree.queryParams['period']).toBe('past'); // les autres filtres sont conservés
    expect(api.list).toHaveBeenLastCalledWith({ status: 'Published', period: 'past', page: 1 });
  });

  it('changer la période remet la page à 1 ; « À venir » retire le paramètre', async () => {
    const { harness, router } = await open('/admin/evenements?page=2&status=Draft&period=past');
    await changeFilter(harness.routeNativeElement as HTMLElement, 'Période', 'À venir');
    await harness.fixture.whenStable();

    const params = router.parseUrl(router.url).queryParams;
    expect(params['period']).toBeUndefined();
    expect(params['page']).toBeUndefined();
    expect(params['status']).toBe('Draft');
  });

  it('la pagination écrit page dans l’URL en gardant les filtres, et page 1 retire le paramètre', async () => {
    const { harness, router } = await open('/admin/evenements?status=Published');
    buttonByText(harness.fixture, 'Suivant').click();
    await harness.fixture.whenStable();
    expect(router.parseUrl(router.url).queryParams).toEqual({ status: 'Published', page: '2' });
    expect(api.list).toHaveBeenLastCalledWith({ status: 'Published', period: 'upcoming', page: 2 });

    buttonByText(harness.fixture, 'Précédent').click();
    await harness.fixture.whenStable();
    expect(router.parseUrl(router.url).queryParams).toEqual({ status: 'Published' });
  });

  it('erreur de l’API : message et « Réessayer » relance la requête', async () => {
    api.list.mockReturnValueOnce(throwError(() => problem(500, 'server_error', 'Une erreur est survenue.')));
    const { harness } = await open('/admin/evenements');
    expect(harness.routeNativeElement!.textContent).toContain('Une erreur est survenue.');
    api.list.mockReturnValue(of(result([item()])));
    buttonByText(harness.fixture, 'Réessayer').click();
    await harness.fixture.whenStable();
    expect(harness.routeNativeElement!.textContent).toContain('Atelier couture');
  });
});
