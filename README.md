# DeepSigma.Persistence

A swappable .NET 10 key-value persistence library. Choose your backend at registration time — your application code stays the same.

Four backends are provided out of the box:

| Package | Backend | Best for |
|---|---|---|
| `DeepSigma.Persistence.InMemory` | `ConcurrentDictionary` | Tests, caches, ephemeral state |
| `DeepSigma.Persistence.FileSystem` | One file per key (JSON) | Single-process, low-volume, human-readable data |
| `DeepSigma.Persistence.Sqlite` | SQLite via `DeepSigma.DataAccess.Sqlite` | Embedded apps, desktop tools, edge nodes |
| `DeepSigma.Persistence.Postgres` | PostgreSQL via `DeepSigma.DataAccess.Postgres` | Production services, shared state, high throughput |

The SQL backends sit on top of [`DeepSigma.DataAccess.RelationalDatabase`](https://github.com/DeepSigma-LLC) (a Dapper-backed CRUD API + `MigrationRunner`). The library does not depend on Dapper, `Microsoft.Data.Sqlite`, or `Npgsql` directly — those come transitively through the provider packages.

---

## Table of Contents

- [Interfaces](#interfaces)
- [Getting Started](#getting-started)
  - [InMemory](#inmemory)
  - [FileSystem](#filesystem)
  - [SQLite](#sqlite)
  - [Postgres](#postgres)
  - [Direct construction (tests and one-off scripts)](#direct-construction-tests-and-one-off-scripts)
- [Common Operations](#common-operations)
  - [Basic CRUD](#basic-crud)
  - [Expiration (TTL)](#expiration-ttl)
  - [Bulk Operations](#bulk-operations)
  - [Listing Keys](#listing-keys)
- [SetOptions Reference](#setoptions-reference)
- [Backend Comparison](#backend-comparison)
- [Project Structure](#project-structure)
- [Running Tests](#running-tests)
- [Design Notes](#design-notes)
  - [Manual schema migration](#manual-schema-migration)
  - [Customizing JSON serialization](#customizing-json-serialization)
- [Building Your Own Backend](#building-your-own-backend)

---

## Interfaces

Three interfaces form a capability hierarchy. Each backend implements all three.

```
IRepository<TValue>
│   GetAsync / SetAsync / DeleteAsync / ExistsAsync
│   ListKeysAsync / ListAsync
│
└── IBulkRepository<TValue>
    │   GetManyAsync / SetManyAsync / DeleteManyAsync
    │
    └── IExpiringRepository<TValue>
            GetTtlAsync / SetTtlAsync / PurgeExpiredAsync
```

Inject whichever interface your code actually needs. This keeps dependencies minimal and makes the code easier to test.

```csharp
// Needs only basic reads and writes
public class ProductService(IRepository<Product> repo) { ... }

// Needs batch operations
public class ImportJob(IBulkRepository<Record> repo) { ... }

// Needs TTL management
public class SessionStore(IExpiringRepository<Session> repo) { ... }
```

---

## Getting Started

All backends register the same three open-generic interface bindings. Pick one per application.

### InMemory

```bash
dotnet add package DeepSigma.Persistence.InMemory
```

```csharp
builder.Services.AddInMemoryPersistence();

// With options
builder.Services.AddInMemoryPersistence(o =>
{
    o.BackgroundPurge = true;
    o.PurgeInterval   = TimeSpan.FromMinutes(5);
});
```

> Data lives only for the lifetime of the process. Every resolved `IRepository<T>` shares the same underlying dictionary, so state is consistent across services that inject different interfaces for the same `TValue`.

---

### FileSystem

```bash
dotnet add package DeepSigma.Persistence.FileSystem
```

```csharp
builder.Services.AddFileSystemPersistence(o =>
{
    o.RootPath        = "./data";
    o.BackgroundPurge = true;
    o.PurgeInterval   = TimeSpan.FromMinutes(15);
});
```

Files are stored at `{RootPath}/{shard}/{sha256-of-key}.json` where `shard` is the first two characters of the SHA-256 hash (256 subdirectories). The full `Envelope<TValue>` (key, value, timestamps, expiry) is JSON-serialised to each file.

> **Single-process only.** There is no cross-process file locking. Concurrent writes from the same process are safe via per-key in-process locking. Practical scale: ~50k keys. Consider SQLite beyond that.

---

### SQLite

```bash
dotnet add package DeepSigma.Persistence.Sqlite
```

```csharp
builder.Services.AddSqlitePersistence(o =>
{
    o.ConnectionString = "Data Source=app.db";
    o.TableName        = "persistence_kv";   // default
    o.AutoMigrate      = true;               // default
    o.BackgroundPurge  = true;
    o.PurgeInterval    = TimeSpan.FromMinutes(15);
});
```

The schema is created automatically on first use when `AutoMigrate = true`. Set it to `false` in production if you want explicit control — a `SchemaVersionException` is thrown on startup if the schema is behind. See [Manual schema migration](#manual-schema-migration).

Behind the scenes, `AddSqlitePersistence` calls `AddDeepSigmaSqlite(connectionString, onConnectionOpened)` from the [`DeepSigma.DataAccess.Sqlite`](https://github.com/DeepSigma-LLC) package, which registers a `SqliteConnectionFactory`, a `RelationalDatabaseApi`, and a `MigrationRunner`. WAL journal mode is enabled once at startup; `foreign_keys=ON` and `busy_timeout=5000` are applied to every connection acquired from the pool.

---

### Postgres

```bash
dotnet add package DeepSigma.Persistence.Postgres
```

```csharp
builder.Services.AddPostgresPersistence(o =>
{
    o.ConnectionString = "Host=localhost;Database=mydb;Username=app;Password=secret";
    o.TableName        = "persistence_kv";   // default
    o.AutoMigrate      = true;               // default
    o.BackgroundPurge  = true;
    o.PurgeInterval    = TimeSpan.FromMinutes(15);
});
```

Values are stored as `JSONB`. `ListKeysAsync` and `ListAsync` stream results row-by-row (via `RelationalDatabaseApi.QueryStreamAsync`) rather than loading everything into memory.

`AddPostgresPersistence` calls `AddDeepSigmaPostgres(connectionString)` from the [`DeepSigma.DataAccess.Postgres`](https://github.com/DeepSigma-LLC) package, which registers a `PostgresConnectionFactory` (backed by a long-lived `NpgsqlDataSource`), a `RelationalDatabaseApi`, a `MigrationRunner`, and a `PostgresBulkCopier` (the latter is unused by the persistence backend itself but available for high-throughput ingestion scenarios in your own code).

---

### Direct construction (tests and one-off scripts)

Every repository exposes a single-argument convenience constructor that takes just its options and builds the rest internally. Use it from tests, throwaway scripts, or anywhere you don't want a DI container:

```csharp
// InMemory — no options required
var inMem = new InMemoryRepository<string>();

// FileSystem
var fs = new FileSystemRepository<string>(new FileSystemOptions { RootPath = "./data" });

// SQLite — runs migrations and applies PRAGMAs internally
var sqlite = new SqliteRepository<string>(new SqliteOptions
{
    ConnectionString = "Data Source=:memory:",
    AutoMigrate = true,
});

// Postgres — runs migrations against the supplied connection string
var pg = new PostgresRepository<string>(new PostgresOptions
{
    ConnectionString = "Host=localhost;Database=mydb;Username=app;Password=secret",
    AutoMigrate = true,
});
```

All four constructors default to `new JsonValueSerializer()`. Production code should prefer the DI extensions so the registered `IJsonValueSerializer` (and shared `RelationalDatabaseApi` / `MigrationRunner`) are honored.

---

## Common Operations

All examples below work identically regardless of which backend is registered.

### Basic CRUD

```csharp
public class ExampleService(IRepository<string> repo)
{
    public async Task RunAsync()
    {
        // Write
        await repo.SetAsync("config:theme", "dark");

        // Read — returns null if the key doesn't exist
        string? theme = await repo.GetAsync("config:theme");

        // Check existence
        bool exists = await repo.ExistsAsync("config:theme");

        // Delete — returns false if the key wasn't present
        bool removed = await repo.DeleteAsync("config:theme");
    }
}
```

### Expiration (TTL)

```csharp
public class SessionService(IExpiringRepository<UserSession> repo)
{
    // Set a key that expires after 30 minutes
    public Task CreateSessionAsync(string id, UserSession session) =>
        repo.SetAsync(id, session, new SetOptions { Ttl = TimeSpan.FromMinutes(30) });

    // Set a key that expires at a specific wall-clock time
    public Task CreateSessionUntilAsync(string id, UserSession session, DateTimeOffset expiresAt) =>
        repo.SetAsync(id, session, new SetOptions { AbsoluteExpiration = expiresAt });

    // Slide the expiration — extend an existing key's TTL without rewriting the value
    public Task SlideAsync(string id) =>
        repo.SetTtlAsync(id, TimeSpan.FromMinutes(30));

    // Make a key permanent — remove its expiration
    public Task MakePermanentAsync(string id) =>
        repo.SetTtlAsync(id, ttl: null);

    // How long is left?
    public async Task<TimeSpan?> GetRemainingAsync(string id) =>
        await repo.GetTtlAsync(id);

    // Manually evict all stale keys (each backend also supports BackgroundPurge)
    public Task<int> CleanupAsync() =>
        repo.PurgeExpiredAsync();
}
```

Expired keys are invisible to all reads (`GetAsync`, `ExistsAsync`, `ListKeysAsync`, etc.) without needing an explicit purge first. Purge only reclaims storage.

### Bulk Operations

```csharp
public class CacheLoader(IBulkRepository<Product> repo)
{
    public async Task WarmAsync(IEnumerable<Product> products)
    {
        var items = products.Select(p => KeyValuePair.Create(p.Id, p));
        await repo.SetManyAsync(items, new SetOptions { Ttl = TimeSpan.FromHours(1) });
    }

    public async Task<IReadOnlyDictionary<string, Product>> FetchAsync(IEnumerable<string> ids)
    {
        // Missing keys are simply absent from the result — no nulls in the dictionary
        return await repo.GetManyAsync(ids);
    }

    public async Task EvictAsync(IEnumerable<string> ids)
    {
        int removed = await repo.DeleteManyAsync(ids);
        Console.WriteLine($"Evicted {removed} entries.");
    }
}
```

SQL backends process bulk operations in chunks of `SqlBulkChunk.Size` (1 000 rows). InMemory and FileSystem iterate per-key with no batching.

### Listing Keys

```csharp
// All keys
await foreach (var key in repo.ListKeysAsync())
    Console.WriteLine(key);

// Keys under a namespace prefix
await foreach (var key in repo.ListKeysAsync("user:"))
    Console.WriteLine(key);

// Key-value pairs with a prefix
await foreach (var (key, value) in repo.ListAsync("session:"))
    Console.WriteLine($"{key} => {value}");

// With cancellation
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await foreach (var key in repo.ListKeysAsync(ct: cts.Token))
    Console.WriteLine(key);
```

---

## SetOptions Reference

`SetOptions` is a `sealed record`. `Ttl` and `AbsoluteExpiration` are mutually exclusive — setting both throws `ArgumentException`.

```csharp
// Relative TTL — expires this many seconds/minutes/hours from now
new SetOptions { Ttl = TimeSpan.FromMinutes(30) }

// Absolute expiration — expires at a specific point in time
new SetOptions { AbsoluteExpiration = DateTimeOffset.UtcNow.AddDays(7) }

// No expiration (default when options is null)
await repo.SetAsync("key", value);
// same as:
await repo.SetAsync("key", value, options: null);
```

---

## Backend Comparison

| Feature | InMemory | FileSystem | SQLite | Postgres |
|---|---|---|---|---|
| Persistence | None | File | File | Server |
| Multi-process safe | — | No | Yes | Yes |
| Max practical keys | Unlimited | ~50k | Millions | Unlimited |
| TTL precision | Tick (100 ns) | Tick (100 ns) | Millisecond | Microsecond |
| Streaming list | Yes | Yes | Yes | Yes (server cursor) |
| Value storage | Live object | JSON file | JSON text | JSONB |
| Background purge | Optional | Optional | Optional | Optional |
| Bulk chunk size | N/A | N/A | 1 000 rows | 1 000 rows |
| Schema migration | N/A | N/A | Auto / manual | Auto / manual |
| Concurrent-write safety | `ConcurrentDictionary` | `AsyncKeyedLock` per key | WAL + `busy_timeout` | Row-level locking |
| Test dependencies | None | Temp dir | Temp `.db` file | Docker |

---

## Project Structure

```
DeepSigma.Persistence/
├── Directory.Build.props          # Shared: version, TFM, nullable, warnings-as-errors
├── Directory.Packages.props       # Central package versioning (CPM)
├── DeepSigma.Persistence.slnx
│
├── src/
│   ├── DeepSigma.Persistence.Abstractions/
│   │   ├── IRepository.cs / IBulkRepository.cs / IExpiringRepository.cs
│   │   ├── IJsonValueSerializer.cs
│   │   ├── SetOptions.cs            # Ttl / AbsoluteExpiration (mutually exclusive)
│   │   ├── PersistenceOptions.cs    # MaxKeyLength
│   │   └── Exceptions/
│   │       ├── PersistenceException.cs
│   │       ├── SerializationException.cs
│   │       └── SchemaVersionException.cs
│   │
│   ├── DeepSigma.Persistence.Core/
│   │   ├── Envelope.cs                        # Versioned wrapper (V=1) stored by file backend
│   │   ├── JsonValueSerializer.cs             # System.Text.Json implementation of IJsonValueSerializer
│   │   ├── JsonValueSerializerExtensions.cs   # SerializeToString / DeserializeFromString helpers
│   │   ├── KeyValidator.cs                    # Validate / ValidateAll — null / empty / null-byte / length checks
│   │   ├── KeyHasher.cs                       # SHA-256 hex + 2-char shard prefix
│   │   ├── SqlPrefix.cs                       # Shared LIKE-prefix escape + clause builder for SQL backends
│   │   ├── SqlBulkChunk.cs                    # Standard chunk size (1 000) for SQL bulk operations
│   │   ├── MigrationLoader.cs                 # Loads (Id, Sql) tuples from embedded .Migrations.*.sql resources
│   │   ├── BackgroundPurgeService.cs          # Generic hosted purge loop — backends supply the delete delegate
│   │   └── ServiceCollectionExtensions.cs     # AddPersistenceCore + AddRepositoryImplementation(typeof(...<>))
│   │
│   ├── DeepSigma.Persistence.InMemory/
│   │   ├── InMemoryOptions.cs
│   │   ├── InMemoryStore.cs        # Singleton ConcurrentDictionary + optional purge task
│   │   ├── InMemoryRepository.cs
│   │   └── ServiceCollectionExtensions.cs
│   │
│   ├── DeepSigma.Persistence.FileSystem/
│   │   ├── FileSystemOptions.cs
│   │   ├── FileSystemRepository.cs
│   │   ├── FileSystemPurge.cs      # Non-generic partial-deserialize purge helper
│   │   └── ServiceCollectionExtensions.cs
│   │
│   ├── DeepSigma.Persistence.Sqlite/
│   │   ├── SqliteOptions.cs
│   │   ├── SqliteSchemaBootstrap.cs   # Pragmas + migration apply via MigrationRunner
│   │   ├── SqliteRepository.cs
│   │   ├── ServiceCollectionExtensions.cs
│   │   └── Migrations/
│   │       └── 001_initial.sql
│   │
│   └── DeepSigma.Persistence.Postgres/
│       ├── PostgresOptions.cs
│       ├── PostgresSchemaBootstrap.cs   # Migration apply via MigrationRunner
│       ├── PostgresRepository.cs
│       ├── ServiceCollectionExtensions.cs
│       └── Migrations/
│           └── 001_initial.sql
│
└── tests/
    ├── DeepSigma.Persistence.Abstractions.Testing/
    │   ├── RepositoryContractTests.cs         # base CRUD + listing + key validation + concurrency
    │   ├── BulkRepositoryContractTests.cs     # GetMany / SetMany / DeleteMany
    │   └── ExpiringRepositoryContractTests.cs # TTL, purge, GetTtl, SetTtl, concurrent SetTtl
    │
    ├── DeepSigma.Persistence.InMemory.Tests/
    ├── DeepSigma.Persistence.FileSystem.Tests/
    ├── DeepSigma.Persistence.Sqlite.Tests/
    └── DeepSigma.Persistence.Postgres.Tests/   # Requires Docker (Testcontainers)
```

---

## Running Tests

```bash
# All backends
dotnet test

# One backend
dotnet test tests/DeepSigma.Persistence.Sqlite.Tests

# Postgres requires Docker Desktop to be running
dotnet test tests/DeepSigma.Persistence.Postgres.Tests
```

Each backend runs the same 63-test contract suite (`RepositoryContractTests` → `BulkRepositoryContractTests` → `ExpiringRepositoryContractTests`). Tests are isolated: InMemory creates a fresh instance per test, SQLite and FileSystem use unique temp paths, Postgres spins up one Testcontainers container and uses a separate table per test.

The contract suite includes four concurrency cases (concurrent same-key writes, concurrent distinct-key writes, racing delete-vs-get, and racing `SetTtl` on the same key) so every backend's thread-safety claim is enforced rather than asserted.

Current status: **252 / 252 passing** across all backends.

---

## Design Notes

**Keys** are strings only. Max length 512 characters (configurable via `PersistenceOptions.MaxKeyLength`). Case-sensitive, null bytes are rejected.

**Swappability** is enforced through open-generic DI registration (`typeof(IRepository<>)` → `typeof(SqliteRepository<>)`). Changing backends is a one-line change in the DI setup.

**Schema migrations** (SQLite and Postgres) are tracked in a `_migrations` table keyed by string id (e.g. `001_initial`). The underlying runner is `DeepSigma.DataAccess.RelationalDatabase.MigrationRunner`, supplied by the provider package and registered automatically. Set `AutoMigrate = false` if you want the application to throw `SchemaVersionException` (with the list of pending ids in the message) rather than apply migrations automatically — useful for controlled production deployments. See [Manual schema migration](#manual-schema-migration) for the explicit-control recipe.

**TTL precision:**
- SQLite stores timestamps as `"yyyy-MM-dd HH:mm:ss.fff"` and compares against `strftime('%Y-%m-%d %H:%M:%f', 'now')` for millisecond accuracy.
- Postgres uses native `TIMESTAMPTZ` with microsecond precision and `NOW()`.

**FileSystem locking** is in-process only using `AsyncKeyedLock`. Each key has its own semaphore (keyed on SHA-256 hash) so writes to different keys never contend with each other.

**Concurrency** — all backends are safe for concurrent use within a single process. Each uses a different mechanism: `ConcurrentDictionary` (InMemory), per-key `AsyncKeyedLock` (FileSystem), WAL journal + `busy_timeout` (SQLite), or PostgreSQL row-level locking. The contract test suite exercises this directly with four concurrency cases shared across every backend, so the claim is enforced rather than asserted. Cross-process safety is *not* provided by InMemory or FileSystem; use SQLite or Postgres if multiple processes touch the same data.

**Envelope versioning** — SQLite and Postgres back-ends do not use the envelope; they store the JSON-serialised value directly. The FileSystem backend wraps each value in `Envelope<TValue> { V = 1, Key, Value, CreatedAt, UpdatedAt, ExpiresAt }` to enable future lazy migration without a full re-write.

**Serialization** — every backend stores JSON-shaped data. The override surface is a configurable `IJsonValueSerializer`:

| Backend | What it serializes | Override surface |
|---|---|---|
| InMemory | Nothing — values are stored as live objects | N/A |
| FileSystem | `Envelope<TValue>` via `IJsonValueSerializer` | Custom `IJsonValueSerializer` registration |
| SQLite | `TValue` via `IJsonValueSerializer`, stored as TEXT | Custom `IJsonValueSerializer` registration |
| Postgres | `TValue` via `IJsonValueSerializer`, stored as JSONB | Custom `IJsonValueSerializer` registration |

The interface is intentionally JSON-only: Postgres `JSONB` requires JSON, FileSystem files are conventionally JSON for human inspection, and SQLite stores TEXT that conventionally holds JSON. Non-JSON binary formats (MessagePack, protobuf) are not currently supported via this hook.

> **Future extension — typed `IValueSerializer<TValue>`.** If real demand emerges for binary serializers (MessagePack, protobuf, source-generated typed JSON), the planned shape is a *separate*, additive interface:
>
> ```csharp
> public interface IValueSerializer<TValue>
> {
>     byte[] Serialize(TValue value);
>     TValue? Deserialize(byte[] data);
> }
> ```
>
> Backends would prefer `IValueSerializer<TValue>` if registered for that `TValue`, and fall back to `IJsonValueSerializer` otherwise. Per-type serializers are how MessagePack/protobuf libraries actually want to be wired (one formatter per `T`, resolved at registration time, no per-call reflection). Adding this is non-breaking — `IJsonValueSerializer` would stay as the default for everyone who doesn't opt in. Filed against the lack of any concrete user demand today; swap this design in if/when the need is real.

### Customizing JSON serialization

To customize JSON behaviour (camelCase, custom converters, AOT/trimming via a source-generated `JsonSerializerContext`), construct a `JsonValueSerializer` with a configured `JsonSerializerOptions` and register it **before** calling the backend extension:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using DeepSigma.Persistence;
using DeepSigma.Persistence.Core;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() },
    // For AOT / trimming, attach a source-generated context:
    // TypeInfoResolver = MyJsonContext.Default,
};

// Register your serializer first — TryAddSingleton inside the backend extension respects it.
builder.Services.AddSingleton<IJsonValueSerializer>(new JsonValueSerializer(jsonOptions));

builder.Services.AddSqlitePersistence(o => { o.ConnectionString = "Data Source=app.db"; });
// builder.Services.AddPostgresPersistence(...);   // would also pick up the same serializer
// builder.Services.AddFileSystemPersistence(...); // ditto
```

`AddSqlitePersistence`, `AddPostgresPersistence`, and `AddFileSystemPersistence` all call `AddPersistenceCore()` internally, which uses `TryAddSingleton` for `IJsonValueSerializer` — the registration above wins because it ran first. The same serializer is used by all three. (`AddInMemoryPersistence` does not need a serializer; values are stored as live objects.)

### Manual schema migration

`AutoMigrate = true` (the default) applies pending migrations the first time a repository is resolved from DI. For controlled production deployments where you want migrations to run as a separate, observable step — not silently on the request that happens to be first — flip `AutoMigrate = false` in the running application and run migrations from a dedicated startup command:

```csharp
// migrate.cs — a one-off entry point your deploy pipeline invokes before the app starts.
//
// Constructing a repository with AutoMigrate = true applies pending migrations as a side
// effect, then we discard the repo. The application proper then runs with AutoMigrate = false.

using DeepSigma.Persistence.Sqlite;

_ = new SqliteRepository<object>(new SqliteOptions
{
    ConnectionString = "Data Source=app.db",
    AutoMigrate = true,
});
Console.WriteLine("Migrations applied.");
```

In the application itself:

```csharp
builder.Services.AddSqlitePersistence(o =>
{
    o.ConnectionString = "Data Source=app.db";
    o.AutoMigrate = false;   // throws SchemaVersionException if the migrate step did not run
});
```

`SchemaVersionException` (from `DeepSigma.Persistence`) carries the list of pending migration ids in its message — useful for error reporting and operations alerting:

```csharp
// In Program.cs (top-level statements return an int):
try
{
    var app = builder.Build();
    app.Run();
    return 0;
}
catch (SchemaVersionException ex)
{
    // Log, page, exit non-zero — schema needs to be applied before the app can start.
    Console.Error.WriteLine($"Cannot start: {ex.Message}");
    return 1;
}
```

The same pattern works for Postgres — replace `SqliteRepository<object>` / `SqliteOptions` / `AddSqlitePersistence` with the Postgres equivalents.

---

## Building Your Own Backend

The `DeepSigma.Persistence.Core` package exposes the helpers used by the built-in backends so a third-party backend stays consistent with the rest:

| Helper | Use it for |
|---|---|
| `IJsonValueSerializer` + `JsonValueSerializer` | Value (de)serialization; respects user-provided `JsonSerializerOptions`. |
| `JsonValueSerializerExtensions.SerializeToString` / `DeserializeFromString` | UTF-8 string conversion when the wire format is TEXT (e.g. SQLite). |
| `KeyValidator.Validate` / `ValidateAll` | Single-key and streaming validation (null / empty / null-byte / length). |
| `KeyHasher.Hash` / `Shard` | SHA-256 hex digest and 2-char shard prefix (for file-per-key layouts). |
| `Envelope<TValue>` | Versioned wrapper if your backend stores values alongside metadata. |
| `SqlPrefix.Build(prefix)` | Produces the `LIKE @Prefix ESCAPE '\\'` clause + escaped value for SQL backends. |
| `SqlBulkChunk.Size` | Standard bulk-operation chunk size (1 000) so your backend matches the SQL backends' defaults. |
| `MigrationLoader.Load(asm, tableName)` | Returns `(Id, Sql)` tuples from embedded `.Migrations.*.sql` resources, with `{TableName}` substitution. |
| `BackgroundPurgeService` | Generic hosted purge loop; supply a `Func<CancellationToken, Task<int>>` and an interval. |
| `services.AddRepositoryImplementation(typeof(MyRepository<>))` | Registers the open-generic repository as all three interfaces (`IRepository<T>`, `IBulkRepository<T>`, `IExpiringRepository<T>`) in one call. |
| `services.AddPersistenceCore()` | `TryAddSingleton` of the default `IJsonValueSerializer`. |

Your backend's `AddXxxPersistence` extension then looks like:

```csharp
public static IServiceCollection AddRedisPersistence(
    this IServiceCollection services,
    Action<RedisOptions> configure)
{
    var options = new RedisOptions { ConnectionString = "" };
    configure(options);
    options.Validate();
    services.TryAddSingleton(options);

    services.AddPersistenceCore();
    services.AddRepositoryImplementation(typeof(RedisRepository<>));

    if (options.BackgroundPurge)
    {
        services.AddHostedService(sp =>
        {
            var opts = sp.GetRequiredService<RedisOptions>();
            // Build whatever your backend needs (Redis multiplexer, S3 client, …) and capture it
            // in the closure. Return value is the count of entries removed in this cycle.
            return new BackgroundPurgeService(
                purge: async ct =>
                {
                    // Example: await db.KeyDeleteAsync(expiredKeys); return expiredKeys.Length;
                    await Task.CompletedTask;
                    return 0;
                },
                interval: opts.PurgeInterval,
                logger: sp.GetService<ILogger<BackgroundPurgeService>>());
        });
    }

    return services;
}
```

To validate behaviour, inherit `ExpiringRepositoryContractTests<MyRepository<string>>` from `DeepSigma.Persistence.Abstractions.Testing` — the same 63-test suite (including four concurrency cases) that exercises every built-in backend.

---

## License

MIT — see [LICENSE](LICENSE) for details.
