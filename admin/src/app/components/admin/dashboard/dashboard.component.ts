import { Component, OnInit, OnDestroy, inject, effect } from '@angular/core';
import { CommonModule, CurrencyPipe, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../services/auth/auth.service';
import { DashboardService, DashboardStats, SalesChartPoint, TopProduct, RecentOrder } from '../../../services/dashboard/dashboard.service';
import { CompanyContextService, CompanyOption } from '../../../services/company-context/company-context.service';
import { Subject, takeUntil, switchMap, forkJoin } from 'rxjs';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, DecimalPipe, DatePipe],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.css'
})
export class DashboardComponent implements OnInit, OnDestroy {
  authService = inject(AuthService);
  dashboardService = inject(DashboardService);
  companyContext = inject(CompanyContextService);

  private destroy$ = new Subject<void>();

  stats: DashboardStats | null = null;
  chartData: SalesChartPoint[] = [];
  topProducts: TopProduct[] = [];
  recentOrders: RecentOrder[] = [];

  isLoading = true;
  statsError = false;

  chartDays = 7;
  maxChartRevenue = 1;

  ngOnInit() {
    if (this.authService.isSuperAdmin() && !this.companyContext.isLoaded()) {
      this.companyContext.loadCompanies().pipe(takeUntil(this.destroy$)).subscribe({
        next: () => this.loadDashboard(),
        error: () => { this.isLoading = false; this.statsError = true; }
      });
    } else {
      this.loadDashboard();
    }
  }

  loadDashboard() {
    this.isLoading = true;
    this.statsError = false;

    const companyId = this.authService.isSuperAdmin()
      ? this.companyContext.getSelectedId() ?? undefined
      : undefined;

    if (this.authService.isSuperAdmin() && !companyId) {
      this.isLoading = false;
      return;
    }

    forkJoin({
      stats: this.dashboardService.getStats(companyId),
      chart: this.dashboardService.getSalesChart(companyId, this.chartDays),
      topProducts: this.dashboardService.getTopProducts(companyId, 5),
      recentOrders: this.dashboardService.getRecentOrders(companyId, 10),
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.stats = res.stats;
        this.chartData = res.chart;
        this.topProducts = res.topProducts;
        this.recentOrders = res.recentOrders;
        this.maxChartRevenue = Math.max(...res.chart.map(d => d.revenue), 1);
        this.isLoading = false;
      },
      error: () => {
        this.statsError = true;
        this.isLoading = false;
      }
    });
  }

  onCompanyIdChange(companyId: string) {
    const company = this.companyContext.allCompanies().find(c => c.id === companyId) ?? null;
    this.onCompanyChange(company);
  }

  onCompanyChange(company: CompanyOption | null) {
    this.companyContext.selectCompany(company);
    this.loadDashboard();
  }

  getChartBarHeight(revenue: number): number {
    return this.maxChartRevenue > 0 ? Math.round((revenue / this.maxChartRevenue) * 180) : 0;
  }

  getStatusClass(status: string): string {
    const map: Record<string, string> = {
      'PENDING': 'status-pending',
      'PROCESSING': 'status-processing',
      'COMPLETED': 'status-completed',
      'CANCELLED': 'status-cancelled',
      'PACKED': 'status-packed',
      'SHIPPED': 'status-shipped',
    };
    return map[status] || 'status-pending';
  }

  getGrowthClass(val: number): string {
    return val >= 0 ? 'growth-up' : 'growth-down';
  }

  getGrowthIcon(val: number): string {
    return val >= 0 ? '↑' : '↓';
  }

  get isSuperAdmin() { return this.authService.isSuperAdmin(); }
  get companies() { return this.companyContext.allCompanies(); }
  get selectedCompany() { return this.companyContext.selectedCompany(); }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
