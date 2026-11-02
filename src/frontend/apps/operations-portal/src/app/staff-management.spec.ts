import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, it, expect } from 'vitest';
import { StaffManagement } from './staff-management';
import { RuntimeConfigService } from '../../../../libs/runtime-config/runtime-config';
describe('Staff role management', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RuntimeConfigService, useValue: { current: { publicApiBasePath: '/api' } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  async function ready() {
    const fixture = TestBed.createComponent(StaffManagement);
    http.expectOne('/api/v1/identity/staff-access').flush({
      userId: 'actor',
      branches: [{ branchId: 'branch', permissions: ['identity.staff.grants.write'] }],
    });
    await fixture.whenStable();
    return fixture;
  }
  it('does not expose management to read-only staff', async () => {
    const fixture = TestBed.createComponent(StaffManagement);
    http.expectOne('/api/v1/identity/staff-access').flush({
      userId: 'actor',
      branches: [{ branchId: 'branch', permissions: ['fixture.read'] }],
    });
    await fixture.whenStable();
    expect(fixture.componentInstance.state()).toBe('denied');
    http.expectNone('/api/v1/identity/staff/branches/branch/roles');
  });
  it('looks up accounts with CSRF, requires review, and waits for backend confirmation', async () => {
    const fixture = await ready();
    const component = fixture.componentInstance;
    component.form.controls.branch.setValue('branch');
    http
      .expectOne('/api/v1/identity/staff/branches/branch/roles')
      .flush([{ id: 'role', name: 'Configured role', permissions: ['fixture.read'] }]);
    await fixture.whenStable();
    component.form.controls.email.setValue('fixture@example.invalid');
    const finding = component.findAccount();
    http.expectOne('/bff/antiforgery').flush({ requestToken: 'csrf' });
    await Promise.resolve();
    await Promise.resolve();
    const lookup = http.expectOne('/api/v1/identity/staff/branches/branch/resolve-account');
    expect(lookup.request.headers.get('X-CSRF-TOKEN')).toBe('csrf');
    expect(lookup.request.body).toEqual({ email: 'fixture@example.invalid' });
    lookup.flush({ userId: 'target', assignedRoleIds: [] });
    await finding;
    component.form.controls.role.setValue('role');
    http.expectNone('/api/v1/identity/staff/target/branches/branch/roles/role');
    component.prepare(true);
    expect(component.review()?.target).toBe('target');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Review role change');
    expect(fixture.nativeElement.textContent).toContain('fixture@example.invalid');
    expect(fixture.nativeElement.textContent).toContain('Configured role');
    const changing = component.confirm();
    await component.confirm();
    http.expectOne('/bff/antiforgery').flush({ requestToken: 'csrf-write' });
    await Promise.resolve();
    await Promise.resolve();
    const write = http.expectOne('/api/v1/identity/staff/target/branches/branch/roles/role');
    expect(write.request.method).toBe('PUT');
    expect(write.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-write');
    expect(component.saved()).toBe('');
    write.flush(null, { status: 204, statusText: 'Saved' });
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
    http
      .expectOne('/api/v1/identity/staff-access')
      .flush({}, { status: 403, statusText: 'Revoked' });
    await changing;
    expect(component.saved()).toBe('Role granted.');
    expect(component.state()).toBe('denied');
    expect(component.target()).toBeNull();
    expect(component.review()).toBeNull();
  });
  it('sends reviewed removals as DELETE and clears the target after a failed write', async () => {
    const fixture = await ready();
    const component = fixture.componentInstance;
    component.form.controls.branch.setValue('branch');
    http
      .expectOne('/api/v1/identity/staff/branches/branch/roles')
      .flush([{ id: 'role', name: 'Configured role', permissions: ['fixture.read'] }]);
    await fixture.whenStable();
    component.form.controls.email.setValue('fixture@example.invalid');
    const finding = component.findAccount();
    http.expectOne('/bff/antiforgery').flush({ requestToken: 'csrf' });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne('/api/v1/identity/staff/branches/branch/resolve-account').flush({
      userId: 'target',
      assignedRoleIds: ['role'],
    });
    await finding;
    component.form.controls.role.setValue('role');
    component.prepare(false);
    expect(component.review()?.grant).toBe(false);
    const changing = component.confirm();
    http.expectOne('/bff/antiforgery').flush({ requestToken: 'csrf-remove' });
    await Promise.resolve();
    await Promise.resolve();
    const write = http.expectOne('/api/v1/identity/staff/target/branches/branch/roles/role');
    expect(write.request.method).toBe('DELETE');
    expect(write.request.headers.get('X-CSRF-TOKEN')).toBe('csrf-remove');
    write.flush({}, { status: 503, statusText: 'Unavailable' });
    await changing;
    expect(component.saved()).toBe('');
    expect(component.target()).toBeNull();
    expect(component.review()).toBeNull();
    expect(component.error()).toContain('could not be confirmed');
  });
  it('clears target and review when email changes', async () => {
    const fixture = await ready();
    const component = fixture.componentInstance;
    component.target.set({ userId: 'target', assignedRoleIds: [] });
    component.review.set({
      branch: 'branch',
      target: 'target',
      email: 'old@example.invalid',
      role: { id: 'role', name: 'role', permissions: [] },
      grant: true,
    });
    component.form.controls.email.setValue('new@example.invalid');
    expect(component.target()).toBeNull();
    expect(component.review()).toBeNull();
  });
});
