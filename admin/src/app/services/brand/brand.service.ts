import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface Brand {
  id: number;
  name: string;
  logoUrl?: string | null;
  description?: string;
}

@Injectable({ providedIn: 'root' })
export class BrandService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/brands`;

  getBrands(): Observable<Brand[]> {
    return this.http.get<Brand[]>(this.apiUrl);
  }

  // Anonymous-safe list for the storefront brand carousel.
  getPublicBrands(): Observable<Brand[]> {
    return this.http.get<Brand[]>(`${this.apiUrl}/public`);
  }
}
