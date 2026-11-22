import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface ShopCollection {
  id: number; name: string; slug: string; description?: string | null;
  isActive: boolean; sortOrder: number; productKeys: string[];
}
export interface CollectionUpsert {
  name: string; description?: string | null; isActive: boolean; sortOrder: number; productKeys?: string[] | null;
}

@Injectable({ providedIn: 'root' })
export class CollectionService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/collections`;

  // Storefront
  publicList(): Observable<ShopCollection[]> { return this.http.get<ShopCollection[]>(`${this.apiUrl}/public`); }
  publicOne(slug: string): Observable<ShopCollection> { return this.http.get<ShopCollection>(`${this.apiUrl}/public/${slug}`); }

  // Admin
  getAll(): Observable<ShopCollection[]> { return this.http.get<ShopCollection[]>(this.apiUrl); }
  create(dto: CollectionUpsert): Observable<any> { return this.http.post(this.apiUrl, dto); }
  update(id: number, dto: CollectionUpsert): Observable<any> { return this.http.put(`${this.apiUrl}/${id}`, dto); }
  remove(id: number): Observable<any> { return this.http.delete(`${this.apiUrl}/${id}`); }
}
