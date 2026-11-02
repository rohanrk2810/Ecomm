import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('../../../../libs/shared-ui/foundation-page').then((m) => m.FoundationPage),
  },
  { path: '**', redirectTo: '' },
];
