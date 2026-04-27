CREATE TABLE IF NOT EXISTS {TableName} (
    key        TEXT NOT NULL PRIMARY KEY,
    value      TEXT NOT NULL,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    expires_at TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_{TableName}_expires
    ON {TableName} (expires_at)
    WHERE expires_at IS NOT NULL;
