import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../auth/auth.service';

export interface Company {
  id: string;
  name: string;
  subdomain: string;
  logoUrl?: string;
  isActive: boolean;
  approvalStatus: string;
  createdAt: string;
}

export interface DashboardStats {
  totalRevenue: number;
  totalRevenueGrowth: number;
  totalOrders: number;
  totalOrdersGrowth: number;
  totalProducts: number;
  lowStockProducts: number;
  outOfStockProducts: number;
  totalCustomers: number;
  pendingOrders: number;
  processingOrders: number;
  completedOrders: number;
  cancelledOrders: number;
  posOrders: number;
  ecomOrders: number;
}

export interface SalesChartPoint {
  date: string;
  label: string;
  revenue: number;
  orders: number;
}

export interface TopProduct {
  productId: string;
  productName: string;
  imageUrl?: string;
  price: number;
  totalSold: number;
  totalRevenue: number;
}

export interface RecentOrder {
  id: string;
  orderNumber: string;
  customerName?: string;
  customerPhone?: string;
  total: number;
  status: string;
  paymentMethod: string;
  paymentStatus: string;
  saleType: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private http = inject(HttpClient);
  private authService = inject(AuthService);
  private apiUrl = `${environment.apiUrl}/dashboard`;

  // Signal to hold the currently selected company for Super Admin
  selectedCompanyId = signal<string | null>(null);

  /** Return current company id to pass to API calls */
  getActiveCompanyId(): string | null {
    if (this.authService.isSuperAdmin()) {
      return this.selectedCompanyId();
    }
    return this.authService.currentUserValue?.companyId ?? null;
  }

  setSelectedCompany(companyId: string | null) {
    this.selectedCompanyId.set(companyId);
  }

  getStats(companyId?: string): Observable<DashboardStats> {
    const cid = companyId ?? this.getActiveCompanyId();
    const params: any = {};
    if (cid) params.companyId = cid;
    return this.http.get<DashboardStats>(`${this.apiUrl}/stats`, { params });
  }

  getSalesChart(companyId?: string, days = 7): Observable<SalesChartPoint[]> {
    const cid = companyId ?? this.getActiveCompanyId();
    const params: any = { days };
    if (cid) params.companyId = cid;
    return this.http.get<SalesChartPoint[]>(`${this.apiUrl}/sales-chart`, { params });
  }

  getTopProducts(companyId?: string, limit = 5): Observable<TopProduct[]> {
    const cid = companyId ?? this.getActiveCompanyId();
    const params: any = { limit };
    if (cid) params.companyId = cid;
    return this.http.get<TopProduct[]>(`${this.apiUrl}/top-products`, { params });
  }

  getRecentOrders(companyId?: string, limit = 10): Observable<RecentOrder[]> {
    const cid = companyId ?? this.getActiveCompanyId();
    const params: any = { limit };
    if (cid) params.companyId = cid;
    return this.http.get<RecentOrder[]>(`${this.apiUrl}/recent-orders`, { params });
  }
}
