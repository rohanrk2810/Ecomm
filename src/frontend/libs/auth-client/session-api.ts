import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { timeout } from 'rxjs';
export interface CustomerSession {
  available: boolean;
  authenticated: boolean;
  userId: string | null;
}
@Injectable({ providedIn: 'root' })
export class SessionApi {
  private readonly http = inject(HttpClient);
  session() {
    return this.http.get<CustomerSession>('/bff/session').pipe(timeout(15000));
  }
  antiforgery() {
    return this.http.get<{ requestToken: string }>('/bff/antiforgery').pipe(timeout(15000));
  }
}
