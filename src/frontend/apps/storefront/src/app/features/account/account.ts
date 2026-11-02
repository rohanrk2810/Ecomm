import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SessionApi } from '../../../../../../libs/auth-client/session-api';
@Component({
  selector: 'commerce-account',
  imports: [RouterLink],
  styleUrl: '../registration/registration.css',
  template: ` <section class="registration" aria-labelledby="account-title">
    <a routerLink="/" class="back">← Back to home</a>
    <p class="eyebrow">Your account</p>
    <h1 id="account-title">Welcome back.</h1>
    @if (state() === 'loading') {
      <p role="status">Checking your session…</p>
    } @else if (state() === 'unavailable') {
      <p role="alert">Your account is temporarily unavailable. Please try again.</p>
      <button type="button" (click)="load()">Try again</button>
    } @else if (state() === 'anonymous') {
      <p>Sign in with your verified email address and password.</p>
      <a href="/bff/login">Sign in →</a>
      <p>New here? <a routerLink="/register">Create an account</a></p>
      <p><a routerLink="/resend-verification">Request a verification email</a></p>
    } @else {
      <div class="pending" role="status">
        <h2>You're signed in.</h2>
        <p>Your account session is active.</p>
      </div>
      <form method="post" action="/bff/logout">
        <input type="hidden" name="__RequestVerificationToken" [value]="csrf()" /><button
          type="submit"
        >
          Sign out
        </button>
      </form>
    }
  </section>`,
})
export class Account {
  private readonly api = inject(SessionApi);
  readonly state = signal<'loading' | 'anonymous' | 'authenticated' | 'unavailable'>('loading');
  readonly csrf = signal('');
  constructor() {
    void this.load();
  }
  async load(): Promise<void> {
    this.state.set('loading');
    this.csrf.set('');
    try {
      const result = await firstValueFrom(this.api.session());
      if (!result.available) throw new Error('Session unavailable');
      if (!result.authenticated) {
        this.state.set('anonymous');
        return;
      }
      if (!result.userId) throw new Error('Incomplete session');
      const token = await firstValueFrom(this.api.antiforgery());
      if (!token.requestToken) throw new Error('Missing request verification');
      this.csrf.set(token.requestToken);
      this.state.set('authenticated');
    } catch {
      this.state.set('unavailable');
    }
  }
}
