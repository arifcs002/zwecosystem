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
  categoryId?: string;
  supplierId?: string;
  imageUrl?: string;
  sizes: SizeQtyDto[];
}

export interface Product {
  id: string;
  name: string;
  slug: string;
  sku: string;
  barcode: string;
  description?: string;
  price: number;
  wholesalePrice: number;
  stockQuantity: number;
  size?: string;
  imageUrl?: string;
  categoryId?: string;
  brandId?: string;
  supplierId?: string;
  status: string;
  createdAt: string;
  category?: any;
  brand?: any;
  supplier?: any;
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

  getProduct(id: string): Observable<Product> {
    return this.http.get<Product>(`${this.apiUrl}/${id}`);
  }

  createProductsBatch(dto: BatchProductCreateDto): Observable<Product[]> {
    return this.http.post<Product[]>(`${this.apiUrl}/batch`, dto);
  }

  updateProduct(id: string, product: any): Observable<Product> {
    return this.http.put<Product>(`${this.apiUrl}/${id}`, product);
  }

  deleteProduct(id: string): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}`);
  }
  
  uploadImage(file: File): Observable<{ imageUrl: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ imageUrl: string }>(`${environment.apiUrl}/upload`, formData);
  }
}
