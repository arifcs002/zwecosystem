import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Company {
  id: string;
  name: string;
  subdomain: string; // Used for multi-tenant URL e.g. /fashion/login
  databaseConnectionString?: string; // Optional: For physical DB isolation
  createdAt: Date;
  themeColor?: string;
  contactEmail?: string;
  companyMobile?: string;
  ownerName?: string;
  ownerMobile?: string;
  division?: string;
  district?: string;
  thana?: string;
  address?: string;
  facebookLink?: string;
  instagramLink?: string;
  bkashNumber?: string;
  nagadNumber?: string;
  bankName?: string;
  bankAccountName?: string;
  isActive: boolean;
  approvalStatus?: string;
  logoUrl?: string;
}

@Injectable({
  providedIn: 'root'
})
export class CompanyService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/companies`;

  getCompanies(): Observable<Company[]> {
    return this.http.get<Company[]>(this.apiUrl);
  }

  getCompanyById(id: string): Observable<Company> {
    return this.http.get<Company>(`${this.apiUrl}/${id}`);
  }

  addCompany(company: Company): Observable<Company> {
    return this.http.post<Company>(this.apiUrl, company);
  }

  updateCompany(updatedCompany: Company): Observable<Company> {
    return this.http.put<Company>(`${this.apiUrl}/${updatedCompany.id}`, updatedCompany);
  }

  deleteCompany(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
}
