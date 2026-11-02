import { RuntimeConfigService } from '../../../../../../libs/runtime-config/runtime-config';
import { Component, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom, timeout } from 'rxjs';
@Component({
  selector: 'commerce-password-recovery',
  imports: [RouterLink, ReactiveFormsModule],
  styleUrl: '../registration/registration.css',
  template: `<section class="registration">
    <a routerLink="/account">Back to sign in</a>
    <h1>{{ reset ? 'Reset your password' : 'Forgot your password?' }}</h1>
    @if (state() === 'success') {
      <p role="status">
        {{
          reset
            ? 'Password changed. Your previous sessions have been closed. Sign in with your new password.'
            : 'If this email belongs to a verified account, a reset link will arrive shortly. Please check your inbox.'
        }}
      </p>
    } @else if (reset && !validLink) {
      <p role="alert">This reset link is missing or invalid.</p>
      <a routerLink="/forgot-password">Request a new link</a>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()">
        @if (!reset) {
          <label for="recovery-email">Email address</label
          ><input
            id="recovery-email"
            type="email"
            formControlName="email"
            autocomplete="email"
            maxlength="254"
          />
        } @else {
          <p>
            Use the same password requirements shown when creating your account. The server checks
            your password before changing it.
          </p>
          <label for="new-password">New password</label
          ><input
            id="new-password"
            type="password"
            formControlName="password"
            autocomplete="new-password"
            maxlength="128"
          />
          <label for="confirm-password">Confirm password</label
          ><input
            id="confirm-password"
            type="password"
            formControlName="confirm"
            autocomplete="new-password"
            maxlength="128"
          />
        }
        @if (error()) {
          <p role="alert">{{ error() }}</p>
        }
        <button type="submit" [disabled]="state() === 'pending'">
          {{
            state() === 'pending' ? 'Please wait...' : reset ? 'Change password' : 'Send reset link'
          }}
        </button>
      </form>
    }
  </section>`,
})
export class PasswordRecovery {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(RuntimeConfigService).current.publicApiBasePath;
  private readonly location = inject(Location);
  readonly reset = inject(ActivatedRoute).snapshot.data['reset'] === true;
  private readonly values = new URLSearchParams(inject(ActivatedRoute).snapshot.fragment ?? '');
  private token = this.values.get('token') ?? '';
  private userId = this.values.get('user') ?? '';
  readonly validLink =
    /^[0-9a-fA-F-]{36}$/.test(this.userId) && /^[A-Za-z0-9_-]{1,4096}$/.test(this.token);
  readonly state = signal<'ready' | 'pending' | 'success'>('ready');
  readonly error = signal('');
  readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true }),
    password: new FormControl('', { nonNullable: true }),
    confirm: new FormControl('', { nonNullable: true }),
  });
  constructor() {
    if (this.values.size)
      this.location.replaceState(this.reset ? '/reset-password' : '/forgot-password');
    if (!this.reset)
      this.form.controls.email.setValidators([
        Validators.required,
        Validators.email,
        Validators.maxLength(254),
      ]);
  }
  async submit(): Promise<void> {
    if (this.state() !== 'ready') return;
    this.error.set('');
    const value = this.form.getRawValue();
    if (
      this.form.invalid ||
      (this.reset && (!this.validLink || !value.password || value.password !== value.confirm))
    ) {
      this.error.set(this.reset ? 'Enter matching passwords.' : 'Enter a valid email address.');
      return;
    }
    this.state.set('pending');
    try {
      const result = await firstValueFrom(
        this.http
          .post<{
            status: string;
          }>(
            this.apiBase +
              '/v1/identity/' +
              (this.reset ? 'password-resets' : 'password-reset-requests'),
            this.reset
              ? { userId: this.userId, token: this.token, newPassword: value.password }
              : { email: value.email },
          )
          .pipe(timeout(15000)),
      );
      if (result.status !== (this.reset ? 'password_reset' : 'request_received'))
        throw new Error('Unexpected response');
      this.form.reset();
      this.token = '';
      this.userId = '';
      this.state.set('success');
    } catch {
      this.form.controls.password.reset();
      this.form.controls.confirm.reset();
      this.error.set(
        this.reset
          ? 'Unable to reset. Check the password policy, or request a new link if it has expired.'
          : 'Unable to send your request. Please try again shortly.',
      );
      this.state.set('ready');
    }
  }
}
