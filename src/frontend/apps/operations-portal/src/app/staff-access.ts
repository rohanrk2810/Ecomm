import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { StaffAccessApi, StaffAccessResponse } from '../../../../libs/auth-client/staff-access-api';
@Component({
  selector: 'commerce-staff-access',
  template: ` <section aria-labelledby="staff-title">
    <p>Operations</p>
    <h1 id="staff-title">Your staff access</h1>
    @if (state() === 'loading') {
      <p role="status">Checking your current access...</p>
    } @else if (state() === 'signin') {
      <p>Sign in to your account, then return here to check your staff access.</p>
      <a href="/bff/login">Sign in</a><button (click)="load()">Check again</button>
    } @else if (state() === 'denied') {
      <p role="alert">
        Staff access requires a verified MFA session and active staff membership. Contact your
        administrator if an assignment is missing.
      </p>
      <button (click)="load()">Check again</button>
    } @else if (state() === 'error') {
      <p role="alert">Your access could not be checked. Please try again.</p>
      <button (click)="load()">Try again</button>
    } @else {
      @if (access()!.branches.length === 0) {
        <p role="status">No branch permissions are currently assigned.</p>
      }
      @for (branch of access()!.branches; track branch.branchId) {
        <article>
          <h2>Branch {{ branch.branchId }}</h2>
          <p>Current permissions</p>
          <ul>
            @for (permission of branch.permissions; track permission) {
              <li>{{ permission }}</li>
            }
          </ul>
        </article>
      }
      <button (click)="load()">Refresh access</button>
    }
  </section>`,
  styles: [
    `
      :host {
        display: block;
        padding: 2rem;
        max-width: 64rem;
        margin: auto;
      }
      article {
        padding: 1rem;
        border: 1px solid #ddd;
        border-radius: 1rem;
        margin-block: 1rem;
      }
      button,
      a {
        margin-inline-end: 1rem;
      }
      h2 {
        overflow-wrap: anywhere;
        font-size: 1.15rem;
      }
    `,
  ],
})
export class StaffAccess {
  private readonly api = inject(StaffAccessApi);
  private loading = false;
  readonly state = signal<'loading' | 'signin' | 'denied' | 'error' | 'ready'>('loading');
  readonly access = signal<StaffAccessResponse | null>(null);
  constructor() {
    void this.load();
  }
  async load(): Promise<void> {
    if (this.loading) return;
    this.loading = true;
    this.state.set('loading');
    this.access.set(null);
    try {
      const result = await firstValueFrom(this.api.read());
      if (
        !result.userId ||
        !Array.isArray(result.branches) ||
        result.branches.some(
          (b) =>
            !b.branchId ||
            !Array.isArray(b.permissions) ||
            b.permissions.some((p) => typeof p !== 'string'),
        )
      )
        throw new Error('Invalid response');
      this.access.set(result);
      this.state.set('ready');
    } catch (error) {
      this.state.set(
        error instanceof HttpErrorResponse && error.status === 401
          ? 'signin'
          : error instanceof HttpErrorResponse && error.status === 403
            ? 'denied'
            : 'error',
      );
    } finally {
      this.loading = false;
    }
  }
}
