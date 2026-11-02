import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { Account } from './account';
describe('Account session', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  it('offers browser sign-in only after an anonymous backend result', async () => {
    const fixture = TestBed.createComponent(Account);
    expect(fixture.componentInstance.state()).toBe('loading');
    http.expectOne('/bff/session').flush({ available: true, authenticated: false, userId: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.componentInstance.state()).toBe('anonymous');
    http.expectNone('/bff/antiforgery');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/bff/login"]'),
    ).not.toBeNull();
  });
  it('uses a native CSRF-protected logout form and never renders OAuth tokens', async () => {
    const fixture = TestBed.createComponent(Account);
    http
      .expectOne('/bff/session')
      .flush({ available: true, authenticated: true, userId: 'synthetic-user' });
    await Promise.resolve();
    await Promise.resolve();
    http.expectOne('/bff/antiforgery').flush({ requestToken: 'synthetic-csrf' });
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(fixture.componentInstance.state()).toBe('authenticated');
    expect(element.querySelector('form')?.getAttribute('method')).toBe('post');
    expect(element.querySelector('form')?.getAttribute('action')).toBe('/bff/logout');
    expect(element.querySelector('input')?.value).toBe('synthetic-csrf');
    expect(element.textContent).not.toContain('synthetic-user');
  });
  it('does not claim an active session when dependencies fail', async () => {
    const fixture = TestBed.createComponent(Account);
    http.expectOne('/bff/session').flush({}, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect(fixture.componentInstance.state()).toBe('unavailable');
  });
});
