import { Routes } from '@angular/router';
import { authGuard } from './guards/auth/auth.guard';

// Route-level code splitting: every component below is loaded on-demand via
// loadComponent() instead of being eagerly bundled into main.js. This keeps the
// initial bundle to just the app shell + router, and each admin page is only
// downloaded the first time that route is visited.
export const routes: Routes = [
  // --- PUBLIC ROUTES ---
  {
    path: 'company/register',
    loadComponent: () => import('./components/company-register/company-register.component').then(m => m.CompanyRegisterComponent)
  },

  // --- SUPER ADMIN ROUTES ---
  {
    path: 'admin/login',
    loadComponent: () => import('./components/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'admin',
    loadComponent: () => import('./components/admin-layout/admin-layout.component').then(m => m.AdminLayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./components/admin/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'companies', loadComponent: () => import('./components/admin/company-management/company-management.component').then(m => m.CompanyManagementComponent) },
      { path: 'company/create', loadComponent: () => import('./components/admin/company-form/company-form.component').then(m => m.CompanyFormComponent) },
      { path: 'company/edit/:id', loadComponent: () => import('./components/admin/company-form/company-form.component').then(m => m.CompanyFormComponent) },
      { path: 'theme', loadComponent: () => import('./components/admin/theme-management/theme-management.component').then(m => m.ThemeManagementComponent) },
      { path: 'users', loadComponent: () => import('./components/admin/user-management/user-management.component').then(m => m.UserManagementComponent) },
      { path: 'roles', loadComponent: () => import('./components/admin/role-management/role-management.component').then(m => m.RoleManagementComponent) },
      // Company Module routes (Super Admin views any company)
      { path: 'products', loadComponent: () => import('./components/admin/product-management/product-management.component').then(m => m.ProductManagementComponent) },
      { path: 'add-product', loadComponent: () => import('./components/admin/add-product/add-product.component').then(m => m.AddProductComponent) },
      { path: 'edit-product/:id', loadComponent: () => import('./components/admin/add-product/add-product.component').then(m => m.AddProductComponent) },
      { path: 'categories', loadComponent: () => import('./components/admin/category-management/category-management.component').then(m => m.CategoryManagementComponent) },
      { path: 'barcodes', loadComponent: () => import('./components/admin/barcode-management/barcode-management.component').then(m => m.BarcodeManagementComponent) },
      { path: 'price-tag', loadComponent: () => import('./components/admin/price-tag/price-tag.component').then(m => m.PriceTagComponent) },
      { path: 'suppliers', loadComponent: () => import('./components/admin/supplier-management/supplier-management.component').then(m => m.SupplierManagementComponent) },
      { path: 'inventory', loadComponent: () => import('./components/admin/inventory/inventory.component').then(m => m.InventoryComponent) },
      { path: 'pos', loadComponent: () => import('./components/admin/pos-management/pos-management.component').then(m => m.PosManagementComponent) },
      { path: 'orders', loadComponent: () => import('./components/admin/order-management/order-management.component').then(m => m.OrderManagementComponent) },
      { path: 'customers', loadComponent: () => import('./components/admin/customer-management/customer-management.component').then(m => m.CustomerManagementComponent) },
      { path: 'delivery', loadComponent: () => import('./components/admin/delivery-management/delivery-management.component').then(m => m.DeliveryManagementComponent) },
      { path: 'payments', loadComponent: () => import('./components/admin/payment-management/payment-management.component').then(m => m.PaymentManagementComponent) },
      { path: 'reports', loadComponent: () => import('./components/admin/reports/reports.component').then(m => m.ReportsComponent) },
      { path: 'config', loadComponent: () => import('./components/admin/dashboard-config/dashboard-config.component').then(m => m.DashboardConfigComponent) },
      { path: 'pricing', loadComponent: () => import('./components/admin/pricing-management/pricing-management.component').then(m => m.PricingManagementComponent) },
      { path: 'ecommerce/builder', loadComponent: () => import('./components/admin/ecommerce/builder/ecommerce-builder.component').then(m => m.EcommerceBuilderComponent) },
      { path: 'ecommerce/navigation', loadComponent: () => import('./components/admin/ecommerce/navigation/ecommerce-navigation.component').then(m => m.EcommerceNavigationComponent) },
      { path: 'ecommerce/products', loadComponent: () => import('./components/admin/ecommerce/product-display/ecommerce-product-display.component').then(m => m.EcommerceProductDisplayComponent) },
      { path: 'ecommerce/checkout', loadComponent: () => import('./components/admin/ecommerce/checkout/ecommerce-checkout.component').then(m => m.EcommerceCheckoutComponent) },
      { path: 'ecommerce/pages', loadComponent: () => import('./components/admin/ecommerce/pages/ecommerce-pages.component').then(m => m.EcommercePagesComponent) },
      { path: 'ecommerce/product-codes', loadComponent: () => import('./components/admin/ecommerce/product-codes/product-codes.component').then(m => m.ProductCodesComponent) },
      { path: 'app-releases', loadComponent: () => import('./components/admin/app-releases/app-releases.component').then(m => m.AppReleasesComponent) },
      { path: 'dashboard-view', loadComponent: () => import('./components/admin/dashboard-view/dashboard-view.component').then(m => m.DashboardViewComponent) },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  // --- COMPANY USER ROUTES ---
  {
    path: ':companySlug/login',
    loadComponent: () => import('./components/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: ':companySlug/workspace',
    loadComponent: () => import('./components/admin-layout/admin-layout.component').then(m => m.AdminLayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./components/admin/dashboard/dashboard.component').then(m => m.DashboardComponent) },
      { path: 'pos', loadComponent: () => import('./components/admin/pos-management/pos-management.component').then(m => m.PosManagementComponent) },
      { path: 'products', loadComponent: () => import('./components/admin/product-management/product-management.component').then(m => m.ProductManagementComponent) },
      { path: 'add-product', loadComponent: () => import('./components/admin/add-product/add-product.component').then(m => m.AddProductComponent) },
      { path: 'edit-product/:id', loadComponent: () => import('./components/admin/add-product/add-product.component').then(m => m.AddProductComponent) },
      { path: 'categories', loadComponent: () => import('./components/admin/category-management/category-management.component').then(m => m.CategoryManagementComponent) },
      { path: 'barcodes', loadComponent: () => import('./components/admin/barcode-management/barcode-management.component').then(m => m.BarcodeManagementComponent) },
      { path: 'price-tag', loadComponent: () => import('./components/admin/price-tag/price-tag.component').then(m => m.PriceTagComponent) },
      { path: 'suppliers', loadComponent: () => import('./components/admin/supplier-management/supplier-management.component').then(m => m.SupplierManagementComponent) },
      { path: 'inventory', loadComponent: () => import('./components/admin/inventory/inventory.component').then(m => m.InventoryComponent) },
      { path: 'orders', loadComponent: () => import('./components/admin/order-management/order-management.component').then(m => m.OrderManagementComponent) },
      { path: 'customers', loadComponent: () => import('./components/admin/customer-management/customer-management.component').then(m => m.CustomerManagementComponent) },
      { path: 'delivery', loadComponent: () => import('./components/admin/delivery-management/delivery-management.component').then(m => m.DeliveryManagementComponent) },
      { path: 'payments', loadComponent: () => import('./components/admin/payment-management/payment-management.component').then(m => m.PaymentManagementComponent) },
      { path: 'reports', loadComponent: () => import('./components/admin/reports/reports.component').then(m => m.ReportsComponent) },
      { path: 'users', loadComponent: () => import('./components/admin/user-management/user-management.component').then(m => m.UserManagementComponent) },
      { path: 'config', loadComponent: () => import('./components/admin/dashboard-config/dashboard-config.component').then(m => m.DashboardConfigComponent) },
      { path: 'pricing', loadComponent: () => import('./components/admin/pricing-management/pricing-management.component').then(m => m.PricingManagementComponent) },
      { path: 'ecommerce/builder', loadComponent: () => import('./components/admin/ecommerce/builder/ecommerce-builder.component').then(m => m.EcommerceBuilderComponent) },
      { path: 'ecommerce/navigation', loadComponent: () => import('./components/admin/ecommerce/navigation/ecommerce-navigation.component').then(m => m.EcommerceNavigationComponent) },
      { path: 'ecommerce/products', loadComponent: () => import('./components/admin/ecommerce/product-display/ecommerce-product-display.component').then(m => m.EcommerceProductDisplayComponent) },
      { path: 'ecommerce/checkout', loadComponent: () => import('./components/admin/ecommerce/checkout/ecommerce-checkout.component').then(m => m.EcommerceCheckoutComponent) },
      { path: 'ecommerce/pages', loadComponent: () => import('./components/admin/ecommerce/pages/ecommerce-pages.component').then(m => m.EcommercePagesComponent) },
      { path: 'ecommerce/product-codes', loadComponent: () => import('./components/admin/ecommerce/product-codes/product-codes.component').then(m => m.ProductCodesComponent) },
      { path: 'dashboard-view', loadComponent: () => import('./components/admin/dashboard-view/dashboard-view.component').then(m => m.DashboardViewComponent) },
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' }
    ]
  },

  // --- PUBLIC SHOP ROUTES ---
  {
    path: ':companySlug',
    loadComponent: () => import('./components/public-shop/public-shop.component').then(m => m.PublicShopComponent)
  },
  {
    path: ':companySlug/category/:categoryId',
    loadComponent: () => import('./components/public-shop/public-shop.component').then(m => m.PublicShopComponent)
  },
  {
    path: ':companySlug/product/:id',
    loadComponent: () => import('./components/public-shop/product-detail/product-detail.component').then(m => m.ProductDetailComponent)
  },
  {
    path: ':companySlug/checkout',
    loadComponent: () => import('./components/public-shop/checkout/checkout.component').then(m => m.CheckoutComponent)
  },
  {
    path: ':companySlug/page/:key',
    loadComponent: () => import('./components/public-shop/policy-page/policy-page.component').then(m => m.PolicyPageComponent)
  },

  // --- DEFAULT ENTRY ---
  // On the web this just forwards to /admin/login (URL bar carries context as
  // before). Inside the packaged app (no address bar) it lets the user pick
  // Super Admin or type their store code, so one APK serves everyone.
  {
    path: '',
    loadComponent: () => import('./components/app-entry/app-entry.component').then(m => m.AppEntryComponent)
  }
];
