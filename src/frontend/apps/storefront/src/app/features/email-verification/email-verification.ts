import { Component, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { RegistrationApi } from '../../../../../../libs/api-clients/registration-api';
@Component({
  selector: 'commerce-email-verification',
  imports: [RouterLink],
  styleUrl: '../registration/registration.css',
  template: ` <section class="registration" aria-labelledby="verify-title">
    <p class="eyebrow">One more step</p>
    <h1 id="verify-title">Verify your email.</h1>
    @if (state() === 'verified') {
      <div class="pending" role="status">
        <h2>Email verified</h2>
        <p>Your email address has been verified.</p>
        <a routerLink="/account">Continue to sign in</a>
      </div>
    } @else if (state() === 'invalid') {
      <p role="alert">This verification link is missing, invalid or has expired.</p>
      <a routerLink="/resend-verification">Request another email</a>
    } @else {
      <p>Confirm this is your email address to finish verifying your account.</p>
      @if (error()) {
        <p role="alert" class="error">{{ error() }}</p>
      }
      <button type="button" (click)="verify()" [disabled]="state() === 'submitting'">
        {{ state() === 'submitting' ? 'Verifying…' : 'Verify email' }}
      </button>
    }
  </section>`,
})
export class EmailVerification {
  private readonly api = inject(RegistrationApi);
  private credentials?: { userId: string; token: string };
  readonly state = signal<'ready' | 'submitting' | 'verified' | 'invalid'>('invalid');
  readonly error = signal('');
  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment;
    const values = new URLSearchParams(fragment ?? '');
    const userId = values.get('user') ?? '';
    const token = values.get('token') ?? '';
    // Remove the bearer secret from the visible/history URL; never render or persist it.
    if (fragment) inject(Location).replaceState('/verify-email');
    if (
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(userId) &&
      /^[A-Za-z0-9_-]{1,4096}$/.test(token)
    ) {
      this.credentials = { userId, token };
      this.state.set('ready');
    }
  }
  async verify(): Promise<void> {
    if (this.state() !== 'ready' || !this.credentials) return;
    this.state.set('submitting');
    this.error.set('');
    try {
      const response = await firstValueFrom(
        this.api.confirmEmail(this.credentials.userId, this.credentials.token),
      );
      if (response.status !== 'verified') throw new Error('Unconfirmed verification state');
      this.credentials = undefined;
      this.state.set('verified');
    } catch (error: unknown) {
      if (error instanceof HttpErrorResponse && error.status === 400) {
        this.credentials = undefined;
        this.state.set('invalid');
      } else {
        this.state.set('ready');
        this.error.set('We could not verify your email. Please wait a little and try again.');
      }
    }
  }
}
