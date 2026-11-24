import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';

// Chaque page est chargée à la demande (loadComponent) : son code est un fichier JS séparé (« chunk »),
// téléchargé à la première visite. L'espace admin entier est un chunk protégé par canMatch.
export const routes: Routes = [
  // Pas encore d'actualités (étape 8) : l'accueil mène aux événements (UX-3).
  { path: '', pathMatch: 'full', redirectTo: 'evenements' },
  {
    path: 'evenements',
    title: 'Événements',
    loadComponent: () => import('./features/public/event-list/event-list').then((m) => m.EventList),
  },
  {
    path: 'evenements/:id',
    title: 'Événement',
    loadComponent: () => import('./features/public/event-detail/event-detail').then((m) => m.EventDetail),
  },
  {
    path: 'inscription',
    title: 'Créer un compte',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/account/register/register').then((m) => m.Register),
  },
  {
    path: 'connexion',
    title: 'Se connecter',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/account/login/login').then((m) => m.Login),
  },
  {
    path: 'mon-compte',
    title: 'Mon compte',
    canActivate: [authGuard],
    loadComponent: () => import('./features/account/my-account/my-account').then((m) => m.MyAccount),
  },
  {
    path: 'mes-reservations',
    title: 'Mes réservations',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./features/reservations/my-reservations/my-reservations').then((m) => m.MyReservations),
  },
  {
    path: 'confidentialite',
    title: 'Politique de confidentialité',
    loadComponent: () => import('./features/public/privacy/privacy').then((m) => m.Privacy),
  },
  {
    path: 'acces-refuse',
    title: 'Accès réservé',
    loadComponent: () => import('./layout/forbidden').then((m) => m.Forbidden),
  },
  {
    path: 'admin',
    canMatch: [adminGuard],
    loadChildren: () => import('./features/admin/admin.routes').then((m) => m.ADMIN_ROUTES),
  },
  {
    path: '**',
    title: 'Contenu introuvable',
    loadComponent: () => import('./layout/not-found').then((m) => m.NotFound),
  },
];
