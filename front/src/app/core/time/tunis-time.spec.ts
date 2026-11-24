import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from '../../app.config';
import { DETAIL_DATE_FORMAT, LIST_DATE_FORMAT } from './date-formats';
import { TUNIS_UTC_OFFSET, toTunisIso, toTunisLocalInput } from './tunis-time';

describe('tunis-time', () => {
  describe('toTunisIso (saisie → API)', () => {
    it("ajoute le décalage de Tunis à la valeur d'un champ datetime-local", () => {
      expect(toTunisIso('2026-12-05T10:00')).toBe('2026-12-05T10:00+01:00');
    });

    it('accepte les secondes', () => {
      expect(toTunisIso('2026-12-05T10:00:30')).toBe('2026-12-05T10:00:30+01:00');
    });

    it("laisse une valeur vide ou mal formée telle quelle (l'API répondra 400 sur le champ)", () => {
      expect(toTunisIso('')).toBe('');
      expect(toTunisIso('05/12/2026 10:00')).toBe('05/12/2026 10:00');
    });

    it('utilise un décalage unique de +01:00 (pas d’heure d’été en Tunisie)', () => {
      expect(TUNIS_UTC_OFFSET).toBe('+01:00');
      // Même résultat en été et en hiver, contrairement au fuseau d'un poste en France.
      expect(toTunisIso('2026-07-14T10:00')).toBe('2026-07-14T10:00+01:00');
    });
  });

  describe('toTunisLocalInput (API → préremplissage)', () => {
    it("convertit l'UTC en heure de Tunis", () => {
      expect(toTunisLocalInput('2026-12-05T09:00:00+00:00')).toBe('2026-12-05T10:00');
    });

    it("passe minuit et le changement d'année par l'instant, pas par les champs", () => {
      expect(toTunisLocalInput('2026-12-31T23:30:00+00:00')).toBe('2027-01-01T00:30');
    });

    it('lit aussi une date déjà exprimée avec un autre décalage', () => {
      expect(toTunisLocalInput('2026-12-05T10:00:00+01:00')).toBe('2026-12-05T10:00');
      expect(toTunisLocalInput('2026-07-01T12:00:00+02:00')).toBe('2026-07-01T11:00');
    });

    it('perd les secondes (champ à la minute)', () => {
      expect(toTunisLocalInput('2026-12-05T09:00:45+00:00')).toBe('2026-12-05T10:00');
    });

    it('renvoie une chaîne vide pour une date illisible', () => {
      expect(toTunisLocalInput('pas une date')).toBe('');
    });

    it('fait un aller-retour exact', () => {
      const local = '2026-03-10T08:15';
      expect(toTunisLocalInput(toTunisIso(local))).toBe(local);
    });
  });

  describe('indépendance vis-à-vis du fuseau du poste', () => {
    // vi.stubEnv modifie process.env.TZ (lu par Node pour le fuseau local) ; unstubAllEnvs le restaure.
    afterEach(() => vi.unstubAllEnvs());

    for (const tz of ['Europe/Paris', 'America/New_York', 'Pacific/Kiritimati']) {
      it(`donne le même résultat avec TZ=${tz}`, () => {
        vi.stubEnv('TZ', tz);
        expect(toTunisLocalInput('2026-07-14T08:30:00+00:00')).toBe('2026-07-14T09:30');
        expect(toTunisIso('2026-07-14T09:30')).toBe('2026-07-14T09:30+01:00');
      });
    }
  });

  describe('affichage par le DatePipe configuré dans app.config.ts', () => {
    @Component({
      selector: 'app-date-host',
      imports: [DatePipe],
      changeDetection: ChangeDetectionStrategy.OnPush,
      template: `<span id="detail">{{ value() | date: detail }}</span><span id="list">{{ value() | date: list }}</span>`,
    })
    class DateHost {
      readonly value = input('2026-12-05T09:00:00+00:00');
      readonly detail = DETAIL_DATE_FORMAT;
      readonly list = LIST_DATE_FORMAT;
    }

    it("affiche l'heure de Tunis en français (format détail et format liste)", async () => {
      TestBed.configureTestingModule({ providers: appConfig.providers });
      const fixture = TestBed.createComponent(DateHost);
      await fixture.whenStable();
      const element = fixture.nativeElement as HTMLElement;
      expect(element.querySelector('#detail')?.textContent).toBe('samedi 5 décembre 2026 à 10:00');
      expect(element.querySelector('#list')?.textContent).toBe('sam. 5 déc. 2026, 10:00');
    });
  });
});
