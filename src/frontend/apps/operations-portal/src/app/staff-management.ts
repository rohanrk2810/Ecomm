import { Component, inject, signal, DestroyRef } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { StaffAccessApi, StaffAccessResponse } from '../../../../libs/auth-client/staff-access-api';
import {
  StaffManagementApi,
  StaffRoleChoice,
  StaffAccountChoice,
} from '../../../../libs/auth-client/staff-management-api';
interface Review {
  branch: string;
  target: string;
  email: string;
  role: StaffRoleChoice;
  grant: boolean;
}
@Component({
  selector: 'commerce-staff-management',
  imports: [ReactiveFormsModule],
  template: ` <section aria-labelledby="management-title">
    <a href="/staff-access">My staff access</a>
    <h1 id="management-title">Manage branch roles</h1>
    @if (saved()) {
      <p role="status">{{ saved() }}</p>
    }
    @if (state() === 'loading') {
      <p role="status">Checking branch management access...</p>
    } @else if (state() === 'signin') {
      <p>Sign in and complete MFA, then return here.</p>
      <a href="/bff/login">Sign in</a>
    } @else if (state() === 'denied') {
      <p role="alert">
        You do not currently have permission to manage staff in any assigned branch.
      </p>
      <button (click)="refresh()" [disabled]="busy()">Refresh access</button>
    } @else if (state() === 'error') {
      <p role="alert">Branch management is temporarily unavailable.</p>
      <button (click)="load()">Try again</button>
    } @else {
      <form [formGroup]="form" (ngSubmit)="findAccount()">
        <fieldset [disabled]="busy()">
          <legend>Choose branch and account</legend>
          <label for="management-branch">Assigned branch</label
          ><select id="management-branch" formControlName="branch">
            <option value="">Choose a branch</option>
            @for (branch of branches(); track branch.branchId) {
              <option [value]="branch.branchId">Branch reference {{ branch.branchId }}</option>
            }
          </select>
          <label for="management-email">Employee email</label
          ><input
            id="management-email"
            type="email"
            autocomplete="off"
            formControlName="email"
            maxlength="254"
          /><button
            type="submit"
            [disabled]="!form.controls.branch.value || form.controls.email.invalid"
          >
            Find verified account
          </button>
          <label for="management-role">Role</label
          ><select id="management-role" formControlName="role">
            <option value="">Choose a role</option>
            @for (role of roles(); track role.id) {
              <option [value]="role.id">{{ role.name || 'Unnamed role' }}</option>
            }
          </select>
          @if (form.controls.branch.value && roles().length === 0) {
            <p>No roles are currently available within your delegation limits.</p>
          }
          @if (target()) {
            <p role="status">Verified MFA-enrolled account found.</p>
            @if (selectedRole(); as role) {
              <p>Permissions in this role</p>
              <ul>
                @for (permission of role.permissions; track permission) {
                  <li>{{ permission }}</li>
                }
              </ul>
              <p>
                {{
                  assigned()
                    ? 'This role is currently assigned in the selected branch.'
                    : 'This role is not currently assigned in the selected branch.'
                }}
              </p>
              <button type="button" (click)="prepare(true)" [disabled]="assigned()">
                Review grant</button
              ><button type="button" (click)="prepare(false)" [disabled]="!assigned()">
                Review removal
              </button>
            }
          }
        </fieldset>
      </form>
      @if (review(); as item) {
        <section aria-labelledby="review-title">
          <h2 id="review-title">Review role change</h2>
          <p>
            {{ item.grant ? 'Grant' : 'Remove' }} {{ item.role.name || 'selected role' }}
            {{ item.grant ? 'for' : 'from' }} {{ item.email }} in branch {{ item.branch }}.
          </p>
          @if (item.target === currentUserId) {
            <p>Changing your own role may close your staff access.</p>
          }
          <button type="button" (click)="confirm()" [disabled]="busy()">
            Confirm {{ item.grant ? 'grant' : 'removal' }}</button
          ><button type="button" (click)="review.set(null)" [disabled]="busy()">Cancel</button>
        </section>
      }
      @if (busy()) {
        <p role="status">Please wait...</p>
      }
      @if (error()) {
        <p role="alert">{{ error() }}</p>
      }
    }
  </section>`,
  styles: [
    `
      :host {
        display: block;
        max-width: 65rem;
        margin: auto;
        padding: 2rem;
      }
      label,
      input,
      select {
        display: block;
        margin-block: 0.6rem;
      }
      input,
      select {
        width: 100%;
        max-width: 38rem;
        padding: 0.6rem;
      }
      fieldset {
        border: 1px solid #ddd;
        padding: 1rem;
        border-radius: 1rem;
      }
      button {
        margin: 0.5rem 0.5rem 0.5rem 0;
        padding: 0.5rem;
      }
      section {
        overflow-wrap: anywhere;
      }
    `,
  ],
})
export class StaffManagement {
  private readonly accessApi = inject(StaffAccessApi);
  private readonly api = inject(StaffManagementApi);
  private readonly destroy = inject(DestroyRef);
  readonly state = signal<'loading' | 'ready' | 'denied' | 'signin' | 'error'>('loading');
  readonly busy = signal(false);
  readonly error = signal('');
  readonly saved = signal('');
  readonly branches = signal<StaffAccessResponse['branches']>([]);
  readonly roles = signal<StaffRoleChoice[]>([]);
  readonly target = signal<StaffAccountChoice | null>(null);
  readonly review = signal<Review | null>(null);
  currentUserId = '';
  private branchEpoch = 0;
  private targetEpoch = 0;
  readonly form = new FormGroup({
    branch: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email, Validators.maxLength(254)],
    }),
    role: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });
  constructor() {
    this.form.controls.branch.valueChanges
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe((branch) => {
        this.roles.set([]);
        this.form.controls.role.setValue('');
        this.clearTarget();
        void this.loadRoles(branch);
      });
    this.form.controls.email.valueChanges
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe(() => this.clearTarget());
    this.form.controls.role.valueChanges
      .pipe(takeUntilDestroyed(this.destroy))
      .subscribe(() => this.review.set(null));
    void this.load();
  }
  private clearTarget() {
    this.targetEpoch++;
    this.target.set(null);
    this.review.set(null);
    this.error.set('');
  }
  private failure(error: unknown) {
    this.review.set(null);
    this.target.set(null);
    if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
      this.roles.set([]);
      this.branches.set([]);
      this.state.set(error.status === 401 ? 'signin' : 'denied');
    } else
      this.error.set(
        this.saved()
          ? 'The change was saved, but current access could not be refreshed. Refresh access before another change.'
          : 'The result could not be confirmed. Find the account again before retrying. Verified email and MFA are required.',
      );
  }
  async load() {
    this.state.set('loading');
    this.clearTarget();
    this.roles.set([]);
    try {
      const access = await firstValueFrom(this.accessApi.read());
      this.currentUserId = access.userId;
      const branches = access.branches.filter((b) =>
        b.permissions.includes('identity.staff.grants.write'),
      );
      this.branches.set(branches);
      if (
        this.form.controls.branch.value &&
        !branches.some((branch) => branch.branchId === this.form.controls.branch.value)
      )
        this.form.controls.branch.setValue('');
      this.state.set(branches.length ? 'ready' : 'denied');
    } catch (error) {
      this.failure(error);
      if (this.state() === 'loading') this.state.set('error');
    }
  }
  async refresh() {
    if (this.busy()) return;
    await this.load();
    if (this.state() === 'ready' && this.form.controls.branch.value)
      await this.loadRoles(this.form.controls.branch.value);
  }
  async loadRoles(branch: string) {
    const epoch = ++this.branchEpoch;
    if (!branch) {
      this.busy.set(false);
      return;
    }
    this.busy.set(true);
    try {
      const roles = await firstValueFrom(this.api.roles(branch));
      if (epoch === this.branchEpoch) this.roles.set(roles);
    } catch (error) {
      if (epoch === this.branchEpoch) this.failure(error);
    } finally {
      if (epoch === this.branchEpoch) this.busy.set(false);
    }
  }
  selectedRole() {
    return this.roles().find((role) => role.id === this.form.controls.role.value);
  }
  assigned() {
    return this.target()?.assignedRoleIds.includes(this.form.controls.role.value) === true;
  }
  async findAccount() {
    if (this.busy() || this.form.controls.email.invalid || !this.form.controls.branch.value) return;
    this.clearTarget();
    const epoch = this.targetEpoch;
    const branch = this.form.controls.branch.value;
    const email = this.form.controls.email.value.trim();
    this.busy.set(true);
    try {
      const target = await this.api.resolve(branch, email);
      if (epoch === this.targetEpoch && branch === this.form.controls.branch.value)
        this.target.set(target);
    } catch (error) {
      if (epoch === this.targetEpoch) this.failure(error);
    } finally {
      this.busy.set(false);
    }
  }
  prepare(grant: boolean) {
    const role = this.selectedRole();
    const target = this.target();
    if (this.busy() || !role || !target || grant === this.assigned()) return;
    this.review.set({
      branch: this.form.controls.branch.value,
      email: this.form.controls.email.value.trim(),
      target: target.userId,
      role,
      grant,
    });
  }
  async confirm() {
    const item = this.review();
    if (!item || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.saved.set('');
    try {
      await this.api.change(item.branch, item.target, item.role.id, item.grant);
      this.review.set(null);
      this.target.set(null);
      this.saved.set(item.grant ? 'Role granted.' : 'Role removed.');
      await this.load();
      if (
        this.state() === 'ready' &&
        this.branches().some((branch) => branch.branchId === item.branch)
      ) {
        const roles = await firstValueFrom(this.api.roles(item.branch));
        this.roles.set(roles);
        if (
          this.form.controls.branch.value === item.branch &&
          this.form.controls.email.value.trim() === item.email
        ) {
          this.target.set(await this.api.resolve(item.branch, item.email));
        }
      }
    } catch (error) {
      this.failure(error);
    } finally {
      this.busy.set(false);
    }
  }
}
