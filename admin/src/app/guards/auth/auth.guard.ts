import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';

const ROUTE_RIGHTS_MAP: { [key: string]: string } = {
  'dashboard': 'PAGE_SHOP_DASHBOARD',
  'companies': 'PAGE_COMPANY_MANAGEMENT',
  'company/create': 'PAGE_COMPANY_MANAGEMENT',
  'company/edit': 'PAGE_COMPANY_MANAGEMENT',
  'theme': 'PAGE_ROLE_MANAGEMENT',
  'users': 'PAGE_USER_MANAGEMENT',
  'roles': 'PAGE_ROLE_MANAGEMENT',
  'products': 'PAGE_PRODUCTS',
  'add-product': 'PAGE_PRODUCTS',
  'categories': 'PAGE_CATEGORIES',
  'barcodes': 'PAGE_PRODUCTS',
  'price-tag': 'PAGE_PRODUCTS',
  'suppliers': 'PAGE_PRODUCTS',
  'pos': 'PAGE_POS',
  'orders': 'PAGE_POS',
  'delivery': 'PAGE_POS',
  'payments': 'PAGE_POS',
  'reports': 'PAGE_REPORTS',
  'config': 'PAGE_STORE_CONFIG',
  'dashboard-view': 'PAGE_DASHBOARD_VIEW'
};

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.getToken()) {
    const user = authService.currentUserValue;
    
    // Cross-tenant protection
    if (user) {
      if (state.url.startsWith('/admin') && user.loginContext !== 'admin') {
        router.navigate(['/admin/login']);
        return false;
      }
      
      const parts = state.url.split('/');
      // e.g. /fashion/workspace/dashboard -> parts = ['', 'fashion', 'workspace', 'dashboard']
      if (parts.length > 2 && parts[2] === 'workspace') {
        const urlSlug = parts[1];
        if (user.loginContext !== urlSlug) {
          // You are logged into 'fashion' but trying to access 'zaira'
          router.navigate(['/', urlSlug, 'login']);
          return false;
        }
      }
    }

    // 1. Get the subpath to determine Right ID
    let subpath = '';
    if (state.url.startsWith('/admin')) {
      subpath = state.url.substring('/admin'.length).replace(/^\//, '').split('?')[0].split('#')[0];
    } else {
      const parts = state.url.split('/');
      if (parts.length > 2 && parts[2] === 'workspace') {
        subpath = parts.slice(3).join('/').split('?')[0].split('#')[0];
      }
    }

    // Handle root path defaults
    if (!subpath || subpath === 'workspace') {
      subpath = 'dashboard';
    }

    // 2. Identify required right ID
    let requiredRight = '';
    if (state.url.startsWith('/admin') && subpath === 'dashboard') {
      requiredRight = 'PAGE_SUPER_DASHBOARD';
    } else {
      requiredRight = ROUTE_RIGHTS_MAP[subpath];
      if (!requiredRight) {
        // Try fuzzy prefix match (e.g. company/edit/123 -> company/edit)
        const matchedKey = Object.keys(ROUTE_RIGHTS_MAP).find(key => subpath.startsWith(key));
        if (matchedKey) requiredRight = ROUTE_RIGHTS_MAP[matchedKey];
      }
    }

    // 3. Verify user has the right
    if (requiredRight && !authService.hasRight(requiredRight)) {
      console.warn(`[AUTH GUARD] Access denied to ${state.url} (Missing Right: ${requiredRight})`);
      if (state.url.startsWith('/admin')) {
        router.navigate(['/admin/dashboard']);
      } else {
        const parts = state.url.split('/');
        router.navigate(['/', parts[1], 'workspace', 'dashboard']);
      }
      return false;
    }

    // Check if route requires specific role (Legacy role data support)
    const requiredRole = route.data?.['role'];
    if (requiredRole === 'SUPER_ADMIN' && !authService.isSuperAdmin()) {
      router.navigate(['/admin/dashboard']);
      return false;
    }

    return true;
  }

  // Not logged in, redirect to login page intelligently
  if (state.url.startsWith('/admin')) {
    router.navigate(['/admin/login'], { queryParams: { returnUrl: state.url } });
  } else {
    const parts = state.url.split('/');
    if (parts.length > 2 && parts[2] === 'workspace') {
      router.navigate(['/', parts[1], 'login'], { queryParams: { returnUrl: state.url } });
    } else {
      router.navigate(['/admin/login']); // Fallback
    }
  }
  return false;
};
