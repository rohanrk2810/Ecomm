import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { RegistrationApi } from '../../../../../../libs/api-clients/registration-api';
@Component({
  selector: 'commerce-resend-verification',
  imports: [ReactiveFormsModule, RouterLink],
  styleUrl: '../registration/registration.css',
  template: `<section class="registration" aria-labelledby="resend-title">
    <a routerLink="/" class="back">← Back to home</a>
    <p class="eyebrow">Let's try again</p>
    <h1 id="resend-title">Request a new link.</h1>
    @if (state() === 'pending') {
      <div class="pending" role="status">
        <p>
          If an account needs verification, you'll receive an email with the next steps. Please
          allow a little time before requesting another.
        </p>
      </div>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()" [attr.aria-busy]="state() === 'submitting'">
        <label for="resend-email">Email address</label
        ><input
          id="resend-email"
          type="email"
          formControlName="email"
          autocomplete="email"
          maxlength="254"
          required
        />
        @if (form.controls.email.touched && form.controls.email.invalid) {
          <p class="error">Enter a valid email address.</p>
        }
        @if (error()) {
          <p role="alert" class="error">{{ error() }}</p>
        }
        <button type="submit" [disabled]="state() === 'submitting'">
          {{ state() === 'submitting' ? 'Submitting…' : 'Request verification email' }}
        </button>
      </form>
    }
  </section>`,
})
export class ResendVerification {
  private readonly api = inject(RegistrationApi);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
  });
  readonly state = signal<'form' | 'submitting' | 'pending'>('form');
  readonly error = signal('');
  async submit(): Promise<void> {
    if (this.state() !== 'form') return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.state.set('submitting');
    this.error.set('');
    try {
      const response = await firstValueFrom(
        this.api.resendVerification(this.form.controls.email.value.trim()),
      );
      if (response.status !== 'pending_verification') throw new Error('Unexpected response');
      this.state.set('pending');
      this.form.reset();
    } catch {
      this.state.set('form');
      this.error.set('We could not submit your request. Please wait a little and try again.');
    }
  }
}
