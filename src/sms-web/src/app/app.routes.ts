import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AuthService } from './core/api';

const authGuard: CanActivateFn = (_, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.me() ? true : router.createUrlTree(['/auth/login'], { queryParams: { returnUrl: state.url } });
};

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/harbours/harbour-map').then(m => m.HarbourMapPage), title: 'Hafenkarte' },
  { path: 'harbours', redirectTo: '' },
  { path: 'harbours/:id', loadComponent: () => import('./features/harbours/harbour-map').then(m => m.HarbourMapPage) },
  { path: 'auth/login', loadComponent: () => import('./features/auth/auth-pages').then(m => m.LoginPage), title: 'Anmelden' },
  { path: 'auth/register', loadComponent: () => import('./features/auth/auth-pages').then(m => m.RegisterPage), title: 'Registrieren' },
  { path: 'auth/confirm', loadComponent: () => import('./features/auth/auth-pages').then(m => m.ConfirmPage) },
  { path: 'auth/forgot-password', loadComponent: () => import('./features/auth/auth-pages').then(m => m.ForgotPage) },
  { path: 'auth/reset-password', loadComponent: () => import('./features/auth/auth-pages').then(m => m.ResetPage) },
  { path: 'ships', canActivate: [authGuard], loadComponent: () => import('./features/ships/ship-list').then(m => m.ShipListPage), title: 'Meine Schiffe' },
  { path: 'ships/:id', loadComponent: () => import('./features/ships/ship-page').then(m => m.ShipPage) },
  { path: 'ships/:id/logbook', loadComponent: () => import('./features/logbook/logbook-page').then(m => m.LogbookPage) },
  { path: 'trips', canActivate: [authGuard], loadComponent: () => import('./features/trips/trip-list').then(m => m.TripListPage), title: 'Meine Törns' },
  { path: 'trips/:id', loadComponent: () => import('./features/trips/trip-page').then(m => m.TripPage) },
  { path: 'trips/:id/:tab', loadComponent: () => import('./features/trips/trip-page').then(m => m.TripPage) },
  { path: 'logbook/:id', loadComponent: () => import('./features/logbook/entry-detail').then(m => m.EntryDetailPage) },
  { path: 'posts/:id', loadComponent: () => import('./features/posts/post-detail').then(m => m.PostDetailPage) },
  { path: 'u/:id', loadComponent: () => import('./features/profile/profile-pages').then(m => m.PublicProfilePage) },
  { path: 'settings', canActivate: [authGuard], loadComponent: () => import('./features/profile/profile-pages').then(m => m.SettingsPage), title: 'Einstellungen' },
  { path: 'invitations/:kind/:token', loadComponent: () => import('./features/trips/invitation-page').then(m => m.InvitationPage) },
  { path: 'admin', canActivate: [authGuard], loadComponent: () => import('./features/admin/admin-page').then(m => m.AdminPage), title: 'Administration' },
  { path: 'p/:slug', loadComponent: () => import('./features/pages/content-page').then(m => m.ContentPageView) },
  { path: '**', redirectTo: '' },
];
