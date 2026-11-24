import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminEventsApi } from './admin-events.api';
import { EventsApi } from './events.api';
import { SettingsApi } from './settings.api';

describe('services d’API', () => {
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('EventsApi.list envoie page et pageSize en paramètres de requête', () => {
    TestBed.inject(EventsApi).list(2).subscribe();
    const req = backend.expectOne((r) => r.url === '/api/events');
    expect(req.request.method).toBe('GET');
    expect(req.request.urlWithParams).toBe('/api/events?page=2&pageSize=20');
  });

  it("AdminEventsApi.list n'envoie le statut que s'il est choisi", () => {
    const api = TestBed.inject(AdminEventsApi);
    api.list({ status: null, period: 'upcoming', page: 1 }).subscribe();
    expect(backend.expectOne((r) => r.url === '/api/admin/events').request.urlWithParams).toBe(
      '/api/admin/events?period=upcoming&page=1&pageSize=20',
    );
    api.list({ status: 'Draft', period: 'all', page: 3 }).subscribe();
    expect(backend.expectOne((r) => r.url === '/api/admin/events').request.urlWithParams).toBe(
      '/api/admin/events?period=all&page=3&pageSize=20&status=Draft',
    );
  });

  it('AdminEventsApi.update envoie un PUT avec la version et le drapeau de confirmation', () => {
    const body = {
      title: 't',
      description: 'd',
      location: 'l',
      startsAt: '2026-12-05T10:00+01:00',
      endsAt: '2026-12-05T12:00+01:00',
      capacity: 30,
      price: null,
      version: 'AAAAAAAAB9E=',
      confirmRegistrantsWarning: false,
    };
    TestBed.inject(AdminEventsApi).update('42', body).subscribe();
    const req = backend.expectOne('/api/admin/events/42');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
  });

  it('SettingsApi.removeContact envoie un DELETE avec un corps JSON { version } (sinon 415)', () => {
    TestBed.inject(SettingsApi).removeContact('AAAAAAAAB9E=').subscribe();
    const req = backend.expectOne('/api/admin/settings/contact');
    expect(req.request.method).toBe('DELETE');
    expect(req.request.body).toEqual({ version: 'AAAAAAAAB9E=' });
    // HttpClient déduit Content-Type: application/json d'un corps objet.
    expect(req.request.detectContentTypeHeader()).toBe('application/json');
  });
});
