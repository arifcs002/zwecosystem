import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { tap } from 'rxjs';
import { AuthService } from '../../services/auth/auth.service';
import { GlobalNotificationService } from '../../services/global-notification/global-notification.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const notificationService = inject(GlobalNotificationService);
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
          notificationService.notify({
            type: 'warning',
            title: 'Session expired',
            message: 'Your session is no longer valid. Please sign in again.',
            ttlMs: 6500
          });
          return;
        }

        const serverMessage = typeof error.error === 'string'
          ? error.error
          : error.error?.message || error.message || 'Something went wrong.';

        const titleByStatus: Record<number, string> = {
          400: 'Check the form',
          403: 'Access denied',
          404: 'Not found',
          409: 'Conflict detected',
          500: 'Server error',
          0: 'Connection problem'
        };

        const messageByStatus: Record<number, string> = {
          400: 'Please review the highlighted fields and try again.',
          403: 'You do not have permission to do that.',
          404: 'The requested resource could not be found.',
          409: 'The data already exists or conflicts with current records.',
          500: 'The backend hit an unexpected error.',
          0: 'The server is unavailable or the network is offline.'
        };

        const status = error.status ?? 0;
        notificationService.notify({
          type: status >= 500 || status === 0 ? 'error' : status === 409 ? 'warning' : 'info',
          title: titleByStatus[status] || 'Request failed',
          message: messageByStatus[status] || 'We could not complete the request.',
          detail: serverMessage,
          ttlMs: 7000
        });
      }
    })
  );
};
