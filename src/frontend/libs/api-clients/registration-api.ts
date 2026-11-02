import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
import { RuntimeConfigService } from '../runtime-config/runtime-config';
export interface RegistrationPolicy {
  minimumPasswordLength: number;
  maximumPasswordLength: number;
  requireDigit: boolean;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireNonAlphanumeric: boolean;
}
export interface RegistrationResponse {
  status: 'pending_verification';
}
@Injectable({ providedIn: 'root' })
export class RegistrationApi {
  confirmEmail(userId: string, token: string) {
    return this.http
      .post<{
        status: 'verified';
      }>(`${this.base}/v1/identity/email-verifications`, { userId, token })
      .pipe(timeout(15000));
  }
  resendVerification(email: string) {
    return this.http
      .post<RegistrationResponse>(`${this.base}/v1/identity/email-verification-requests`, { email })
      .pipe(timeout(15000));
  }
  private readonly http = inject(HttpClient);
  private readonly base = inject(RuntimeConfigService).current.publicApiBasePath;
  policy() {
    return this.http
      .get<RegistrationPolicy>(`${this.base}/v1/identity/registration-policy`)
      .pipe(timeout(10000));
  }
  register(email: string, password: string) {
    return this.http
      .post<RegistrationResponse>(`${this.base}/v1/identity/registrations`, { email, password })
      .pipe(timeout(15000));
  }
}
