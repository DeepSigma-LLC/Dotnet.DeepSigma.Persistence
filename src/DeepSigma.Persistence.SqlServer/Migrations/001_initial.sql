-- {Schema} and {TableName} are substituted at load time by SqlServerSchemaBootstrap.
-- [key] is limited to NVARCHAR(450) (= 900 bytes) — the SQL Server clustered-index entry max —
-- so SqlServerOptions enforces a MaxKeyLength of 450 by default rather than the library-wide 512.
IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
               WHERE s.name = N'{Schema}' AND t.name = N'{TableName}')
BEGIN
    CREATE TABLE [{Schema}].[{TableName}] (
        [key]        NVARCHAR(450)  NOT NULL CONSTRAINT [PK_{TableName}] PRIMARY KEY,
        [value]      NVARCHAR(MAX)  NOT NULL,
        [created_at] DATETIMEOFFSET NOT NULL,
        [updated_at] DATETIMEOFFSET NOT NULL,
        [expires_at] DATETIMEOFFSET NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_{TableName}_expires')
BEGIN
    CREATE INDEX [IX_{TableName}_expires]
        ON [{Schema}].[{TableName}]([expires_at])
        WHERE [expires_at] IS NOT NULL;
END;
