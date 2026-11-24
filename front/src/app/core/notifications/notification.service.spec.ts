import { TestBed } from '@angular/core/testing';
import { NotificationHost } from '../../layout/notification-host';
import { NotificationService, PENDING_NOTIFICATION_KEY } from './notification.service';

describe('NotificationService', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] }));
  afterEach(() => vi.useRealTimers());

  it('disparaît après 6 secondes, pas avant', () => {
    const service = TestBed.inject(NotificationService);
    service.show('Enregistré.');
    vi.advanceTimersByTime(5999);
    expect(service.current()?.message).toBe('Enregistré.');
    vi.advanceTimersByTime(1);
    expect(service.current()).toBeNull();
  });

  it('un nouveau message remet le compteur à zéro (l’ancien minuteur ne l’efface pas)', () => {
    const service = TestBed.inject(NotificationService);
    service.show('Premier');
    vi.advanceTimersByTime(4000);
    service.show('Second');
    vi.advanceTimersByTime(4000); // 8 s après le premier, 4 s après le second
    expect(service.current()?.message).toBe('Second');
    vi.advanceTimersByTime(2000);
    expect(service.current()).toBeNull();
  });

  it('deux messages identiques successifs ont des identifiants distincts', () => {
    const service = TestBed.inject(NotificationService);
    service.show('Enregistré.');
    const first = service.current()!.id;
    service.show('Enregistré.');
    expect(service.current()!.id).not.toBe(first);
  });

  it('dismiss() ferme tout de suite', () => {
    const service = TestBed.inject(NotificationService);
    service.show('x', 'warning');
    service.dismiss();
    expect(service.current()).toBeNull();
  });

  it('la zone d’annonce affiche puis retire le message (bouton Fermer)', async () => {
    const service = TestBed.inject(NotificationService);
    const fixture = TestBed.createComponent(NotificationHost);
    const root = fixture.nativeElement as HTMLElement;
    service.show('Enregistré.');
    await fixture.whenStable();
    expect(root.textContent).toContain('Enregistré.');
    expect(root.querySelector('[role=status]')).not.toBeNull();

    root.querySelector<HTMLButtonElement>('button[aria-label="Fermer le message"]')!.click();
    await fixture.whenStable();
    expect(root.textContent).not.toContain('Enregistré.');
  });

  it('message « flash » : conservé dans l’onglet, affiché une seule fois au démarrage suivant', () => {
    sessionStorage.clear();
    TestBed.inject(NotificationService).showAfterReload('Autre compte.', 'warning');

    // Nouvelle instance = application redémarrée après location.reload().
    TestBed.resetTestingModule();
    expect(TestBed.inject(NotificationService).current()).toMatchObject({ message: 'Autre compte.', kind: 'warning' });
    expect(sessionStorage.getItem(PENDING_NOTIFICATION_KEY)).toBeNull();

    TestBed.resetTestingModule();
    expect(TestBed.inject(NotificationService).current()).toBeNull();
  });

  it('message « flash » illisible : ignoré sans erreur', () => {
    sessionStorage.setItem(PENDING_NOTIFICATION_KEY, '{pas du json');
    expect(TestBed.inject(NotificationService).current()).toBeNull();
  });
});
