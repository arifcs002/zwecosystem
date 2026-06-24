import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface User {
  id: string;
  email: string;
  fullName: string;
  companyId: string | null;
  loginContext: string; // The subdomain where they logged in (or 'admin')
  roles: string[];
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  email: string;
  fullName: string;
  companyId: string | null;
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

  login(email: string, password: string, loginContext: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(`${this.apiUrl}/login`, { email, password, loginContext })
      .pipe(tap(response => {
        if (response && response.token) {
          // If the backend returned a successful login but it doesn't match the subdomain context, 
          // we can reject it here. We'll trust the backend to validate if this user belongs to this companySlug.
          
          localStorage.setItem('token', response.token);
          
          const user: User = {
            id: '', 
            email: response.email,
            fullName: response.fullName,
            companyId: response.companyId,
            loginContext: loginContext,
            roles: response.roles
          };
          
          localStorage.setItem('user', JSON.stringify(user));
          this.currentUserSubject.next(user);
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
    return this.currentUserValue?.roles.includes('SUPER_ADMIN') ?? false;
  }

  isCompanyAdmin(): boolean {
    return this.currentUserValue?.roles.includes('COMPANY_ADMIN') ?? false;
  }

  getToken(): string | null {
    return localStorage.getItem('token');
  }
}
