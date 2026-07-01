using Microsoft.EntityFrameworkCore;
using Ecommerce.Api.Domain;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Ecommerce.Api.Infrastructure
{
    public class ApplicationDbContext : DbContext
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public int? CompanyId { get; private set; }
        public int? CurrentUserId { get; private set; }

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            IHttpContextAccessor? httpContextAccessor = null) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
            ResolveTenantAndUser();
        }

        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<CompanySetting> CompanySettings { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<PricingTag> PricingTags { get; set; }
        public DbSet<AppVersion> AppVersions { get; set; }

        private void ResolveTenantAndUser()
        {
            if (_httpContextAccessor?.HttpContext != null)
            {
                var claimsPrincipal = _httpContextAccessor.HttpContext.User;
                
                // Resolve CompanyId (Tenant)
                var tenantClaim = claimsPrincipal?.FindFirst("company_id")?.Value;
                if (int.TryParse(tenantClaim, out var tokenTenantId))
                {
                    CompanyId = tokenTenantId;
                }
                else if (_httpContextAccessor.HttpContext.Request.Headers.TryGetValue("X-Tenant-ID", out var headerTenant))
                {
                    if (int.TryParse(headerTenant.ToString(), out var headerTenantId))
                    {
                        CompanyId = headerTenantId;
                    }
                }

                // Resolve UserId
                var subClaim = claimsPrincipal?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                               ?? claimsPrincipal?.FindFirst("sub")?.Value;
                if (int.TryParse(subClaim, out var parsedUserId))
                {
                    CurrentUserId = parsedUserId;
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Composite Key for UserRole
            modelBuilder.Entity<UserRole>()
                .HasKey(ur => new { ur.UserId, ur.RoleId });

            // Composite Key for RolePermission
            modelBuilder.Entity<RolePermission>()
                .HasKey(rp => new { rp.RoleId, rp.PermissionId });

            // Composite Key for CompanySetting
            modelBuilder.Entity<CompanySetting>()
                .HasKey(cs => new { cs.CompanyId, cs.Key });

            // Unique Indexes for Tenant Isolation
            modelBuilder.Entity<Category>()
                .HasIndex(c => new { c.CompanyId, c.Slug })
                .IsUnique();

            modelBuilder.Entity<Brand>()
                .HasIndex(b => new { b.CompanyId, b.Slug })
                .IsUnique();

            modelBuilder.Entity<Product>()
                .HasIndex(p => new { p.CompanyId, p.Sku })
                .IsUnique();

            modelBuilder.Entity<Product>()
                .HasIndex(p => new { p.CompanyId, p.Barcode })
                .IsUnique();

            modelBuilder.Entity<Order>()
                .HasIndex(o => new { o.CompanyId, o.OrderNumber })
                .IsUnique();

            // Set up Foreign Keys
            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId);

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId);

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId);

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId);

            // Precision for Decimal fields
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
            {
                if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                {
                    property.SetPrecision(18);
                    property.SetScale(2);
                }
            }

            // Global Query Filters (Active Tenant & Soft Delete Filters)
            modelBuilder.Entity<User>().HasQueryFilter(u => u.IsDeleted == 0 && (!CompanyId.HasValue || u.CompanyId == CompanyId));
            modelBuilder.Entity<Category>().HasQueryFilter(c => c.IsDeleted == 0 && (!CompanyId.HasValue || c.CompanyId == CompanyId));
            modelBuilder.Entity<Brand>().HasQueryFilter(b => b.IsDeleted == 0 && (!CompanyId.HasValue || b.CompanyId == CompanyId));
            modelBuilder.Entity<Product>().HasQueryFilter(p => p.IsDeleted == 0 && (!CompanyId.HasValue || p.CompanyId == CompanyId));
            modelBuilder.Entity<Order>().HasQueryFilter(o => o.IsDeleted == 0 && (!CompanyId.HasValue || o.CompanyId == CompanyId));
            modelBuilder.Entity<Payment>().HasQueryFilter(p => p.IsDeleted == 0 && (!CompanyId.HasValue || p.CompanyId == CompanyId));
            modelBuilder.Entity<CompanySetting>().HasQueryFilter(cs => cs.IsDeleted == 0 && (!CompanyId.HasValue || cs.CompanyId == CompanyId));
            modelBuilder.Entity<AuditLog>().HasQueryFilter(a => a.IsDeleted == 0 && (!CompanyId.HasValue || a.CompanyId == CompanyId));
            modelBuilder.Entity<Supplier>().HasQueryFilter(s => s.IsDeleted == 0 && (!CompanyId.HasValue || s.CompanyId == CompanyId));
            modelBuilder.Entity<PricingTag>().HasQueryFilter(pt => pt.IsDeleted == 0 && (!CompanyId.HasValue || pt.CompanyId == CompanyId));
            modelBuilder.Entity<Company>().HasQueryFilter(c => c.IsDeleted == 0);
            modelBuilder.Entity<SubscriptionPlan>().HasQueryFilter(sp => sp.IsDeleted == 0);
            modelBuilder.Entity<Role>().HasQueryFilter(r => r.IsDeleted == 0);
            modelBuilder.Entity<Permission>().HasQueryFilter(p => p.IsDeleted == 0);
            modelBuilder.Entity<UserRole>().HasQueryFilter(ur => ur.IsDeleted == 0);
            modelBuilder.Entity<RolePermission>().HasQueryFilter(rp => rp.IsDeleted == 0);

            // Seed SeedData
            SeedInitialData(modelBuilder);
        }

        private void SeedInitialData(ModelBuilder modelBuilder)
        {
            var staticDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Seed Subscription Plans
            var basicPlanId = 1;
            var premiumPlanId = 2;

            modelBuilder.Entity<SubscriptionPlan>().HasData(
                new SubscriptionPlan { Id = basicPlanId, Name = "Basic Plan", Price = 1500.00m, BillingCycle = "monthly", Features = "{\"max_products\": 200, \"pos_enabled\": true, \"ecommerce_enabled\": true}", CreatedDate = staticDate, UpdatedDate = staticDate },
                new SubscriptionPlan { Id = premiumPlanId, Name = "Premium Plan", Price = 3500.00m, BillingCycle = "monthly", Features = "{\"max_products\": 5000, \"pos_enabled\": true, \"ecommerce_enabled\": true, \"multi_staff\": true}", CreatedDate = staticDate, UpdatedDate = staticDate }
            );

            // Seed Roles
            var superAdminRoleId = 1;
            var companyAdminRoleId = 2;
            var companyManagerRoleId = 3;
            var salesStaffRoleId = 4;
            var customerRoleId = 5;

            modelBuilder.Entity<Role>().HasData(
                new Role { Id = superAdminRoleId, Name = "superadmin", Value = "Super Admin", Description = "Platform Owner", CreatedDate = staticDate },
                new Role { Id = companyAdminRoleId, Name = "companyadmin", Value = "Company Admin", Description = "Company Owner", CreatedDate = staticDate },
                new Role { Id = companyManagerRoleId, Name = "companymanager", Value = "Company Manager", Description = "Store Manager", CreatedDate = staticDate },
                new Role { Id = salesStaffRoleId, Name = "salesstaff", Value = "Sales Staff", Description = "POS Checkout Operator", CreatedDate = staticDate },
                new Role { Id = customerRoleId, Name = "customer", Value = "Customer", Description = "Public Customer", CreatedDate = staticDate }
            );

            // Seed Permissions
            var diagPermId = 1;
            var compPermId = 2;
            var subsPermId = 3;
            var settPermId = 4;
            var staffPermId = 5;
            var invPermId = 6;
            var posPermId = 7;
            var repFullPermId = 8;
            var repOpPermId = 9;
            var buyPermId = 10;

            modelBuilder.Entity<Permission>().HasData(
                new Permission { Id = diagPermId, Name = "platform:diagnostics", Description = "Access SaaS metrics and platform logs", CreatedDate = staticDate },
                new Permission { Id = compPermId, Name = "manage:companies", Description = "Add, suspend, or upgrade tenant companies", CreatedDate = staticDate },
                new Permission { Id = subsPermId, Name = "manage:subscriptions", Description = "Configure billing plans", CreatedDate = staticDate },
                new Permission { Id = settPermId, Name = "company:settings", Description = "Update company-wide configuration", CreatedDate = staticDate },
                new Permission { Id = staffPermId, Name = "manage:staff", Description = "Create/deactivate managers and checkout staff", CreatedDate = staticDate },
                new Permission { Id = invPermId, Name = "manage:inventory", Description = "Create products and trigger barcode generation", CreatedDate = staticDate },
                new Permission { Id = posPermId, Name = "pos:checkout", Description = "Scan barcodes and complete POS checkout", CreatedDate = staticDate },
                new Permission { Id = repFullPermId, Name = "reports:full", Description = "Access full company financial and inventory audits", CreatedDate = staticDate },
                new Permission { Id = repOpPermId, Name = "reports:operational", Description = "Access daily operational reports", CreatedDate = staticDate },
                new Permission { Id = buyPermId, Name = "store:buy", Description = "Purchase products online", CreatedDate = staticDate }
            );

            // Role-Permissions Map
            modelBuilder.Entity<RolePermission>().HasData(
                // Super Admin
                new RolePermission { RoleId = superAdminRoleId, PermissionId = diagPermId },
                new RolePermission { RoleId = superAdminRoleId, PermissionId = compPermId },
                new RolePermission { RoleId = superAdminRoleId, PermissionId = subsPermId },
                // Company Admin
                new RolePermission { RoleId = companyAdminRoleId, PermissionId = settPermId },
                new RolePermission { RoleId = companyAdminRoleId, PermissionId = staffPermId },
                new RolePermission { RoleId = companyAdminRoleId, PermissionId = invPermId },
                new RolePermission { RoleId = companyAdminRoleId, PermissionId = posPermId },
                new RolePermission { RoleId = companyAdminRoleId, PermissionId = repFullPermId },
                // Company Manager
                new RolePermission { RoleId = companyManagerRoleId, PermissionId = invPermId },
                new RolePermission { RoleId = companyManagerRoleId, PermissionId = posPermId },
                new RolePermission { RoleId = companyManagerRoleId, PermissionId = repOpPermId },
                // Sales Staff
                new RolePermission { RoleId = salesStaffRoleId, PermissionId = posPermId },
                // Customer
                new RolePermission { RoleId = customerRoleId, PermissionId = buyPermId }
            );

            // Seed Companies
            var zwCompanyId = 1;
            var demoCompanyId = 2;
            modelBuilder.Entity<Company>().HasData(
                new Company
                {
                    Id = zwCompanyId,
                    Name = "ZW Ecosystem",
                    Subdomain = "zw",
                    ContactEmail = "info@zwecosystem.com",
                    IsActive = true,
                    ApprovalStatus = "Approved",
                    SubscriptionPlanId = premiumPlanId,
                    SubscriptionExpiresAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    CreatedDate = staticDate,
                    UpdatedDate = staticDate
                },
                new Company
                {
                    Id = demoCompanyId,
                    Name = "Zaira's World",
                    Subdomain = "zairasworld",
                    ContactEmail = "info@zairasworld.com",
                    ContactPhone = "01626-458189",
                    Address = "Dhaka, Bangladesh",
                    LogoUrl = "/uploads/zairas_world_logo.png",
                    DeliveryCharge = 60.00m,
                    IsActive = true,
                    ApprovalStatus = "Approved",
                    SubscriptionPlanId = premiumPlanId,
                    SubscriptionExpiresAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    CreatedDate = staticDate,
                    UpdatedDate = staticDate
                }
            );

            // Seed Users (Password is hashed: '123456' using BCrypt)
            var arifUserId = 1;
            var superAdminUserId = 2;
            var compAdminUserId = 3;

            var superAdminPasswordHash = "$2b$11$f1FDqBdveY.KIotMM1fKM.OoZUEGh1tnXTAlWX6aGj3zZHsW2KCrK"; // 123456
            var passwordHash = "$2a$11$Y7M1r2zZ9n2L8T9eW5uV.uN1rT2C8O4pT5Q8aA7qT8I2kL9jH6W2S"; // admin123

            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = arifUserId,
                    CompanyId = zwCompanyId,
                    UserType = "superadmin",
                    Email = "arif",
                    PasswordHash = superAdminPasswordHash,
                    FirstName = "Arif",
                    LastName = "SystemUser",
                    IsActive = true,
                    CreatedDate = staticDate
                },
                new User
                {
                    Id = superAdminUserId,
                    CompanyId = null, // Global
                    UserType = "superadmin",
                    Email = "arifsuperadmin",
                    PasswordHash = superAdminPasswordHash,
                    FirstName = "Platform",
                    LastName = "Owner",
                    IsActive = true,
                    CreatedDate = staticDate
                },
                new User
                {
                    Id = compAdminUserId,
                    CompanyId = demoCompanyId, // Bound to Demo Company
                    UserType = "companyadmin",
                    Email = "admin@zairasworld.com",
                    PasswordHash = passwordHash,
                    FirstName = "Admin",
                    LastName = "User",
                    IsActive = true,
                    CreatedDate = staticDate
                }
            );

            // User-Roles Assignment
            modelBuilder.Entity<UserRole>().HasData(
                new UserRole { UserId = arifUserId, RoleId = superAdminRoleId },
                new UserRole { UserId = superAdminUserId, RoleId = superAdminRoleId },
                new UserRole { UserId = compAdminUserId, RoleId = companyAdminRoleId }
            );

            // Seed Demo Company Settings
            modelBuilder.Entity<CompanySetting>().HasData(
                new CompanySetting { CompanyId = demoCompanyId, Key = "shop_currency", Value = "BDT", GroupName = "GENERAL" },
                new CompanySetting { CompanyId = demoCompanyId, Key = "receipt_header", Value = "Thank you for shopping at Zaira's World!", GroupName = "POS" },
                new CompanySetting { CompanyId = demoCompanyId, Key = "receipt_footer", Value = "Follow us on FB: fb.com/profile.php?id=61583524082495", GroupName = "POS" }
            );

            // Seed Demo Suppliers
            var supplierApexId = 1;
            var supplierBataId = 2;
            modelBuilder.Entity<Supplier>().HasData(
                new Supplier { Id = supplierApexId, CompanyId = demoCompanyId, Name = "Apex Bangladesh", PhoneNumber = "01711122233", Address = "Dhaka", CreatedDate = staticDate, UpdatedDate = staticDate },
                new Supplier { Id = supplierBataId, CompanyId = demoCompanyId, Name = "Bata Bangladesh", PhoneNumber = "01799887766", Address = "Tongi, Gazipur", CreatedDate = staticDate, UpdatedDate = staticDate }
            );

            // Seed Demo Category & Brand & Product
            var clothingCatId = 1;
            var babyShoesCatId = 2;
            var teenageShoesCatId = 3;
            var olderShoesCatId = 4;

            modelBuilder.Entity<Category>().HasData(
                new Category { Id = clothingCatId, CompanyId = demoCompanyId, Name = "Sports Shoes", Slug = "sports-shoes", Description = "Running, tennis and sportswear shoes", Sizes = "39,40,41,42,43,44", CreatedDate = staticDate, UpdatedDate = staticDate },
                new Category { Id = babyShoesCatId, CompanyId = demoCompanyId, Name = "Baby Shoes", Slug = "baby-shoes", Description = "Shoe sizes 1 to 6 for toddlers", Sizes = "1,2,3,4,5,6", CreatedDate = staticDate, UpdatedDate = staticDate },
                new Category { Id = teenageShoesCatId, CompanyId = demoCompanyId, Name = "Teenage Shoes", Slug = "teenage-shoes", Description = "Shoe sizes 6 to 10 for kids & teens", Sizes = "6,7,8,9,10", CreatedDate = staticDate, UpdatedDate = staticDate },
                new Category { Id = olderShoesCatId, CompanyId = demoCompanyId, Name = "Casual Sneakers", Slug = "casual-sneakers", Description = "Shoe sizes 39 to 45 for adults", Sizes = "39,40,41,42,43,44,45", CreatedDate = staticDate, UpdatedDate = staticDate }
            );

            var ecotexBrandId = 1;
            modelBuilder.Entity<Brand>().HasData(
                new Brand { Id = ecotexBrandId, CompanyId = demoCompanyId, Name = "Zaira Brand", Slug = "zaira-brand", Description = "Zaira's World Premium Shoe Selection", CreatedDate = staticDate, UpdatedDate = staticDate }
            );

            var poloProductId = 1;
            modelBuilder.Entity<Product>().HasData(
                new Product
                {
                    Id = poloProductId,
                    CompanyId = demoCompanyId,
                    Name = "Air Force Retro Sneaker",
                    Slug = "air-force-retro-sneaker",
                    Sku = "Z-SNEAKER-001",
                    Barcode = "2000010010015",
                    Description = "Stylish retro sport shoe for casual wear.",
                    Price = 3200.00m,
                    WholesalePrice = 1800.00m,
                    StockQuantity = 45,
                    Status = "PUBLISHED",
                    CategoryId = olderShoesCatId,
                    BrandId = ecotexBrandId,
                    Size = "42",
                    ImageUrl = "/uploads/zairas_world_logo.png",
                    CreatedDate = staticDate,
                    UpdatedDate = staticDate
                }
            );
        }

        public override int SaveChanges()
        {
            PopulateTenantId();
            PopulateAuditAndSoftDeleteFields();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            PopulateTenantId();
            PopulateAuditAndSoftDeleteFields();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void PopulateTenantId()
        {
            if (!CompanyId.HasValue) return;

            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.State == EntityState.Added)
                {
                    var companyIdProp = entry.Entity.GetType().GetProperty("CompanyId");
                    if (companyIdProp != null && companyIdProp.PropertyType == typeof(int))
                    {
                        var currentVal = (int)companyIdProp.GetValue(entry.Entity);
                        if (currentVal == 0)
                        {
                            companyIdProp.SetValue(entry.Entity, CompanyId.Value);
                        }
                    }
                    else if (companyIdProp != null && companyIdProp.PropertyType == typeof(int?))
                    {
                        var currentVal = (int?)companyIdProp.GetValue(entry.Entity);
                        if (currentVal == null || currentVal == 0)
                        {
                            companyIdProp.SetValue(entry.Entity, CompanyId);
                        }
                    }
                }
            }
        }

        private void PopulateAuditAndSoftDeleteFields()
        {
            var userId = CurrentUserId;
            var now = DateTime.UtcNow;

            foreach (var entry in ChangeTracker.Entries())
            {
                // Soft delete
                if (entry.State == EntityState.Deleted && entry.Entity is AuditEntity auditEntityDeleted)
                {
                    entry.State = EntityState.Modified; // Intercept deletion and turn into modification
                    auditEntityDeleted.DeletedDate = now;
                    auditEntityDeleted.DeletedBy = userId;
                    auditEntityDeleted.IsDeleted = 1;
                }

                if (entry.Entity is AuditEntity auditEntity)
                {
                    if (entry.State == EntityState.Added)
                    {
                        auditEntity.CreatedDate = now;
                        auditEntity.CreatedBy = userId;
                        auditEntity.UpdatedDate = now;
                        auditEntity.UpdatedBy = userId;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        auditEntity.UpdatedDate = now;
                        auditEntity.UpdatedBy = userId;
                    }
                }
            }
        }
    }
}
