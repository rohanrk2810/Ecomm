import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
bootstrapApplication(App, appConfig).catch(() => {
  const target = document.querySelector('app-root');
  if (target) {
    target.setAttribute('role', 'alert');
    target.className = 'startup-error';
    target.textContent = 'The application could not start. Please try refreshing the page.';
  }
});
