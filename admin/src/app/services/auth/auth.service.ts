import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface User {
  id: number;
  email: string;
  fullName: string;
  companyId: number | null;
  loginContext: string; // The subdomain where they logged in (or 'admin')
  roles: string[];
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  email: string;
  fullName: string;
  companyId: number | null;
  roles: string[];
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = environment.apiUrl + '/auth';
  
  private currentUserSubject = new BehaviorSubject<User | null>(null);
  public currentUser$ = this.currentUserSubject.asObservable();

  constructor(private http: HttpClient) {
    this.loadUserFromStorage();
  }

  public get currentUserValue(): User | null {
    return this.currentUserSubject.value;
  }

  login(email: string, password: string, loginContext: string): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/login`, { email, password, loginContext })
      .pipe(tap(response => {
        if (response) {
          const token = response.token || response.Token;
          if (token) {
            localStorage.setItem('token', token);
            
            const user: User = {
              id: response.id || response.Id || 0, 
              email: response.email || response.Email,
              fullName: response.fullName || response.FullName,
              companyId: response.companyId !== undefined ? response.companyId : response.CompanyId,
              loginContext: loginContext,
              roles: response.roles || response.Roles || []
            };
            
            localStorage.setItem('user', JSON.stringify(user));
            this.currentUserSubject.next(user);
          }
        }
      }));
  }

  logout() {
    localStorage.removeItem('token');
    localStorage.removeItem('user');
    this.currentUserSubject.next(null);
  }

  private loadUserFromStorage() {
    const userStr = localStorage.getItem('user');
    if (userStr) {
      this.currentUserSubject.next(JSON.parse(userStr));
    }
  }

  isSuperAdmin(): boolean {
    const roles = this.currentUserValue?.roles || [];
    return roles.some(r => r.toLowerCase() === 'superadmin' || r.toLowerCase() === 'super_admin');
  }

  isCompanyAdmin(): boolean {
    const roles = this.currentUserValue?.roles || [];
    return roles.some(r => r.toLowerCase() === 'companyadmin' || r.toLowerCase() === 'company_admin');
  }

  getUserRights(): string[] {
    const roles = this.currentUserValue?.roles || [];
    const rights: string[] = [];
    roles.forEach(role => {
      const roleRights = ROLE_RIGHTS[role.toUpperCase()] || ROLE_RIGHTS[role.toLowerCase()] || [];
      roleRights.forEach(r => {
        if (!rights.includes(r)) rights.push(r);
      });
    });
    return rights;
  }

  hasRight(right: string): boolean {
    return this.getUserRights().includes(right);
  }

  getToken(): string | null {
    return localStorage.getItem('token');
  }
}

const ROLE_RIGHTS: { [role: string]: string[] } = {
  'SUPER_ADMIN': [
    'PAGE_SUPER_DASHBOARD',
    'PAGE_COMPANY_MANAGEMENT',
    'PAGE_USER_MANAGEMENT',
    'PAGE_ROLE_MANAGEMENT',
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_STORE_CONFIG',
    'PAGE_DASHBOARD_VIEW'
  ],
  'superadmin': [
    'PAGE_SUPER_DASHBOARD',
    'PAGE_COMPANY_MANAGEMENT',
    'PAGE_USER_MANAGEMENT',
    'PAGE_ROLE_MANAGEMENT',
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_STORE_CONFIG',
    'PAGE_DASHBOARD_VIEW'
  ],
  'COMPANY_ADMIN': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_STORE_CONFIG',
    'PAGE_USER_MANAGEMENT',
    'PAGE_DASHBOARD_VIEW'
  ],
  'companyadmin': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_STORE_CONFIG',
    'PAGE_USER_MANAGEMENT',
    'PAGE_DASHBOARD_VIEW'
  ],
  'COMPANY_MANAGER': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_DASHBOARD_VIEW'
  ],
  'companymanager': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_DASHBOARD_VIEW'
  ],
  'MANAGER': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_PRODUCTS',
    'PAGE_CATEGORIES',
    'PAGE_REPORTS',
    'PAGE_DASHBOARD_VIEW'
  ],
  'SALES_STAFF': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_DASHBOARD_VIEW'
  ],
  'salesstaff': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_DASHBOARD_VIEW'
  ],
  'SALES': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_DASHBOARD_VIEW'
  ],
  'CASHIER': [
    'PAGE_SHOP_DASHBOARD',
    'PAGE_POS',
    'PAGE_DASHBOARD_VIEW'
  ]
};
