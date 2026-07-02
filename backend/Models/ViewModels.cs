namespace Ecommerce.Api.Models
{
    // ── Company ─────────────────────────────────────────────
    public class CompanyListVm
    {
        public int     id             { get; set; }
        public string  name           { get; set; } = string.Empty;
        public string  subdomain      { get; set; } = string.Empty;
        public string? logoUrl        { get; set; }
        public string? contactEmail   { get; set; }
        public string? contactPhone   { get; set; }
        public string? companyMobile  { get; set; }
        public string? ownerName      { get; set; }
        public string? ownerMobile    { get; set; }
        public string? division       { get; set; }
        public string? district       { get; set; }
        public string? thana          { get; set; }
        public string? address        { get; set; }
        public string? facebookLink   { get; set; }
        public string? instagramLink  { get; set; }
        public string? bkashNumber    { get; set; }
        public string? nagadNumber    { get; set; }
        public string? bankName       { get; set; }
        public string? bankAccountName { get; set; }
        public decimal deliveryCharge { get; set; }
        public bool    isActive       { get; set; }
        public string? approvalStatus { get; set; }
        public DateTime createdAt     { get; set; }
    }

    public class CompanyToggleVm
    {
        public bool   isActive { get; set; }
        public string message  { get; set; } = string.Empty;
    }

    // ── Users ───────────────────────────────────────────────
    public class UserListVm
    {
        public int     Id          { get; set; }
        public string  Email       { get; set; } = string.Empty;
        public string  FirstName   { get; set; } = string.Empty;
        public string  LastName    { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public bool    IsActive    { get; set; }
        public int?    CompanyId   { get; set; }
        public string  Roles       { get; set; } = string.Empty; // comma-separated
    }

    public class RoleVm
    {
        public int    Id    { get; set; }
        public string Name  { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    // ── Suppliers ───────────────────────────────────────────
    public class SupplierListVm
    {
        public int      id          { get; set; }
        public string   name        { get; set; } = string.Empty;
        public string?  address     { get; set; }
        public string?  phoneNumber { get; set; }
        public DateTime createdAt   { get; set; }
    }

    // ── Categories ──────────────────────────────────────────
    public class CategoryListVm
    {
        public int      id          { get; set; }
        public string   name        { get; set; } = string.Empty;
        public string?  slug        { get; set; }
        public string?  description { get; set; }
        public int?     parentId    { get; set; }
        public string?  sizes       { get; set; }
        public DateTime createdAt   { get; set; }
    }

    // ── Pricing Tags ────────────────────────────────────────
    public class PricingTagListVm
    {
        public int       id              { get; set; }
        public string    name            { get; set; } = string.Empty;
        public decimal   profitPercent   { get; set; }
        public decimal?  discountPercent { get; set; }
        public DateTime? promoStartDate  { get; set; }
        public DateTime? promoEndDate    { get; set; }
        public bool      isActive        { get; set; }
        public DateTime  createdAt       { get; set; }
    }

    // ── Orders ──────────────────────────────────────────────
    public class OrderListVm
    {
        public int      Id            { get; set; }
        public string   OrderNumber   { get; set; } = string.Empty;
        public string?  CustomerName  { get; set; }
        public string?  CustomerPhone { get; set; }
        public decimal  Total         { get; set; }
        public string   Status        { get; set; } = string.Empty;
        public string?  PaymentMethod { get; set; }
        public string?  PaymentStatus { get; set; }
        public string?  SaleType      { get; set; }
        public DateTime CreatedDate   { get; set; }
        public string?  ItemsJson     { get; set; } // JSON array from SP
    }

    // ── Dashboard ────────────────────────────────────────────
    public class DashboardStatsVm
    {
        public decimal totalRevenue        { get; set; }
        public decimal totalRevenueGrowth  { get; set; }
        public int     totalOrders         { get; set; }
        public decimal totalOrdersGrowth   { get; set; }
        public int     totalProducts       { get; set; }
        public int     lowStockProducts    { get; set; }
        public int     outOfStockProducts  { get; set; }
        public int     totalCustomers      { get; set; }
        public int     pendingOrders       { get; set; }
        public int     processingOrders    { get; set; }
        public int     completedOrders     { get; set; }
        public int     cancelledOrders     { get; set; }
        public int     posOrders           { get; set; }
        public int     ecomOrders          { get; set; }
    }

    public class SalesChartVm
    {
        public string  date    { get; set; } = string.Empty;
        public string  label   { get; set; } = string.Empty;
        public decimal revenue { get; set; }
        public int     orders  { get; set; }
    }

    public class TopProductVm
    {
        public int     productId      { get; set; }
        public string  productName    { get; set; } = string.Empty;
        public string? imageUrl       { get; set; }
        public decimal price          { get; set; }
        public int     totalSold      { get; set; }
        public decimal totalRevenue   { get; set; }
    }

    public class RecentOrderVm
    {
        public int      Id            { get; set; }
        public string   OrderNumber   { get; set; } = string.Empty;
        public string?  CustomerName  { get; set; }
        public string?  CustomerPhone { get; set; }
        public decimal  Total         { get; set; }
        public string   Status        { get; set; } = string.Empty;
        public string?  PaymentMethod { get; set; }
        public string?  PaymentStatus { get; set; }
        public string?  SaleType      { get; set; }
        public DateTime CreatedDate   { get; set; }
        public DateTime createdAt     { get; set; } // alias for Angular interface
    }

    // ── Settings ────────────────────────────────────────────
    public class SettingVm
    {
        public int     companyId { get; set; }
        public string  key       { get; set; } = string.Empty;
        public string  value     { get; set; } = string.Empty;
        public string? groupName { get; set; }
    }

    // ── Inventory ────────────────────────────────────────────
    // Column names lower-cased to match sp_get_inventory_movements output.
    public class InventoryMovementVm
    {
        public int       id            { get; set; }
        public int       product_id    { get; set; }
        public string?   product_name  { get; set; }
        public string    movement_type { get; set; } = string.Empty;
        public int       quantity      { get; set; }
        public string?   reason        { get; set; }
        public decimal?  unit_cost     { get; set; }
        public string?   reference     { get; set; }
        public int?      stock_after   { get; set; }
        public DateTime  created_date  { get; set; }
    }

    // ── POS Product Lookup ───────────────────────────────────
    public class ProductLookupVm
    {
        public int     id            { get; set; }
        public string  name          { get; set; } = string.Empty;
        public string  barcode       { get; set; } = string.Empty;
        public string  sku           { get; set; } = string.Empty;
        public decimal price         { get; set; }
        public decimal wholesalePrice { get; set; }
        public int     stockQuantity { get; set; }
        public string? imageUrl      { get; set; }
        public string? categoryName  { get; set; }
    }
}
