import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';
import { TenantService } from '../../services/tenant/tenant.service';

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
  'pricing': 'PAGE_PRICING',
  'app-releases': 'PAGE_APP_RELEASES',
  'dashboard-view': 'PAGE_DASHBOARD_VIEW'
};

// Splits a workspace URL into { slug, subpath } — the "workspace" segment
// sits at a different position depending on whether we're on a real company
// subdomain (bare /workspace/...) or the legacy /:companySlug/workspace/...
// path (IP access / local dev without subdomain DNS). Returns null if the
// URL isn't a workspace URL at all.
function parseWorkspaceUrl(url: string, tenant: TenantService): { slug: string; subpath: string } | null {
  const segments = url.split('?')[0].split('#')[0].split('/').filter(Boolean);
  if (tenant.hasSubdomain()) {
    if (segments[0] !== 'workspace') return null;
    return { slug: tenant.getSubdomainFromHost() || '', subpath: segments.slice(1).join('/') };
  }
  if (segments[1] !== 'workspace') return null;
  return { slug: segments[0], subpath: segments.slice(2).join('/') };
}

export const authGuard: CanActivateFn = (route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const tenant = inject(TenantService);

  if (authService.getToken()) {
    const user = authService.currentUserValue;
    const workspace = parseWorkspaceUrl(state.url, tenant);

    // Cross-tenant protection
    if (user) {
      if (state.url.startsWith('/admin') && user.loginContext !== 'admin') {
        router.navigate(['/admin/login']);
        return false;
      }

      if (workspace && user.loginContext !== workspace.slug) {
        // You are logged into 'fashion' but trying to access 'zaira'
        router.navigate(tenant.routeSegments(workspace.slug, 'login'));
        return false;
      }
    }

    // 1. Get the subpath to determine Right ID
    let subpath = '';
    if (state.url.startsWith('/admin')) {
      subpath = state.url.substring('/admin'.length).replace(/^\//, '').split('?')[0].split('#')[0];
    } else if (workspace) {
      subpath = workspace.subpath;
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
      } else if (workspace) {
        router.navigate(tenant.routeSegments(workspace.slug, 'workspace', 'dashboard'));
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
    const workspace = parseWorkspaceUrl(state.url, tenant);
    if (workspace) {
      router.navigate(tenant.routeSegments(workspace.slug, 'login'), { queryParams: { returnUrl: state.url } });
    } else {
      router.navigate(['/admin/login']); // Fallback
    }
  }
  return false;
};
