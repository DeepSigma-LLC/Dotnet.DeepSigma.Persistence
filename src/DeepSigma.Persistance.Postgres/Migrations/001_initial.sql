CREATE TABLE IF NOT EXISTS {TableName} (
    key        TEXT PRIMARY KEY,
    value      JSONB NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NULL
);
CREATE INDEX IF NOT EXISTS ix_{TableName}_expires ON {TableName}(expires_at) WHERE expires_at IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_{TableName}_key_prefix ON {TableName}(key text_pattern_ops)
