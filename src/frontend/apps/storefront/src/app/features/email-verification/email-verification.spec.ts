import { TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EmailVerification } from './email-verification';
import { ResendVerification } from './resend-verification';
import { RuntimeConfigService } from '../../../../../../libs/runtime-config/runtime-config';
describe('Email verification', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: RuntimeConfigService, useValue: { current: { publicApiBasePath: '/api' } } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              fragment: 'user=11111111-1111-1111-1111-111111111111&token=synthetic-token',
            },
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  it('removes the secret URL and waits for explicit confirmation before making a request', () => {
    const replace = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(EmailVerification);
    fixture.detectChanges();
    expect(replace).toHaveBeenCalledWith('/verify-email');
    expect(fixture.componentInstance.state()).toBe('ready');
    http.expectNone('/api/v1/identity/email-verifications');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('synthetic-token');
  });
  it('waits for backend success and prevents repeated clicks', async () => {
    const component = TestBed.createComponent(EmailVerification).componentInstance;
    const pending = component.verify();
    await component.verify();
    expect(component.state()).toBe('submitting');
    http.expectOne('/api/v1/identity/email-verifications').flush({ status: 'verified' });
    await pending;
    expect(component.state()).toBe('verified');
  });
  it('offers a replacement after an invalid or expired link', async () => {
    const component = TestBed.createComponent(EmailVerification).componentInstance;
    const pending = component.verify();
    http
      .expectOne('/api/v1/identity/email-verifications')
      .flush({}, { status: 400, statusText: 'Invalid' });
    await pending;
    expect(component.state()).toBe('invalid');
  });
  it('keeps a safe retry state on a temporary failure', async () => {
    const component = TestBed.createComponent(EmailVerification).componentInstance;
    const pending = component.verify();
    http
      .expectOne('/api/v1/identity/email-verifications')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await pending;
    expect(component.state()).toBe('ready');
    expect(component.error()).toContain('try again');
  });
  it('shows the same neutral resend response without claiming email delivery', async () => {
    const component = TestBed.createComponent(ResendVerification).componentInstance;
    component.form.setValue({ email: 'synthetic@example.invalid' });
    const pending = component.submit();
    http
      .expectOne('/api/v1/identity/email-verification-requests')
      .flush({ status: 'pending_verification' });
    await pending;
    expect(component.state()).toBe('pending');
    expect(component.form.controls.email.value).toBe('');
  });
});
