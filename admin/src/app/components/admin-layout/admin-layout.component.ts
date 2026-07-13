import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';
import { ConfirmDialogComponent } from '../shared/confirm-dialog/confirm-dialog.component';
import { CompanySelectorComponent } from '../shared/company-selector/company-selector.component';

const NAV_GROUP_STORAGE_KEY = 'zw_nav_expanded_groups';

// Route suffixes (matched against the end of router.url) used to auto-expand
// whichever group contains the page the user landed on.
const GROUP_ROUTES: Record<string, string[]> = {
  catalog: ['/products', '/add-product', '/edit-product', '/categories', '/barcodes', '/price-tag', '/suppliers', '/inventory'],
  sales: ['/customers', '/delivery', '/payments', '/fraud', '/refunds', '/pricing'],
  admin: ['/reports', '/users', '/config'],
  ecommerce: ['/ecommerce']
};

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, ConfirmDialogComponent, CompanySelectorComponent],
  templateUrl: './admin-layout.component.html',
  styleUrl: './admin-layout.component.css'
})
export class AdminLayoutComponent implements OnInit {
  authService = inject(AuthService);
  router = inject(Router);
  sidebarCollapsed = false;
  mobileSidebarOpen = false; // off-canvas drawer state, < 1024px only

  private expandedGroups = new Set<string>();

  ngOnInit() {
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
      return `/${user.loginContext}/workspace`;
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
    const user = this.authService.currentUserValue;
    const loginPath = user?.loginContext === 'admin' ? '/admin/login' : `/${user?.loginContext}/login`;
    this.authService.logout();
    this.router.navigate([loginPath]);
  }
}
