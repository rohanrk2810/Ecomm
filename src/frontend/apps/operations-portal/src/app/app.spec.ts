import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { App } from './app';
import { RuntimeConfigService } from '../../../../libs/runtime-config/runtime-config';
describe('Operations foundation', () => {
  it('shows setup state without privileged features or a pretend staff login', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: RuntimeConfigService,
          useValue: {
            current: { appDisplayName: 'Synthetic operations', supportContactLabel: 'Pending' },
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Opening soon');
    expect(element.querySelector('form')).toBeNull();
  });
});
