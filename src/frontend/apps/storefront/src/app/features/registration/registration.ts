import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import {
  RegistrationApi,
  RegistrationPolicy,
} from '../../../../../../libs/api-clients/registration-api';

@Component({
  selector: 'commerce-registration',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './registration.html',
  styleUrl: './registration.css',
})
export class Registration {
  private readonly api = inject(RegistrationApi);
  private readonly builder = inject(FormBuilder).nonNullable;
  readonly policy = signal<RegistrationPolicy | undefined>(undefined);
  readonly state = signal<'loading' | 'form' | 'submitting' | 'pending' | 'unavailable'>('loading');
  readonly error = signal('');
  readonly form = this.builder.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', Validators.required],
  });
  constructor() {
    void this.loadPolicy();
  }
  async loadPolicy(): Promise<void> {
    this.state.set('loading');
    this.error.set('');
    try {
      const policy = await firstValueFrom(this.api.policy());
      if (
        !Number.isInteger(policy.minimumPasswordLength) ||
        policy.minimumPasswordLength < 12 ||
        !Number.isInteger(policy.maximumPasswordLength) ||
        policy.maximumPasswordLength > 128 ||
        policy.maximumPasswordLength < policy.minimumPasswordLength
      )
        throw new Error('Invalid policy');
      this.policy.set(policy);
      this.form.controls.password.setValidators([
        Validators.required,
        Validators.minLength(policy.minimumPasswordLength),
        Validators.maxLength(policy.maximumPasswordLength),
      ]);
      this.form.controls.password.updateValueAndValidity();
      this.state.set('form');
    } catch {
      this.state.set('unavailable');
    }
  }
  async submit(): Promise<void> {
    if (this.state() !== 'form') return;
    this.form.markAllAsTouched();
    if (this.form.invalid || !this.policy()) return;
    this.state.set('submitting');
    this.error.set('');
    try {
      const values = this.form.getRawValue();
      const response = await firstValueFrom(
        this.api.register(values.email.trim(), values.password),
      );
      if (response.status !== 'pending_verification')
        throw new Error('Unexpected registration state');
      this.form.controls.password.reset();
      this.state.set('pending');
    } catch (error: unknown) {
      this.form.controls.password.reset();
      this.state.set('form');
      this.error.set(
        error instanceof HttpErrorResponse && error.status === 429
          ? 'Too many attempts. Please wait a little and try again.'
          : error instanceof HttpErrorResponse && error.status === 400
            ? 'Check your email address and the password requirements, then try again.'
            : 'We could not submit your request. Please try again.',
      );
    }
  }
}
