import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, timeout } from 'rxjs';
import { RuntimeConfigService } from '../runtime-config/runtime-config';
export interface StaffRoleChoice {
  id: string;
  name: string | null;
  permissions: string[];
}
export interface StaffAccountChoice {
  userId: string;
  assignedRoleIds: string[];
}
@Injectable({ providedIn: 'root' })
export class StaffManagementApi {
  private readonly http = inject(HttpClient);
  private readonly base =
    inject(RuntimeConfigService).current.publicApiBasePath + '/v1/identity/staff';
  roles(branch: string) {
    return this.http
      .get<StaffRoleChoice[]>(this.base + '/branches/' + encodeURIComponent(branch) + '/roles')
      .pipe(timeout(15000));
  }
  private async headers() {
    const token = await firstValueFrom(
      this.http.get<{ requestToken: string }>('/bff/antiforgery').pipe(timeout(15000)),
    );
    if (!token.requestToken) throw new Error('Request verification unavailable');
    return { 'X-CSRF-TOKEN': token.requestToken };
  }
  async resolve(branch: string, email: string) {
    return firstValueFrom(
      this.http
        .post<StaffAccountChoice>(
          this.base + '/branches/' + encodeURIComponent(branch) + '/resolve-account',
          { email },
          { headers: await this.headers() },
        )
        .pipe(timeout(15000)),
    );
  }
  async change(branch: string, target: string, role: string, grant: boolean) {
    const url =
      this.base +
      '/' +
      encodeURIComponent(target) +
      '/branches/' +
      encodeURIComponent(branch) +
      '/roles/' +
      encodeURIComponent(role);
    const options = { headers: await this.headers() };
    await firstValueFrom(
      (grant ? this.http.put<void>(url, null, options) : this.http.delete<void>(url, options)).pipe(
        timeout(15000),
      ),
    );
  }
}
