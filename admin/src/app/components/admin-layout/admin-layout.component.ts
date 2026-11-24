import { Component, OnInit, inject } from '@angular/core';

import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';
import { ConfirmDialogComponent } from '../shared/confirm-dialog/confirm-dialog.component';
import { CompanySelectorComponent } from '../shared/company-selector/company-selector.component';
import { TenantService } from '../../services/tenant/tenant.service';
import { BillingService, BillingSummary } from '../../services/billing/billing.service';

const NAV_GROUP_STORAGE_KEY = 'zw_nav_expanded_groups';

// Route suffixes (matched against the end of router.url) used to auto-expand
// whichever group contains the page the user landed on.
const GROUP_ROUTES: Record<string, string[]> = {
  catalog: ['/products', '/add-product', '/edit-product', '/categories', '/product-labels', '/barcodes', '/price-tag', '/suppliers', '/inventory',
            '/customers', '/delivery', '/payments', '/fraud', '/refunds', '/pricing'],
  admin: ['/reports', '/users', '/config'],
  ecommerce: ['/ecommerce']
};

@Component({
    selector: 'app-admin-layout',
    imports: [RouterModule, ConfirmDialogComponent, CompanySelectorComponent],
    templateUrl: './admin-layout.component.html',
    styleUrl: './admin-layout.component.css'
})
export class AdminLayoutComponent implements OnInit {
  authService = inject(AuthService);
  router = inject(Router);
  private tenant = inject(TenantService);
  private billingService = inject(BillingService);
  billing: BillingSummary | null = null;
  sidebarCollapsed = false;
  mobileSidebarOpen = false; // off-canvas drawer state, < 1024px only

  private expandedGroups = new Set<string>();

  // Shows the plan banner and hides modules the plan doesn't include. Platform staff have no shop plan.
  get planBanner(): { kind: 'expired' | 'warn'; text: string } | null {
    const b = this.billing;
    if (!b) return null;
    const end = b.expiresAt ? new Date(b.expiresAt).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }) : '';
    if (b.status === 'expired') return { kind: 'expired', text: `Your subscription ended on ${end}. You can look at everything, but changes are paused until you renew.` };
    if (b.status === 'grace') return { kind: 'warn', text: `Your subscription ended on ${end}. Please renew within ${b.graceDays} days or changes will be paused.` };
    if (b.daysLeft != null && b.daysLeft <= 7) return { kind: 'warn', text: `Your subscription ends in ${Math.max(b.daysLeft, 0)} day${b.daysLeft === 1 ? '' : 's'} (${end}).` };
    return null;
  }

  ngOnInit() {
    if (!this.authService.isSuperAdmin()) {
      this.billingService.mine().subscribe({ next: b => this.billing = b, error: () => { /* menu simply stays complete */ } });
    }
    const saved = localStorage.getItem(NAV_GROUP_STORAGE_KEY);
    if (saved) {
      try { this.expandedGroups = new Set(JSON.parse(saved)); } catch { /* ignore bad JSON */ }
    } else {
      // First visit — auto-expand whichever group contains the current route
      // so the user doesn't land on a page hidden inside a collapsed group.
      const url = this.router.url;
      for (const [group, routes] of Object.entries(GROUP_ROUTES)) {
        if (routes.some(r => url.includes(r))) { this.expandedGroups.add(group); break; }
      }
    }
  }

  isGroupExpanded(name: string): boolean { return this.expandedGroups.has(name); }

  toggleGroup(name: string) {
    if (this.expandedGroups.has(name)) this.expandedGroups.delete(name);
    else this.expandedGroups.add(name);
    localStorage.setItem(NAV_GROUP_STORAGE_KEY, JSON.stringify([...this.expandedGroups]));
  }

  get basePath(): string {
    const user = this.authService.currentUserValue;
    if (user?.loginContext === 'admin') {
      return '/admin';
    } else if (user?.loginContext) {
      return this.tenant.hasSubdomain() ? '/workspace' : `/${user.loginContext}/workspace`;
    }
    return '/admin';
  }

  closeMobileSidebar() { this.mobileSidebarOpen = false; }

  // Single menu control for both breakpoints: below 1024px it slides the
  // sidebar in/out as an overlay drawer; at desktop widths it collapses the
  // sidebar to icon-only. One button, one handler — no separate mobile vs
  // desktop navigation logic to keep in sync.
  toggleNav() {
    if (window.innerWidth <= 1024) {
      this.mobileSidebarOpen = !this.mobileSidebarOpen;
      if (this.mobileSidebarOpen) this.sidebarCollapsed = false;
    } else {
      this.sidebarCollapsed = !this.sidebarCollapsed;
    }
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/']);
  }
}
