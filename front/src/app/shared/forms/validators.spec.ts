import { FormControl, FormGroup } from '@angular/forms';
import { messagesFor, PHONE_FORMAT_MESSAGE, VALIDATION_MESSAGES } from './validation-messages';
import {
  capacityValidator,
  endAfterStart,
  parsePrice,
  passwordStrength,
  priceValidator,
  tunisianPhone,
} from './validators';

const check = (validator: typeof tunisianPhone, value: string) => validator(new FormControl(value));

describe('validateurs', () => {
  describe('tunisianPhone (RG-11, cas de US-A1)', () => {
    it.each(['+216 20 123 456', '+21620123456', '+216 71 123 456', ' +216 20 123 456 '])('accepte %s', (value) => {
      expect(check(tunisianPhone, value)).toBeNull();
    });

    it.each([
      ['12 34', 'trop court'],
      ['20 123 456', 'saisie locale sans +216'],
      ['0021620123456', 'préfixe 00'],
      ['+33 6 12 34 56 78', 'autre pays'],
      ['+216 20 123 45', 'moins de 8 chiffres'],
      ['+216 20 123 4567', 'plus de 8 chiffres'],
      ['+216-20-123-456', 'tirets'],
      ['+216 ٢٠ ١٢٣ ٤٥٦', 'chiffres arabes orientaux'],
    ])('refuse %s (%s)', (value) => {
      expect(check(tunisianPhone, value)).toEqual({ tunisianPhone: true });
    });

    it('laisse le champ vide à Validators.required', () => {
      expect(check(tunisianPhone, '')).toBeNull();
    });

    it("affiche le même message que l'API", () => {
      expect(PHONE_FORMAT_MESSAGE).toBe(
        'Le numéro de téléphone doit être un numéro tunisien au format +216 suivi de 8 chiffres, par exemple +216 20 123 456.',
      );
    });
  });

  describe('passwordStrength (R21)', () => {
    it('exige une lettre (accents compris) et un chiffre', () => {
      expect(check(passwordStrength, 'motdepasse1')).toBeNull();
      expect(check(passwordStrength, 'éééééééé1')).toBeNull();
      expect(check(passwordStrength, '12345678')).toEqual({ passwordLetter: true });
      expect(check(passwordStrength, 'abcdefgh')).toEqual({ passwordDigit: true });
    });
  });

  describe('prix (ADR 0010)', () => {
    it('accepte la virgule et le vide (gratuit)', () => {
      expect(parsePrice('12,5')).toBe(12.5);
      expect(parsePrice('')).toBeNull();
      expect(check(priceValidator, '12,500')).toBeNull();
      expect(check(priceValidator, '99999,999')).toBeNull();
    });

    it.each([
      ['abc', 'priceFormat'],
      ['-1', 'priceNegative'],
      ['1,2345', 'priceDecimals'],
      ['100000', 'priceTooHigh'],
    ])('refuse %s (%s)', (value, key) => {
      expect(check(priceValidator, value)).toEqual({ [key]: true });
    });
  });

  describe('capacité (RG-13)', () => {
    it.each(['1', '30', '10000'])('accepte %s', (value) => expect(check(capacityValidator, value)).toBeNull());
    it.each(['0', '10001', '2.5', 'trente', '-3'])('refuse %s', (value) =>
      expect(check(capacityValidator, value)).toEqual({ capacityRange: true }),
    );
  });

  describe('endAfterStart (validateur de groupe)', () => {
    const group = (startsAt: string, endsAt: string) =>
      new FormGroup({ startsAt: new FormControl(startsAt), endsAt: new FormControl(endsAt) });

    it('refuse une fin égale ou antérieure au début', () => {
      expect(endAfterStart(group('2026-12-05T10:00', '2026-12-05T10:00'))).toEqual({ endBeforeStart: true });
      expect(endAfterStart(group('2026-12-05T10:00', '2026-12-05T09:00'))).toEqual({ endBeforeStart: true });
    });

    it('accepte une fin postérieure, et ignore un champ vide', () => {
      expect(endAfterStart(group('2026-12-05T10:00', '2026-12-05T12:00'))).toBeNull();
      expect(endAfterStart(group('', '2026-12-05T12:00'))).toBeNull();
    });
  });
});

describe('messages de validation', () => {
  it("reprennent mot pour mot ceux de l'API (api.md)", () => {
    expect(VALIDATION_MESSAGES['capacity']['capacityRange']).toBe('La capacité doit être comprise entre 1 et 10 000 places.');
    expect(VALIDATION_MESSAGES['price']['priceDecimals']).toBe('Le prix est en dinars, avec au plus 3 décimales (millimes).');
    expect(VALIDATION_MESSAGES['price']['priceTooHigh']).toBe('Le prix ne doit pas dépasser 99 999,999 TND.');
    expect(VALIDATION_MESSAGES['acceptPrivacyPolicy']['required']).toBe(
      'Vous devez accepter la politique de confidentialité pour créer un compte.',
    );
  });

  it("affichent tels quels les messages venus du serveur, sans doublon", () => {
    expect(messagesFor('email', { server: ['Un compte existe déjà avec cet e-mail.'] })).toEqual([
      'Un compte existe déjà avec cet e-mail.',
    ]);
    expect(messagesFor('password', { minlength: {}, passwordDigit: true })).toEqual([
      'Le mot de passe doit contenir au moins 8 caractères.',
      'Le mot de passe doit contenir au moins un chiffre.',
    ]);
  });
});
