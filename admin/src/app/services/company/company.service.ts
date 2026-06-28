import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Company {
  id: number;
  name: string;
  subdomain: string;
  createdAt?: Date;
  contactEmail?: string;
  contactPhone?: string;
  companyMobile?: string;
  ownerName?: string;
  ownerMobile?: string;
  division?: string;
  district?: string;
  thana?: string;
  unionName?: string;
  address?: string;
  facebookLink?: string;
  instagramLink?: string;
  bkashNumber?: string;
  nagadNumber?: string;
  bankName?: string;
  bankAccountName?: string;
  deliveryCharge?: number;
  isActive: boolean;
  approvalStatus?: string;
  logoUrl?: string;
  bannerUrl?: string;
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

  getCompanyById(id: number): Observable<Company> {
    return this.http.get<Company>(`${this.apiUrl}/${id}`);
  }

  addCompany(company: Company): Observable<Company> {
    return this.http.post<Company>(this.apiUrl, company);
  }

  updateCompany(updatedCompany: Company): Observable<Company> {
    return this.http.put<Company>(`${this.apiUrl}/${updatedCompany.id}`, updatedCompany);
  }

  toggleStatus(id: number): Observable<{ isActive: boolean; message: string }> {
    return this.http.patch<{ isActive: boolean; message: string }>(`${this.apiUrl}/${id}/toggle-status`, {});
  }

  deleteCompany(id: number): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.apiUrl}/${id}`);
  }
}
