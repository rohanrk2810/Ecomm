import { RuntimeConfigService } from '../../../../../../libs/runtime-config/runtime-config';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { Location } from '@angular/common';
import { describe, it, expect } from 'vitest';
import { PasswordRecovery } from './password-recovery';
describe('Password recovery', () => {
  it('clears the bearer fragment and waits for explicit submission', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: RuntimeConfigService, useValue: { current: { publicApiBasePath: '/api' } } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              data: { reset: true },
              fragment: 'user=11111111-1111-1111-1111-111111111111&token=synthetic',
            },
          },
        },
      ],
    });
    const location = TestBed.inject(Location);
    let cleared = '';
    location.replaceState = (path: string) => {
      cleared = path;
    };
    const component = TestBed.createComponent(PasswordRecovery).componentInstance;
    const http = TestBed.inject(HttpTestingController);
    expect(cleared).toBe('/reset-password');
    http.expectNone('/api/v1/identity/password-resets');
    component.form.patchValue({
      password: 'Synthetic!Password888',
      confirm: 'Synthetic!Password888',
    });
    const pending = component.submit();
    await component.submit();
    http.expectOne('/api/v1/identity/password-resets').flush({ status: 'password_reset' });
    await pending;
    expect(component.state()).toBe('success');
    expect(component.form.controls.password.value).toBe('');
    http.verify();
    TestBed.resetTestingModule();
  });
});
