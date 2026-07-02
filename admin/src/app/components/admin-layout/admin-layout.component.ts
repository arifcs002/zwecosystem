import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { AuthService } from '../../services/auth/auth.service';
import { ConfirmDialogComponent } from '../shared/confirm-dialog/confirm-dialog.component';
import { CompanySelectorComponent } from '../shared/company-selector/company-selector.component';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [CommonModule, RouterModule, ConfirmDialogComponent, CompanySelectorComponent],
  templateUrl: './admin-layout.component.html',
  styleUrl: './admin-layout.component.css'
})
export class AdminLayoutComponent {
  authService = inject(AuthService);
  router = inject(Router);
  sidebarCollapsed = false;
  mobileSidebarOpen = false; // off-canvas drawer state, < 1024px only

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

  logout() {
    const user = this.authService.currentUserValue;
    const loginPath = user?.loginContext === 'admin' ? '/admin/login' : `/${user?.loginContext}/login`;
    this.authService.logout();
    this.router.navigate([loginPath]);
  }
}
