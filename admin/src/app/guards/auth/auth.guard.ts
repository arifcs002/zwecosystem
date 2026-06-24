import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';

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

    // Check if route requires specific role
    const requiredRole = route.data?.['role'];
    
    if (requiredRole === 'SUPER_ADMIN' && !authService.isSuperAdmin()) {
      router.navigate(['/admin/dashboard']); // or some unauthorized page
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
