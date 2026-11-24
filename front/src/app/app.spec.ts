import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { App } from './app';

@Component({ template: '<h1>Page A</h1><button type="button">Action</button>' })
class PageA {}

@Component({ template: '<h1>Page B</h1>' })
class PageB {}

@Component({ template: '<p>Chargement…</p>' })
class PageWithoutTitle {}

describe('App (coquille) : accessibilité du focus', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'a', component: PageA },
          { path: 'b', component: PageB },
          { path: 'c', component: PageWithoutTitle },
        ]),
      ],
    });
  });

  async function render(initialUrl = '/a') {
    const fixture = TestBed.createComponent(App);
    const router = TestBed.inject(Router);
    await router.navigateByUrl(initialUrl);
    await fixture.whenStable();
    return { fixture, router, root: fixture.nativeElement as HTMLElement };
  }

  it('« Aller au contenu » donne le focus à <main> sans suivre le lien (pas de rechargement via <base href>)', async () => {
    const { root } = await render();
    const link = root.querySelector<HTMLAnchorElement>('a.skip-link')!;
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });

    link.dispatchEvent(click);

    expect(click.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(root.querySelector('main'));
  });

  it('premier affichage : le focus n’est pas déplacé (le lien d’évitement reste le premier Tab)', async () => {
    const { root } = await render();
    expect(root.querySelector('main')!.contains(document.activeElement)).toBe(false);
  });

  it('changement de page : focus sur le <h1> de la nouvelle page', async () => {
    const { fixture, router, root } = await render();
    await router.navigateByUrl('/b');
    await fixture.whenStable();

    const title = root.querySelector('main h1')!;
    expect(title.textContent).toBe('Page B');
    expect(document.activeElement).toBe(title);
    expect(title.getAttribute('tabindex')).toBe('-1');
  });

  it('page sans <h1> (encore en chargement) : focus sur <main>', async () => {
    const { fixture, router, root } = await render();
    await router.navigateByUrl('/c');
    await fixture.whenStable();
    expect(document.activeElement).toBe(root.querySelector('main'));
  });

  it('seuls les paramètres changent (?page=2) : le focus reste où il est', async () => {
    const { fixture, router, root } = await render('/b');
    await router.navigateByUrl('/a');
    await fixture.whenStable();
    const button = root.querySelector<HTMLButtonElement>('main button')!;
    button.focus();

    await router.navigateByUrl('/a?page=2');
    await fixture.whenStable();
    expect(document.activeElement).toBe(button);
  });
});
