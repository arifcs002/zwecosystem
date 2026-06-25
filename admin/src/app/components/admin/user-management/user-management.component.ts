import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { UserService, User } from '../../../services/user/user.service';
import { CompanyService, Company } from '../../../services/company/company.service';

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './user-management.component.html',
  styleUrl: './user-management.component.css'
})
export class UserManagementComponent implements OnInit {
  
  private userService = inject(UserService);
  private companyService = inject(CompanyService);

  companies: Company[] = [];
  selectedCompanyContext: string = 'all';
  
  // Custom Select2-like state
  isDropdownOpen = false;
  searchQuery = '';
  filteredCompanies: Company[] = [];

  users: any[] = [];
  filteredUsers: any[] = [];

  showModal = false;
  isEditMode = false;
  currentUser: any = this.getEmptyUser();
  successMsg = '';
  errorMsg = '';

  availableRoles = ['SUPER_ADMIN', 'COMPANY_ADMIN', 'MANAGER', 'SALES', 'ACCOUNTANT'];

  ngOnInit() {
    this.loadCompanies();
    this.loadUsers();
  }

  loadCompanies() {
    this.companyService.getCompanies().subscribe({
      next: (data) => {
        this.companies = data;
        this.filteredCompanies = [...this.companies];
      },
      error: (err) => console.error(err)
    });
  }

  loadUsers() {
    this.userService.getUsers().subscribe({
      next: (data) => {
        this.users = data;
        this.filterUsers();
      },
      error: (err) => console.error(err)
    });
  }

  // Custom Select2 Logic
  toggleDropdown() {
    this.isDropdownOpen = !this.isDropdownOpen;
    if (this.isDropdownOpen) {
      this.searchQuery = '';
      this.filteredCompanies = [...this.companies];
    }
  }

  onSearchChange() {
    const query = this.searchQuery.toLowerCase();
    this.filteredCompanies = this.companies.filter(c => c.name.toLowerCase().includes(query));
  }

  selectCompany(compId: string) {
    this.selectedCompanyContext = compId;
    this.isDropdownOpen = false;
    this.onCompanyContextChange();
  }

  getSelectedCompanyName(): string {
    if (this.selectedCompanyContext === 'all') return '-- All Companies --';
    return this.companies.find(c => c.id === this.selectedCompanyContext)?.name || 'Select Company';
  }

  filterUsers() {
    if (this.selectedCompanyContext === 'all') {
      this.filteredUsers = [...this.users];
    } else {
      this.filteredUsers = this.users.filter(u => u.companyId === this.selectedCompanyContext);
    }
  }

  onCompanyContextChange() {
    this.filterUsers();
    // Only pre-fill the form if a specific company is selected, otherwise leave empty
    this.currentUser.companyId = this.selectedCompanyContext === 'all' ? '' : this.selectedCompanyContext;
  }

  getEmptyUser(): any {
    return { id: '', firstName: '', lastName: '', email: '', phoneNumber: '', role: '', companyId: this.selectedCompanyContext === 'all' ? null : this.selectedCompanyContext, isActive: true, password: '' };
  }

  openAddModal() {
    this.isEditMode = false;
    this.currentUser = this.getEmptyUser();
    this.errorMsg = '';
    this.showModal = true;
  }

  editUser(user: any) {
    this.isEditMode = true;
    this.currentUser = { ...user, role: user.roles?.[0] || 'COMPANY_ADMIN', password: '' };
    this.errorMsg = '';
    this.showModal = true;
  }

  closeModal() {
    this.showModal = false;
    this.errorMsg = '';
  }

  saveUser() {
    this.errorMsg = '';
    if (this.isEditMode) {
      this.userService.updateUser(this.currentUser.id, this.currentUser).subscribe({
        next: () => {
          this.successMsg = 'User updated successfully!';
          this.loadUsers();
          this.closeModal();
          setTimeout(() => this.successMsg = '', 3000);
        },
        error: (err) => {
          this.errorMsg = typeof err.error === 'string' ? err.error : (err.error?.message || JSON.stringify(err.error) || err.message);
        }
      });
    } else {
      this.userService.addUser(this.currentUser).subscribe({
        next: () => {
          this.successMsg = 'User added successfully!';
          this.loadUsers();
          this.closeModal();
          setTimeout(() => this.successMsg = '', 3000);
        },
        error: (err) => {
          this.errorMsg = typeof err.error === 'string' ? err.error : (err.error?.message || JSON.stringify(err.error) || err.message);
        }
      });
    }
  }

  toggleStatus(user: any) {
    const payload = { ...user, role: user.roles?.[0] || 'COMPANY_ADMIN', isActive: !user.isActive };
    if(confirm(`Are you sure you want to change status for ${user.firstName}?`)) {
      this.userService.updateUser(user.id, payload).subscribe({
        next: () => this.loadUsers(),
        error: (err) => alert('Failed to update status')
      });
    }
  }

  deleteUser(user: any) {
    if(confirm(`Are you sure you want to delete user ${user.firstName}?`)) {
      this.userService.deleteUser(user.id).subscribe({
        next: () => this.loadUsers(),
        error: (err) => alert('Failed to delete user')
      });
    }
  }
}
