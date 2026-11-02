import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { RuntimeConfigService } from '../../../../libs/runtime-config/runtime-config';
@Component({ selector: 'app-root', imports: [RouterOutlet], templateUrl: './app.html' })
export class App {
  protected readonly config = inject(RuntimeConfigService).current;
}
