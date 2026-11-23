-- Batch 9: offline POS. Each sale made on a device carries a unique client reference so that a retry
-- (after a dropped connection) returns the original sale instead of charging and decrementing stock twice.
ALTER TABLE orders ADD COLUMN IF NOT EXISTS client_ref TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_company_client_ref ON orders (company_id, client_ref) WHERE client_ref IS NOT NULL;
