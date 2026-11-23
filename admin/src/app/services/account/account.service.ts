import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, tap } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface ShopperProfile {
  id: number; name: string; phone: string; email?: string | null;
  address?: string | null; district?: string | null; thana?: string | null;
  group: { name: string; discountPercent: number } | null;
}
export interface ShopperOrder {
  orderNumber: string; status: string; total: number; discount: number; discountLabel?: string | null;
  date: string; trackingNumber?: string | null; courierName?: string | null;
}
interface SessionResult { token: string; account: ShopperProfile; }

// The storefront shopper's login for the current shop. The token is kept per shop in localStorage and
// attached to /account/* and the public checkout call by the HTTP interceptor — never to staff APIs.
@Injectable({ providedIn: 'root' })
export class AccountService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/account`;

  profile: ShopperProfile | null = null;
  private tenantId = '';
  ready = false;

  get loggedIn() { return !!this.profile; }

  /** Call once a storefront page knows its store: restores the saved session for THIS shop (if any). */
  use(tenantId: string | number) {
    const id = String(tenantId);
    if (id === this.tenantId) return;
    this.tenantId = id; this.profile = null; this.ready = false;
    if (!this.readToken()) { this.ready = true; return; }
    this.http.get<ShopperProfile>(`${this.apiUrl}/me`).pipe(catchError(() => { this.clearToken(); return of(null); }))
      .subscribe(p => { this.profile = p; this.ready = true; });
  }

  register(dto: { name: string; phone: string; password: string; email?: string }): Observable<SessionResult> {
    return this.http.post<SessionResult>(`${this.apiUrl}/register`, dto).pipe(tap(r => this.start(r)));
  }
  login(phone: string, password: string): Observable<SessionResult> {
    return this.http.post<SessionResult>(`${this.apiUrl}/login`, { phone, password }).pipe(tap(r => this.start(r)));
  }
  logout(): Observable<any> {
    const done = () => { this.clearToken(); this.profile = null; };
    return this.http.post(`${this.apiUrl}/logout`, {}).pipe(tap({ next: done, error: done }));
  }
  update(dto: { name: string; email?: string; address?: string; district?: string; thana?: string }): Observable<ShopperProfile> {
    return this.http.put<ShopperProfile>(`${this.apiUrl}/me`, dto).pipe(tap(p => this.profile = p));
  }
  changePassword(currentPassword: string, newPassword: string): Observable<any> {
    return this.http.put(`${this.apiUrl}/password`, { currentPassword, newPassword });
  }
  sendOtp(phone: string, purpose: 'reset' | 'verify'): Observable<any> {
    return this.http.post(`${this.apiUrl}/otp/send`, { phone, purpose });
  }
  resetPassword(phone: string, code: string, newPassword: string): Observable<any> {
    return this.http.post(`${this.apiUrl}/password/reset`, { phone, code, newPassword });
  }
  orders(): Observable<ShopperOrder[]> { return this.http.get<ShopperOrder[]>(`${this.apiUrl}/orders`); }

  private start(r: SessionResult) {
    try { localStorage.setItem('shop_cust_token:' + this.tenantId, r.token); } catch { /* private mode */ }
    this.profile = r.account;
  }
  private readToken(): string | null { try { return localStorage.getItem('shop_cust_token:' + this.tenantId); } catch { return null; } }
  private clearToken() { try { localStorage.removeItem('shop_cust_token:' + this.tenantId); } catch { /* ignore */ } }
}
