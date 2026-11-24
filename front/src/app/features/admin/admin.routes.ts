import { Routes } from '@angular/router';
import { unsavedChangesGuard } from '../../shared/forms/unsaved-changes.guard';
import { AdminLayout } from './admin-layout';

// Routes enfants de « admin » : ce fichier n'est téléchargé que si adminGuard (canMatch) accepte.
// L'ordre compte : « evenements/nouveau » doit précéder « evenements/:id » (le routeur prend la première qui correspond).
export const ADMIN_ROUTES: Routes = [
  {
    path: '',
    component: AdminLayout,
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'evenements' },
      {
        path: 'actualites',
        title: 'Administration — actualités',
        loadComponent: () => import('./news/admin-news-list/admin-news-list').then((m) => m.AdminNewsList),
      },
      {
        // Déclarée AVANT « actualites/:id » : sinon « nouvelle » serait pris pour un identifiant.
        path: 'actualites/nouvelle',
        title: 'Nouvelle actualité',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./news/admin-news-form/admin-news-form').then((m) => m.AdminNewsForm),
      },
      {
        path: 'actualites/:id',
        title: "Modifier l'actualité",
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./news/admin-news-form/admin-news-form').then((m) => m.AdminNewsForm),
      },
      {
        path: 'evenements',
        title: 'Administration — événements',
        loadComponent: () => import('./events/admin-event-list/admin-event-list').then((m) => m.AdminEventList),
      },
      {
        path: 'evenements/nouveau',
        title: 'Nouvel événement',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./events/admin-event-form/admin-event-form').then((m) => m.AdminEventForm),
      },
      {
        // Déclarée AVANT « evenements/:id » : même préfixe, la plus précise d'abord.
        path: 'evenements/:id/inscrits',
        title: 'Inscrits',
        loadComponent: () =>
          import('./reservations/event-registrants/event-registrants').then((m) => m.EventRegistrants),
      },
      {
        path: 'demandes',
        title: 'Demandes à traiter',
        loadComponent: () =>
          import('./reservations/pending-requests/pending-requests').then((m) => m.PendingRequests),
      },
      {
        path: 'evenements/:id',
        title: "Modifier l'événement",
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./events/admin-event-form/admin-event-form').then((m) => m.AdminEventForm),
      },
      {
        path: 'parametres',
        title: 'Numéro de contact',
        canDeactivate: [unsavedChangesGuard],
        loadComponent: () => import('./settings/contact-settings/contact-settings').then((m) => m.ContactSettings),
      },
    ],
  },
];
