import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./foundation-page').then((m) => m.OperationsFoundationPage),
  },
  { path: '**', redirectTo: '' },
];
