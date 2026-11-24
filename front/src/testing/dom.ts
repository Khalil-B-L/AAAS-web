// Aides de test pour manipuler le DOM rendu par un composant, comme le ferait un utilisateur.
// Importé uniquement par des fichiers *.spec.ts.
import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture } from '@angular/core/testing';

export function query<T extends Element = HTMLElement>(fixture: ComponentFixture<unknown>, selector: string): T {
  const element = (fixture.nativeElement as HTMLElement).querySelector<T>(selector);
  if (!element) {
    throw new Error(`Élément introuvable : ${selector}`);
  }
  return element;
}

export function text(fixture: ComponentFixture<unknown>): string {
  return ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
}

/** Saisit une valeur dans un champ (événement « input », écouté par les reactive forms) puis le quitte (« blur »). */
export function type(fixture: ComponentFixture<unknown>, selector: string, value: string): void {
  const input = query<HTMLInputElement | HTMLTextAreaElement>(fixture, selector);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('blur'));
}

export function submit(fixture: ComponentFixture<unknown>): void {
  query<HTMLFormElement>(fixture, 'form').dispatchEvent(new Event('submit'));
}

export function buttonByText(fixture: ComponentFixture<unknown>, label: string): HTMLButtonElement {
  const buttons = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
  const button = buttons.find((b) => b.textContent?.trim() === label);
  if (!button) {
    throw new Error(`Bouton introuvable : ${label}`);
  }
  return button;
}

/** Erreur HTTP telle que HttpClient la produit, avec un corps ProblemDetails. */
export function problem(status: number, code: string, title: string, errors?: Record<string, string[]>) {
  return new HttpErrorResponse({ status, error: { status, code, title, errors }, url: '/api' });
}
