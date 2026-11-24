using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Ecommerce.Api.Domain
{
    [Table("purchase_orders")]
    public class PurchaseOrder
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("company_id")] public int CompanyId { get; set; }
        [Column("supplier_id")] public int SupplierId { get; set; }
        [Column("po_number")] public string PoNumber { get; set; } = "";
        [Column("status")] public string Status { get; set; } = "ORDERED";   // ORDERED | PARTIAL | RECEIVED | CANCELLED
        [Column("note")] public string? Note { get; set; }
        [Column("created_by")] public int? CreatedBy { get; set; }
        [Column("created_date")] public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        [Column("received_date")] public DateTime? ReceivedDate { get; set; }
        [ForeignKey(nameof(PurchaseOrderItem.PurchaseOrderId))]
        public List<PurchaseOrderItem> Items { get; set; } = new();
    }

    [Table("purchase_order_items")]
    public class PurchaseOrderItem
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("purchase_order_id")] public int PurchaseOrderId { get; set; }
        [Column("product_id")] public int ProductId { get; set; }
        [Column("quantity")] public int Quantity { get; set; }
        [Column("unit_cost")] public decimal UnitCost { get; set; }
        [Column("received_qty")] public int ReceivedQty { get; set; }
    }

    [Table("supplier_ledger")]
    public class SupplierLedgerEntry
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("company_id")] public int CompanyId { get; set; }
        [Column("supplier_id")] public int SupplierId { get; set; }
        [Column("entry_type")] public string EntryType { get; set; } = "";   // PURCHASE | PAYMENT | ADJUST
        [Column("amount")] public decimal Amount { get; set; }                // + we owe more, - we owe less
        [Column("purchase_order_id")] public int? PurchaseOrderId { get; set; }
        [Column("method")] public string? Method { get; set; }
        [Column("note")] public string? Note { get; set; }
        [Column("recorded_by")] public int? RecordedBy { get; set; }
        [Column("created_date")] public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }

    [Table("cash_closings")]
    public class CashClosing
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("company_id")] public int CompanyId { get; set; }
        [Column("business_date", TypeName = "date")] public DateTime BusinessDate { get; set; }
        [Column("opening_cash")] public decimal OpeningCash { get; set; }
        [Column("expected_cash")] public decimal ExpectedCash { get; set; }
        [Column("counted_cash")] public decimal CountedCash { get; set; }
        [Column("difference")] public decimal Difference { get; set; }
        [Column("breakdown")] public string? Breakdown { get; set; }
        [Column("note")] public string? Note { get; set; }
        [Column("closed_by")] public int? ClosedBy { get; set; }
        [Column("closed_at")] public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("stock_counts")]
    public class StockCount
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("company_id")] public int CompanyId { get; set; }
        [Column("name")] public string Name { get; set; } = "";
        [Column("status")] public string Status { get; set; } = "OPEN";     // OPEN | APPLIED | CANCELLED
        [Column("note")] public string? Note { get; set; }
        [Column("created_by")] public int? CreatedBy { get; set; }
        [Column("created_date")] public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
        [Column("applied_date")] public DateTime? AppliedDate { get; set; }
        [ForeignKey(nameof(StockCountLine.StockCountId))]
        public List<StockCountLine> Lines { get; set; } = new();
    }

    [Table("stock_count_lines")]
    public class StockCountLine
    {
        [Key] [Column("id")] public int Id { get; set; }
        [Column("stock_count_id")] public int StockCountId { get; set; }
        [Column("product_id")] public int ProductId { get; set; }
        [Column("expected_qty")] public int ExpectedQty { get; set; }
        [Column("counted_qty")] public int? CountedQty { get; set; }
        [Column("unit_cost")] public decimal UnitCost { get; set; }
    }
}
