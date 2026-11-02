import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { timeout } from 'rxjs';
import { RuntimeConfigService } from '../runtime-config/runtime-config';
export interface StaffAccessResponse {
  userId: string;
  branches: { branchId: string; permissions: string[] }[];
}
@Injectable({ providedIn: 'root' })
export class StaffAccessApi {
  private readonly http = inject(HttpClient);
  private readonly base = inject(RuntimeConfigService).current.publicApiBasePath;
  read() {
    return this.http
      .get<StaffAccessResponse>(this.base + '/v1/identity/staff-access')
      .pipe(timeout(15000));
  }
}
