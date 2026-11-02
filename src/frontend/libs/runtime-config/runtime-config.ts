import { Injectable } from '@angular/core';

export interface RuntimeConfig {
  readonly appDisplayName: string;
  readonly publicApiBasePath: string;
  readonly defaultLocale: string;
  readonly supportContactLabel: string;
  readonly heading: string;
  readonly description: string;
}

export function validateRuntimeConfig(value: unknown): RuntimeConfig {
  if (value === null || typeof value !== 'object')
    throw new Error('Invalid runtime configuration.');
  const record = value as Record<string, unknown>;
  const required = [
    'appDisplayName',
    'publicApiBasePath',
    'defaultLocale',
    'supportContactLabel',
    'heading',
    'description',
  ] as const;
  for (const key of required) {
    if (typeof record[key] !== 'string' || !record[key].trim() || record[key].length > 500) {
      throw new Error('Invalid runtime configuration.');
    }
  }
  // Only path segments, never schemes, protocol-relative URLs or encoded separators.
  if (!/^\/(?:[a-zA-Z0-9_-]+\/)*[a-zA-Z0-9_-]+$/.test(String(record['publicApiBasePath']))) {
    throw new Error('API path must be same-origin.');
  }
  try {
    new Intl.Locale(String(record['defaultLocale']));
  } catch {
    throw new Error('Invalid locale.');
  }
  return Object.freeze(
    Object.fromEntries(required.map((key) => [key, record[key]])),
  ) as unknown as RuntimeConfig;
}

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  const response = await fetch('/runtime-config.json', { signal: AbortSignal.timeout(8000) });
  if (!response.ok) throw new Error('Runtime configuration unavailable.');
  return validateRuntimeConfig(await response.json());
}

@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private config?: RuntimeConfig;
  async load(): Promise<void> {
    this.config = await loadRuntimeConfig();
    document.title = this.config.appDisplayName;
    document.documentElement.lang = this.config.defaultLocale;
  }
  get current(): RuntimeConfig {
    if (!this.config) throw new Error('Runtime configuration not loaded.');
    return this.config;
  }
}
