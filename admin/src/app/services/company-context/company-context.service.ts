import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../../environments/environment';

export interface CompanyOption {
  id: number;
  name: string;
  subdomain: string;
  logoUrl?: string;
  isActive: boolean;
  approvalStatus: string;
}

@Injectable({ providedIn: 'root' })
export class CompanyContextService {
  private http = inject(HttpClient);

  /** Currently selected company for Super Admin mode */
  selectedCompany = signal<CompanyOption | null>(null);
  allCompanies = signal<CompanyOption[]>([]);
  isLoaded = signal(false);

  loadCompanies(): Observable<CompanyOption[]> {
    return this.http.get<CompanyOption[]>(`${environment.apiUrl}/companies`).pipe(
      tap(companies => {
        this.allCompanies.set(companies);
        // Auto-select first active company if none selected
        if (!this.selectedCompany() && companies.length > 0) {
          const first = companies.find(c => c.isActive) || companies[0];
          this.selectedCompany.set(first);
        }
        this.isLoaded.set(true);
      })
    );
  }

  selectCompany(company: CompanyOption | null) {
    this.selectedCompany.set(company);
  }

  getSelectedId(): number | null {
    return this.selectedCompany()?.id ?? null;
  }
}
