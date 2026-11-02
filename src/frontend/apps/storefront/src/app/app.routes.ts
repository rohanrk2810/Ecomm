import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'register',
    loadComponent: () => import('./features/registration/registration').then((m) => m.Registration),
  },
  {
    path: '',
    loadComponent: () =>
      import('../../../../libs/shared-ui/foundation-page').then((m) => m.FoundationPage),
  },
  { path: '**', redirectTo: '' },
];
