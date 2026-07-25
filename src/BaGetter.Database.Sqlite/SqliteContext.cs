using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaGetter.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BaGetter.Database.Sqlite;

public class SqliteContext : AbstractContext<SqliteContext>
{
    private readonly DatabaseOptions _bagetterOptions;

    /// <summary>
    /// The Sqlite error code for when a unique constraint is violated.
    /// </summary>
    private const int SqliteUniqueConstraintViolationErrorCode = 19;

    public SqliteContext(DbContextOptions<SqliteContext> efOptions, IOptionsSnapshot<BaGetterOptions> bagetterOptions)
        : base(efOptions)
    {
        _bagetterOptions = bagetterOptions.Value.Database;
    }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteException &&
            sqliteException.SqliteErrorCode == SqliteUniqueConstraintViolationErrorCode;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Package>()
            .Property(p => p.Id)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<Package>()
            .Property(p => p.NormalizedVersionString)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<PackageDependency>()
            .Property(d => d.Id)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<PackageType>()
            .Property(t => t.Name)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<TargetFramework>()
            .Property(f => f.Moniker)
            .HasColumnType("TEXT COLLATE NOCASE");
    }

    public override async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        if (Database.GetDbConnection() is SqliteConnection connection)
        {
            /* Create the folder of the Sqlite blob if it does not exist. */
            EnsureDataSourceDirectoryExists(connection);
        }

        await base.RunMigrationsAsync(cancellationToken);
        await ApplyRuntimePragmasAsync(cancellationToken);
    }

    /// <summary>
    /// Creates directories specified in the Database::ConnectionString config for the Sqlite database file.
    /// </summary>
    /// <param name="connection">Instance of the <see cref="SqliteConnection"/>.</param>
    private static void EnsureDataSourceDirectoryExists(SqliteConnection connection)
    {
        var pathToCreate = Path.GetDirectoryName(connection.DataSource);

        if (string.IsNullOrWhiteSpace(pathToCreate)) return;

        Directory.CreateDirectory(pathToCreate);
    }

    private async Task ApplyRuntimePragmasAsync(CancellationToken cancellationToken)
    {
        if (Database.GetDbConnection() is not SqliteConnection connection)
        {
            return;
        }

        var shouldCloseConnection = connection.State == ConnectionState.Closed;
        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            // FULL synchronous is the historical BaGet default; keep durability.
            await Database.ExecuteSqlRawAsync("PRAGMA synchronous = '1';", cancellationToken);
            await Database.ExecuteSqlRawAsync("PRAGMA journal_mode = 'WAL';", cancellationToken);

            // Cap how long SQLite waits for a busy (locked) database before failing.
            // The legacy default let Microsoft.Data.Sqlite hold a command open for its
            // full 30s CommandTimeout, which under write contention cascaded into
            // threadpool runaway. 5s is plenty for the (now rare) contention that the
            // background-batched download counter leaves behind.
            await Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 5000;", cancellationToken);
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Drop "Cache=Shared" from connection strings before reaching here (it forces
            // shared-cache table-level locking, which surfaces write races as the
            // non-retriable SQLITE_LOCKED instead of the retriable SQLITE_BUSY).
            optionsBuilder.UseSqlite(_bagetterOptions.ConnectionString, sqlite =>
            {
                // Fail fast instead of stalling the worker for the EF default 30s on the
                // rare lock contention that remains after background-batching the writes.
                sqlite.CommandTimeout(10);
            });
        }
    }
}
