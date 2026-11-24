using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ecommerce.Api.Infrastructure
{
    // Who may do what, enforced on the SERVER (the admin menu only hides links; it is not security).
    // Three tiers, each including everything below it:
    //   Staff   – cashier / sales staff: sells, takes payments, looks up customers and orders
    //   Manager – also runs the catalogue, stock, purchasing, cash closing, couriers
    //   Owner   – also money reports, settings, discounts, staff accounts, approvals
    // Every staff-only controller must be listed here; unlisted ones default to Owner (fail closed),
    // and a test fails when a controller is missing so a new one can't slip through unnoticed.
    public enum AccessTier { None = 0, Staff = 1, Manager = 2, Owner = 3 }

    public record AccessRule(AccessTier Read, AccessTier Write, params (string Match, AccessTier Tier)[] Overrides);

    public static class AccessPolicy
    {
        const AccessTier S = AccessTier.Staff, M = AccessTier.Manager, O = AccessTier.Owner;

        // Override keys look like "POST remind-all": HTTP method + the end of the route template.
        public static readonly Dictionary<string, AccessRule> Rules = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AbandonedCarts"] = new(M, M),
            ["Activity"] = new(O, O),
            ["AppVersions"] = new(O, O, ("GET latest", S)),
            ["Auth"] = new(S, S),
            ["Billing"] = new(O, O, ("GET my", S)),
            ["Brands"] = new(M, M),
            ["Cashbook"] = new(M, M),
            ["Categories"] = new(M, M),
            ["Collections"] = new(M, O),
            ["Companies"] = new(O, O),
            ["Coupons"] = new(O, O),
            ["Courier"] = new(M, M),
            ["CustomerGroups"] = new(O, O),
            ["Customers"] = new(S, S),
            ["Dashboard"] = new(S, S, ("GET money", M)),
            ["Expenses"] = new(O, O),
            ["ProfitReport"] = new(O, O),
            ["ReportExport"] = new(M, M, ("GET profit", O), ("GET expenses", O), ("GET stock", O), ("GET products", O), ("GET dead-stock", O)),
            ["Analytics"] = new(O, O),
            ["Fraud"] = new(M, M),
            ["Inventory"] = new(M, M),
            ["Loyalty"] = new(S, O, ("GET customers", M)),
            ["Shifts"] = new(S, S, ("GET history", M), ("GET cashiers", M)),
            ["SmsCampaigns"] = new(O, O),
            ["Offers"] = new(M, O, ("POST preview", S)),
            ["Batches"] = new(M, M),
            ["Reorder"] = new(M, M),
            ["Alerts"] = new(M, M, ("POST summary", O)),
            ["Ledger"] = new(S, S, ("POST adjustments", O), ("POST remind-all", O)),
            ["Orders"] = new(S, S, ("PUT courier", M), ("POST cancel", M)),
            ["POS"] = new(S, S),
            ["Payments"] = new(M, M),
            ["PricingTags"] = new(M, M),
            ["Products"] = new(M, M),
            ["Purchases"] = new(M, M),
            ["SupplierAccounts"] = new(M, M, ("POST adjustments", O)),
            ["Refunds"] = new(M, M, ("POST", S), ("PUT approve", O), ("PUT reject", O), ("PUT complete", O)),
            ["Returns"] = new(S, S),
            ["Reviews"] = new(M, M),
            ["Seo"] = new(S, S),
            ["Settings"] = new(S, O),
            ["StockCounts"] = new(M, M),
            ["Storefront"] = new(S, S),
            ["SubscriptionPlans"] = new(O, O, ("GET my-plan", S)),
            ["Suppliers"] = new(M, M),
            ["Tenant"] = new(S, O),
            ["Upload"] = new(M, M),
            ["Users"] = new(O, O),
        };

        // Highest tier among the user's roles (a person with several roles gets the best of them).
        public static AccessTier TierOf(ClaimsPrincipal user)
        {
            var best = AccessTier.None;
            foreach (var c in user.Claims.Where(c => c.Type == ClaimTypes.Role))
            {
                var r = c.Value.Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
                var t = r switch
                {
                    "superadmin" or "companyadmin" => AccessTier.Owner,
                    "companymanager" or "manager" => AccessTier.Manager,
                    "salesstaff" or "sales" or "cashier" => AccessTier.Staff,
                    _ => AccessTier.None
                };
                if (t > best) best = t;
            }
            return best;
        }

        public static AccessTier Required(string controller, string httpMethod, string? template)
        {
            if (!Rules.TryGetValue(controller, out var rule)) return O;
            var isRead = httpMethod is "GET" or "HEAD";
            var tier = isRead ? rule.Read : rule.Write;
            var t = (template ?? "").Trim('/');
            foreach (var (match, ov) in rule.Overrides)
            {
                var parts = match.Split(' ', 2);
                var verb = parts[0];
                var tail = parts.Length > 1 ? parts[1] : "";
                if (!string.Equals(verb, httpMethod, StringComparison.OrdinalIgnoreCase)) continue;
                // an empty tail means the collection root, i.e. just "api/<controller>"
                var hit = tail == "" ? t.Split('/').Length <= 2 : t.Contains(tail, StringComparison.OrdinalIgnoreCase);
                if (hit) return ov;
            }
            return tier;
        }
    }

    public class AccessPolicyFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
        {
            var user = ctx.HttpContext.User;
            // not signed in: [Authorize] (or public access) decides; anonymous endpoints stay open
            if (user.Identity?.IsAuthenticated != true) return Task.CompletedTask;
            if (ctx.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return Task.CompletedTask;
            if (ctx.ActionDescriptor is not ControllerActionDescriptor cad) return Task.CompletedTask;

            var controller = cad.ControllerName;
            // the profit report lives in ExpensesController.cs under its own class
            if (cad.ControllerTypeInfo.Name == "ProfitReportController") controller = "ProfitReport";
            if (cad.ControllerTypeInfo.Name == "SupplierAccountsController") controller = "SupplierAccounts";

            var method = ctx.HttpContext.Request.Method.ToUpperInvariant();
            var template = cad.AttributeRouteInfo?.Template;
            var need = AccessPolicy.Required(controller, method, template);
            if (AccessPolicy.TierOf(user) >= need) return Task.CompletedTask;

            ctx.Result = new ObjectResult(new { message = "You don't have permission to do this. Ask the shop owner." }) { StatusCode = 403 };
            return Task.CompletedTask;
        }
    }
}
