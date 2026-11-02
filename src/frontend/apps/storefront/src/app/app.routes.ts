import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'verify-email',
    loadComponent: () =>
      import('./features/email-verification/email-verification').then((m) => m.EmailVerification),
  },
  {
    path: 'resend-verification',
    loadComponent: () =>
      import('./features/email-verification/resend-verification').then((m) => m.ResendVerification),
  },
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
