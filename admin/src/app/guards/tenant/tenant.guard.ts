import { inject } from '@angular/core';
import { CanMatchFn } from '@angular/router';
import { TenantService } from '../../services/tenant/tenant.service';

// canMatch (not canActivate) — on failure the router falls through to try
// the NEXT route definition for the same path instead of blocking
// navigation. This lets the bare tenant routes (e.g. '', 'login',
// 'workspace/...') sit above the platform's own routes for the same paths
// and only take over when the app is actually being served from a company
// subdomain (zairasworld.auleco.com).
export const tenantHostGuard: CanMatchFn = () => {
  return inject(TenantService).hasSubdomain();
};
