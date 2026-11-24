import { Component, OnInit, OnDestroy, inject, effect } from '@angular/core';
import { CommonModule, CurrencyPipe, DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../services/auth/auth.service';
import { DashboardService, DashboardStats, SalesChartPoint, TopProduct, RecentOrder } from '../../../services/dashboard/dashboard.service';
import { CompanyContextService, CompanyOption } from '../../../services/company-context/company-context.service';
import { Subject, takeUntil, switchMap, forkJoin, of, catchError } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { SettingsService } from '../../../services/settings/settings.service';

@Component({
    selector: 'app-dashboard',
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
  money: any = null;
  alerts: { level: string; text: string; link: string }[] = [];
  private http = inject(HttpClient);
  private settingsApi = inject(SettingsService);
  sumEnabled = false; sumPhone = ''; sumMsg = '';
  get isOwner() { return this.authService.hasRight('PAGE_STORE_CONFIG'); }
  alertName(link: string) { return ({ inventory: 'Inventory', reorder: 'What to Reorder', expiry: 'Expiry Dates', ledger: 'Customer Accounts', 'cash-closing': 'Cash Closing', shifts: 'My Shift' } as Record<string, string>)[link] || ''; }
  loadSummarySettings() {
    this.settingsApi.getSettings().subscribe({ next: s => { this.sumEnabled = ['1', 'true'].includes(s.find(x => x.key === 'daily_summary_enabled')?.value || ''); this.sumPhone = s.find(x => x.key === 'daily_summary_phone')?.value || ''; }, error: () => { /* leave defaults */ } });
  }
  saveSummary() {
    this.sumMsg = '';
    this.settingsApi.updateSettings([{ key: 'daily_summary_enabled', value: this.sumEnabled ? '1' : '0', groupName: 'SUMMARY' }, { key: 'daily_summary_phone', value: this.sumPhone.trim(), groupName: 'SUMMARY' }]).subscribe({ next: () => this.sumMsg = 'Saved.', error: () => this.sumMsg = 'Could not save.' });
  }
  testSummary() {
    this.sumMsg = 'Sending…';
    this.http.post<{ sent: boolean; message: string }>(`${environment.apiUrl}/alerts/summary/test`, {}).subscribe({ next: r => this.sumMsg = r.sent ? 'Sent: ' + r.message : 'The SMS gateway did not accept it.', error: e => this.sumMsg = e?.error?.message || 'Could not send.' });
  }
  chartData: SalesChartPoint[] = [];
  topProducts: TopProduct[] = [];
  recentOrders: RecentOrder[] = [];

  isLoading = true;
  statsError = false;

  chartDays = 7;
  maxChartRevenue = 1;

  ngOnInit() {
    if (this.isOwner) this.loadSummarySettings();
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
      money: this.dashboardService.getMoney().pipe(catchError(() => of(null))),
      alerts: this.http.get<{ alerts: any[] }>(`${environment.apiUrl}/alerts`).pipe(catchError(() => of(null))),
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res) => {
        this.stats = res.stats;
        this.money = res.money;
        this.alerts = (res as any).alerts?.alerts || [];
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
    const parsedId = companyId ? parseInt(companyId, 10) : null;
    const company = this.companyContext.allCompanies().find(c => c.id === parsedId) ?? null;
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
