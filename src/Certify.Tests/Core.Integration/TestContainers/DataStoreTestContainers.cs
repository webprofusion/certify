using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

namespace Certify.Core.Tests.DataStores
{
    internal static class DataStoreTestContainers
    {
        private static readonly SemaphoreSlim InitLock = new SemaphoreSlim(1, 1);
        private static int _referenceCount;
        private static bool _initialized;

        private const string PostgresSchemaSql = "CREATE TABLE IF NOT EXISTS manageditem (id TEXT NOT NULL PRIMARY KEY, itemtype TEXT NOT NULL, instanceid TEXT NOT NULL DEFAULT '', config JSONB NOT NULL, itemvalue TEXT NULL);";
        private const string SqlServerSchemaSql = @"IF OBJECT_ID('manageditem', 'U') IS NULL
BEGIN
    CREATE TABLE manageditem (
        id NVARCHAR(64) NOT NULL PRIMARY KEY,
        itemtype NVARCHAR(100) NOT NULL,
        instanceid NVARCHAR(64) NOT NULL DEFAULT '',
        config NVARCHAR(MAX) NOT NULL,
        itemvalue NVARCHAR(MAX) NULL
    );
END";

        public static PostgreSqlContainer PostgresContainer { get; private set; }
        public static MsSqlContainer SqlServerContainer { get; private set; }

        public static string PostgresConnectionString { get; private set; }
        public static string SqlServerConnectionString { get; private set; }

        public static async Task InitializeAsync()
        {
            await InitLock.WaitAsync();
            try
            {
                _referenceCount++;
                if (_initialized)
                {
                    return;
                }

                PostgresContainer = new PostgreSqlBuilder("certify-postgres-test")
                    .WithImage("postgres:16-alpine")
                    .WithDatabase("certify")
                    .WithUsername("certify")
                    .WithPassword("certify")
                    .Build();

                await PostgresContainer.StartAsync();
                PostgresConnectionString = PostgresContainer.GetConnectionString();
                await EnsurePostgresSchema(PostgresConnectionString);

                SqlServerContainer = new MsSqlBuilder("certify-sqlserver-test")
                    .Build();

                await SqlServerContainer.StartAsync();
                SqlServerConnectionString = SqlServerContainer.GetConnectionString() + ";TrustServerCertificate=True";
                await EnsureSqlServerSchema(SqlServerConnectionString);

                _initialized = true;
            }
            finally
            {
                InitLock.Release();
            }
        }

        public static async Task DisposeAsync()
        {
            await InitLock.WaitAsync();
            try
            {
                _referenceCount--;
                if (_referenceCount > 0)
                {
                    return;
                }

                if (PostgresContainer != null)
                {
                    await PostgresContainer.DisposeAsync();
                    PostgresContainer = null;
                }

                if (SqlServerContainer != null)
                {
                    await SqlServerContainer.DisposeAsync();
                    SqlServerContainer = null;
                }

                PostgresConnectionString = null;
                SqlServerConnectionString = null;
                _initialized = false;
            }
            finally
            {
                InitLock.Release();
            }
        }

        private static async Task EnsurePostgresSchema(string connectionString)
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(PostgresSchemaSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task EnsureSqlServerSchema(string connectionString)
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(SqlServerSchemaSql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
