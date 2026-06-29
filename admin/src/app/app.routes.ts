import { Routes } from '@angular/router';

import { AdminLayoutComponent } from './components/admin-layout/admin-layout.component';
import { PublicShopComponent } from './components/public-shop/public-shop.component';
import { LoginComponent } from './components/auth/login/login.component';
import { DashboardComponent } from './components/admin/dashboard/dashboard.component';
import { CompanyManagementComponent } from './components/admin/company-management/company-management.component';
import { CompanyFormComponent } from './components/admin/company-form/company-form.component';
import { ThemeManagementComponent } from './components/admin/theme-management/theme-management.component';
import { UserManagementComponent } from './components/admin/user-management/user-management.component';
import { RoleManagementComponent } from './components/admin/role-management/role-management.component';
import { PosManagementComponent } from './components/admin/pos-management/pos-management.component';
import { ProductManagementComponent } from './components/admin/product-management/product-management.component';
import { AddProductComponent } from './components/admin/add-product/add-product.component';
import { CategoryManagementComponent } from './components/admin/category-management/category-management.component';
import { ReportsComponent } from './components/admin/reports/reports.component';
import { PriceTagComponent } from './components/admin/price-tag/price-tag.component';
import { DashboardConfigComponent } from './components/admin/dashboard-config/dashboard-config.component';
import { DashboardViewComponent } from './components/admin/dashboard-view/dashboard-view.component';
import { SupplierManagementComponent } from './components/admin/supplier-management/supplier-management.component';
import { OrderManagementComponent } from './components/admin/order-management/order-management.component';
import { DeliveryManagementComponent } from './components/admin/delivery-management/delivery-management.component';
import { PaymentManagementComponent } from './components/admin/payment-management/payment-management.component';
import { BarcodeManagementComponent } from './components/admin/barcode-management/barcode-management.component';
import { authGuard } from './guards/auth/auth.guard';
import { CompanyRegisterComponent } from './components/company-register/company-register.component';

export const routes: Routes = [
  // --- PUBLIC ROUTES ---
  { path: 'company/register', component: CompanyRegisterComponent },

  // --- SUPER ADMIN ROUTES ---
  { path: 'admin/login', component: LoginComponent },
  {
    path: 'admin',
    component: AdminLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      { path: 'companies', component: CompanyManagementComponent },
      { path: 'company/create', component: CompanyFormComponent },
      { path: 'company/edit/:id', component: CompanyFormComponent },
      { path: 'theme', component: ThemeManagementComponent },
      { path: 'users', component: UserManagementComponent },
      { path: 'roles', component: RoleManagementComponent },
      // Company Module routes (Super Admin views any company)
      { path: 'products', component: ProductManagementComponent },
      { path: 'add-product', component: AddProductComponent },
      { path: 'categories', component: CategoryManagementComponent },
      { path: 'barcodes', component: BarcodeManagementComponent },
      { path: 'price-tag', component: PriceTagComponent },
      { path: 'suppliers', component: SupplierManagementComponent },
      { path: 'pos', component: PosManagementComponent },
      { path: 'orders', component: OrderManagementComponent },
      { path: 'delivery', component: DeliveryManagementComponent },
      { path: 'payments', component: PaymentManagementComponent },
      { path: 'reports', component: ReportsComponent },
      { path: 'config', component: DashboardConfigComponent },
      { path: 'dashboard-view', component: DashboardViewComponent },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  // --- COMPANY USER ROUTES ---
  { path: ':companySlug/login', component: LoginComponent },
  {
    path: ':companySlug/workspace',
    component: AdminLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      { path: 'pos', component: PosManagementComponent },
      { path: 'products', component: ProductManagementComponent },
      { path: 'add-product', component: AddProductComponent },
      { path: 'categories', component: CategoryManagementComponent },
      { path: 'barcodes', component: BarcodeManagementComponent },
      { path: 'price-tag', component: PriceTagComponent },
      { path: 'suppliers', component: SupplierManagementComponent },
      { path: 'orders', component: OrderManagementComponent },
      { path: 'delivery', component: DeliveryManagementComponent },
      { path: 'payments', component: PaymentManagementComponent },
      { path: 'reports', component: ReportsComponent },
      { path: 'users', component: UserManagementComponent },
      { path: 'config', component: DashboardConfigComponent },
      { path: 'dashboard-view', component: DashboardViewComponent },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  // --- PUBLIC SHOP ROUTES ---
  { path: ':companySlug', component: PublicShopComponent },
  { path: ':companySlug/category/:categoryId', component: PublicShopComponent },

  // --- DEFAULT REDIRECT ---
  { path: '', redirectTo: '/admin/login', pathMatch: 'full' }
];
