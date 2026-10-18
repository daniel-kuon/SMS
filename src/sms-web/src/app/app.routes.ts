import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AuthService } from './core/api';
import { SiteService } from './core/site';

const authGuard: CanActivateFn = (_, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.me() ? true : router.createUrlTree(['/auth/login'], { queryParams: { returnUrl: state.url } });
};

const shipMode = () => inject(SiteService).site().mode === 'ship';

export const routes: Routes = [
  // a ship domain shows the ship's website instead of the portal start page
  { path: '', canMatch: [shipMode], loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSiteHome) },
  { path: 'pages/:path', canMatch: [shipMode], loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSitePage) },
  { path: 'pages/:path/:p2', canMatch: [shipMode], loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSitePage) },
  { path: 'pages/:path/:p2/:p3', canMatch: [shipMode], loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSitePage) },
  { path: '', loadComponent: () => import('./features/harbours/harbour-map').then(m => m.HarbourMapPage), title: 'Hafenkarte' },
  { path: 'ships/:shipId/site', loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSiteHome) },
  { path: 'ships/:shipId/site/pages/:path', loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSitePage) },
  { path: 'ships/:shipId/site/pages/:path/:p2', loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSitePage) },
  { path: 'ships/:shipId/site/pages/:path/:p2/:p3', loadComponent: () => import('./features/cms/ship-site').then(m => m.ShipSitePage) },
  { path: 'harbours', redirectTo: '' },
  { path: 'harbours/:id', loadComponent: () => import('./features/harbours/harbour-map').then(m => m.HarbourMapPage) },
  { path: 'auth/login', loadComponent: () => import('./features/auth/auth-pages').then(m => m.LoginPage), title: 'Anmelden' },
  { path: 'auth/register', loadComponent: () => import('./features/auth/auth-pages').then(m => m.RegisterPage), title: 'Registrieren' },
  { path: 'auth/confirm', loadComponent: () => import('./features/auth/auth-pages').then(m => m.ConfirmPage) },
  { path: 'auth/forgot-password', loadComponent: () => import('./features/auth/auth-pages').then(m => m.ForgotPage) },
  { path: 'auth/reset-password', loadComponent: () => import('./features/auth/auth-pages').then(m => m.ResetPage) },
  { path: 'feed', canActivate: [authGuard], loadComponent: () => import('./features/social/social').then(m => m.FeedPage), title: 'Feed' },
  { path: 'explore', loadComponent: () => import('./features/social/social').then(m => m.FeedPage), title: 'Entdecken' },
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
  { path: 'admin/corrections', canActivate: [authGuard], loadComponent: () => import('./features/admin/routing-corrections').then(m => m.RoutingCorrections), title: 'Routing-Korrekturen' },
  { path: 'p/:slug', loadComponent: () => import('./features/pages/content-page').then(m => m.ContentPageView) },
  { path: '**', redirectTo: '' },
];
