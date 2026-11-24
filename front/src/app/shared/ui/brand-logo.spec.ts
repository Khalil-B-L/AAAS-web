import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Footer } from '../../layout/footer';
import { Header } from '../../layout/header';
import { APP_INFO } from '../../core/config/app-info';
import { query, text } from '../../../testing/dom';
import { BrandLogo } from './brand-logo';

describe('Identité de l’association (logo, nom)', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  it('logo : fichier local, texte alternatif = nom complet, dimensions fixes aux proportions du fichier', async () => {
    const fixture = TestBed.createComponent(BrandLogo);
    fixture.componentRef.setInput('height', 48);
    await fixture.whenStable();

    const img = query<HTMLImageElement>(fixture, 'img');
    // Chemin relatif au site : la CSP (img-src 'self') refuserait une image d'un autre domaine.
    expect(img.getAttribute('src')).toBe('/logo-aaas.jpg');
    expect(img.alt).toBe("Association d'amitié et d'action sociale");
    expect(img.getAttribute('height')).toBe('48');
    expect(img.getAttribute('width')).toBe('52'); // 48 × 466 / 429 = 52,1
  });

  it('en-tête : logo et sigle dans le lien vers l’accueil (/ → actualités)', async () => {
    const fixture = TestBed.createComponent(Header);
    await fixture.whenStable();

    const brand = query<HTMLAnchorElement>(fixture, 'a.brand');
    expect(brand.getAttribute('href')).toBe('/');
    expect(brand.querySelector('img')?.alt).toBe(APP_INFO.legalName);
    expect(brand.textContent?.trim()).toBe('A.A.A.S');
  });

  it('pied de page : logo, © année et sigle, lien vers la politique de confidentialité', async () => {
    const fixture = TestBed.createComponent(Footer);
    await fixture.whenStable();

    expect(query(fixture, '.footer__badge img')).toBeTruthy();
    expect(text(fixture)).toContain(`© ${new Date().getFullYear()} A.A.A.S`);
    expect(query<HTMLAnchorElement>(fixture, 'a').getAttribute('href')).toBe('/confidentialite');
  });
});
