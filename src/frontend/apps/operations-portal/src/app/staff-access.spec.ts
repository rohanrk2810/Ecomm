import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, it, expect } from 'vitest';
import { StaffAccess } from './staff-access';
import { RuntimeConfigService } from '../../../../libs/runtime-config/runtime-config';
describe('Current staff access', () => {
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
  it.each([
    [401, 'signin'],
    [403, 'denied'],
    [503, 'error'],
  ])('handles %s without claiming access', async (status, state) => {
    const fixture = TestBed.createComponent(StaffAccess);
    http
      .expectOne('/api/v1/identity/staff-access')
      .flush({}, { status: Number(status), statusText: 'Denied' });
    await fixture.whenStable();
    expect(fixture.componentInstance.state()).toBe(state);
    expect(fixture.componentInstance.access()).toBeNull();
  });
  it('renders current server grants and clears them immediately during refresh', async () => {
    const fixture = TestBed.createComponent(StaffAccess);
    http.expectOne('/api/v1/identity/staff-access').flush({
      userId: 'synthetic-user',
      branches: [{ branchId: 'synthetic-branch', permissions: ['fixture.read'] }],
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('fixture.read');
    const pending = fixture.componentInstance.load();
    await fixture.componentInstance.load();
    expect(fixture.componentInstance.access()).toBeNull();
    http
      .expectOne('/api/v1/identity/staff-access')
      .flush({}, { status: 403, statusText: 'Revoked' });
    await pending;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('fixture.read');
  });
});
