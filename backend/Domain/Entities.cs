using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Ecommerce.Api.Domain
{
    public enum RoleName
    {
        superadmin,
        companyadmin,
        companymanager,
        salesstaff,
        customer
    }

    public abstract class AuditEntity
    {
        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("created_date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [Column("updated_by")]
        public int? UpdatedBy { get; set; }

        [Column("updated_date")]
        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

        [Column("deleted_by")]
        public int? DeletedBy { get; set; }

        [Column("deleted_date")]
        public DateTime? DeletedDate { get; set; }

        [Column("is_deleted")]
        public int IsDeleted { get; set; } = 0;
    }

    [Table("subscription_plans")]
    public class SubscriptionPlan : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("price")]
        public decimal Price { get; set; }

        [Required]
        [Column("billing_cycle")]
        public string BillingCycle { get; set; } = "monthly";

        [Column("features", TypeName = "jsonb")]
        public string? Features { get; set; } // JSON string

        [JsonIgnore]
        public List<Company> Companies { get; set; } = new();
    }

    [Table("companies")]
    public class Company : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("subdomain")]
        public string Subdomain { get; set; } = string.Empty;

        [Column("logo_url")]
        public string? LogoUrl { get; set; }

        [Column("banner_url")]
        public string? BannerUrl { get; set; }

        [Column("contact_email")]
        public string? ContactEmail { get; set; }

        [Column("contact_phone")]
        public string? ContactPhone { get; set; }

        [Column("address")]
        public string? Address { get; set; }

        [Column("delivery_charge")]
        public decimal DeliveryCharge { get; set; } = 0.00m;

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("subscription_plan_id")]
        public int? SubscriptionPlanId { get; set; }

        [ForeignKey(nameof(SubscriptionPlanId))]
        public SubscriptionPlan? SubscriptionPlan { get; set; }

        [Column("subscription_expires_at")]
        public DateTime? SubscriptionExpiresAt { get; set; }

        [Column("approval_status")]
        public string ApprovalStatus { get; set; } = "Pending"; // Pending, Approved, Rejected

        // Extra details for registration
        [Column("owner_name")]
        public string? OwnerName { get; set; }

        [Column("owner_mobile")]
        public string? OwnerMobile { get; set; }

        [Column("company_mobile")]
        public string? CompanyMobile { get; set; }

        [Column("facebook_link")]
        public string? FacebookLink { get; set; }

        [Column("instagram_link")]
        public string? InstagramLink { get; set; }

        [Column("bkash_number")]
        public string? BkashNumber { get; set; }

        [Column("nagad_number")]
        public string? NagadNumber { get; set; }

        [Column("bank_name")]
        public string? BankName { get; set; }

        [Column("bank_account_name")]
        public string? BankAccountName { get; set; }

        [Column("division")]
        public string? Division { get; set; }

        [Column("district")]
        public string? District { get; set; }

        [Column("thana")]
        public string? Thana { get; set; }

        // Navigation properties
        [JsonIgnore]
        public List<User> Users { get; set; } = new();
        [JsonIgnore]
        public List<Category> Categories { get; set; } = new();
        [JsonIgnore]
        public List<Brand> Brands { get; set; } = new();
        [JsonIgnore]
        public List<Product> Products { get; set; } = new();
        [JsonIgnore]
        public List<Order> Orders { get; set; } = new();
        [JsonIgnore]
        public List<Payment> Payments { get; set; } = new();
        [JsonIgnore]
        public List<CompanySetting> CompanySettings { get; set; } = new();
        [JsonIgnore]
        public List<AuditLog> AuditLogs { get; set; } = new();
    }

    [Table("roles")]
    public class Role : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty; // 'superadmin', 'companyadmin', etc.

        [Required]
        [Column("value")]
        public string Value { get; set; } = string.Empty; // 'Super Admin', 'Company Admin', etc.

        [Column("description")]
        public string? Description { get; set; }

        [JsonIgnore]
        public List<UserRole> UserRoles { get; set; } = new();
        [JsonIgnore]
        public List<RolePermission> RolePermissions { get; set; } = new();
    }

    [Table("permissions")]
    public class Permission : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [JsonIgnore]
        public List<RolePermission> RolePermissions { get; set; } = new();
    }

    [Table("user_roles")]
    public class UserRole : AuditEntity
    {
        [Column("user_id")]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }

        [Column("role_id")]
        public int RoleId { get; set; }

        [ForeignKey(nameof(RoleId))]
        public Role? Role { get; set; }
    }

    [Table("role_permissions")]
    public class RolePermission : AuditEntity
    {
        [Column("role_id")]
        public int RoleId { get; set; }

        [ForeignKey(nameof(RoleId))]
        public Role? Role { get; set; }

        [Column("permission_id")]
        public int PermissionId { get; set; }

        [ForeignKey(nameof(PermissionId))]
        public Permission? Permission { get; set; }
    }

    [Table("users")]
    public class User : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int? CompanyId { get; set; } // Nullable for Super Admin

        [ForeignKey(nameof(CompanyId))]
        public Company? Company { get; set; }

        [Required]
        [EmailAddress]
        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [JsonIgnore]
        [Column("password_hash")]
        public string PasswordHash { get; set; } = string.Empty;

        [Required]
        [Column("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [Column("last_name")]
        public string LastName { get; set; } = string.Empty;

        [Column("phone_number")]
        public string? PhoneNumber { get; set; }

        // Determines if this is a ZW Admin, Company User, or Customer
        [Column("user_type")]
        public string UserType { get; set; } = "CompanyUser"; 

        [Column("address")]
        public string? Address { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("otp")]
        public string? Otp { get; set; }

        [Column("otp_expires_at")]
        public DateTime? OtpExpiresAt { get; set; }

        public List<UserRole> UserRoles { get; set; } = new();
        [JsonIgnore]
        public List<Order> Orders { get; set; } = new();
        [JsonIgnore]
        public List<AuditLog> AuditLogs { get; set; } = new();
    }

    [Table("suppliers")]
    public class Supplier : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("address")]
        public string? Address { get; set; }

        [Column("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    [Table("categories")]
    public class Category : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [Column("sizes")]
        public string? Sizes { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("slug")]
        public string Slug { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [Column("parent_id")]
        public int? ParentId { get; set; }

        [ForeignKey(nameof(ParentId))]
        [JsonIgnore]
        public Category? Parent { get; set; }

        public List<Category> Children { get; set; } = new();
        
        [JsonIgnore]
        public List<Product> Products { get; set; } = new();
    }

    [Table("brands")]
    public class Brand : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("slug")]
        public string Slug { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [Column("logo_url")]
        public string? LogoUrl { get; set; }

        [JsonIgnore]
        public List<Product> Products { get; set; } = new();
    }

    [Table("products")]
    public class Product : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("slug")]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [Column("sku")]
        public string Sku { get; set; } = string.Empty;

        [Required]
        [Column("barcode")]
        public string Barcode { get; set; } = string.Empty;

        [Column("description")]
        public string? Description { get; set; }

        [Required]
        [Column("price")]
        public decimal Price { get; set; } // Retail Price

        [Required]
        [Column("wholesale_price")]
        public decimal WholesalePrice { get; set; }

        [Column("stock_quantity")]
        public int StockQuantity { get; set; } = 0;

        [Column("status")]
        public string Status { get; set; } = "PUBLISHED"; // 'PUBLISHED', 'DRAFT', 'OUT_OF_STOCK'

        [Column("image_url")]
        public string? ImageUrl { get; set; }

        [Column("category_id")]
        public int? CategoryId { get; set; }

        [ForeignKey(nameof(CategoryId))]
        public Category? Category { get; set; }

        [Column("brand_id")]
        public int? BrandId { get; set; }

        [ForeignKey(nameof(BrandId))]
        public Brand? Brand { get; set; }

        [Column("size")]
        public string? Size { get; set; }

        [Column("supplier_id")]
        public int? SupplierId { get; set; }

        [ForeignKey(nameof(SupplierId))]
        public Supplier? Supplier { get; set; }
    }

    [Table("orders")]
    public class Order : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Required]
        [Column("order_number")]
        public string OrderNumber { get; set; } = string.Empty;

        [Required]
        [Column("sale_type")]
        public string SaleType { get; set; } = "ECOMMERCE"; // 'ECOMMERCE', 'POS'

        [Column("sales_staff_id")]
        public int? SalesStaffId { get; set; }

        [ForeignKey(nameof(SalesStaffId))]
        public User? SalesStaff { get; set; }

        [Column("customer_name")]
        public string? CustomerName { get; set; }

        [Column("customer_phone")]
        public string? CustomerPhone { get; set; }

        [Column("status")]
        public string Status { get; set; } = "PENDING"; // 'PENDING', 'PROCESSING', 'COMPLETED', 'CANCELLED'

        [Column("subtotal")]
        public decimal Subtotal { get; set; }

        [Column("discount")]
        public decimal Discount { get; set; } = 0.00m;

        [Column("tax")]
        public decimal Tax { get; set; } = 0.00m;

        [Column("shipping_fee")]
        public decimal ShippingFee { get; set; } = 0.00m;

        [Column("total")]
        public decimal Total { get; set; }

        [Required]
        [Column("payment_method")]
        public string PaymentMethod { get; set; } = "CASH"; // 'CASH', 'BKASH', 'NAGAD', 'ROCKET', 'CARD'

        [Column("payment_status")]
        public string PaymentStatus { get; set; } = "PENDING"; // 'PENDING', 'PAID', 'FAILED'

        public List<OrderItem> Items { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();
    }

    [Table("order_items")]
    public class OrderItem : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [ForeignKey(nameof(OrderId))]
        [JsonIgnore]
        public Order? Order { get; set; }

        [Column("product_id")]
        public int ProductId { get; set; }

        [ForeignKey(nameof(ProductId))]
        public Product? Product { get; set; }

        [Column("quantity")]
        public int Quantity { get; set; }

        [Column("price")]
        public decimal Price { get; set; }

        [Column("total_price")]
        public decimal TotalPrice { get; set; }
    }

    [Table("payments")]
    public class Payment : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Column("order_id")]
        public int OrderId { get; set; }

        [ForeignKey(nameof(OrderId))]
        [JsonIgnore]
        public Order? Order { get; set; }

        [Column("transaction_id")]
        public string? TransactionId { get; set; }

        [Required]
        [Column("provider")]
        public string Provider { get; set; } = string.Empty; // 'BKASH', 'NAGAD', 'ROCKET', 'CASH'

        [Column("amount")]
        public decimal Amount { get; set; }

        [Column("status")]
        public string Status { get; set; } = "PENDING"; // 'PENDING', 'SUCCESS', 'FAILED'

        [Column("payment_type")]
        public string PaymentType { get; set; } = "AUTOMATED"; // 'AUTOMATED', 'MANUAL'

        [Column("sender_number")]
        public string? SenderNumber { get; set; }

        [Column("reference_log")]
        public string? ReferenceLog { get; set; }
    }

    [Table("company_settings")]
    public class CompanySetting : AuditEntity
    {
        [Column("company_id")]
        public int CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Required]
        [Column("key")]
        public string Key { get; set; } = string.Empty;

        [Required]
        [Column("value")]
        public string Value { get; set; } = string.Empty;

        [Column("group_name")]
        public string GroupName { get; set; } = "GENERAL"; // 'GENERAL', 'ECOMMERCE', 'POS', 'PAYMENT'
    }

    [Table("audit_logs")]
    public class AuditLog : AuditEntity
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("company_id")]
        public int? CompanyId { get; set; }

        [ForeignKey(nameof(CompanyId))]
        [JsonIgnore]
        public Company? Company { get; set; }

        [Column("user_id")]
        public int? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        [JsonIgnore]
        public User? User { get; set; }

        [Required]
        [Column("action")]
        public string Action { get; set; } = string.Empty;

        [Column("details")]
        public string? Details { get; set; }

        [Column("ip_address")]
        public string? IpAddress { get; set; }

        [Column("user_agent")]
        public string? UserAgent { get; set; }
    }

    [Table("user_sessions")]
    public class UserSession
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("session_token")]
        public string SessionToken { get; set; } = string.Empty;

        [Column("user_id")]
        public int UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        [JsonIgnore]
        public User? User { get; set; }

        [Column("company_id")]
        public int? CompanyId { get; set; }

        [Column("roles")]
        public string Roles { get; set; } = string.Empty; // comma-separated

        [Column("user_type")]
        public string? UserType { get; set; }

        [Column("email")]
        public string Email { get; set; } = string.Empty;

        [Column("full_name")]
        public string FullName { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("ip_address")]
        public string? IpAddress { get; set; }

        [Column("user_agent")]
        public string? UserAgent { get; set; }
    }
}
