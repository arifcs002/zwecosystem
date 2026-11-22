import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface CustomerGroup { id: number; name: string; description?: string | null; discountPercent: number; members: number; }
export interface CustomerAccountRow {
  id: number; name: string; phone: string; email?: string | null; isActive: boolean;
  groupId?: number | null; groupName?: string | null; orders: number; createdDate: string; lastLogin?: string | null;
}
export interface ActivityRow { id: number; action: string; details?: string | null; user?: string | null; ipAddress?: string | null; date: string; }
export interface StaffSession {
  id: number; email: string; fullName: string; userType?: string | null; ipAddress?: string | null;
  userAgent?: string | null; createdAt: string; expiresAt: string; current: boolean;
}

@Injectable({ providedIn: 'root' })
export class CustomerAdminService {
  private http = inject(HttpClient);
  private groups = `${environment.apiUrl}/customer-groups`;
  private activity = `${environment.apiUrl}/activity`;

  getGroups(): Observable<CustomerGroup[]> { return this.http.get<CustomerGroup[]>(this.groups); }
  saveGroup(id: number | null, dto: { name: string; description?: string | null; discountPercent: number }): Observable<any> {
    return id ? this.http.put(`${this.groups}/${id}`, dto) : this.http.post(this.groups, dto);
  }
  deleteGroup(id: number): Observable<any> { return this.http.delete(`${this.groups}/${id}`); }

  getAccounts(search = ''): Observable<CustomerAccountRow[]> {
    return this.http.get<CustomerAccountRow[]>(`${this.groups}/accounts`, { params: search ? { search } : {} });
  }
  assignGroup(accountId: number, groupId: number | null): Observable<any> {
    return this.http.put(`${this.groups}/accounts/${accountId}/group`, { groupId });
  }
  setActive(accountId: number, active: boolean): Observable<any> {
    return this.http.put(`${this.groups}/accounts/${accountId}/active`, active);
  }

  getLog(filter: { action?: string; from?: string; to?: string } = {}): Observable<ActivityRow[]> {
    const params: Record<string, string> = {};
    if (filter.action) params['action'] = filter.action;
    if (filter.from) params['from'] = filter.from;
    if (filter.to) params['to'] = filter.to;
    return this.http.get<ActivityRow[]>(`${this.activity}/log`, { params });
  }
  getSessions(): Observable<StaffSession[]> { return this.http.get<StaffSession[]>(`${this.activity}/sessions`); }
  revokeSession(id: number): Observable<any> { return this.http.delete(`${this.activity}/sessions/${id}`); }
}
