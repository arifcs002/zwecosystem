import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface SizeQtyDto {
  size: string;
  quantity: number;
}

export interface BatchProductCreateDto {
  name: string;
  price: number;
  wholesalePrice: number;
  description?: string;
  categoryId?: number;
  supplierId?: number;
  imageUrl?: string;
  sizes: SizeQtyDto[];
  pricingTagId?: number | null;
  compareAtPrice?: number | null;
}

export interface BulkProductLineDto {
  name: string;
  price: number;
  wholesalePrice: number;
  description?: string;
  imageUrl?: string;
  pricingTagId?: number | null;
  compareAtPrice?: number | null;
  sizes: SizeQtyDto[];
}
export interface BulkProductsCreateDto {
  categoryId?: number;
  supplierId?: number;
  products: BulkProductLineDto[];
}

export interface Product {
  id: number;
  name: string;
  slug: string;
  sku: string;
  barcode: string;
  description?: string;
  price: number;
  compareAtPrice?: number | null;
  wholesalePrice: number;
  stockQuantity: number;
  size?: string;
  imageUrl?: string;
  categoryId?: number;
  brandId?: number;
  supplierId?: number;
  status: string;
  createdAt: string;
  createdDate?: string; // public endpoint returns this (used for "newest" sort / New badge)
  category?: any;
  brand?: any;
  supplier?: any;
  pricingTagId?: number | null;
}

@Injectable({
  providedIn: 'root'
})
export class ProductService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/products`;

  getProducts(search?: string): Observable<Product[]> {
    const params: any = {};
    if (search) params.search = search;
    return this.http.get<Product[]>(this.apiUrl, { params });
  }

  // Anonymous-safe storefront listing — excludes wholesale/cost price and other
  // admin-only fields. Used by the public shop.
  getPublicProducts(): Observable<Product[]> {
    return this.http.get<Product[]>(`${this.apiUrl}/public`);
  }

  getProduct(id: number): Observable<Product> {
    return this.http.get<Product>(`${this.apiUrl}/${id}`);
  }

  createProductsBatch(dto: BatchProductCreateDto): Observable<Product[]> {
    return this.http.post<Product[]>(`${this.apiUrl}/batch`, dto);
  }

  // Bulk add — one supplier + category, many products, one transactional submit.
  createBulkProducts(dto: BulkProductsCreateDto): Observable<{ count: number; products: Product[] }> {
    return this.http.post<{ count: number; products: Product[] }>(`${this.apiUrl}/bulk`, dto);
  }

  updateProduct(id: number, product: any): Observable<Product> {
    return this.http.put<Product>(`${this.apiUrl}/${id}`, product);
  }

  deleteProduct(id: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
  
  adjustStock(id: number, delta: number): Observable<{ id: number; stockQuantity: number }> {
    return this.http.patch<{ id: number; stockQuantity: number }>(`${this.apiUrl}/${id}/stock`, { delta });
  }

  uploadImage(file: File, folder: 'product' | 'logo' | 'other' = 'product'): Observable<{ imageUrl: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ imageUrl: string }>(`${environment.apiUrl}/upload?folder=${folder}`, formData);
  }
}
