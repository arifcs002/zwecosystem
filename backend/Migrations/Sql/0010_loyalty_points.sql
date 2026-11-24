-- Batch 12: customer loyalty points. Append-only like the money ledgers: balance = SUM(points) per customer phone.
-- EARN (+) when a sale is paid, REDEEM (-) when points pay part of a bill, RETURN_REVERSE (-) when goods come back,
-- CANCEL_EARN (-) / CANCEL_REDEEM (+) when a whole order is cancelled, ADJUST (+/-) by the owner.
CREATE TABLE IF NOT EXISTS loyalty_ledger (
    id           SERIAL PRIMARY KEY,
    company_id   INTEGER     NOT NULL,
    phone        TEXT        NOT NULL,
    entry_type   TEXT        NOT NULL,
    points       INTEGER     NOT NULL,
    order_id     INTEGER,
    note         TEXT,
    recorded_by  INTEGER,
    created_date TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS ix_loyalty_party ON loyalty_ledger (company_id, phone, id);
CREATE INDEX IF NOT EXISTS ix_loyalty_order ON loyalty_ledger (company_id, order_id);
-- A sale can earn / redeem / cancel only once, even if a request is retried.
CREATE UNIQUE INDEX IF NOT EXISTS ux_loyalty_once ON loyalty_ledger (company_id, order_id, entry_type)
    WHERE order_id IS NOT NULL AND entry_type IN ('EARN','REDEEM','CANCEL_EARN','CANCEL_REDEEM');
