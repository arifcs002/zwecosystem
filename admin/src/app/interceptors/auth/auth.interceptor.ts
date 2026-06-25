import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { AuthService } from '../../services/auth/auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken();
  const tenantId = localStorage.getItem('tenant_company_id');

  let headers = req.headers;

  if (token) {
    headers = headers.set('Authorization', `Bearer ${token}`);
  }
  if (tenantId) {
    headers = headers.set('X-Tenant-ID', tenantId);
  }

  const clonedReq = req.clone({ headers });
  
  return next(clonedReq).pipe(
    tap({
      error: (error) => {
        if (error.status === 401) {
          authService.logout();
          const currentPath = window.location.pathname;
          if (currentPath.startsWith('/admin')) {
            window.location.href = '/admin/login';
          } else {
            const parts = currentPath.split('/');
            if (parts.length > 1 && parts[1] && parts[1] !== 'company') {
              window.location.href = `/${parts[1]}/login`;
            } else {
              window.location.href = '/admin/login';
            }
          }
        }
      }
    })
  );
};
