using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecommerce.Api.Domain
{
    /// One line of a customer's running account. Append-only; see migration 0008.
    [Table("credit_ledger")]
    public class CreditLedgerEntry
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("company_id")] public int CompanyId { get; set; }
        [Column("phone")] public string Phone { get; set; } = "";
        [Column("customer_name")] public string? CustomerName { get; set; }
        [Column("entry_type")] public string EntryType { get; set; } = "";   // SALE | PAYMENT | RETURN | ADJUST
        [Column("amount")] public decimal Amount { get; set; }                // + owes more, - owes less
        [Column("order_id")] public int? OrderId { get; set; }
        [Column("method")] public string? Method { get; set; }
        [Column("note")] public string? Note { get; set; }
        [Column("recorded_by")] public int? RecordedBy { get; set; }
        [Column("created_date")] public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }

    [Table("order_returns")]
    public class OrderReturn
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("company_id")] public int CompanyId { get; set; }
        [Column("order_id")] public int OrderId { get; set; }
        [Column("return_number")] public string ReturnNumber { get; set; } = "";
        [Column("kind")] public string Kind { get; set; } = "RETURN";                 // RETURN | EXCHANGE
        [Column("refund_amount")] public decimal RefundAmount { get; set; }          // value of the goods taken back
        [Column("refund_method")] public string RefundMethod { get; set; } = "CASH"; // CASH | ADJUST_DUE | EXCHANGE
        [Column("cash_refunded")] public decimal CashRefunded { get; set; }
        [Column("restocked_cost")] public decimal RestockedCost { get; set; }        // wholesale value put back on the shelf
        [Column("new_order_id")] public int? NewOrderId { get; set; }
        [Column("reason")] public string? Reason { get; set; }
        [Column("recorded_by")] public int? RecordedBy { get; set; }
        [Column("created_date")] public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        [ForeignKey(nameof(OrderReturnItem.ReturnId))]
        public List<OrderReturnItem> Items { get; set; } = new();
    }

    [Table("order_return_items")]
    public class OrderReturnItem
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("return_id")] public int ReturnId { get; set; }
        [Column("order_item_id")] public int OrderItemId { get; set; }
        [Column("product_id")] public int ProductId { get; set; }
        [Column("quantity")] public int Quantity { get; set; }
        [Column("unit_price")] public decimal UnitPrice { get; set; }
        [Column("restocked")] public bool Restocked { get; set; } = true;
    }
}
