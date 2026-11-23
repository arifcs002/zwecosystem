-- Batch 7: unit-of-measure label and free-form spec list per product.
ALTER TABLE products ADD COLUMN IF NOT EXISTS uom TEXT;
ALTER TABLE products ADD COLUMN IF NOT EXISTS attributes TEXT;
