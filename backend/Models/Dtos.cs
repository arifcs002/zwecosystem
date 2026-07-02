namespace Ecommerce.Api.Models
{
    // ── Products ─────────────────────────────────────────────
    public class StockAdjustDto { public int Delta { get; set; } }  // positive = add, negative = minus

    // ── Categories ───────────────────────────────────────────
    public class CategoryUpsertDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Sizes { get; set; }
        public int? ParentId { get; set; }
    }

    // ── Auth ────────────────────────────────────────────────
    public record LoginDto(string Email, string Password, string? LoginContext);
    public record LoginResponse(string Token, string SessionToken, string Email, string FullName, int? CompanyId, List<string> Roles, string? UserType);
    public record ForgotPasswordDto(string Email);
    public record ResetPasswordOtpDto(string Email, string Otp, string NewPassword);
    public record RegisterDto(string Email, string Password, string FirstName, string LastName, string? PhoneNumber, string? CompanyName, string? Subdomain);

    public class CompanyRegisterDto
    {
        public string CompanyName { get; set; } = string.Empty;
        public string Subdomain   { get; set; } = string.Empty;
        public string OwnerFirstName { get; set; } = string.Empty;
        public string OwnerLastName  { get; set; } = string.Empty;
        public string OwnerEmail  { get; set; } = string.Empty;
        public string OwnerPhone  { get; set; } = string.Empty;
        public string Password    { get; set; } = string.Empty;
        public string Address     { get; set; } = string.Empty;
        public string Division    { get; set; } = string.Empty;
        public string District    { get; set; } = string.Empty;
        public string Thana       { get; set; } = string.Empty;
    }

    // ── Users ───────────────────────────────────────────────
    public class CreateUserDto
    {
        public string Email       { get; set; } = string.Empty;
        public string Password    { get; set; } = string.Empty;
        public string FirstName   { get; set; } = string.Empty;
        public string LastName    { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role        { get; set; } = string.Empty;
        public int?   CompanyId   { get; set; }
        public bool   IsActive    { get; set; } = true;
    }

    public class UpdateUserDto
    {
        public string  Email       { get; set; } = string.Empty;
        public string  FirstName   { get; set; } = string.Empty;
        public string  LastName    { get; set; } = string.Empty;
        public string  PhoneNumber { get; set; } = string.Empty;
        public string  Role        { get; set; } = string.Empty;
        public int?    CompanyId   { get; set; }
        public bool    IsActive    { get; set; }
        public string? Password    { get; set; }
    }

    public record AdminResetPasswordDto(string NewPassword, string ConfirmPassword);

    // ── Products ────────────────────────────────────────────
    public record ProductCreateDto(
        string Name, string SKU, decimal Price, decimal WholesalePrice,
        int StockQuantity, string? Description, int? CategoryId, int? BrandId,
        string? Barcode, string? ImageUrl, int? PricingTagId = null,
        decimal? CompareAtPrice = null);

    public record SizeQtyDto(string Size, int Quantity);
    public record BatchProductCreateDto(
        string Name, decimal Price, decimal WholesalePrice,
        string? Description, int? CategoryId, int? SupplierId,
        string? ImageUrl, List<SizeQtyDto> Sizes, int? PricingTagId = null,
        decimal? CompareAtPrice = null);

    // ── Pricing Tags ────────────────────────────────────────
    public record PricingTagUpsertDto(
        string Name, decimal ProfitPercent, decimal? DiscountPercent,
        DateTime? PromoStartDate, DateTime? PromoEndDate, bool IsActive);

    // ── Orders / POS ────────────────────────────────────────
    public record OrderItemDto(int ProductId, int Quantity);
    public record POSCheckoutDto(
        List<OrderItemDto> Items, decimal Discount, string PaymentMethod,
        string? CustomerName, string? CustomerPhone, string? TransactionId);

    // Storefront guest checkout — no auth; tenant comes from the X-Tenant-ID header.
    public record PublicCheckoutDto(
        List<OrderItemDto> Items,
        string CustomerName, string CustomerPhone,
        string ShippingAddress, string? ShippingDistrict, string? ShippingThana,
        string? OrderNotes, string PaymentMethod);

    public class StatusUpdateDto
    {
        public string  Status { get; set; } = string.Empty;
        public string? Notes  { get; set; }
    }

    // ── Inventory ───────────────────────────────────────────
    // Manual stock correction. Positive delta = add, negative = remove.
    public record InventoryAdjustDto(int ProductId, int Delta, string? Reason);
    // Restock from a supplier — always adds stock.
    public record InventoryPurchaseDto(int ProductId, int Quantity, decimal? UnitCost, string? Supplier, string? Reason);

    // ── Payments ────────────────────────────────────────────
    public record MfsPaymentVerifyDto(
        int OrderId, string TransactionId, string Provider,
        decimal Amount, string SenderNumber, string? ReferenceLog);

    // ── Settings / Company ──────────────────────────────────
    public record CompanyUpdateDto(
        string Name, string? LogoUrl, string? BannerUrl,
        string? ContactEmail, string? ContactPhone,
        string? Address, decimal DeliveryCharge);

    // Value is intentionally nullable/optional here (unlike the CompanySetting
    // entity's [Required]) — clearing a setting means writing an empty value,
    // which is a legitimate request, not a validation error.
    public record CompanySettingDto(string Key, string? Value, string? GroupName);
}
