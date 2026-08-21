using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Certify.Models.Config;
using Certify.Models.Providers;

namespace Certify.Providers
{
    /// <summary>
    /// The state of a data store schema relative to the schema this version of the application requires
    /// </summary>
    public enum DataStoreSchemaState
    {
        /// <summary>
        /// The schema state could not be determined, e.g. the database could not be reached
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The database is reachable but the expected tables do not exist yet
        /// </summary>
        NotPresent = 1,

        /// <summary>
        /// The schema exists but one or more migrations need to be applied before it can be used
        /// </summary>
        MigrationRequired = 2,

        /// <summary>
        /// The schema can be used as it is. Optional migrations may still be available - see
        /// <see cref="DataStoreSchemaCheckResult.OptionalMigrations"/>
        /// </summary>
        Current = 3
    }

    /// <summary>
    /// A single schema migration step which can be independently detected and applied
    /// </summary>
    public class DataStoreSchemaMigration
    {
        /// <summary>
        /// Stable identifier for this migration step
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Human readable description of what the step changes
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// True if the data store continues to work without this step being applied, in which case it is only
        /// ever applied when the operator explicitly asks for it. Optional steps are structural changes to an
        /// existing table (as opposed to additive columns and indexes) so they are not applied unattended.
        /// </summary>
        public bool IsOptional { get; set; }

        /// <summary>
        /// Human readable explanation of what applying an optional step gains, and what continuing without it
        /// means
        /// </summary>
        public string OptionalReason { get; set; } = string.Empty;
    }

    /// <summary>
    /// The result of inspecting a data store schema without modifying it
    /// </summary>
    public class DataStoreSchemaCheckResult
    {
        public DataStoreSchemaState State { get; set; } = DataStoreSchemaState.Unknown;

        /// <summary>
        /// All migration steps which have not been applied, in the order they would be applied
        /// </summary>
        public List<DataStoreSchemaMigration> PendingMigrations { get; set; } = new List<DataStoreSchemaMigration>();

        /// <summary>
        /// Pending steps which have to be applied before the data store can be used
        /// </summary>
        public List<DataStoreSchemaMigration> RequiredMigrations => PendingMigrations.Where(m => !m.IsOptional).ToList();

        /// <summary>
        /// Pending steps which are recommended but which the data store works without
        /// </summary>
        public List<DataStoreSchemaMigration> OptionalMigrations => PendingMigrations.Where(m => m.IsOptional).ToList();

        /// <summary>
        /// True if the credentials used for this check are able to perform schema changes. Runtime credentials
        /// are commonly limited to reading and writing data, in which case migrations must be applied using a
        /// separate data store connection configured with credentials which do have schema modification rights.
        /// </summary>
        public bool CanApplySchemaChanges { get; set; }

        /// <summary>
        /// True if migrations need to be applied before the data store can be used
        /// </summary>
        public bool IsMigrationRequired => State == DataStoreSchemaState.NotPresent || State == DataStoreSchemaState.MigrationRequired;

        /// <summary>
        /// True if the store is usable as it is but a recommended upgrade is available
        /// </summary>
        public bool HasOptionalMigrations => OptionalMigrations.Count > 0;

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Stable keys for the ActionStep results returned by data store connection tests and migrations, so that
    /// UI can identify a specific result without matching on the display title.
    /// </summary>
    public static class DataStoreActionKeys
    {
        /// <summary>
        /// The schema needs creating or migrating before the data store can be used. HasError is set when the
        /// credentials on the tested connection are not able to apply the change themselves.
        /// </summary>
        public const string SchemaMigrationRequired = "datastore-schema-migration-required";

        /// <summary>
        /// The data store works as it is, but a recommended schema upgrade is available. Never reported as an
        /// error - an existing installation is not obliged to apply it.
        /// </summary>
        public const string SchemaUpgradeAvailable = "datastore-schema-upgrade-available";

        public const string ConnectionFailed = "datastore-connection-failed";
        public const string ConnectionOK = "datastore-connection-ok";
        public const string InitFailed = "datastore-init-failed";
        public const string ApplyMigrations = "datastore-apply-migrations";
    }

    /// <summary>
    /// Implemented by data store providers which maintain their own database schema, allowing schema checks
    /// and migrations to be performed as an explicit operation rather than implicitly on connection. This lets
    /// the runtime database user be restricted to reading and writing data, with schema changes applied
    /// separately using a connection which has schema modification rights.
    /// </summary>
    public interface IDataStoreSchemaProvider
    {
        /// <summary>
        /// Inspect the schema for the given connection without modifying it
        /// </summary>
        Task<DataStoreSchemaCheckResult> CheckSchema(string connectionString, ILog log = null);

        /// <summary>
        /// Apply pending schema migrations, creating the schema first if it is not present. Requires the
        /// connection to have schema modification rights.
        /// </summary>
        /// <param name="includeOptional">
        /// When true (the operator explicitly asked to upgrade) optional structural steps are applied as well.
        /// When false only the steps needed for the store to function are applied.
        /// </param>
        Task<ActionResult<List<DataStoreSchemaMigration>>> ApplySchemaMigrations(string connectionString, ILog log = null, bool includeOptional = true);
    }
}
