-- Batch 10: credit ("baki") sales with a customer ledger, and returns / exchanges.

-- Append-only: a correction is a new ADJUST row, never an edit. Balance = SUM(amount) per customer phone.
-- amount > 0: the customer owes more (SALE). amount < 0: owes less (PAYMENT, RETURN, negative ADJUST).
CREATE TABLE IF NOT EXISTS credit_ledger (
    id             SERIAL PRIMARY KEY,
    company_id     INTEGER       NOT NULL,
    phone          TEXT          NOT NULL,
    customer_name  TEXT,
    entry_type     TEXT          NOT NULL,
    amount         NUMERIC(18,2) NOT NULL,
    order_id       INTEGER,
    method         TEXT,
    note           TEXT,
    recorded_by    INTEGER,
    created_date   TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_credit_ledger_party ON credit_ledger (company_id, phone, id);
CREATE INDEX IF NOT EXISTS ix_credit_ledger_order ON credit_ledger (company_id, order_id);

CREATE TABLE IF NOT EXISTS order_returns (
    id             SERIAL PRIMARY KEY,
    company_id     INTEGER       NOT NULL,
    order_id       INTEGER       NOT NULL,
    return_number  TEXT          NOT NULL,
    kind           TEXT          NOT NULL,
    refund_amount  NUMERIC(18,2) NOT NULL DEFAULT 0,
    refund_method  TEXT          NOT NULL,
    cash_refunded  NUMERIC(18,2) NOT NULL DEFAULT 0,
    restocked_cost NUMERIC(18,2) NOT NULL DEFAULT 0,
    new_order_id   INTEGER,
    reason         TEXT,
    recorded_by    INTEGER,
    created_date   TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_order_returns_number ON order_returns (company_id, return_number);
CREATE INDEX IF NOT EXISTS ix_order_returns_order ON order_returns (company_id, order_id);

CREATE TABLE IF NOT EXISTS order_return_items (
    id             SERIAL PRIMARY KEY,
    return_id      INTEGER       NOT NULL,
    order_item_id  INTEGER       NOT NULL,
    product_id     INTEGER       NOT NULL,
    quantity       INTEGER       NOT NULL,
    unit_price     NUMERIC(18,2) NOT NULL,
    restocked      BOOLEAN       NOT NULL DEFAULT TRUE
);
CREATE INDEX IF NOT EXISTS ix_order_return_items_item ON order_return_items (order_item_id);
