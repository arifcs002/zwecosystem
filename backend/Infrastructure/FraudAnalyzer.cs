using Ecommerce.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ecommerce.Api.Infrastructure
{
    public class FraudAnalysisResult
    {
        public int RiskScore { get; set; }
        public string Decision { get; set; } = "PASS"; // 'PASS', 'REVIEW', 'BLOCK'
        public List<string> Flags { get; set; } = new();
    }

    // Advisory-only risk scoring — a BLOCK decision never rejects the order
    // automatically, it only flags it for admin review via api/fraud/flagged.
    public interface IFraudAnalyzer
    {
        Task<FraudAnalysisResult> AnalyzeAsync(Order order, string? ipAddress);
    }

    public class FraudAnalyzer : IFraudAnalyzer
    {
        private static readonly TimeSpan VelocityWindow = TimeSpan.FromMinutes(15);
        private const int VelocityThreshold = 3;
        private const decimal UnusualAmountMultiplier = 3.0m;

        private readonly ApplicationDbContext _context;
        private readonly IFileTextLogger _logger;

        public FraudAnalyzer(ApplicationDbContext context, IFileTextLogger logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<FraudAnalysisResult> AnalyzeAsync(Order order, string? ipAddress)
        {
            var result = new FraudAnalysisResult();

            try
            {
                var windowStart = DateTime.UtcNow - VelocityWindow;

                // Velocity by IP: too many orders from the same IP in a short window.
                if (!string.IsNullOrWhiteSpace(ipAddress))
                {
                    var ipOrderCount = await _context.Orders
                        .IgnoreQueryFilters()
                        .Where(o => o.CompanyId == order.CompanyId
                                 && o.CustomerIp == ipAddress
                                 && o.Id != order.Id
                                 && o.CreatedDate >= windowStart)
                        .CountAsync();

                    if (ipOrderCount >= VelocityThreshold)
                    {
                        result.RiskScore += 40;
                        result.Flags.Add($"velocity_ip:{ipOrderCount}_orders_in_{VelocityWindow.TotalMinutes}min");
                    }
                }

                // Velocity by phone number: too many orders from the same customer number.
                if (!string.IsNullOrWhiteSpace(order.CustomerPhone))
                {
                    var phoneOrderCount = await _context.Orders
                        .IgnoreQueryFilters()
                        .Where(o => o.CompanyId == order.CompanyId
                                 && o.CustomerPhone == order.CustomerPhone
                                 && o.Id != order.Id
                                 && o.CreatedDate >= windowStart)
                        .CountAsync();

                    if (phoneOrderCount >= VelocityThreshold)
                    {
                        result.RiskScore += 25;
                        result.Flags.Add($"velocity_phone:{phoneOrderCount}_orders_in_{VelocityWindow.TotalMinutes}min");
                    }

                    // Address mismatch: first order from this phone number but a high value.
                    var priorOrderCount = await _context.Orders
                        .IgnoreQueryFilters()
                        .Where(o => o.CompanyId == order.CompanyId
                                 && o.CustomerPhone == order.CustomerPhone
                                 && o.Id != order.Id)
                        .CountAsync();

                    if (priorOrderCount == 0 && order.Total >= 10000)
                    {
                        result.RiskScore += 15;
                        result.Flags.Add("first_time_high_value");
                    }

                    // Unusual amount vs this customer's historical average.
                    if (priorOrderCount > 0)
                    {
                        var avgTotal = await _context.Orders
                            .IgnoreQueryFilters()
                            .Where(o => o.CompanyId == order.CompanyId
                                     && o.CustomerPhone == order.CustomerPhone
                                     && o.Id != order.Id)
                            .AverageAsync(o => o.Total);

                        if (avgTotal > 0 && order.Total > avgTotal * UnusualAmountMultiplier)
                        {
                            result.RiskScore += 20;
                            result.Flags.Add($"unusual_amount:{order.Total}_vs_avg_{avgTotal:F2}");
                        }
                    }
                }

                result.RiskScore = Math.Min(result.RiskScore, 100);
                result.Decision = result.RiskScore >= 75 ? "BLOCK"
                                 : result.RiskScore >= 40 ? "REVIEW"
                                 : "PASS";

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("FRAUD", $"Analysis failed for order {order.Id}", ex);
                // Fail open to REVIEW rather than silently passing risky orders through
                // or blocking checkout entirely on an analyzer error.
                return new FraudAnalysisResult { RiskScore = 0, Decision = "REVIEW", Flags = { "analysis_error" } };
            }
        }
    }
}
