import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmDialogService } from '../../../services/confirm-dialog/confirm-dialog.service';
import { GlobalNotificationService } from '../../../services/global-notification/global-notification.service';

interface PageRight {
  id: string;
  name: string;
  category: string;
}

interface Role {
  id: string;
  name: string;
  type: 'Global' | 'Company';
  rights: string[];
}

@Component({
  selector: 'app-role-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './role-management.component.html',
  styleUrl: './role-management.component.css'
})
export class RoleManagementComponent {
  private confirmSvc = inject(ConfirmDialogService);
  private notify = inject(GlobalNotificationService);
  availableRights: PageRight[] = [
    { id: 'PAGE_SUPER_DASHBOARD', name: 'Super Admin Dashboard', category: 'Super Admin' },
    { id: 'PAGE_COMPANY_MANAGEMENT', name: 'Company Management', category: 'Super Admin' },
    { id: 'PAGE_USER_MANAGEMENT', name: 'Global User Management', category: 'Super Admin' },
    { id: 'PAGE_ROLE_MANAGEMENT', name: 'Role Management', category: 'Super Admin' },
    { id: 'PAGE_SHOP_DASHBOARD', name: 'Shop Dashboard', category: 'Shop Admin' },
    { id: 'PAGE_POS', name: 'POS System', category: 'Shop Admin' },
    { id: 'PAGE_PRODUCTS', name: 'Product Management', category: 'Shop Admin' },
    { id: 'PAGE_CATEGORIES', name: 'Category Management', category: 'Shop Admin' },
    { id: 'PAGE_REPORTS', name: 'Reports View', category: 'Shop Admin' },
    { id: 'PAGE_STORE_CONFIG', name: 'Store Configuration', category: 'Shop Admin' },
    { id: 'PAGE_PRICING', name: 'Pricing', category: 'Shop Admin' }
  ];

  roles: Role[] = [
    { id: 'r1', name: 'SUPER_ADMIN', type: 'Global', rights: this.availableRights.map(r => r.id) },
    { id: 'r2', name: 'COMPANY_ADMIN', type: 'Company', rights: ['PAGE_SHOP_DASHBOARD', 'PAGE_POS', 'PAGE_PRODUCTS', 'PAGE_CATEGORIES', 'PAGE_REPORTS', 'PAGE_STORE_CONFIG', 'PAGE_PRICING'] },
    { id: 'r3', name: 'CASHIER', type: 'Company', rights: ['PAGE_SHOP_DASHBOARD', 'PAGE_POS'] }
  ];

  showModal = false;
  isEditMode = false;
  currentRole: Role = this.getEmptyRole();
  successMsg = '';
  errorMsg = '';

  get categories(): string[] {
    return [...new Set(this.availableRights.map(r => r.category))];
  }

  getEmptyRole(): Role {
    return { id: '', name: '', type: 'Company', rights: [] };
  }

  getRightsByCategory(category: string) {
    return this.availableRights.filter(r => r.category === category);
  }

  hasRight(rightId: string): boolean {
    return this.currentRole.rights.includes(rightId);
  }

  toggleRight(rightId: string, event: Event) {
    const isChecked = (event.target as HTMLInputElement).checked;
    if (isChecked) {
      if (!this.currentRole.rights.includes(rightId)) {
        this.currentRole.rights.push(rightId);
      }
    } else {
      this.currentRole.rights = this.currentRole.rights.filter(id => id !== rightId);
    }
  }

  openAddModal() {
    this.isEditMode = false;
    this.currentRole = this.getEmptyRole();
    this.errorMsg = '';
    this.showModal = true;
  }

  openEditModal(role: Role) {
    this.isEditMode = true;
    this.currentRole = { ...role, rights: [...role.rights] };
    this.errorMsg = '';
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.errorMsg = '';
  }

  saveRole() {
    this.errorMsg = '';
    if (!this.currentRole.name) {
      this.errorMsg = 'Role Name is required!';
      return;
    }

    if (this.isEditMode) {
      const idx = this.roles.findIndex(r => r.id === this.currentRole.id);
      if (idx !== -1) {
        this.roles[idx] = { ...this.currentRole, name: this.currentRole.name.toUpperCase().replace(/\s+/g, '_') };
        this.successMsg = 'Role updated successfully!';
      }
    } else {
      this.currentRole.id = Date.now().toString();
      this.currentRole.name = this.currentRole.name.toUpperCase().replace(/\s+/g, '_');
      this.roles.push({ ...this.currentRole });
      this.successMsg = 'Role created successfully!';
    }
    this.closeModal();
    setTimeout(() => this.successMsg = '', 3000);
  }

  async deleteRole(role: Role) {
    if (role.name === 'SUPER_ADMIN') {
      this.notify.notify({ type: 'warning', title: 'Cannot Delete', message: 'The root SUPER_ADMIN role cannot be deleted.', ttlMs: 4000 });
      return;
    }
    const ok = await this.confirmSvc.confirm({ title: 'Delete Role', message: `Delete role "${role.name}"? This cannot be undone.`, confirmLabel: 'Delete', danger: true });
    if (!ok) return;
    this.roles = this.roles.filter(r => r.id !== role.id);
    this.successMsg = 'Role deleted successfully!';
    setTimeout(() => this.successMsg = '', 3000);
  }
}
