import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'staff-management',
    loadComponent: () => import('./staff-management').then((m) => m.StaffManagement),
  },
  {
    path: 'staff-access',
    loadComponent: () => import('./staff-access').then((m) => m.StaffAccess),
  },
  {
    path: '',
    loadComponent: () => import('./foundation-page').then((m) => m.OperationsFoundationPage),
  },
  { path: '**', redirectTo: '' },
];
