import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { beforeEach, afterEach, describe, expect, it } from 'vitest';
import { Registration } from './registration';
import { RuntimeConfigService } from '../../../../../../libs/runtime-config/runtime-config';
const policy = {
  minimumPasswordLength: 12,
  maximumPasswordLength: 128,
  requireDigit: true,
  requireUppercase: true,
  requireLowercase: true,
  requireNonAlphanumeric: true,
};
describe('Registration flow', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
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
    const fixture = TestBed.createComponent(Registration);
    http.expectOne('/api/v1/identity/registration-policy').flush(policy);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }
  it('blocks registration when server policy is unavailable', async () => {
    const fixture = TestBed.createComponent(Registration);
    http
      .expectOne('/api/v1/identity/registration-policy')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBe('unavailable');
    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
  });
  it('waits for backend confirmation, blocks duplicate clicks and clears the password', async () => {
    const fixture = await ready();
    const component = fixture.componentInstance;
    component.form.setValue({
      email: 'synthetic@example.invalid',
      password: 'Synthetic!Password9',
    });
    const pending = component.submit();
    await component.submit();
    expect(component.state()).toBe('submitting');
    const request = http.expectOne('/api/v1/identity/registrations');
    expect(request.request.body).toEqual({
      email: 'synthetic@example.invalid',
      password: 'Synthetic!Password9',
    });
    request.flush({ status: 'pending_verification' });
    await pending;
    expect(component.state()).toBe('pending');
    expect(component.form.controls.password.value).toBe('');
  });
  it('shows a safe retry state after rate limiting', async () => {
    const fixture = await ready();
    const component = fixture.componentInstance;
    component.form.setValue({
      email: 'synthetic@example.invalid',
      password: 'Synthetic!Password9',
    });
    const pending = component.submit();
    http
      .expectOne('/api/v1/identity/registrations')
      .flush({}, { status: 429, statusText: 'Too many requests' });
    await pending;
    expect(component.state()).toBe('form');
    expect(component.error()).toContain('Too many attempts');
    expect(component.form.controls.password.value).toBe('');
  });
  it('never sends invalid form data', async () => {
    const fixture = await ready();
    await fixture.componentInstance.submit();
    http.expectNone('/api/v1/identity/registrations');
  });
});
