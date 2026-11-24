import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { Router, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, query, submit, text, type } from '../../testing/dom';
import { AdminNewsApi } from '../core/api/admin-news.api';
import { AdminReservationsApi } from '../core/api/admin-reservations.api';
import { NewsApi } from '../core/api/news.api';
import { FORMATTING_PROVIDERS } from '../core/config/formatting.providers';
import { AdminNewsDto, AdminNewsSummaryDto, NewsDetailDto, NewsSummaryDto, PagedResult } from '../core/models/api-models';
import { NotificationService } from '../core/notifications/notification.service';
import { ConfirmDialogService } from '../shared/ui/confirm-dialog';
import { AdminLayout } from './admin/admin-layout';
import { NEWS_CONFLICT_MESSAGE, NEWS_CONFLICT_TITLE, AdminNewsForm } from './admin/news/admin-news-form/admin-news-form';
import { AdminNewsList } from './admin/news/admin-news-list/admin-news-list';
import { NewsDetail } from './public/news-detail/news-detail';
import { NewsList } from './public/news-list/news-list';

const ID_A = '6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b';
const ID_B = '0b6f7c1e-3a52-4d8e-9f10-2c4b5a6d7e81';
const HOSTILE = '<script>alert(1)</script><img src=x onerror=alert(1)><b>gras</b>';
const FORBIDDEN_ELEMENTS = 'script, img, b';

function adminNews(id: string, overrides: Partial<AdminNewsDto> = {}): AdminNewsDto {
  return {
    id,
    title: `Titre ${id.slice(0, 4)}`,
    content: `Texte ${id.slice(0, 4)}`,
    status: 'Draft',
    publishedAt: null,
    createdAt: '2026-11-24T09:00:00+00:00',
    updatedAt: '2026-11-24T09:00:00+00:00',
    version: `v-${id.slice(0, 4)}`,
    ...overrides,
  };
}

function pageOf<T>(items: T[], totalCount = items.length, page = 1, pageSize = 10): PagedResult<T> {
  return { items, page, pageSize, totalCount };
}

function root(fixture: { nativeElement: unknown }): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

describe('AdminNewsForm : fiche changée pendant un envoi (isStale) et dialogue 409', () => {
  let api: Record<'get' | 'create' | 'update' | 'publish' | 'unpublish' | 'delete', ReturnType<typeof vi.fn>>;
  let confirm: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    api = { get: vi.fn(), create: vi.fn(), update: vi.fn(), publish: vi.fn(), unpublish: vi.fn(), delete: vi.fn() };
    api.get.mockImplementation((id: string) => of(adminNews(id)));
    confirm = vi.fn(() => of(true));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: AdminNewsApi, useValue: api },
        { provide: ConfirmDialogService, useValue: { confirm } },
      ],
    });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });

  async function open(id: string | undefined) {
    const fixture = TestBed.createComponent(AdminNewsForm);
    if (id) {
      fixture.componentRef.setInput('id', id);
    }
    await fixture.whenStable();
    return fixture;
  }

  async function switchTo(fixture: Awaited<ReturnType<typeof open>>, id: string) {
    fixture.componentRef.setInput('id', id);
    await fixture.whenStable();
  }

  const notification = () => TestBed.inject(NotificationService).current();
  const titleValue = (fixture: Awaited<ReturnType<typeof open>>) =>
    query<HTMLInputElement>(fixture, 'input[formControlName=title]').value;

  it('PUT de la fiche A encore en vol quand on ouvre B : la réponse de A ne remplace ni le formulaire ni la version de B', async () => {
    const fixture = await open(ID_A);
    const pending = new Subject<AdminNewsDto>();
    api.update.mockReturnValueOnce(pending);
    type(fixture, 'input[formControlName=title]', 'A modifiée');
    submit(fixture);
    await fixture.whenStable();

    await switchTo(fixture, ID_B);
    expect(titleValue(fixture)).toBe(`Titre ${ID_B.slice(0, 4)}`);

    pending.next(adminNews(ID_A, { title: 'A modifiée', version: 'v-A-2' }));
    pending.complete();
    await fixture.whenStable();

    expect(titleValue(fixture)).toBe(`Titre ${ID_B.slice(0, 4)}`); // B intact
    expect(notification()?.message).toBe('Enregistré.'); // l'utilisateur est quand même prévenu
    // Le prochain enregistrement de B part avec la version de B, pas celle de A.
    api.update.mockReturnValueOnce(of(adminNews(ID_B, { version: 'v-B-2' })));
    type(fixture, 'input[formControlName=title]', 'B modifiée');
    submit(fixture);
    await fixture.whenStable();
    expect(api.update).toHaveBeenLastCalledWith(ID_B, expect.objectContaining({ version: `v-${ID_B.slice(0, 4)}` }));
  });

  it('envoi en vol pour A puis B ouverte : le bouton « Enregistrer » de B n’est pas bloqué par l’envoi de A une fois terminé', async () => {
    const fixture = await open(ID_A);
    const pending = new Subject<AdminNewsDto>();
    api.update.mockReturnValueOnce(pending);
    type(fixture, 'input[formControlName=title]', 'A modifiée');
    submit(fixture);
    await fixture.whenStable();
    await switchTo(fixture, ID_B);

    pending.next(adminNews(ID_A));
    pending.complete();
    await fixture.whenStable();

    expect(buttonByText(fixture, 'Enregistrer').disabled).toBe(false);
    expect(root(fixture).textContent).not.toContain('Envoi en cours');
  });

  it('erreur 409 de A reçue quand B est affichée : simple avertissement, aucun dialogue de conflit, B intacte', async () => {
    const fixture = await open(ID_A);
    const pending = new Subject<AdminNewsDto>();
    api.update.mockReturnValueOnce(pending);
    type(fixture, 'input[formControlName=title]', 'A modifiée');
    submit(fixture);
    await fixture.whenStable();
    await switchTo(fixture, ID_B);

    pending.error(problem(409, 'concurrent_update', 'Modifiée.'));
    await fixture.whenStable();

    expect(confirm).not.toHaveBeenCalled();
    expect(notification()?.message).toBe('Modifiée.');
    expect(titleValue(fixture)).toBe(`Titre ${ID_B.slice(0, 4)}`);
  });

  it('« Publier » de A en vol puis B ouverte : l’état de B (brouillon) n’est pas écrasé par la fiche publiée de A', async () => {
    const fixture = await open(ID_A);
    const pending = new Subject<AdminNewsDto>();
    api.publish.mockReturnValueOnce(pending);
    buttonByText(fixture, 'Publier').click();
    await fixture.whenStable();
    await switchTo(fixture, ID_B);

    pending.next(adminNews(ID_A, { status: 'Published', publishedAt: '2026-11-24T10:00:00+00:00' }));
    pending.complete();
    await fixture.whenStable();

    expect(buttonByText(fixture, 'Publier')).toBeTruthy(); // B reste un brouillon
    expect(() => buttonByText(fixture, 'Dépublier')).toThrow();
    expect(notification()?.message).toBe("L'actualité est publiée.");
  });

  it('création en vol puis fiche existante ouverte : pas de navigation vers la fiche créée, bouton libéré', async () => {
    const fixture = await open(undefined);
    const pending = new Subject<AdminNewsDto>();
    api.create.mockReturnValueOnce(pending);
    type(fixture, 'input[formControlName=title]', 'Nouvelle');
    type(fixture, 'textarea[formControlName=content]', 'Texte');
    submit(fixture);
    await fixture.whenStable();
    await switchTo(fixture, ID_B);

    pending.next(adminNews(ID_A));
    pending.complete();
    await fixture.whenStable();

    expect(navigate).not.toHaveBeenCalled();
    expect(buttonByText(fixture, 'Enregistrer').disabled).toBe(false);
    expect(notification()?.message).toBe('Enregistré.');
  });

  it('réponse de A reçue APRÈS destruction du composant : aucune erreur, avertissement seulement', async () => {
    const fixture = await open(ID_A);
    const pending = new Subject<AdminNewsDto>();
    api.update.mockReturnValueOnce(pending);
    type(fixture, 'input[formControlName=title]', 'A modifiée');
    submit(fixture);
    await fixture.whenStable();

    fixture.destroy();
    pending.error(problem(500, 'server_error', 'Erreur.'));

    expect(notification()?.message).toBe('Erreur.');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('409 + « Fermer » : pas de relecture, saisie conservée, message de conflit affiché, formulaire utilisable', async () => {
    api.update.mockReturnValue(throwError(() => problem(409, 'concurrent_update', 'Modifiée.')));
    confirm.mockReturnValue(of(false));
    const fixture = await open(ID_A);
    type(fixture, 'input[formControlName=title]', 'Ma saisie');
    type(fixture, 'textarea[formControlName=content]', 'Mon texte à recopier');
    submit(fixture);
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledWith(
      expect.objectContaining({ title: NEWS_CONFLICT_TITLE, confirmLabel: 'Recharger', cancelLabel: 'Fermer' }),
    );
    expect(api.get).toHaveBeenCalledTimes(1); // aucune relecture
    expect(titleValue(fixture)).toBe('Ma saisie');
    expect(query<HTMLTextAreaElement>(fixture, 'textarea[formControlName=content]').value).toBe('Mon texte à recopier');
    expect(query(fixture, '[role=alert]').textContent).toContain(NEWS_CONFLICT_MESSAGE);
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    expect(buttonByText(fixture, 'Enregistrer').disabled).toBe(false);
  });

  it('409 + « Recharger » : la fiche est relue et la saisie remplacée par la version du serveur', async () => {
    api.update.mockReturnValue(throwError(() => problem(409, 'concurrent_update', 'Modifiée.')));
    const fixture = await open(ID_A);
    api.get.mockReturnValue(of(adminNews(ID_A, { title: 'Version du serveur', version: 'v-serveur' })));
    type(fixture, 'input[formControlName=title]', 'Ma saisie');
    submit(fixture);
    await fixture.whenStable();

    expect(api.get).toHaveBeenCalledTimes(2);
    expect(titleValue(fixture)).toBe('Version du serveur');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
  });

  it('409 + « Fermer » sur « Supprimer » : rien n’est supprimé ni quitté, message de conflit affiché', async () => {
    api.delete.mockReturnValue(throwError(() => problem(409, 'concurrent_update', 'Modifiée.')));
    const fixture = await open(ID_A);
    confirm.mockReturnValueOnce(of(true)).mockReturnValueOnce(of(false)); // 1) supprimer ? oui  2) conflit : Fermer
    buttonByText(fixture, 'Supprimer').click();
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledTimes(2);
    expect(navigate).not.toHaveBeenCalled();
    expect(query(fixture, '[role=alert]').textContent).toContain(NEWS_CONFLICT_MESSAGE);
  });

  describe('compteur annoncé (aria-live) seulement sous 200 caractères restants', () => {
    const live = (fixture: Awaited<ReturnType<typeof open>>) => query(fixture, '.visually-hidden[aria-live=polite]').textContent!.replace(/\s+/g, ' ').trim();

    async function typeContent(fixture: Awaited<ReturnType<typeof open>>, value: string) {
      type(fixture, 'textarea[formControlName=content]', value);
      await fixture.whenStable();
    }

    it.each([
      [0, ''],
      [100, ''],
      [7800, ''], // 200 restants : pas encore annoncé
      [7801, 'Caractères restants : 199'],
      [7999, 'Caractères restants : 1'],
      [8000, 'Caractères restants : 0'],
      [8001, 'Limite dépassée.'],
    ])('%i caractères saisis → annonce « %s »', async (length, expected) => {
      const fixture = await open(undefined);
      await typeContent(fixture, 'a'.repeat(length));

      expect(live(fixture)).toBe(expected);
    });

    it('la région live existe en permanence (vide au départ) : sans elle, la première annonce serait perdue', async () => {
      const fixture = await open(undefined);
      expect(query(fixture, '.visually-hidden[aria-live=polite]').textContent!.trim()).toBe('');
    });

    it('le décompte suit l’API : \\r\\n compte pour 1, espaces autour ignorés', async () => {
      const fixture = await open(undefined);
      // 3 000 × « ab\r\n » = 9 000 bruts, 9 000 normalisés (a, b, \n = 3) → 9 000 ; on mesure à la frontière avec 2 600 × 3 = 7 800 + 1.
      await typeContent(fixture, `  ${'ab\r\n'.repeat(2600)}a  `);
      expect(live(fixture)).toBe('Caractères restants : 199');
    });
  });
});

describe('AdminNewsList : filtre et pagination par le routeur', () => {
  let api: { list: ReturnType<typeof vi.fn> };
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    api = {
      list: vi.fn((q: { page: number }) =>
        of(pageOf<AdminNewsSummaryDto>(
          [{ id: '1', title: 'Fiche', status: 'Draft', publishedAt: null, updatedAt: '2026-11-24T09:00:00+00:00' }],
          60,
          q.page,
          20,
        )),
      ),
    };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'admin/actualites', component: AdminNewsList }], withComponentInputBinding()),
        ...FORMATTING_PROVIDERS,
        { provide: AdminNewsApi, useValue: api },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  async function open(url: string) {
    const component = await harness.navigateByUrl(url, AdminNewsList);
    await harness.fixture.whenStable();
    return { component, params: () => TestBed.inject(Router).parseUrl(TestBed.inject(Router).url).queryParams };
  }

  it('lit le statut et la page dans l’URL et les envoie à l’API', async () => {
    await open('/admin/actualites?status=Draft&page=2');
    expect(api.list).toHaveBeenCalledWith({ status: 'Draft', page: 2 });
  });

  it('statut inconnu ou casse différente (« draft ») dans l’URL : repli sur « Tous », jamais envoyé à l’API', async () => {
    await open('/admin/actualites?status=draft&page=abc');
    expect(api.list).toHaveBeenCalledWith({ status: null, page: 1 });
  });

  it('changer le filtre met l’URL à jour (statut, page retirée) et relance la liste', async () => {
    const { component, params } = await open('/admin/actualites?page=3');

    component['onStatusChange']('Published');
    await harness.fixture.whenStable();

    expect(params()['status']).toBe('Published');
    expect(params()['page']).toBeUndefined();
    expect(api.list).toHaveBeenLastCalledWith({ status: 'Published', page: 1 });

    component['onStatusChange'](null);
    await harness.fixture.whenStable();
    expect(params()['status']).toBeUndefined();
    expect(api.list).toHaveBeenLastCalledWith({ status: null, page: 1 });
  });

  it('« Suivant » / « Précédent » écrivent ?page= en gardant le statut ; page 1 retire le paramètre', async () => {
    const { params } = await open('/admin/actualites?status=Draft');

    buttonByText(harness.fixture, 'Suivant').click();
    await harness.fixture.whenStable();
    expect(params()).toMatchObject({ status: 'Draft', page: '2' });
    expect(api.list).toHaveBeenLastCalledWith({ status: 'Draft', page: 2 });

    buttonByText(harness.fixture, 'Précédent').click();
    await harness.fixture.whenStable();
    expect(params()['page']).toBeUndefined();
    expect(params()['status']).toBe('Draft');
    expect(api.list).toHaveBeenLastCalledWith({ status: 'Draft', page: 1 });
  });

  it('dernière page (3 sur 3) : « Suivant » désactivé', async () => {
    await open('/admin/actualites?page=3');
    expect(buttonByText(harness.fixture, 'Suivant').disabled).toBe(true);
    expect(text(harness.fixture)).toContain('Page 3 sur 3');
  });
});

describe('sous-menu de l’espace admin', () => {
  @Component({ selector: 'app-dummy', template: '' })
  class Dummy {}

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          {
            path: 'admin',
            component: AdminLayout,
            children: ['actualites', 'evenements', 'demandes', 'parametres'].map((path) => ({ path, component: Dummy })),
          },
        ]),
        {
          provide: AdminReservationsApi,
          useValue: { pending: vi.fn(() => of(pageOf([], 3, 1, 1))) },
        },
      ],
    });
  });

  it('liens dans l’ordre Actualités, Événements, Demandes, Numéro de contact, vers les bonnes adresses', async () => {
    const harness = await RouterTestingHarness.create('/admin/actualites');
    await harness.fixture.whenStable();

    const links = Array.from(root(harness.fixture).querySelectorAll<HTMLAnchorElement>('nav[aria-label="Administration"] a'));
    expect(links.map((a) => a.textContent?.trim())).toEqual(['Actualités', 'Événements', 'Demandes (3)', 'Numéro de contact']);
    expect(links.map((a) => a.getAttribute('href'))).toEqual([
      '/admin/actualites',
      '/admin/evenements',
      '/admin/demandes',
      '/admin/parametres',
    ]);
  });

  it('« Actualités » est marqué actif sur /admin/actualites et seulement lui', async () => {
    const harness = await RouterTestingHarness.create('/admin/actualites');
    await harness.fixture.whenStable();

    const active = Array.from(root(harness.fixture).querySelectorAll('nav a.active')).map((a) => a.textContent?.trim());
    expect(active).toEqual(['Actualités']);
  });
});

describe('XSS : aucun script, img ni b dans le DOM (liste, détail, aperçu, liste admin)', () => {
  it('liste publique : titre et extrait hostiles affichés en texte', async () => {
    const list = vi.fn(() =>
      of(pageOf<NewsSummaryDto>([{ id: ID_A, title: HOSTILE, excerpt: HOSTILE, publishedAt: '2026-12-05T09:00:00+00:00' }])),
    );
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...FORMATTING_PROVIDERS, { provide: NewsApi, useValue: { list } }],
    });
    const fixture = TestBed.createComponent(NewsList);
    await fixture.whenStable();

    expect(root(fixture).querySelector(FORBIDDEN_ELEMENTS)).toBeNull();
    expect(text(fixture)).toContain('<script>alert(1)</script>');
    expect(text(fixture)).toContain('<b>gras</b>');
    expect(root(fixture).querySelector('h2')?.textContent).toBe(HOSTILE);
  });

  it('détail : titre et texte hostiles (avec <b>) affichés en texte', async () => {
    const detail: NewsDetailDto = { id: ID_A, title: HOSTILE, content: `${HOSTILE}\n\n${HOSTILE}`, publishedAt: '2026-12-05T09:00:00+00:00' };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...FORMATTING_PROVIDERS, { provide: NewsApi, useValue: { get: vi.fn(() => of(detail)) } }],
    });
    const fixture = TestBed.createComponent(NewsDetail);
    fixture.componentRef.setInput('id', ID_A);
    await fixture.whenStable();

    expect(root(fixture).querySelector(FORBIDDEN_ELEMENTS)).toBeNull();
    expect(root(fixture).querySelectorAll('.news-article__paragraph')).toHaveLength(2);
    expect(root(fixture).querySelector('h1')?.textContent).toBe(HOSTILE);
  });

  it('aperçu admin : titre et texte saisis (avec <b>) restent du texte, au chargement et à la frappe', async () => {
    const api = { get: vi.fn(() => of(adminNews(ID_A, { title: HOSTILE, content: HOSTILE }))) };
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: AdminNewsApi, useValue: api },
        { provide: ConfirmDialogService, useValue: { confirm: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(AdminNewsForm);
    fixture.componentRef.setInput('id', ID_A);
    await fixture.whenStable();
    buttonByText(fixture, 'Aperçu').click();
    await fixture.whenStable();

    const preview = query(fixture, '.preview');
    expect(preview.querySelector(FORBIDDEN_ELEMENTS)).toBeNull();
    expect(preview.textContent).toContain('<b>gras</b>');

    type(fixture, 'input[formControlName=title]', '<b>Nouveau</b>');
    type(fixture, 'textarea[formControlName=content]', '<img src=x onerror=alert(1)>');
    await fixture.whenStable();
    expect(preview.querySelector(FORBIDDEN_ELEMENTS)).toBeNull();
    expect(preview.querySelector('h2')?.textContent).toBe('<b>Nouveau</b>');
  });

  it('liste admin : un titre hostile est un texte dans le lien, pas du HTML', async () => {
    const list = vi.fn(() =>
      of(pageOf<AdminNewsSummaryDto>([{ id: ID_A, title: HOSTILE, status: 'Published', publishedAt: '2026-11-20T09:00:00+00:00', updatedAt: '2026-11-21T09:00:00+00:00' }], 1, 1, 20)),
    );
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...FORMATTING_PROVIDERS, { provide: AdminNewsApi, useValue: { list } }],
    });
    const fixture = TestBed.createComponent(AdminNewsList);
    await fixture.whenStable();

    expect(root(fixture).querySelector(FORBIDDEN_ELEMENTS)).toBeNull();
    expect(root(fixture).querySelector('tbody a')?.textContent).toBe(HOSTILE);
  });
});
