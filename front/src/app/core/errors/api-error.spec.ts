import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup } from '@angular/forms';
import {
  FORBIDDEN_MESSAGE,
  GENERIC_ERROR_MESSAGE,
  NETWORK_ERROR_MESSAGE,
  UNAVAILABLE_MESSAGE,
  toApiError,
} from './api-error';
import { applyServerErrors } from './apply-server-errors';

function httpError(status: number, error: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error, url: '/api/x' });
}

describe('toApiError', () => {
  it('lit un ProblemDetails : title, code et erreurs par champ', () => {
    const result = toApiError(
      httpError(400, {
        title: 'Certains champs sont invalides.',
        status: 400,
        code: 'validation_failed',
        errors: { phoneNumber: ['Le numéro…'], password: ['a', 'b'] },
      }),
    );
    expect(result).toEqual({
      status: 400,
      code: 'validation_failed',
      message: 'Certains champs sont invalides.',
      fieldErrors: { phoneNumber: ['Le numéro…'], password: ['a', 'b'] },
    });
  });

  it("ramène les clés « $.champ » (erreur de type JSON) au nom du champ", () => {
    const result = toApiError(
      httpError(400, { title: 'x', code: 'validation_failed', errors: { '$.capacity': ['type'], capacity: ['range'] } }),
    );
    expect(result.fieldErrors).toEqual({ capacity: ['type', 'range'] });
  });

  it('lit un 409 sans errors', () => {
    const result = toApiError(httpError(409, { title: 'Un compte existe déjà avec cet e-mail.', code: 'email_taken' }));
    expect(result.code).toBe('email_taken');
    expect(result.message).toBe('Un compte existe déjà avec cet e-mail.');
    expect(result.fieldErrors).toEqual({});
  });

  it('traduit une erreur réseau (statut 0)', () => {
    expect(toApiError(httpError(0, new ProgressEvent('error')))).toMatchObject({
      status: 0,
      code: 'network',
      message: NETWORK_ERROR_MESSAGE,
    });
  });

  it.each([502, 503, 504])('traduit %i (API qui démarre derrière nginx) en service indisponible', (status) => {
    expect(toApiError(httpError(status, '<html>Bad Gateway</html>'))).toMatchObject({
      code: 'unavailable',
      message: UNAVAILABLE_MESSAGE,
    });
  });

  it('donne un message générique à une réponse sans corps exploitable', () => {
    expect(toApiError(httpError(415, null))).toMatchObject({ status: 415, code: 'unknown', message: GENERIC_ERROR_MESSAGE });
  });

  it('reconnaît un 403 sans corps', () => {
    expect(toApiError(httpError(403, null))).toMatchObject({ code: 'forbidden', message: FORBIDDEN_MESSAGE });
  });

  it("lit un corps JSON reçu en texte", () => {
    const result = toApiError(httpError(404, '{"title":"Contenu introuvable.","code":"not_found"}'));
    expect(result).toMatchObject({ code: 'not_found', message: 'Contenu introuvable.' });
  });

  it("déballe l'erreur enveloppée par rxResource (propriété cause)", () => {
    const wrapped = new Error('Resource error', { cause: httpError(404, { title: 'Contenu introuvable.', code: 'not_found' }) });
    expect(toApiError(wrapped).code).toBe('not_found');
  });

  it("ne plante pas sur une erreur qui n'est pas HTTP", () => {
    expect(toApiError(new TypeError('boom'))).toMatchObject({ code: 'unknown', message: GENERIC_ERROR_MESSAGE });
  });
});

describe('applyServerErrors', () => {
  function form() {
    return new FormGroup({
      phoneNumber: new FormControl('+216 12'),
      email: new FormControl('a@b.tn'),
    });
  }

  it('place les messages sur les contrôles de même nom, marqués « touchés »', () => {
    const f = form();
    const unmatched = applyServerErrors(f, {
      status: 400,
      code: 'validation_failed',
      message: 'x',
      fieldErrors: { phoneNumber: ['Format invalide.'] },
    });
    expect(unmatched).toEqual([]);
    expect(f.controls.phoneNumber.errors).toEqual({ server: ['Format invalide.'] });
    expect(f.controls.phoneNumber.touched).toBe(true);
  });

  it('renvoie les messages sans contrôle correspondant (bandeau)', () => {
    const unmatched = applyServerErrors(form(), {
      status: 400,
      code: 'validation_failed',
      message: 'x',
      fieldErrors: { version: ['La version de la fiche est invalide. Rechargez-la.'] },
    });
    expect(unmatched).toEqual(['La version de la fiche est invalide. Rechargez-la.']);
  });

  it("efface l'erreur serveur dès que l'utilisateur modifie le champ", () => {
    const f = form();
    applyServerErrors(f, { status: 400, code: 'validation_failed', message: 'x', fieldErrors: { email: ['Pris.'] } });
    f.controls.email.setValue('autre@b.tn');
    expect(f.controls.email.errors).toBeNull();
  });
});
