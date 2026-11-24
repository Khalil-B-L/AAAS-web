import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { buttonByText, problem, query, submit, text, type } from '../../testing/dom';
import { ADMIN_NEWS_PAGE_SIZE, AdminNewsApi } from '../core/api/admin-news.api';
import { NEWS_PAGE_SIZE, NewsApi } from '../core/api/news.api';
import { FORMATTING_PROVIDERS } from '../core/config/formatting.providers';
import { AdminNewsDto, AdminNewsSummaryDto, NewsDetailDto, NewsSummaryDto, PagedResult } from '../core/models/api-models';
import { NotificationService } from '../core/notifications/notification.service';
import { routes } from '../app.routes';
import { Header } from '../layout/header';
import { NewsArticle } from '../shared/news/news-article';
import { newsContentLength } from '../shared/news/news-labels';
import { toParagraphs } from '../shared/news/paragraphs';
import { ConfirmDialogService } from '../shared/ui/confirm-dialog';
import { ADMIN_ROUTES } from './admin/admin.routes';
import {
  ALREADY_DELETED_MESSAGE,
  AdminNewsForm,
  NEWS_CONFLICT_TITLE,
} from './admin/news/admin-news-form/admin-news-form';
import { AdminNewsList, toNewsStatusFilter } from './admin/news/admin-news-list/admin-news-list';
import { NewsDetail } from './public/news-detail/news-detail';
import { NewsList } from './public/news-list/news-list';

const NEWS_ID = '6f1c2a3b-4d5e-4f60-8a7b-9c0d1e2f3a4b';
const MALICIOUS = '<script>alert(1)</script><img src=x onerror=alert(1)>\n\nSecond <b>paragraphe</b>';

function detail(overrides: Partial<NewsDetailDto> = {}): NewsDetailDto {
  return {
    id: NEWS_ID,
    title: 'Assemblée générale',
    content: 'Chers membres,\n\nL’assemblée aura lieu samedi.',
    publishedAt: '2026-12-05T09:00:00+00:00',
    ...overrides,
  };
}

function adminNews(overrides: Partial<AdminNewsDto> = {}): AdminNewsDto {
  return {
    id: NEWS_ID,
    title: 'Assemblée générale',
    content: 'Premier paragraphe.',
    status: 'Draft',
    publishedAt: null,
    createdAt: '2026-11-24T09:00:00+00:00',
    updatedAt: '2026-11-24T09:00:00+00:00',
    version: 'v1',
    ...overrides,
  };
}

function page<T>(items: T[], totalCount = items.length, pageNumber = 1, pageSize = NEWS_PAGE_SIZE): PagedResult<T> {
  return { items, page: pageNumber, pageSize, totalCount };
}

function root(fixture: { nativeElement: unknown }): HTMLElement {
  return fixture.nativeElement as HTMLElement;
}

describe('toParagraphs (ADR 0023)', () => {
  it.each([
    ['', []],
    ['   \n\n  ', []],
    ['Un seul paragraphe', ['Un seul paragraphe']],
    ['Ligne 1\nLigne 2', ['Ligne 1\nLigne 2']],
    ['A\n\nB', ['A', 'B']],
    ['A\n\n\n\nB', ['A', 'B']],
    ['A\r\n\r\nB\r\nC', ['A', 'B\nC']],
    ['A\n  \t\nB', ['A', 'B']],
    ['  A  \n\n  B  ', ['A', 'B']],
  ])('%j → %j', (input, expected) => {
    expect(toParagraphs(input)).toEqual(expected);
  });

  it('null ou undefined → aucun paragraphe', () => {
    expect(toParagraphs(null)).toEqual([]);
    expect(toParagraphs(undefined)).toEqual([]);
  });

  it('newsContentLength compte comme l’API : \\r\\n → \\n, espaces autour retirés', () => {
    expect(newsContentLength('  ab\r\ncd  ')).toBe(5);
  });
});

describe('<app-news-article> (rendu partagé)', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [...FORMATTING_PROVIDERS] }));

  async function render(content: string, publishedAt: string | null = '2026-12-05T09:00:00+00:00', title = 'Titre') {
    const fixture = TestBed.createComponent(NewsArticle);
    fixture.componentRef.setInput('title', title);
    fixture.componentRef.setInput('content', content);
    fixture.componentRef.setInput('publishedAt', publishedAt);
    await fixture.whenStable();
    return fixture;
  }

  it('XSS : un texte malveillant s’affiche EN TEXTE ; aucun élément script, img ni b dans le DOM', async () => {
    const fixture = await render(MALICIOUS, null, '<img src=x onerror=alert(2)>');
    const element = root(fixture);

    expect(element.querySelector('script, img, b')).toBeNull();
    expect(text(fixture)).toContain('<script>alert(1)</script><img src=x onerror=alert(1)>');
    expect(text(fixture)).toContain('Second <b>paragraphe</b>');
    expect(element.querySelector('h1')?.textContent).toBe('<img src=x onerror=alert(2)>');
  });

  it('un <p> par paragraphe', async () => {
    const fixture = await render('A\n\nB\nC\n\nD');
    const paragraphs = Array.from(root(fixture).querySelectorAll('.news-article__paragraph')).map((p) => p.textContent);
    expect(paragraphs).toEqual(['A', 'B\nC', 'D']);
  });

  it('publiée à 23:30Z : datée du lendemain (jour de Tunis), <time datetime> en UTC', async () => {
    const fixture = await render('x', '2026-12-05T23:30:00+00:00');
    const time = query<HTMLTimeElement>(fixture, 'time');
    expect(time.getAttribute('datetime')).toBe('2026-12-05T23:30:00+00:00');
    expect(text(fixture)).toContain('Publiée le 6 décembre 2026');
  });

  it('sans date : « Brouillon, non publié » ; niveau de titre 2 pour l’aperçu', async () => {
    const fixture = TestBed.createComponent(NewsArticle);
    fixture.componentRef.setInput('title', 'T');
    fixture.componentRef.setInput('content', 'x');
    fixture.componentRef.setInput('headingLevel', 2);
    await fixture.whenStable();
    expect(text(fixture)).toContain('Brouillon, non publié');
    expect(root(fixture).querySelector('h2')?.textContent).toBe('T');
    expect(root(fixture).querySelector('h1')).toBeNull();
  });
});

describe('pages publiques des actualités (US-B1, US-B2)', () => {
  let api: { list: ReturnType<typeof vi.fn>; get: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    api = { list: vi.fn(), get: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...FORMATTING_PROVIDERS, { provide: NewsApi, useValue: api }],
    });
  });

  async function renderDetail(id: string) {
    const fixture = TestBed.createComponent(NewsDetail);
    fixture.componentRef.setInput('id', id);
    await fixture.whenStable();
    return fixture;
  }

  it('détail : texte malveillant affiché en texte, aucun script ni img dans le DOM', async () => {
    api.get.mockReturnValue(of(detail({ content: MALICIOUS })));
    const fixture = await renderDetail(NEWS_ID);

    expect(root(fixture).querySelector('script, img')).toBeNull();
    expect(text(fixture)).toContain('<script>alert(1)</script>');
    expect(text(fixture)).toContain('← Retour aux actualités');
    expect(root(fixture).querySelector('h1')?.textContent).toBe('Assemblée générale');
  });

  it('détail : 404 → « Contenu introuvable » avec « Retour à l’accueil »', async () => {
    api.get.mockReturnValue(throwError(() => problem(404, 'not_found', 'Contenu introuvable.')));
    const fixture = await renderDetail(NEWS_ID);

    expect(text(fixture)).toContain('Contenu introuvable');
    expect(text(fixture)).toContain('Retour à l’accueil'.replace('’', "'"));
    expect(query<HTMLAnchorElement>(fixture, 'a').getAttribute('href')).toBe('/');
  });

  it('détail : identifiant qui n’est pas un GUID → « Contenu introuvable » sans appel HTTP', async () => {
    const fixture = await renderDetail('abc');

    expect(api.get).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Contenu introuvable');
  });

  it('détail : autre erreur → bandeau et « Réessayer »', async () => {
    api.get.mockReturnValueOnce(throwError(() => problem(500, 'internal_error', 'Un problème est survenu.')));
    api.get.mockReturnValueOnce(of(detail()));
    const fixture = await renderDetail(NEWS_ID);
    expect(text(fixture)).toContain('Un problème est survenu.');

    buttonByText(fixture, 'Réessayer').click();
    await fixture.whenStable();
    expect(text(fixture)).toContain('Assemblée générale');
  });

  it('liste : aucune actualité → message vide', async () => {
    api.list.mockReturnValue(of(page<NewsSummaryDto>([])));
    const fixture = TestBed.createComponent(NewsList);
    await fixture.whenStable();

    expect(text(fixture)).toContain('Aucune actualité pour le moment.');
    expect(api.list).toHaveBeenCalledWith(1);
  });

  it('liste : cartes (h2, date de Tunis, extrait en texte, « Lire la suite : titre »), chargement puis réponse', async () => {
    const response = new Subject<PagedResult<NewsSummaryDto>>();
    api.list.mockReturnValue(response);
    const fixture = TestBed.createComponent(NewsList);
    fixture.detectChanges();
    expect(text(fixture)).toContain('Chargement');

    response.next(
      page([{ id: NEWS_ID, title: 'AG', excerpt: '<img src=x onerror=alert(1)> Début…', publishedAt: '2026-12-05T23:30:00+00:00' }]),
    );
    response.complete();
    await fixture.whenStable();

    expect(root(fixture).querySelector('h2')?.textContent).toBe('AG');
    expect(root(fixture).querySelector('img')).toBeNull();
    expect(text(fixture)).toContain('Publiée le 6 décembre 2026');
    expect(text(fixture)).toContain('<img src=x onerror=alert(1)> Début…');
    const link = query<HTMLAnchorElement>(fixture, 'a[href^="/actualites/"]');
    expect(link.getAttribute('href')).toBe(`/actualites/${NEWS_ID}`);
    expect(link.textContent?.replace(/\s+/g, ' ').trim()).toBe('Lire la suite : AG');
  });

  it('liste : page au-delà de la dernière → message et lien vers la première page ; pagination 10 par page', async () => {
    api.list.mockReturnValue(of(page<NewsSummaryDto>([], 25, 4)));
    const fixture = TestBed.createComponent(NewsList);
    fixture.componentRef.setInput('page', '4');
    await fixture.whenStable();

    expect(api.list).toHaveBeenCalledWith(4);
    expect(text(fixture)).toContain('Cette page ne contient plus d');
    expect(text(fixture)).toContain('Page 4 sur 3');
  });

  it('liste : « Suivant » change ?page= dans l’URL', async () => {
    api.list.mockReturnValue(of(page([{ id: NEWS_ID, title: 'AG', excerpt: 'x', publishedAt: '2026-12-05T09:00:00+00:00' }], 25)));
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(NewsList);
    await fixture.whenStable();

    buttonByText(fixture, 'Suivant').click();
    expect(navigate).toHaveBeenCalledWith([], { queryParams: { page: 2 } });
  });
});

describe('AdminNewsList (US-E1)', () => {
  it.each([
    ['Draft', 'Draft'],
    ['Published', 'Published'],
    ['published', null],
    [undefined, null],
    ['1; DROP TABLE', null],
  ])('filtre %j → %j', (value, expected) => {
    expect(toNewsStatusFilter(value)).toBe(expected);
  });

  it('colonnes : statut en toutes lettres, « Première publication » ou « — », filtre envoyé à l’API', async () => {
    const list = vi.fn(() =>
      of(
        page<AdminNewsSummaryDto>(
          [
            { id: '1', title: 'Brouillon jamais publié', status: 'Draft', publishedAt: null, updatedAt: '2026-11-24T09:00:00+00:00' },
            { id: '2', title: 'Publiée', status: 'Published', publishedAt: '2026-11-20T09:00:00+00:00', updatedAt: '2026-11-21T09:00:00+00:00' },
          ],
          2,
          1,
          ADMIN_NEWS_PAGE_SIZE,
        ),
      ),
    );
    TestBed.configureTestingModule({
      providers: [provideRouter([]), ...FORMATTING_PROVIDERS, { provide: AdminNewsApi, useValue: { list } }],
    });
    const fixture = TestBed.createComponent(AdminNewsList);
    fixture.componentRef.setInput('status', 'Draft');
    await fixture.whenStable();

    expect(list).toHaveBeenCalledWith({ status: 'Draft', page: 1 });
    const rows = Array.from(root(fixture).querySelectorAll('tbody tr')).map((r) => r.textContent?.replace(/\s+/g, ' ').trim());
    expect(rows[0]).toContain('Brouillon');
    expect(rows[0]).toContain('—');
    expect(rows[1]).toContain('Publiée');
    expect(rows[1]).toContain('ven. 20 nov. 2026');
    expect(text(fixture)).toContain('Première publication');
  });

  it('aucune actualité : « Aucune actualité ne correspond à ce filtre. »', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        ...FORMATTING_PROVIDERS,
        { provide: AdminNewsApi, useValue: { list: vi.fn(() => of(page<AdminNewsSummaryDto>([], 0, 1, 20))) } },
      ],
    });
    const fixture = TestBed.createComponent(AdminNewsList);
    await fixture.whenStable();
    expect(text(fixture)).toContain('Aucune actualité ne correspond à ce filtre.');
  });
});

describe('AdminNewsForm (US-E1)', () => {
  let api: Record<'get' | 'create' | 'update' | 'publish' | 'unpublish' | 'delete', ReturnType<typeof vi.fn>>;
  let confirm: ReturnType<typeof vi.fn>;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    api = { get: vi.fn(), create: vi.fn(), update: vi.fn(), publish: vi.fn(), unpublish: vi.fn(), delete: vi.fn() };
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

  async function renderNew() {
    const fixture = TestBed.createComponent(AdminNewsForm);
    await fixture.whenStable();
    return fixture;
  }

  async function renderExisting(news = adminNews()) {
    api.get.mockReturnValue(of(news));
    const fixture = TestBed.createComponent(AdminNewsForm);
    fixture.componentRef.setInput('id', news.id);
    await fixture.whenStable();
    return fixture;
  }

  function notification() {
    return TestBed.inject(NotificationService).current()?.message;
  }

  it('création : POST puis navigation vers la fiche (replaceUrl), formulaire marqué non modifié', async () => {
    api.create.mockReturnValue(of(adminNews()));
    const fixture = await renderNew();
    type(fixture, 'input[formControlName=title]', 'AG');
    type(fixture, 'textarea[formControlName=content]', 'Texte');
    submit(fixture);
    await fixture.whenStable();

    expect(api.create).toHaveBeenCalledWith({ title: 'AG', content: 'Texte' });
    expect(navigate).toHaveBeenCalledWith(['/admin/actualites', NEWS_ID], { replaceUrl: true });
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    expect(notification()).toBe('Enregistré.');
  });

  it('validation locale : texte blanc refusé (« obligatoire »), sans appel', async () => {
    const fixture = await renderNew();
    type(fixture, 'input[formControlName=title]', 'AG');
    type(fixture, 'textarea[formControlName=content]', '   \n  ');
    submit(fixture);
    await fixture.whenStable();

    expect(api.create).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Le texte est obligatoire.');
  });

  it('compteur : longueur comptée comme l’API (espaces autour ignorés)', async () => {
    const fixture = await renderNew();
    type(fixture, 'textarea[formControlName=content]', '   abc   ');
    await fixture.whenStable();
    expect(text(fixture)).toMatch(/3 \/ 8\s000/);
  });

  it('400 : messages de l’API recopiés sous les champs', async () => {
    api.create.mockReturnValue(
      throwError(() =>
        problem(400, 'validation_failed', 'Certains champs sont invalides.', {
          content: ['Le texte ne doit pas dépasser 8 000 caractères.'],
        }),
      ),
    );
    const fixture = await renderNew();
    type(fixture, 'input[formControlName=title]', 'AG');
    type(fixture, 'textarea[formControlName=content]', 'x');
    submit(fixture);
    await fixture.whenStable();

    expect(text(fixture)).toContain('Le texte ne doit pas dépasser 8 000 caractères.');
  });

  it('modification : envoie la version ; la nouvelle version remplace l’ancienne', async () => {
    api.update.mockReturnValueOnce(of(adminNews({ title: 'AG 2', version: 'v2' })));
    api.update.mockReturnValueOnce(of(adminNews({ title: 'AG 3', version: 'v3' })));
    const fixture = await renderExisting();
    type(fixture, 'input[formControlName=title]', 'AG 2');
    submit(fixture);
    await fixture.whenStable();
    type(fixture, 'input[formControlName=title]', 'AG 3');
    submit(fixture);
    await fixture.whenStable();

    expect(api.update).toHaveBeenNthCalledWith(1, NEWS_ID, { title: 'AG 2', content: 'Premier paragraphe.', version: 'v1' });
    expect(api.update).toHaveBeenNthCalledWith(2, NEWS_ID, { title: 'AG 3', content: 'Premier paragraphe.', version: 'v2' });
  });

  it('409 concurrent_update à l’enregistrement : dialogue « Recharger » / « Fermer », puis relecture', async () => {
    api.update.mockReturnValue(throwError(() => problem(409, 'concurrent_update', 'Modifiée.')));
    const fixture = await renderExisting();
    type(fixture, 'input[formControlName=title]', 'Autre');
    submit(fixture);
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledWith(
      expect.objectContaining({ title: NEWS_CONFLICT_TITLE, confirmLabel: 'Recharger', cancelLabel: 'Fermer' }),
    );
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('409 concurrent_update sur « Publier » : même dialogue', async () => {
    api.publish.mockReturnValue(throwError(() => problem(409, 'concurrent_update', 'Modifiée.')));
    const fixture = await renderExisting();
    buttonByText(fixture, 'Publier').click();
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ title: NEWS_CONFLICT_TITLE }));
  });

  it('« Publier » désactivé tant que le formulaire est modifié ; publication → statut et message', async () => {
    api.publish.mockReturnValue(of(adminNews({ status: 'Published', publishedAt: '2026-11-24T10:00:00+00:00', version: 'v2' })));
    const fixture = await renderExisting();
    type(fixture, 'input[formControlName=title]', 'Modifié');
    await fixture.whenStable();
    expect(buttonByText(fixture, 'Publier').disabled).toBe(true);
    expect(text(fixture)).toContain('Enregistrez d\'abord vos modifications.');

    type(fixture, 'input[formControlName=title]', 'Assemblée générale');
    fixture.componentInstance['form'].markAsPristine();
    await fixture.whenStable();
    buttonByText(fixture, 'Publier').click();
    await fixture.whenStable();

    expect(notification()).toBe("L'actualité est publiée.");
    expect(text(fixture)).toContain('Publiée');
    expect(buttonByText(fixture, 'Dépublier')).toBeTruthy();
  });

  it('news_state_changed : message de l’API puis relecture de la fiche', async () => {
    api.unpublish.mockReturnValue(throwError(() => problem(409, 'news_state_changed', "Cette actualité n'est pas publiée.")));
    const fixture = await renderExisting(adminNews({ status: 'Published', publishedAt: '2026-11-20T09:00:00+00:00' }));
    buttonByText(fixture, 'Dépublier').click();
    await fixture.whenStable();

    expect(text(fixture)).toContain("Cette actualité n'est pas publiée.");
    expect(api.get).toHaveBeenCalledTimes(2);
  });

  it('suppression d’une publiée : confirmation renforcée, 204 → message et retour à la liste', async () => {
    api.delete.mockReturnValue(of(undefined));
    const fixture = await renderExisting(adminNews({ status: 'Published', publishedAt: '2026-11-20T09:00:00+00:00' }));
    buttonByText(fixture, 'Supprimer').click();
    await fixture.whenStable();

    expect(confirm).toHaveBeenCalledWith(
      expect.objectContaining({
        title: 'Supprimer définitivement cette actualité ?',
        message: expect.stringContaining('Elle n’est plus visible du public dès maintenant.'),
        danger: true,
      }),
    );
    expect(api.delete).toHaveBeenCalledWith(NEWS_ID);
    expect(notification()).toBe("L'actualité a été supprimée.");
    expect(navigate).toHaveBeenCalledWith(['/admin/actualites']);
  });

  it('suppression : 404 → « déjà supprimée » et retour à la liste ; annulation du dialogue → aucun appel', async () => {
    api.delete.mockReturnValue(throwError(() => problem(404, 'not_found', 'Actualité introuvable.')));
    const fixture = await renderExisting();
    confirm.mockReturnValueOnce(of(false));
    buttonByText(fixture, 'Supprimer').click();
    await fixture.whenStable();
    expect(api.delete).not.toHaveBeenCalled();

    buttonByText(fixture, 'Supprimer').click();
    await fixture.whenStable();
    expect(confirm).toHaveBeenLastCalledWith(expect.objectContaining({ message: 'Cette action est définitive.' }));
    expect(notification()).toBe(ALREADY_DELETED_MESSAGE);
    expect(navigate).toHaveBeenCalledWith(['/admin/actualites']);
  });

  it('garde de sortie : modifications non enregistrées signalées', async () => {
    const fixture = await renderExisting();
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    type(fixture, 'textarea[formControlName=content]', 'Nouveau');
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('aperçu : même rendu que le public, suit la saisie, sans HTML interprété', async () => {
    const fixture = await renderExisting();
    buttonByText(fixture, 'Aperçu').click();
    await fixture.whenStable();
    expect(query(fixture, '.preview').textContent).toContain('Brouillon, non publié');
    expect(buttonByText(fixture, 'Aperçu').getAttribute('aria-pressed')).toBe('true');

    type(fixture, 'textarea[formControlName=content]', MALICIOUS);
    await fixture.whenStable();
    const preview = query(fixture, '.preview');
    expect(preview.querySelector('script, img')).toBeNull();
    expect(preview.querySelectorAll('.news-article__paragraph')).toHaveLength(2);
    expect(preview.textContent).toContain('<script>alert(1)</script>');
  });

  it('fiche inconnue (404) ou id non GUID : « Actualité introuvable. » + « Retour à la liste »', async () => {
    const fixture = TestBed.createComponent(AdminNewsForm);
    fixture.componentRef.setInput('id', 'abc');
    await fixture.whenStable();
    expect(api.get).not.toHaveBeenCalled();
    expect(text(fixture)).toContain('Actualité introuvable.');
    expect(text(fixture)).toContain('Retour à la liste');
  });
});

describe('routes et menu (accueil = actualités)', () => {
  it('« / » redirige vers « actualites » avec pathMatch: full', () => {
    expect(routes.find((r) => r.path === '')).toMatchObject({ redirectTo: 'actualites', pathMatch: 'full' });
    expect(routes.some((r) => r.path === 'actualites')).toBe(true);
    expect(routes.some((r) => r.path === 'actualites/:id')).toBe(true);
  });

  it('admin : « actualites/nouvelle » est déclarée avant « actualites/:id »', () => {
    const children = ADMIN_ROUTES[0].children!.map((r) => r.path);
    expect(children.indexOf('actualites/nouvelle')).toBeLessThan(children.indexOf('actualites/:id'));
  });

  it('en-tête : « Actualités » est le premier lien du menu', async () => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(Header);
    await fixture.whenStable();

    const first = root(fixture).querySelector<HTMLAnchorElement>('nav a');
    expect(first?.textContent?.trim()).toBe('Actualités');
    expect(first?.getAttribute('href')).toBe('/actualites');
  });
});
