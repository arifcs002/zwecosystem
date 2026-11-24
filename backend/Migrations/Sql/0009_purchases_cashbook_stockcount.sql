-- Batch 11: purchase orders + what we owe suppliers, end-of-day cash closing, stock counts.

CREATE TABLE IF NOT EXISTS purchase_orders (
    id             SERIAL PRIMARY KEY,
    company_id     INTEGER       NOT NULL,
    supplier_id    INTEGER       NOT NULL,
    po_number      TEXT          NOT NULL,
    status         TEXT          NOT NULL DEFAULT 'ORDERED',     -- ORDERED | PARTIAL | RECEIVED | CANCELLED
    note           TEXT,
    created_by     INTEGER,
    created_date   TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    received_date  TIMESTAMPTZ
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_orders_number ON purchase_orders (company_id, po_number);
CREATE INDEX IF NOT EXISTS ix_purchase_orders_supplier ON purchase_orders (company_id, supplier_id);

CREATE TABLE IF NOT EXISTS purchase_order_items (
    id                 SERIAL PRIMARY KEY,
    purchase_order_id  INTEGER       NOT NULL,
    product_id         INTEGER       NOT NULL,
    quantity           INTEGER       NOT NULL,
    unit_cost          NUMERIC(18,2) NOT NULL,
    received_qty       INTEGER       NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS ix_po_items_po ON purchase_order_items (purchase_order_id);

-- What the shop owes a supplier. Append-only, like the customer ledger: amount > 0 = owes more, < 0 = owes less.
CREATE TABLE IF NOT EXISTS supplier_ledger (
    id                 SERIAL PRIMARY KEY,
    company_id         INTEGER       NOT NULL,
    supplier_id        INTEGER       NOT NULL,
    entry_type         TEXT          NOT NULL,                   -- PURCHASE | PAYMENT | ADJUST
    amount             NUMERIC(18,2) NOT NULL,
    purchase_order_id  INTEGER,
    method             TEXT,
    note               TEXT,
    recorded_by        INTEGER,
    created_date       TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_supplier_ledger_party ON supplier_ledger (company_id, supplier_id, id);

CREATE TABLE IF NOT EXISTS cash_closings (
    id              SERIAL PRIMARY KEY,
    company_id      INTEGER       NOT NULL,
    business_date   DATE          NOT NULL,
    opening_cash    NUMERIC(18,2) NOT NULL,
    expected_cash   NUMERIC(18,2) NOT NULL,
    counted_cash    NUMERIC(18,2) NOT NULL,
    difference      NUMERIC(18,2) NOT NULL,
    breakdown       TEXT,
    note            TEXT,
    closed_by       INTEGER,
    closed_at       TIMESTAMPTZ   NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cash_closings_day ON cash_closings (company_id, business_date);

CREATE TABLE IF NOT EXISTS stock_counts (
    id            SERIAL PRIMARY KEY,
    company_id    INTEGER     NOT NULL,
    name          TEXT        NOT NULL,
    status        TEXT        NOT NULL DEFAULT 'OPEN',           -- OPEN | APPLIED | CANCELLED
    note          TEXT,
    created_by    INTEGER,
    created_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    applied_date  TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS ix_stock_counts_company ON stock_counts (company_id, id DESC);

CREATE TABLE IF NOT EXISTS stock_count_lines (
    id            SERIAL PRIMARY KEY,
    stock_count_id INTEGER NOT NULL,
    product_id    INTEGER NOT NULL,
    expected_qty  INTEGER NOT NULL,                              -- stock when the count started
    counted_qty   INTEGER,                                       -- NULL = not counted yet
    unit_cost     NUMERIC(18,2) NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_count_lines ON stock_count_lines (stock_count_id, product_id);
