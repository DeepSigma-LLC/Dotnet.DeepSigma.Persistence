# DeepSigma.Persistance

A swappable .NET 10 key-value persistence library. Choose your backend at registration time — your application code stays the same.

Four backends are provided out of the box:

| Package | Backend | Best for |
|---|---|---|
| `DeepSigma.Persistance.InMemory` | `ConcurrentDictionary` | Tests, caches, ephemeral state |
| `DeepSigma.Persistance.FileSystem` | One file per key (JSON) | Single-process, low-volume, human-readable data |
| `DeepSigma.Persistance.Sqlite` | SQLite via Dapper | Embedded apps, desktop tools, edge nodes |
| `DeepSigma.Persistance.Postgres` | PostgreSQL via Dapper + Npgsql | Production services, shared state, high throughput |

---

## Table of Contents

- [Interfaces](#interfaces)
- [Getting Started](#getting-started)
  - [InMemory](#inmemory)
  - [FileSystem](#filesystem)
  - [SQLite](#sqlite)
  - [Postgres](#postgres)
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
dotnet add package DeepSigma.Persistance.InMemory
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
dotnet add package DeepSigma.Persistance.FileSystem
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
dotnet add package DeepSigma.Persistance.Sqlite
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

The schema is created automatically on first use when `AutoMigrate = true`. Set it to `false` in production if you want explicit control — a `SchemaVersionException` is thrown on startup if the schema is behind.

---

### Postgres

```bash
dotnet add package DeepSigma.Persistance.Postgres
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

Values are stored as `JSONB`. `ListKeysAsync` and `ListAsync` stream results row-by-row via a server-side Npgsql reader rather than loading everything into memory.

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

SQLite processes bulk operations in chunks of 500 rows; Postgres in chunks of 1000.

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
| Bulk chunk size | N/A | N/A | 500 rows | 1 000 rows |
| Schema migration | N/A | N/A | Auto / manual | Auto / manual |
| Test dependencies | None | Temp dir | Temp `.db` file | Docker |

---

## Project Structure

```
DeepSigma.Persistance/
├── Directory.Build.props          # Shared: version, TFM, nullable, warnings-as-errors
├── Directory.Packages.props       # Central package versioning (CPM)
├── nuget.config                   # Scoped to nuget.org (avoids NU1507 with private feeds)
├── DeepSigma.Persistance.slnx
│
├── src/
│   ├── DeepSigma.Persistance.Abstractions/
│   │   ├── IRepository.cs
│   │   ├── IBulkRepository.cs
│   │   ├── IExpiringRepository.cs
│   │   ├── IJsonValueSerializer.cs
│   │   ├── SetOptions.cs
│   │   ├── PersistenceOptions.cs
│   │   └── Exceptions/
│   │       ├── PersistenceException.cs
│   │       ├── SerializationException.cs
│   │       └── SchemaVersionException.cs
│   │
│   ├── DeepSigma.Persistance.Core/
│   │   ├── Envelope.cs                        # Versioned wrapper (V=1) stored by file backend
│   │   ├── JsonValueSerializer.cs             # System.Text.Json implementation of IJsonValueSerializer
│   │   ├── JsonValueSerializerExtensions.cs   # SerializeToString / DeserializeFromString helpers
│   │   ├── KeyValidator.cs                    # Validate / ValidateAll — null / empty / null-byte / length checks
│   │   ├── KeyHasher.cs                       # SHA-256 hex + 2-char shard prefix
│   │   ├── SqlPrefix.cs                       # Shared LIKE-prefix escape + clause builder for SQL backends
│   │   └── ServiceCollectionExtensions.cs     # AddPersistenceCore + AddRepositoryImplementation(typeof(...<>))
│   │
│   ├── DeepSigma.Persistance.InMemory/
│   │   ├── InMemoryOptions.cs
│   │   ├── InMemoryStore.cs        # Singleton ConcurrentDictionary + optional purge task
│   │   ├── InMemoryRepository.cs
│   │   └── ServiceCollectionExtensions.cs
│   │
│   ├── DeepSigma.Persistance.FileSystem/
│   │   ├── FileSystemOptions.cs
│   │   ├── FileSystemRepository.cs
│   │   ├── FileSystemPurgeService.cs
│   │   └── ServiceCollectionExtensions.cs
│   │
│   ├── DeepSigma.Persistance.Sqlite/
│   │   ├── SqliteOptions.cs
│   │   ├── SqliteMigrations.cs
│   │   ├── SqliteRepository.cs
│   │   ├── SqlitePurgeService.cs
│   │   ├── ServiceCollectionExtensions.cs
│   │   └── Migrations/
│   │       └── 001_initial.sql
│   │
│   └── DeepSigma.Persistance.Postgres/
│       ├── PostgresOptions.cs
│       ├── PostgresMigrations.cs
│       ├── PostgresRepository.cs
│       ├── PostgresPurgeService.cs
│       ├── ServiceCollectionExtensions.cs
│       └── Migrations/
│           └── 001_initial.sql
│
└── tests/
    ├── DeepSigma.Persistance.Abstractions.Testing/
    │   ├── RepositoryContractTests.cs         # base CRUD + listing + key validation
    │   ├── BulkRepositoryContractTests.cs     # GetMany / SetMany / DeleteMany
    │   └── ExpiringRepositoryContractTests.cs # TTL, purge, GetTtl, SetTtl
    │
    ├── DeepSigma.Persistance.InMemory.Tests/
    ├── DeepSigma.Persistance.FileSystem.Tests/
    ├── DeepSigma.Persistance.Sqlite.Tests/
    └── DeepSigma.Persistance.Postgres.Tests/   # Requires Docker (Testcontainers)
```

---

## Running Tests

```bash
# All backends
dotnet test

# One backend
dotnet test tests/DeepSigma.Persistance.Sqlite.Tests

# Postgres requires Docker Desktop to be running
dotnet test tests/DeepSigma.Persistance.Postgres.Tests
```

Each backend runs the same 59-test contract suite (`RepositoryContractTests` → `BulkRepositoryContractTests` → `ExpiringRepositoryContractTests`). Tests are isolated: InMemory creates a fresh instance per test, SQLite and FileSystem use unique temp paths, Postgres spins up one Testcontainers container and uses a separate table per test.

Current status: **236 / 236 passing** across all backends.

---

## Design Notes

**Keys** are strings only. Max length 512 characters (configurable via `PersistenceOptions.MaxKeyLength`). Case-sensitive, null bytes are rejected.

**Swappability** is enforced through open-generic DI registration (`typeof(IRepository<>)` → `typeof(SqliteRepository<>)`). Changing backends is a one-line change in the DI setup.

**Schema migrations** (SQLite and Postgres) are tracked in a `persistence_migrations` table. Set `AutoMigrate = false` if you want the application to throw `SchemaVersionException` rather than apply migrations automatically — useful for controlled production deployments.

**TTL precision:**
- SQLite stores timestamps as `"yyyy-MM-dd HH:mm:ss.fff"` and compares against `strftime('%Y-%m-%d %H:%M:%f', 'now')` for millisecond accuracy.
- Postgres uses native `TIMESTAMPTZ` with microsecond precision and `NOW()`.

**FileSystem locking** is in-process only using `AsyncKeyedLock`. Each key has its own semaphore (keyed on SHA-256 hash) so writes to different keys never contend with each other.

**Envelope versioning** — SQLite and Postgres back-ends do not use the envelope; they store the JSON-serialised value directly. The FileSystem backend wraps each value in `Envelope<TValue> { V = 1, Key, Value, CreatedAt, UpdatedAt, ExpiresAt }` to enable future lazy migration without a full re-write.

**Serialization** — every backend stores JSON-shaped data. The override surface is a configurable `IJsonValueSerializer`:

| Backend | What it serializes | Override surface |
|---|---|---|
| InMemory | Nothing — values are stored as live objects | N/A |
| FileSystem | `Envelope<TValue>` via `System.Text.Json` directly | None — the envelope is the file content |
| SQLite | `TValue` via `IJsonValueSerializer`, stored as TEXT | Custom `IJsonValueSerializer` registration |
| Postgres | `TValue` via `IJsonValueSerializer`, stored as JSONB | Custom `IJsonValueSerializer` registration |

The interface is intentionally JSON-only: Postgres `JSONB` requires JSON, FileSystem uses `System.Text.Json` directly, and SQLite stores TEXT that conventionally holds JSON. Non-JSON binary formats (MessagePack, protobuf) are not currently supported via this hook.

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

To customize JSON behaviour (camelCase, custom converters, AOT/trimming via a source-generated `JsonSerializerContext`), construct a `JsonValueSerializer` with a configured `JsonSerializerOptions` and register it before calling the backend extension:

```csharp
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() },
    // For AOT / trimming, attach a source-generated context:
    // TypeInfoResolver = MyJsonContext.Default,
};

builder.Services.AddSingleton<IJsonValueSerializer>(new JsonValueSerializer(jsonOptions));
builder.Services.AddSqlitePersistence(o => { o.ConnectionString = "Data Source=app.db"; });
```

`AddSqlitePersistence` and `AddPostgresPersistence` call `AddPersistenceCore()` internally, which uses `TryAddSingleton` — the registration above wins because it ran first.

---

## Building Your Own Backend

The `DeepSigma.Persistance.Core` package exposes the helpers used by the built-in backends so a third-party backend stays consistent with the rest:

- `SqlPrefix.Build(prefix)` — produces the `LIKE @Prefix ESCAPE '\\'` fragment and the escaped value
- `JsonValueSerializerExtensions.SerializeToString` / `DeserializeFromString` — UTF-8 string conversion around `IJsonValueSerializer`
- `KeyValidator.Validate` / `ValidateAll` — single-key and streaming validation
- `KeyHasher.Hash` / `Shard` — SHA-256 hex digest and 2-char shard prefix
- `Envelope<TValue>` — versioned wrapper if your backend stores values with metadata
- `services.AddRepositoryImplementation(typeof(MyRepository<>))` — registers the open-generic repository as all three interfaces in one call

Your backend's `AddXxxPersistence` extension then looks like:

```csharp
public static IServiceCollection AddRedisPersistence(
    this IServiceCollection services,
    Action<RedisOptions> configure)
{
    var options = new RedisOptions { ConnectionString = "" };
    configure(options);
    services.TryAddSingleton(options);
    services.AddRepositoryImplementation(typeof(RedisRepository<>));
    return services;
}
```

To validate behaviour, inherit `ExpiringRepositoryContractTests<MyRepository<string>>` from `DeepSigma.Persistance.Abstractions.Testing` — the same 59-test suite that exercises every built-in backend.

---

## License

MIT — see [LICENSE](LICENSE) for details.
