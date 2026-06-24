import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

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
    { id: 'PAGE_STORE_CONFIG', name: 'Store Configuration', category: 'Shop Admin' }
  ];

  roles: Role[] = [
    { id: 'r1', name: 'SUPER_ADMIN', type: 'Global', rights: this.availableRights.map(r => r.id) },
    { id: 'r2', name: 'COMPANY_ADMIN', type: 'Company', rights: ['PAGE_SHOP_DASHBOARD', 'PAGE_POS', 'PAGE_PRODUCTS', 'PAGE_CATEGORIES', 'PAGE_REPORTS', 'PAGE_STORE_CONFIG'] },
    { id: 'r3', name: 'CASHIER', type: 'Company', rights: ['PAGE_SHOP_DASHBOARD', 'PAGE_POS'] }
  ];

  showModal = false;
  isEditMode = false;
  currentRole: Role = this.getEmptyRole();

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
    this.showModal = true;
  }

  openEditModal(role: Role) {
    this.isEditMode = true;
    this.currentRole = { ...role, rights: [...role.rights] };
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
  }

  saveRole() {
    if (!this.currentRole.name) {
      alert('Role Name is required!');
      return;
    }

    if (this.isEditMode) {
      const idx = this.roles.findIndex(r => r.id === this.currentRole.id);
      if (idx !== -1) {
        this.roles[idx] = { ...this.currentRole, name: this.currentRole.name.toUpperCase().replace(/\s+/g, '_') };
      }
    } else {
      this.currentRole.id = Date.now().toString();
      this.currentRole.name = this.currentRole.name.toUpperCase().replace(/\s+/g, '_');
      this.roles.push({ ...this.currentRole });
    }
    this.closeModal();
  }

  deleteRole(role: Role) {
    if (role.name === 'SUPER_ADMIN') {
      alert('Cannot delete the root SUPER_ADMIN role.');
      return;
    }
    if (confirm(`Are you sure you want to delete role: ${role.name}?`)) {
      this.roles = this.roles.filter(r => r.id !== role.id);
    }
  }
}
