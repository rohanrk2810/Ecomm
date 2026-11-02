import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  RuntimeConfigService,
  loadRuntimeConfig,
  validateRuntimeConfig,
} from '../../../../libs/runtime-config/runtime-config';
import { appConfig } from './app.config';
import { ApplicationInitStatus } from '@angular/core';

const valid = {
  appDisplayName: 'Synthetic store',
  publicApiBasePath: '/api',
  defaultLocale: 'en-IN',
  supportContactLabel: 'Test only',
  heading: 'Test',
  description: 'Synthetic configuration',
};
afterEach(() => {
  vi.unstubAllGlobals();
  TestBed.resetTestingModule();
});
describe('Runtime startup configuration', () => {
  it.each([
    'https://evil.invalid',
    '//evil.invalid',
    '/api/../evil',
    '/api%2fexample',
    '/api?redirect=x',
    '/api\\evil',
  ])('rejects unsafe API path %s', (path) =>
    expect(() => validateRuntimeConfig({ ...valid, publicApiBasePath: path })).toThrow(),
  );
  it('rejects incomplete configuration', () => expect(() => validateRuntimeConfig({})).toThrow());
  it('discards unexpected properties and freezes settings', () => {
    const config = validateRuntimeConfig({ ...valid, unexpected: 'discard' });
    expect(config).not.toHaveProperty('unexpected');
    expect(Object.isFrozen(config)).toBe(true);
  });
  it('does not start with unavailable runtime configuration', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false }));
    await expect(loadRuntimeConfig()).rejects.toThrow('unavailable');
  });
  it('loads configuration through the actual application initializer', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: async () => valid }));
    TestBed.configureTestingModule({ providers: appConfig.providers });
    const startup = TestBed.inject(ApplicationInitStatus);
    await startup.donePromise;
    expect(TestBed.inject(RuntimeConfigService).current.appDisplayName).toBe('Synthetic store');
  });
});
