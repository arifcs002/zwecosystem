using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Ecommerce.Api.Domain;
using Ecommerce.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.AspNetCore.Hosting;

namespace Ecommerce.Api.Controllers
{
    // DTOs
    public record RegisterDto(string Email, string Password, string FirstName, string LastName, string? PhoneNumber, string? CompanyName, string? Subdomain);
    public class CompanyRegisterDto
    {
        public string CompanyName { get; set; } = string.Empty;
        public string Subdomain { get; set; } = string.Empty;
        public string OwnerFirstName { get; set; } = string.Empty;
        public string OwnerLastName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string OwnerPhone { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Division { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string Thana { get; set; } = string.Empty;
    }

    public class CreateUserDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateUserDto
    {
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public int? CompanyId { get; set; }
        public bool IsActive { get; set; }
        public string? Password { get; set; }
    }
    public record LoginDto(string Email, string Password, string? LoginContext);
    public record LoginResponse(string Token, string RefreshToken, string Email, string FullName, int? CompanyId, List<string> Roles, string? UserType);
    public record ProductCreateDto(string Name, string SKU, decimal Price, decimal WholesalePrice, int StockQuantity, string? Description, int? CategoryId, int? BrandId, string? Barcode, string? ImageUrl);
    public record SizeQtyDto(string Size, int Quantity);
    public record BatchProductCreateDto(string Name, decimal Price, decimal WholesalePrice, string? Description, int? CategoryId, int? SupplierId, string? ImageUrl, List<SizeQtyDto> Sizes);
    public record OrderItemDto(int ProductId, int Quantity);
    public record POSCheckoutDto(List<OrderItemDto> Items, decimal Discount, string PaymentMethod, string? CustomerName, string? CustomerPhone, string? TransactionId);
    public record CompanyUpdateDto(string Name, string? LogoUrl, string? BannerUrl, string? ContactEmail, string? ContactPhone, string? Address, decimal DeliveryCharge);
    public record MfsPaymentVerifyDto(int OrderId, string TransactionId, string Provider, decimal Amount, string SenderNumber, string? ReferenceLog);
    public record ForgotPasswordDto(string Email);
    public record ResetPasswordOtpDto(string Email, string Otp, string NewPassword);
    public record AdminResetPasswordDto(string NewPassword, string ConfirmPassword);

    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _config;
        private readonly IFileTextLogger _fileLogger;

        public AuthController(ApplicationDbContext context, IConfiguration config, IFileTextLogger fileLogger)
        {
            _context = context;
            _config = config;
            _fileLogger = fileLogger;
        }

        [HttpPost("register-company")]
        public async Task<IActionResult> RegisterCompany([FromBody] CompanyRegisterDto dto)
        {
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.OwnerEmail))
                return BadRequest(new { message = "Email already registered" });

            var subdomain = dto.Subdomain.ToLower().Replace(" ", "");
            if (await _context.Companies.AnyAsync(c => c.Subdomain == subdomain))
                return BadRequest(new { message = $"Subdomain '{subdomain}' is already taken." });

            var basicPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == "Basic Plan");
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "companyadmin");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var companyId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_register_company({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12})",
                dto.CompanyName, subdomain, dto.Address, dto.Division, dto.District, dto.Thana,
                dto.OwnerFirstName, dto.OwnerLastName, dto.OwnerEmail, dto.OwnerPhone, passwordHash,
                basicPlan?.Id ?? 0, role?.Id ?? 0
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { message = "Company registration successful. Awaiting Super Admin approval.", email = dto.OwnerEmail, companyId = companyId });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email already registered" });

            int? companyId = null;

            if (!string.IsNullOrEmpty(dto.CompanyName))
            {
                var subdomain = dto.Subdomain ?? dto.CompanyName.ToLower().Replace(" ", "");
                if (await _context.Companies.AnyAsync(c => c.Subdomain == subdomain))
                    return BadRequest(new { message = $"Subdomain '{subdomain}' is already taken." });

                var basicPlan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Name == "Basic Plan");
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "companyadmin");

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                companyId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_register_company({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12})",
                    dto.CompanyName, subdomain, "", "", "", "", "",
                    dto.FirstName, dto.LastName, dto.Email, dto.PhoneNumber ?? "", passwordHash,
                    basicPlan?.Id ?? 0, role?.Id ?? 0
                ).ToListAsync()).FirstOrDefault();

                return Ok(new { message = "Registration successful. Awaiting Super Admin approval.", email = dto.Email, companyId });
            }
            else
            {
                var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == "salesstaff");
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

                var userId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_create_user({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
                    null, dto.Email, passwordHash, dto.FirstName, dto.LastName,
                    dto.PhoneNumber ?? "", "salesstaff", true, role?.Id, null
                ).ToListAsync()).FirstOrDefault();

                return Ok(new { message = "Registration successful", email = dto.Email, companyId = (int?)null });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                var user = await _context.Users
                    .IgnoreQueryFilters()
                    .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                    .FirstOrDefaultAsync(u => u.Email == dto.Email);

                if (user == null)
                {
                    _fileLogger.LogError("LOGIN", $"User not found for email '{dto.Email}' with context '{dto.LoginContext}'");
                    return Unauthorized(new { message = "Invalid email or password" });
                }

                if (dto.LoginContext == "admin")
                {
                    var userRoles = user.UserRoles.Select(ur => ur.Role!.Name).ToList();
                    if (!string.Equals(user.UserType, "superadmin", StringComparison.OrdinalIgnoreCase) &&
                        !userRoles.Contains("superadmin"))
                    {
                        _fileLogger.LogError("LOGIN", $"Admin login denied for '{dto.Email}' due to role mismatch. UserType='{user.UserType}', Roles='{string.Join(",", userRoles)}'");
                        return Unauthorized(new { message = "Only platform administrators can log in here." });
                    }
                }
                else if (!string.IsNullOrEmpty(dto.LoginContext))
                {
                    var company = await _context.Companies
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Subdomain, dto.LoginContext));
                    
                    if (company == null || user.CompanyId != company.Id)
                    {
                        _fileLogger.LogError("LOGIN", $"Company login denied for '{dto.Email}'. Context='{dto.LoginContext}', UserCompanyId='{user.CompanyId}', MatchedCompanyId='{company?.Id}'");
                        return Unauthorized(new { message = "You do not have access to this store dashboard." });
                    }
                }

                bool passwordMatch = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);

                if (!passwordMatch)
                {
                    _fileLogger.LogError("LOGIN", $"Password mismatch for '{dto.Email}' with context '{dto.LoginContext}'");
                    return Unauthorized(new { message = "Invalid email or password" });
                }

                if (!user.IsActive)
                {
                    _fileLogger.LogError("LOGIN", $"Inactive account blocked for '{dto.Email}'");
                    return BadRequest(new { message = "User account is suspended" });
                }

                var roles = user.UserRoles.Select(ur => ur.Role!.Name).ToList();
                var token = GenerateJwtToken(user, roles);
                var refreshToken = Guid.NewGuid().ToString();

                _fileLogger.LogInfo("LOGIN", $"Login success for '{dto.Email}' with context '{dto.LoginContext}'");
                return Ok(new LoginResponse(token, refreshToken, user.Email, $"{user.FirstName} {user.LastName}", user.CompanyId, roles, user.UserType));
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("LOGIN", $"Unhandled login exception for '{dto.Email}'", ex);
                throw;
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var user = await _context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return BadRequest(new { message = "User with this email does not exist." });

            var rand = new Random();
            var otpCode = rand.Next(100000, 999999).ToString();

            user.Otp = otpCode;
            user.OtpExpiresAt = DateTime.UtcNow.AddMinutes(15);

            await _context.SaveChangesAsync();

            // Log to console (always keep this for easy local sandbox debugging/autofilling)
            Console.WriteLine($"[OTP SIMULATOR] Generated OTP {otpCode} for email {dto.Email}");

            // Dispatch actual SMTP email
            await SendOtpEmailAsync(dto.Email, otpCode);

            return Ok(new { message = "Verification OTP code successfully generated and sent to your email." });
        }

        private async Task SendOtpEmailAsync(string toEmail, string otpCode)
        {
            try
            {
                var smtpHost = _config["SmtpSettings:Host"] ?? "smtp.gmail.com";
                var smtpPortStr = _config["SmtpSettings:Port"] ?? "587";
                int.TryParse(smtpPortStr, out var smtpPort);
                var enableSsl = bool.Parse(_config["SmtpSettings:EnableSsl"] ?? "true");
                var username = _config["SmtpSettings:Username"] ?? "";
                var password = _config["SmtpSettings:Password"] ?? "";
                var fromAddress = _config["SmtpSettings:FromAddress"] ?? "noreply@ghorerbazar.com";
                var fromName = _config["SmtpSettings:FromName"] ?? "Ghorer Bazar Organic";

                using (var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort))
                {
                    client.EnableSsl = enableSsl;
                    client.UseDefaultCredentials = false;
                    
                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        client.Credentials = new System.Net.NetworkCredential(username, password);
                    }

                    var mailMessage = new System.Net.Mail.MailMessage
                    {
                        From = new System.Net.Mail.MailAddress(fromAddress, fromName),
                        Subject = "Verification Code for Password Recovery - Ghorer Bazar",
                        Body = $@"
                        <div style='font-family: Arial, sans-serif; padding: 20px; color: #333; max-width: 600px; margin: auto; border: 1px solid #e2e8f0; border-radius: 12px;'>
                            <div style='background-color: #0D5237; padding: 15px; text-align: center; border-radius: 8px 8px 0 0;'>
                                <h2 style='color: #ffffff; margin: 0;'>Ghorer Bazar Organic</h2>
                            </div>
                            <div style='padding: 20px;'>
                                <p>Hello,</p>
                                <p>We received a request to recover your password. Please use the following 6-digit verification code (OTP) to reset your password:</p>
                                <div style='text-align: center; margin: 30px 0;'>
                                    <span style='background-color: #f8fafc; border: 2px dashed #0D5237; color: #0D5237; font-size: 28px; font-weight: bold; padding: 10px 24px; font-family: monospace; letter-spacing: 4px; border-radius: 6px; display: inline-block;'>{otpCode}</span>
                                </div>
                                <p>This code will expire in 15 minutes. If you did not request this, please ignore this email.</p>
                            </div>
                            <div style='border-top: 1px solid #e2e8f0; padding-top: 15px; font-size: 11px; color: #64748b; text-align: center;'>
                                <p>Eat pure, stay healthy.</p>
                                <p>&copy; {DateTime.UtcNow.Year} Ghorer Bazar Organic Store. All rights reserved.</p>
                            </div>
                        </div>",
                        IsBodyHtml = true
                    };

                    mailMessage.To.Add(toEmail);

                    await client.SendMailAsync(mailMessage);
                    Console.WriteLine($"--> SMTP: Successfully sent OTP email to {toEmail}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"--> SMTP ERROR: Failed to send email to {toEmail}: {ex.Message}");
            }
        }

        [HttpPost("reset-password-otp")]
        public async Task<IActionResult> ResetPasswordOtp([FromBody] ResetPasswordOtpDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null)
                return BadRequest(new { message = "User with this email does not exist." });

            if (string.IsNullOrEmpty(user.Otp) || user.Otp != dto.Otp || !user.OtpExpiresAt.HasValue || user.OtpExpiresAt.Value < DateTime.UtcNow)
                return BadRequest(new { message = "Invalid or expired OTP verification code." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.Otp = null;
            user.OtpExpiresAt = null;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Password reset completed successfully. You can now login with your new password." });
        }

        private string GenerateJwtToken(User user, List<string> roles)
        {
            var keyStr = _config["Jwt:Secret"] ?? "super-secret-key-change-in-prod-long-enough-32-chars";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyStr));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", $"{user.FirstName} {user.LastName}")
            };

            if (user.CompanyId.HasValue)
            {
                claims.Add(new Claim("company_id", user.CompanyId.Value.ToString()));
            }

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddHours(4),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ProductsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetProducts([FromQuery] string? search)
        {
            var query = _context.Products
                .IgnoreQueryFilters()
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Supplier)
                .AsQueryable();

            if (_context.CompanyId.HasValue)
            {
                query = query.Where(p => p.CompanyId == _context.CompanyId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(term) ||
                    p.Sku.ToLower().Contains(term) ||
                    p.Barcode.ToLower().Contains(term));
            }

            var items = await query
                .OrderByDescending(p => p.CreatedDate)
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProduct(int id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (product == null) return NotFound();
            return Ok(product);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {
            // Resolve Tenant Context from DbContext (populated by X-Tenant-ID header or token)
            var currentCompanyId = _context.CompanyId;
            if (!currentCompanyId.HasValue)
                return BadRequest("Company context is required to add products.");

            // Auto-generate Code 128 barcode if not provided
            var barcode = dto.Barcode;
            if (string.IsNullOrEmpty(barcode))
            {
                barcode = GenerateCode128Barcode(currentCompanyId.Value);
            }

            // Verify barcode unique per company
            if (await _context.Products.AnyAsync(p => p.CompanyId == currentCompanyId.Value && p.Barcode == barcode))
            {
                return BadRequest(new { message = $"Barcode '{barcode}' is already registered in your company." });
            }

            var slug = dto.Name.ToLower().Replace(" ", "-").Replace("/", "-");

            var productId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_product({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15})",
                currentCompanyId.Value, dto.Name, slug, dto.SKU, barcode, dto.Description ?? "",
                dto.Price, dto.WholesalePrice, dto.StockQuantity, dto.ImageUrl ?? "", dto.CategoryId,
                dto.BrandId, "PUBLISHED", "", (int?)null, _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            var product = await _context.Products.FindAsync(productId);
            return CreatedAtAction(nameof(GetProduct), new { id = productId }, product);
        }

        [HttpPost("batch")]
        public async Task<IActionResult> CreateProductsBatch([FromBody] BatchProductCreateDto dto)
        {
            var currentCompanyId = _context.CompanyId;
            if (!currentCompanyId.HasValue)
                return BadRequest("Company context is required to add products.");

            var createdProducts = new List<Product>();

            foreach (var sizeQty in dto.Sizes)
            {
                if (sizeQty.Quantity <= 0) continue;

                var barcode = GenerateCode128Barcode(currentCompanyId.Value);

                // Ensure unique barcode
                while (await _context.Products.AnyAsync(p => p.CompanyId == currentCompanyId.Value && p.Barcode == barcode))
                {
                    barcode = GenerateCode128Barcode(currentCompanyId.Value);
                }

                var cleanName = dto.Name;
                var slugName = $"{dto.Name.ToLower().Replace(" ", "-").Replace("/", "-")}-{sizeQty.Size.ToLower()}";
                var sku = $"{dto.Name.Replace(" ", "-").ToUpper()}-{sizeQty.Size.ToUpper()}";

                var productId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_create_product({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15})",
                    currentCompanyId.Value, $"{cleanName} (Size {sizeQty.Size})", slugName, sku, barcode,
                    dto.Description ?? "", dto.Price, dto.WholesalePrice, sizeQty.Quantity, dto.ImageUrl ?? "",
                    dto.CategoryId, (int?)null, "PUBLISHED", sizeQty.Size, dto.SupplierId, _context.CurrentUserId
                ).ToListAsync()).FirstOrDefault();

                var product = await _context.Products.FindAsync(productId);
                if (product != null) createdProducts.Add(product);
            }

            return Ok(createdProducts);
        }

        [HttpPost("{id}/print-barcode")]
        public async Task<IActionResult> PrintBarcode(int id, [FromQuery] string ipAddress = "192.168.1.100", [FromQuery] int port = 9100)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            // Mock Zebra Programming Language (ZPL) label format for printing barcodes
            var zpl = $@"
^XA
^LH30,30
^FO20,10^A0N,28,24^FD{product.Name}^FS
^FO20,40^A0N,20,16^FDSKU: {product.Sku}  Price: {product.Price} BDT^FS
^FO20,70^BY2
^BCN,50,Y,N,N
^FD{product.Barcode}^FS
^XZ";

            // Print service protocol log simulation
            Console.WriteLine($"--> PRINT SERVICE: Sending raw socket stream to Wi-Fi printer at {ipAddress}:{port}");
            Console.WriteLine(zpl);

            return Ok(new
            {
                message = $"Barcode print command successfully sent to printer at {ipAddress}:{port}",
                zpl = zpl.Trim(),
                ipAddress,
                port
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] ProductCreateDto dto)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_product({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12})",
                id, dto.Name, dto.SKU, dto.Price, dto.WholesalePrice, dto.StockQuantity, dto.Description ?? "",
                dto.ImageUrl ?? "", dto.CategoryId, dto.BrandId, "", (int?)null, _context.CurrentUserId);

            var product = await _context.Products.FindAsync(id);
            return Ok(product);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            if (!await _context.Products.AnyAsync(p => p.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_product({0})", id);
            return NoContent();
        }

        private string GenerateCode128Barcode(int companyId)
        {
            var prefix = "ZW";
            var rand = new Random();
            var number = rand.Next(100000, 999999);
            return $"{prefix}-{number}";
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class POSController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public POSController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("lookup")]
        public async Task<IActionResult> LookupProduct([FromQuery] string barcode)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .FirstOrDefaultAsync(p => p.Barcode == barcode);

            if (product == null)
                return NotFound(new { message = "Product not found for the scanned barcode" });

            return Ok(product);
        }

        [HttpPost("checkout")]
        public async Task<IActionResult> POSCheckout([FromBody] POSCheckoutDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required to complete sales.");

            // Resolve cashier user (claims)
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int? cashierId = null;
            int parsedId = 0;
            if (int.TryParse(userIdClaim, out parsedId))
            {
                cashierId = parsedId;
            }

            decimal subtotal = 0;
            var productIds = new List<int>();
            var quantities = new List<int>();
            var prices = new List<decimal>();

            foreach (var itemDto in dto.Items)
            {
                var product = await _context.Products.FindAsync(itemDto.ProductId);
                if (product == null)
                    return BadRequest(new { message = $"Product with ID {itemDto.ProductId} not found." });

                if (product.StockQuantity < itemDto.Quantity)
                    return BadRequest(new { message = $"Insufficient stock for product '{product.Name}'. Available: {product.StockQuantity}" });

                subtotal += product.Price * itemDto.Quantity;
                productIds.Add(itemDto.ProductId);
                quantities.Add(itemDto.Quantity);
                prices.Add(product.Price);
            }

            var total = subtotal - dto.Discount;
            if (total < 0) total = 0;

            var orderNumber = $"POS-{DateTime.UtcNow.Ticks}";
            var paymentStatus = dto.PaymentMethod == "CASH" ? "PAID" : "PENDING";

            // Call sp_checkout_order
            var orderId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_checkout_order({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}, {15})",
                companyId.Value, orderNumber, "POS", cashierId, dto.CustomerName ?? "Walk-in Customer",
                dto.CustomerPhone ?? "", "COMPLETED", subtotal, dto.Discount, total, dto.PaymentMethod, paymentStatus,
                productIds.ToArray(), quantities.ToArray(), prices.ToArray(), parsedId
            ).ToListAsync()).FirstOrDefault();

            if (dto.PaymentMethod != "CASH" && !string.IsNullOrEmpty(dto.TransactionId))
            {
                var paymentId = (await _context.Database.SqlQueryRaw<int>(
                    "SELECT sp_verify_payment({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
                    companyId.Value, orderId, dto.TransactionId, dto.PaymentMethod, total, "SUCCESS", "AUTOMATED", "", ""
                ).ToListAsync()).FirstOrDefault();
            }

            var settings = await _context.CompanySettings
                .Where(s => s.CompanyId == companyId.Value && s.GroupName == "POS")
                .ToDictionaryAsync(s => s.Key, s => s.Value);

            var header = settings.ContainsKey("receipt_header") ? settings["receipt_header"] : "Thank you!";
            var footer = settings.ContainsKey("receipt_footer") ? settings["receipt_footer"] : "Please visit again.";

            return Ok(new
            {
                message = "POS Transaction completed successfully",
                orderId = orderId,
                orderNumber = orderNumber,
                receipt = new
                {
                    header,
                    footer,
                    orderNumber = orderNumber,
                    subtotal = subtotal,
                    discount = dto.Discount,
                    total = total,
                    paymentMethod = dto.PaymentMethod,
                    cashierName = User.FindFirst("name")?.Value ?? "Cashier"
                }
            });
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PaymentsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("mfs-verify")]
        public async Task<IActionResult> VerifyMfsPayment([FromBody] MfsPaymentVerifyDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required.");

            if (!await _context.Orders.AnyAsync(o => o.Id == dto.OrderId))
                return NotFound("Order not found");

            var referenceLog = dto.ReferenceLog ?? $"Manual MFS Verification for TrxID {dto.TransactionId}";

            var paymentId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_verify_payment({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
                companyId.Value, dto.OrderId, dto.TransactionId, dto.Provider.ToUpper(),
                dto.Amount, "SUCCESS", "MANUAL", dto.SenderNumber, referenceLog
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { message = "MFS Transaction successfully logged and verified", status = "SUCCESS", paymentId = paymentId });
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required.");

            var settings = await _context.CompanySettings
                .Where(s => s.CompanyId == companyId.Value)
                .ToListAsync();

            return Ok(settings);
        }

        [HttpPut]
        public async Task<IActionResult> UpdateSettings([FromBody] List<CompanySetting> settingsList)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required.");

            foreach (var setting in settingsList)
            {
                var existing = await _context.CompanySettings
                    .FirstOrDefaultAsync(s => s.CompanyId == companyId.Value && s.Key == setting.Key);

                if (existing != null)
                {
                    existing.Value = setting.Value;
                    existing.GroupName = setting.GroupName;
                }
                else
                {
                    setting.CompanyId = companyId.Value;
                    _context.CompanySettings.Add(setting);
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Settings updated successfully" });
        }

        [HttpPut("company-profile")]
        public async Task<IActionResult> UpdateCompanyProfile([FromBody] CompanyUpdateDto dto)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required.");

            var company = await _context.Companies.FindAsync(companyId.Value);
            if (company == null) return NotFound();

            company.Name = dto.Name;
            company.LogoUrl = dto.LogoUrl;
            company.BannerUrl = dto.BannerUrl;
            company.ContactEmail = dto.ContactEmail;
            company.ContactPhone = dto.ContactPhone;
            company.Address = dto.Address;
            company.DeliveryCharge = dto.DeliveryCharge;
            company.UpdatedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(company);
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId = _context.CompanyId;

            var query = _context.Users
                .IgnoreQueryFilters()
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .AsQueryable();

            if (!isSuperAdmin && companyId.HasValue)
            {
                query = query.Where(u => u.CompanyId == companyId);
            }

            var rawUsers = await query
                .OrderByDescending(u => u.CreatedDate)
                .ToListAsync();

            var list = rawUsers.Select(u => new {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.IsActive,
                CompanyId = u.CompanyId,
                Roles = u.UserRoles.Select(ur => ur.Role!.Name).ToList()
            }).ToList();

            return Ok(list);
        }

        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _context.Roles
                .Select(r => new { r.Id, r.Name, r.Value })
                .ToListAsync();
            return Ok(roles);
        }

        [HttpPost("{userId}/admin-reset-password")]
        public async Task<IActionResult> AdminResetPassword(int userId, [FromBody] AdminResetPasswordDto dto)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var isCompanyAdmin = User.IsInRole("companyadmin");

            if (!isSuperAdmin && !isCompanyAdmin)
                return Forbid();

            var companyId = _context.CompanyId;

            var targetUser = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId)
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (targetUser == null)
                return NotFound("User not found");

            if (!isSuperAdmin)
            {
                if (targetUser.CompanyId != companyId)
                    return Forbid();
            }

            if (dto.NewPassword != dto.ConfirmPassword)
                return BadRequest(new { message = "New password and confirm password do not match." });

            targetUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Password for user {targetUser.Email} updated successfully by Administrator." });
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId = _context.CompanyId;

            if (!isSuperAdmin)
            {
                if (!companyId.HasValue) return Forbid();
                if (dto.CompanyId != companyId.Value) return Forbid();
            }

            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.Email))
                return BadRequest(new { message = "Email already exists" });

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var userId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_user({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9})",
                dto.CompanyId, dto.Email, passwordHash, dto.FirstName, dto.LastName,
                dto.PhoneNumber, dto.Role, dto.IsActive, role?.Id, _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            return Ok(new { Id = userId, dto.Email, dto.FirstName, dto.LastName, dto.IsActive, dto.CompanyId });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserDto dto)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId = _context.CompanyId;

            var targetUser = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id)
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (targetUser == null) return NotFound("User not found");

            if (!isSuperAdmin && targetUser.CompanyId != companyId)
                return Forbid();

            var passwordHash = !string.IsNullOrEmpty(dto.Password) ? BCrypt.Net.BCrypt.HashPassword(dto.Password) : null;
            var targetCompanyId = isSuperAdmin ? dto.CompanyId : targetUser.CompanyId;
            var newRole = await _context.Roles.FirstOrDefaultAsync(r => r.Name == dto.Role);

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_user({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})",
                id, dto.Email, dto.FirstName, dto.LastName, dto.PhoneNumber, dto.IsActive,
                dto.Role, passwordHash, targetCompanyId, newRole?.Id, _context.CurrentUserId);

            return Ok(new { Id = id, dto.Email, dto.FirstName, dto.LastName, dto.IsActive, CompanyId = targetCompanyId });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            var companyId = _context.CompanyId;

            var targetUser = isSuperAdmin
                ? await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id)
                : await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

            if (targetUser == null) return NotFound("User not found");

            if (string.Equals(targetUser.Email, "arif", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "System user arif cannot be deleted." });

            if (!isSuperAdmin && targetUser.CompanyId != companyId)
                return Forbid();

            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_user({0}, {1})", id, _context.CurrentUserId);

            return Ok(new { message = "User deleted successfully" });
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class UploadController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        public UploadController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            var uploadsFolder = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var fileUrl = "/uploads/" + uniqueFileName;
            return Ok(new { imageUrl = fileUrl });
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class SuppliersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SuppliersController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetSuppliers()
        {
            var companyId = _context.CompanyId;
            var query = _context.Suppliers
                .IgnoreQueryFilters()
                .AsQueryable();

            if (companyId.HasValue)
                query = query.Where(s => s.CompanyId == companyId.Value);

            var suppliers = await query
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => new
                {
                    id = s.Id,
                    name = s.Name,
                    address = s.Address,
                    phoneNumber = s.PhoneNumber,
                    createdAt = s.CreatedDate
                })
                .ToListAsync();
            return Ok(suppliers);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSupplier([FromBody] Supplier supplier)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required.");

            var supplierId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_supplier({0}, {1}, {2}, {3}, {4})",
                companyId.Value, supplier.Name, supplier.PhoneNumber ?? "", supplier.Address ?? "", _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            var created = await _context.Suppliers.FindAsync(supplierId);
            return Ok(created);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSupplier(int id)
        {
            if (!await _context.Suppliers.AnyAsync(s => s.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_supplier({0}, {1})", id, _context.CurrentUserId);
            return NoContent();
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CategoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var companyId = _context.CompanyId;
            var query = _context.Categories
                .IgnoreQueryFilters()
                .AsQueryable();

            if (companyId.HasValue)
                query = query.Where(c => c.CompanyId == companyId.Value);

            var categories = await query
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    slug = c.Slug,
                    description = c.Description,
                    parentId = c.ParentId,
                    sizes = c.Sizes,
                    createdAt = c.CreatedDate
                })
                .ToListAsync();
            return Ok(categories);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCategory([FromBody] Category category)
        {
            var companyId = _context.CompanyId;
            if (!companyId.HasValue)
                return BadRequest("Company context is required.");

            var slug = category.Name.ToLower().Replace(" ", "-").Replace("/", "-");

            var categoryId = (await _context.Database.SqlQueryRaw<int>(
                "SELECT sp_create_category({0}, {1}, {2}, {3}, {4}, {5})",
                companyId.Value, category.Name, slug, category.Description ?? "", category.Sizes ?? "", _context.CurrentUserId
            ).ToListAsync()).FirstOrDefault();

            var created = await _context.Categories.FindAsync(categoryId);
            return Ok(created);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync("CALL sp_delete_category({0}, {1})", id, _context.CurrentUserId);
            return NoContent();
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class CompaniesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CompaniesController(ApplicationDbContext context)
        {
            _context = context;
        }

        internal static string? NormalizeStringForColumn(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim();
            return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
        }

        internal static string NormalizeRequiredStringForColumn(string? value, int maxLength)
        {
            var normalized = value?.Trim() ?? string.Empty;
            return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
        }

        private static void NormalizeCompanyPayload(Company company)
        {
            company.Name = NormalizeRequiredStringForColumn(company.Name, 255);
            company.Subdomain = NormalizeRequiredStringForColumn(company.Subdomain, 100).ToLowerInvariant();
            company.ContactEmail = NormalizeStringForColumn(company.ContactEmail, 255);
            company.ContactPhone = NormalizeStringForColumn(company.ContactPhone, 50);
            company.OwnerName = NormalizeStringForColumn(company.OwnerName, 255);
            company.OwnerMobile = NormalizeStringForColumn(company.OwnerMobile, 50);
            company.CompanyMobile = NormalizeStringForColumn(company.CompanyMobile, 50);
            company.Division = NormalizeStringForColumn(company.Division, 100);
            company.District = NormalizeStringForColumn(company.District, 100);
            company.Thana = NormalizeStringForColumn(company.Thana, 100);
            company.Address = string.IsNullOrWhiteSpace(company.Address) ? null : company.Address.Trim();
            company.FacebookLink = NormalizeStringForColumn(company.FacebookLink, 500);
            company.InstagramLink = NormalizeStringForColumn(company.InstagramLink, 500);
            company.BkashNumber = NormalizeStringForColumn(company.BkashNumber, 50);
            company.NagadNumber = NormalizeStringForColumn(company.NagadNumber, 50);
            company.BankName = NormalizeStringForColumn(company.BankName, 255);
            company.BankAccountName = NormalizeStringForColumn(company.BankAccountName, 255);
            company.LogoUrl = NormalizeStringForColumn(company.LogoUrl, 500);
            company.BannerUrl = NormalizeStringForColumn(company.BannerUrl, 500);
            company.ApprovalStatus = NormalizeStringForColumn(company.ApprovalStatus, 50) ?? "Pending";
        }

        [HttpGet]
        public async Task<IActionResult> GetCompanies()
        {
            var companies = await _context.Companies
                .IgnoreQueryFilters()
                .Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    subdomain = c.Subdomain,
                    logoUrl = c.LogoUrl,
                    isActive = c.IsActive,
                    approvalStatus = c.ApprovalStatus,
                    createdAt = c.CreatedDate
                })
                .ToListAsync();
            return Ok(companies);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCompany(int id)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();
            return Ok(company);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCompany([FromBody] Company company)
        {
            if (company == null)
                return BadRequest(new { message = "Company payload is required." });

            NormalizeCompanyPayload(company);

            if (string.IsNullOrWhiteSpace(company.Name))
                return BadRequest(new { message = "Company name is required." });

            if (string.IsNullOrWhiteSpace(company.Subdomain))
                return BadRequest(new { message = "Company subdomain is required." });

            if (await _context.Companies.AnyAsync(c => c.Subdomain == company.Subdomain))
                return Conflict(new { message = $"Subdomain '{company.Subdomain}' is already taken." });

            company.CreatedDate = DateTime.UtcNow;
            company.UpdatedDate = DateTime.UtcNow;
            company.IsActive = true;
            if (string.IsNullOrWhiteSpace(company.ApprovalStatus))
                company.ApprovalStatus = "Pending";

            _context.Companies.Add(company);

            try
            {
                await _context.SaveChangesAsync();
                return Ok(company);
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"[COMPANY CREATE ERROR] {ex.GetBaseException().Message}");
                return BadRequest(new
                {
                    message = "Failed to create company.",
                    detail = ex.GetBaseException().Message
                });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCompany(int id, [FromBody] Company updateDto)
        {
            if (!await _context.Companies.AnyAsync(c => c.Id == id)) return NotFound();

            var targetCompany = await _context.Companies.FindAsync(id);
            var isSuperAdmin = User.IsInRole("superadmin");

            if (targetCompany != null && string.Equals(targetCompany.Subdomain, "zw", StringComparison.OrdinalIgnoreCase))
            {
                if (!isSuperAdmin)
                {
                    return Forbid();
                }
            }
            if (targetCompany == null)
                return NotFound();

            NormalizeCompanyPayload(updateDto);

            var normalizedSubdomain = updateDto.Subdomain ?? string.Empty;
            if (string.IsNullOrWhiteSpace(updateDto.Name))
                return BadRequest(new { message = "Company name is required." });

            if (string.IsNullOrWhiteSpace(normalizedSubdomain))
                return BadRequest(new { message = "Company subdomain is required." });

            if (await _context.Companies.AnyAsync(c => c.Id != id && c.Subdomain == normalizedSubdomain))
                return Conflict(new { message = $"Subdomain '{normalizedSubdomain}' is already taken." });

            targetCompany.Name = updateDto.Name;
            targetCompany.Subdomain = normalizedSubdomain;
            targetCompany.ContactEmail = updateDto.ContactEmail;
            targetCompany.ContactPhone = updateDto.ContactPhone;
            targetCompany.OwnerName = updateDto.OwnerName;
            targetCompany.OwnerMobile = updateDto.OwnerMobile;
            targetCompany.CompanyMobile = updateDto.CompanyMobile;
            targetCompany.Division = updateDto.Division;
            targetCompany.District = updateDto.District;
            targetCompany.Thana = updateDto.Thana;
            targetCompany.Address = updateDto.Address;
            targetCompany.FacebookLink = updateDto.FacebookLink;
            targetCompany.InstagramLink = updateDto.InstagramLink;
            targetCompany.BkashNumber = updateDto.BkashNumber;
            targetCompany.NagadNumber = updateDto.NagadNumber;
            targetCompany.BankName = updateDto.BankName;
            targetCompany.BankAccountName = updateDto.BankAccountName;
            targetCompany.IsActive = updateDto.IsActive;
            targetCompany.ApprovalStatus = string.IsNullOrWhiteSpace(updateDto.ApprovalStatus) ? targetCompany.ApprovalStatus : updateDto.ApprovalStatus;
            targetCompany.LogoUrl = string.IsNullOrWhiteSpace(updateDto.LogoUrl) ? targetCompany.LogoUrl : updateDto.LogoUrl;
            targetCompany.UpdatedDate = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
                return Ok(targetCompany);
            }
            catch (DbUpdateException ex)
            {
                Console.WriteLine($"[COMPANY UPDATE ERROR] {ex.GetBaseException().Message}");
                return BadRequest(new
                {
                    message = "Failed to update company.",
                    detail = ex.GetBaseException().Message
                });
            }
        }
    }

    // ============================================================
    // DASHBOARD CONTROLLER
    // ============================================================
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// GET /api/dashboard/stats
        /// Returns KPI stats for a company. Super Admin can pass ?companyId=xxx to view any company.
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats([FromQuery] int? companyId)
        {
            var resolvedCompanyId = ResolveCompanyId(companyId);
            if (resolvedCompanyId == null)
                return Ok(new
                {
                    totalRevenue = 0.0,
                    totalRevenueGrowth = 0.0,
                    totalOrders = 0,
                    totalOrdersGrowth = 0.0,
                    totalProducts = 0,
                    lowStockProducts = 0,
                    outOfStockProducts = 0,
                    totalCustomers = 0,
                    pendingOrders = 0,
                    processingOrders = 0,
                    completedOrders = 0,
                    cancelledOrders = 0,
                    posOrders = 0,
                    ecomOrders = 0
                });

            var now = DateTime.UtcNow;
            var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var startOfLastMonth = startOfMonth.AddMonths(-1);

            var orders = await _context.Orders
                .Where(o => o.CompanyId == resolvedCompanyId)
                .ToListAsync();

            var products = await _context.Products
                .Where(p => p.CompanyId == resolvedCompanyId)
                .ToListAsync();

            var customers = await _context.Users
                .Where(u => u.CompanyId == resolvedCompanyId)
                .CountAsync();

            // This month
            var thisMonthOrders = orders.Where(o => o.CreatedDate >= startOfMonth).ToList();
            var lastMonthOrders = orders.Where(o => o.CreatedDate >= startOfLastMonth && o.CreatedDate < startOfMonth).ToList();

            var thisMonthRevenue = thisMonthOrders
                .Where(o => o.Status != "CANCELLED")
                .Sum(o => o.Total);

            var lastMonthRevenue = lastMonthOrders
                .Where(o => o.Status != "CANCELLED")
                .Sum(o => o.Total);

            var revenueGrowth = lastMonthRevenue == 0 ? 100.0 :
                Math.Round(((double)(thisMonthRevenue - lastMonthRevenue) / (double)lastMonthRevenue) * 100, 1);

            var ordersGrowth = lastMonthOrders.Count == 0 ? 100.0 :
                Math.Round(((double)(thisMonthOrders.Count - lastMonthOrders.Count) / (double)lastMonthOrders.Count) * 100, 1);

            var lowStockCount = products.Count(p => p.StockQuantity > 0 && p.StockQuantity <= 10);
            var outOfStockCount = products.Count(p => p.StockQuantity == 0);

            return Ok(new
            {
                totalRevenue = Math.Round(thisMonthRevenue, 2),
                totalRevenueGrowth = revenueGrowth,
                totalOrders = thisMonthOrders.Count,
                totalOrdersGrowth = ordersGrowth,
                totalProducts = products.Count,
                lowStockProducts = lowStockCount,
                outOfStockProducts = outOfStockCount,
                totalCustomers = customers,
                pendingOrders = orders.Count(o => o.Status == "PENDING"),
                processingOrders = orders.Count(o => o.Status == "PROCESSING"),
                completedOrders = orders.Count(o => o.Status == "COMPLETED"),
                cancelledOrders = orders.Count(o => o.Status == "CANCELLED"),
                posOrders = orders.Count(o => o.SaleType == "POS"),
                ecomOrders = orders.Count(o => o.SaleType == "ECOMMERCE"),
            });
        }

        /// <summary>
        /// GET /api/dashboard/sales-chart
        /// Returns last 7 days daily sales totals
        /// </summary>
        [HttpGet("sales-chart")]
        public async Task<IActionResult> GetSalesChart([FromQuery] int? companyId, [FromQuery] int days = 7)
        {
            var resolvedCompanyId = ResolveCompanyId(companyId);
            if (resolvedCompanyId == null)
                return Ok(new List<object>());

            var from = DateTime.UtcNow.Date.AddDays(-(days - 1));

            var orders = await _context.Orders
                 .Where(o => o.CompanyId == resolvedCompanyId && o.CreatedDate >= from && o.Status != "CANCELLED")
                 .ToListAsync();

            var chart = Enumerable.Range(0, days).Select(i =>
            {
                var day = from.AddDays(i);
                var dayOrders = orders.Where(o => o.CreatedDate.Date == day).ToList();
                return new
                {
                    date = day.ToString("MM/dd"),
                    label = day.ToString("ddd"),
                    revenue = Math.Round(dayOrders.Sum(o => o.Total), 2),
                    orders = dayOrders.Count
                };
            }).ToList();

            return Ok(chart);
        }

        /// <summary>
        /// GET /api/dashboard/top-products
        /// Returns top 5 selling products
        /// </summary>
        [HttpGet("top-products")]
        public async Task<IActionResult> GetTopProducts([FromQuery] int? companyId, [FromQuery] int limit = 5)
        {
            var resolvedCompanyId = ResolveCompanyId(companyId);
            if (resolvedCompanyId == null)
                return Ok(new List<object>());

            var topProducts = await _context.OrderItems
                .Include(oi => oi.Product)
                .Include(oi => oi.Order)
                .Where(oi => oi.Order!.CompanyId == resolvedCompanyId && oi.Order.Status != "CANCELLED")
                .GroupBy(oi => new { oi.ProductId, oi.Product!.Name, oi.Product.ImageUrl, oi.Product.Price })
                .Select(g => new
                {
                    productId = g.Key.ProductId,
                    productName = g.Key.Name,
                    imageUrl = g.Key.ImageUrl,
                    price = g.Key.Price,
                    totalSold = g.Sum(x => x.Quantity),
                    totalRevenue = Math.Round(g.Sum(x => x.TotalPrice), 2)
                })
                .OrderByDescending(x => x.totalSold)
                .Take(limit)
                .ToListAsync();

            return Ok(topProducts);
        }

        /// <summary>
        /// GET /api/dashboard/recent-orders
        /// Returns last 10 orders
        /// </summary>
        [HttpGet("recent-orders")]
        public async Task<IActionResult> GetRecentOrders([FromQuery] int? companyId, [FromQuery] int limit = 10)
        {
            var resolvedCompanyId = ResolveCompanyId(companyId);
            if (resolvedCompanyId == null)
                return Ok(new List<object>());

            var orders = await _context.Orders
                .Where(o => o.CompanyId == resolvedCompanyId)
                .OrderByDescending(o => o.CreatedDate)
                .Take(limit)
                .Select(o => new
                {
                    o.Id,
                    o.OrderNumber,
                    o.CustomerName,
                    o.CustomerPhone,
                    o.Total,
                    o.Status,
                    o.PaymentMethod,
                    o.PaymentStatus,
                    o.SaleType,
                    o.CreatedDate
                })
                .ToListAsync();

            return Ok(orders);
        }

        // Resolves companyId: company users get their own; super admin passes via query
        private int? ResolveCompanyId(int? queryCompanyId)
        {
            var isSuperAdmin = User.IsInRole("superadmin");
            if (isSuperAdmin && queryCompanyId.HasValue && queryCompanyId.Value > 0)
                return queryCompanyId;

            var companyIdClaim = User.FindFirst("companyId")?.Value ?? User.FindFirst("company_id")?.Value;
            if (int.TryParse(companyIdClaim, out var cid))
                return cid;

            return null;
        }
    }

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetOrders()
        {
            var companyId = _context.CompanyId;
            var query = _context.Orders
                .IgnoreQueryFilters()
                .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
                .AsQueryable();

            if (companyId.HasValue)
            {
                query = query.Where(o => o.CompanyId == companyId.Value);
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedDate)
                .ToListAsync();

            return Ok(orders);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOrder(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .ThenInclude(oi => oi.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();
            return Ok(order);
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateOrderStatus(int id, [FromBody] StatusUpdateDto dto)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync(
                "CALL sp_update_order_status({0}, {1}, {2})",
                id, dto.Status, dto.Notes ?? "");

            var order = await _context.Orders.FindAsync(id);
            return Ok(order);
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelOrder(int id)
        {
            if (!await _context.Orders.AnyAsync(o => o.Id == id)) return NotFound();

            await _context.Database.ExecuteSqlRawAsync("CALL sp_cancel_order({0})", id);

            var order = await _context.Orders.FindAsync(id);
            return Ok(order);
        }
    }

    public class StatusUpdateDto
    {
        public string Status { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }
}
