using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ecommerce.Api.Tests;

[Collection("api")]
public class Batch11CashbookTests(ApiFixture f)
{
    private static string DayAgo(int n) => DateTime.UtcNow.AddHours(6).AddDays(-n).ToString("yyyy-MM-dd");
    // A random day in the closable window; figures are always compared with what the day showed BEFORE the test added data,
    // so leftovers from earlier runs can't break the sums.
    private static int RandomOffset() => Random.Shared.Next(8, 56);

    private async Task<(int Orders, int Ledger, int SupLedger, int Returns)> Baseline() => (
        await f.Scalar<int>("SELECT COALESCE(MAX(id),0)::int FROM orders"),
        await f.Scalar<int>("SELECT COALESCE(MAX(id),0)::int FROM credit_ledger"),
        await f.Scalar<int>("SELECT COALESCE(MAX(id),0)::int FROM supplier_ledger"),
        await f.Scalar<int>("SELECT COALESCE(MAX(id),0)::int FROM order_returns"));

    private async Task MoveToDay(Shop s, (int Orders, int Ledger, int SupLedger, int Returns) b, string day)
    {
        var noon = DateTime.Parse(day + "T12:00:00Z").AddHours(-6);                // noon Bangladesh time on that day
        await f.Exec("UPDATE orders SET created_date=@d WHERE company_id=@c AND id>@b", ("d", noon), ("c", s.CompanyId), ("b", b.Orders));
        await f.Exec("UPDATE credit_ledger SET created_date=@d WHERE company_id=@c AND id>@b", ("d", noon), ("c", s.CompanyId), ("b", b.Ledger));
        await f.Exec("UPDATE supplier_ledger SET created_date=@d WHERE company_id=@c AND id>@b", ("d", noon), ("c", s.CompanyId), ("b", b.SupLedger));
        await f.Exec("UPDATE order_returns SET created_date=@d WHERE company_id=@c AND id>@b", ("d", noon), ("c", s.CompanyId), ("b", b.Returns));
    }

    private async Task CleanDays(params string[] days)
    {
        foreach (var d in days) await f.Exec("DELETE FROM cash_closings WHERE business_date=@d::date AND company_id IN (@a,@b)", ("d", d), ("a", f.A.CompanyId), ("b", f.B.CompanyId));
    }

    private Task<HttpResponseMessage> Sale(HttpClient c, int qty, string method, string? phone = null, string? name = null, decimal? paid = null) =>
        c.PostAsJsonAsync("/api/pos/checkout", new { items = new[] { new { productId = f.A.ProductId, quantity = qty, price = (decimal?)null } }, discount = 0, paymentMethod = method, customerPhone = phone, customerName = name, amountPaid = paid });

    private async Task<JsonElement> Fig(HttpClient c, string day) => (await Shop.Body(await c.GetAsync($"/api/cashbook/day?date={day}"))).GetProperty("figures");
    private static decimal D(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    [Fact]
    public async Task ExpectedCash_AddsUpEveryCashMovement_AndIgnoresTheRest()
    {
        var day = DayAgo(RandomOffset());
        var a = await f.A.LoginAsync(); var bClient = await f.B.LoginAsync();
        var phone = "017" + Random.Shared.Next(10000000, 99999999);
        var before = await Fig(a, day); var beforeB = await Fig(bClient, day);
        var b0 = await Baseline();

        await Shop.Ok(await Sale(a, 2, "CASH"), "cash sale 2000");
        await Shop.Ok(await Sale(a, 1, "BKASH"), "mobile money sale 1000 (not cash)");
        await Shop.Ok(await Sale(a, 1, "CREDIT", phone, "Rahim", paid: 300), "credit sale 1000, 300 paid in cash at the till");
        await Shop.Ok(await a.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 200, method = "cash" }), "200 cash on account");
        await Shop.Ok(await a.PostAsJsonAsync("/api/ledger/payments", new { phone, amount = 150, method = "bkash" }), "150 bkash on account (not cash)");

        var sale = await Shop.Body(await Sale(a, 1, "CASH"));                                        // +1000, then refunded in cash
        var oid = sale.GetProperty("orderId").GetInt32();
        var item = (await Shop.Body(await a.GetAsync($"/api/returns/order/{oid}"))).GetProperty("items")[0].GetProperty("orderItemId").GetInt32();
        await Shop.Ok(await a.PostAsJsonAsync("/api/returns", new { orderId = oid, items = new[] { new { orderItemId = item, quantity = 1, restock = true } }, refundMethod = "CASH" }), "return -1000");

        var s2 = await Shop.Body(await Sale(a, 1, "CASH"));                                          // +1000, swapped for 2 units (2000)
        var oid2 = s2.GetProperty("orderId").GetInt32();
        var item2 = (await Shop.Body(await a.GetAsync($"/api/returns/order/{oid2}"))).GetProperty("items")[0].GetProperty("orderItemId").GetInt32();
        await Shop.Ok(await a.PostAsJsonAsync("/api/returns", new { orderId = oid2, items = new[] { new { orderItemId = item2, quantity = 1, restock = true } }, exchangeItems = new[] { new { productId = f.A.ProductId, quantity = 2 } } }), "exchange");

        await Shop.Ok(await a.PostAsJsonAsync("/api/expenses", new { category = "Tea", amount = 120, expenseDate = day }), "expense");
        var sup = (await Shop.Body(await a.PostAsJsonAsync("/api/suppliers", new { name = "S " + Guid.NewGuid().ToString("N")[..5], phoneNumber = "01700000000" }))).GetProperty("id").GetInt32();
        await Shop.Ok(await a.PostAsJsonAsync("/api/supplier-accounts/payments", new { supplierId = sup, amount = 50, method = "cash" }), "supplier cash");
        await Shop.Ok(await a.PostAsJsonAsync("/api/supplier-accounts/payments", new { supplierId = sup, amount = 25, method = "bank" }), "supplier bank (not cash)");
        await MoveToDay(f.A, b0, day);

        var after = await Fig(a, day);
        // cash sales: 2000 + 1000 + 1000 + exchange order 2000 = 6000 (the credit and bKash sales are not cash)
        Assert.Equal(6000m, D(after, "cashSales") - D(before, "cashSales"));
        Assert.Equal(1000m, D(after, "exchangeCreditApplied") - D(before, "exchangeCreditApplied"));    // paid with returned goods, not cash
        Assert.Equal(500m, D(after, "cashReceivedOnAccount") - D(before, "cashReceivedOnAccount"));      // 300 at sale + 200 later
        Assert.Equal(1000m, D(after, "cashRefunds") - D(before, "cashRefunds"));
        Assert.Equal(120m, D(after, "expenses") - D(before, "expenses"));
        Assert.Equal(50m, D(after, "supplierCashPayments") - D(before, "supplierCashPayments"));
        Assert.Equal(1000m, D(after.GetProperty("info"), "nonCashSales") - D(before.GetProperty("info"), "nonCashSales"));
        Assert.Equal(1000m, D(after.GetProperty("info"), "soldOnCredit") - D(before.GetProperty("info"), "soldOnCredit"));
        Assert.Equal(150m, D(after.GetProperty("info"), "nonCashReceivedOnAccount") - D(before.GetProperty("info"), "nonCashReceivedOnAccount"));
        // +6000 -1000 +500 -1000 -120 -50
        Assert.Equal(4330m, D(after, "expectedCash") - D(before, "expectedCash"));

        // another shop's day is untouched
        Assert.Equal(D(beforeB, "expectedCash"), D(await Fig(bClient, day), "expectedCash"));
    }

    [Fact]
    public async Task Closing_MatchedDrawer_NeedsNoNote_AndNextDayStartsFromTheCount()
    {
        var off = RandomOffset(); var d1 = DayAgo(off); var d2 = DayAgo(off - 1);
        await CleanDays(d1, d2);
        var a = await f.A.LoginAsync();
        var b0 = await Baseline();
        try
        {
            await Shop.Ok(await Sale(a, 1, "CASH"), "sale 1000");
            await MoveToDay(f.A, b0, d1);
            var expected = D(await Fig(a, d1), "expectedCash");

            var ok = await Shop.Body(await a.PostAsJsonAsync("/api/cashbook/close", new { date = d1, countedCash = expected, openingCash = D(await Fig(a, d1), "openingCash") }));
            Assert.Equal(expected, ok.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(0m, ok.GetProperty("difference").GetDecimal());

            // the next day's drawer starts from what was counted
            Assert.Equal(expected, D(await Fig(a, d2), "openingCash"));
            var e2 = D(await Fig(a, d2), "expectedCash");
            var next = await Shop.Body(await a.PostAsJsonAsync("/api/cashbook/close", new { date = d2, countedCash = e2, openingCash = 99999 }));    // a typed opening is ignored once a closing exists
            Assert.Equal(e2, next.GetProperty("expectedCash").GetDecimal());
            Assert.Equal(expected, (await Shop.Body(await a.GetAsync($"/api/cashbook/day?date={d2}"))).GetProperty("figures").GetProperty("openingCash").GetDecimal());
        }
        finally { await CleanDays(d1, d2); }
    }

    [Fact]
    public async Task ADrawerThatIsShortOrOver_NeedsAnExplanation_AndTheGapIsRecorded()
    {
        var day = DayAgo(RandomOffset());
        await CleanDays(day);
        var a = await f.A.LoginAsync();
        var b0 = await Baseline();
        try
        {
            await Shop.Ok(await Sale(a, 1, "CASH"), "sale");
            await MoveToDay(f.A, b0, day);
            var expected = D(await Fig(a, day), "expectedCash");

            var refused = await a.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = expected - 100 });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("short", (await Shop.Body(refused)).GetProperty("message").GetString());

            var closed = await Shop.Body(await a.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = expected - 100, note = "Gave change 100 to a customer, forgot to enter" }));
            Assert.Equal(-100m, closed.GetProperty("difference").GetDecimal());
            var list = (await Shop.Body(await a.GetAsync("/api/cashbook/closings"))).EnumerateArray().First(x => x.GetProperty("date").GetString()!.StartsWith(day));
            Assert.Equal(-100m, list.GetProperty("difference").GetDecimal());
            Assert.Contains("Gave change", list.GetProperty("note").GetString());
            var view = await Shop.Body(await a.GetAsync($"/api/cashbook/day?date={day}"));
            Assert.Equal(expected - 100, view.GetProperty("closing").GetProperty("countedCash").GetDecimal());

            // over is explained the same way
            var d2 = DayAgo(Random.Shared.Next(1, 7));
            await CleanDays(d2);
            var e2 = D(await Fig(a, d2), "expectedCash");
            var over = await a.PostAsJsonAsync("/api/cashbook/close", new { date = d2, countedCash = e2 + 40 });
            Assert.Equal(HttpStatusCode.BadRequest, over.StatusCode);
            Assert.Contains("over", (await Shop.Body(over)).GetProperty("message").GetString());
        }
        finally { await CleanDays(day); }
    }

    [Fact]
    public async Task Rules_OneClosingPerDay_NoFutureDays_NoBadInput_AndParallelClosesMakeOne()
    {
        var day = DayAgo(RandomOffset());
        await CleanDays(day);
        var a = await f.A.LoginAsync();
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/cashbook/close", new { date = "2099-01-01", countedCash = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/cashbook/close", new { date = "24/11/2026", countedCash = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/cashbook/close", new { date = DayAgo(200), countedCash = 0 })).StatusCode);   // too old
            Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = -5 })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/cashbook/day?date=2099-01-01")).StatusCode);

            var expected = D(await Fig(a, day), "expectedCash");
            var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => a.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = expected, openingCash = D(Fig(a, day).Result, "openingCash") }))));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await f.Scalar<int>("SELECT COUNT(*)::int FROM cash_closings WHERE company_id=@c AND business_date=@d::date", ("c", f.A.CompanyId), ("d", day)));
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.NewClient().GetAsync("/api/cashbook/closings")).StatusCode);
        }
        finally { await CleanDays(day); }
    }

    [Fact]
    public async Task EachShopClosesItsOwnBooks()
    {
        var day = DayAgo(RandomOffset());
        await CleanDays(day);
        var a = await f.A.LoginAsync(); var b = await f.B.LoginAsync();
        try
        {
            await Shop.Ok(await a.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = D(await Fig(a, day), "expectedCash") }), "A closes");
            await Shop.Ok(await b.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = D(await Fig(b, day), "expectedCash") }), "B closes the same day separately");
            Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/cashbook/close", new { date = day, countedCash = 0 })).StatusCode);
            Assert.Single((await Shop.Body(await b.GetAsync("/api/cashbook/closings"))).EnumerateArray().Where(x => x.GetProperty("date").GetString()!.StartsWith(day)));
        }
        finally { await CleanDays(day); }
    }
}
