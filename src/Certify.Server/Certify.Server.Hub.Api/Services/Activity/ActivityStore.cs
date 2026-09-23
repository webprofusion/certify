using System.Globalization;
using System.Text.Json;
using Certify.Models;
using Certify.Models.Hub;
using Certify.Models.Reporting;
using Certify.Shared;
using Microsoft.Data.Sqlite;

namespace Certify.Server.Hub.Api.Services.Activity
{
    /// <summary>
    /// The columns of a stored activity event or request run that decide whether a caller may see it, read without
    /// reading the whole record
    /// </summary>
    public readonly record struct ActivityRecordScope(string? InstanceId, string? ManagedItemId, ActivityCategory Category);

    /// <summary>
    /// Persists the hub activity history (activity events, request runs and daily status snapshots) in a SQLite
    /// database held by the hub alongside its other local data. The history is the hub's own record of what it has
    /// seen, so it is kept locally rather than in the configured data store, which holds configuration shared with
    /// managed instances.
    /// </summary>
    public class ActivityStore
    {
        private const int SchemaVersion = 1;

        private readonly string _connectionString;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly ILogger<ActivityStore>? _logger;
        private bool _isInitialised;

        /// <summary>
        /// Create a store using the database file at the given path
        /// </summary>
        public ActivityStore(string databasePath, ILogger<ActivityStore>? logger = null)
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true
            }.ToString();

            _logger = logger;
        }

        /// <summary>
        /// Default location of the hub activity database
        /// </summary>
        public static string GetDefaultDatabasePath() => Path.Combine(EnvironmentUtil.EnsuredAppDataPath("activity"), "activity.db");

        /// <summary>
        /// Create the database and its tables if they do not exist
        /// </summary>
        public async Task InitAsync()
        {
            if (_isInitialised)
            {
                return;
            }

            await _writeLock.WaitAsync();

            try
            {
                if (_isInitialised)
                {
                    return;
                }

                await using var connection = await OpenAsync();

                await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;");

                await ExecuteAsync(connection, """
                    CREATE TABLE IF NOT EXISTS activity_event (
                        id TEXT NOT NULL PRIMARY KEY,
                        ts INTEGER NOT NULL,
                        instance_id TEXT NULL,
                        item_id TEXT NULL,
                        run_id TEXT NULL,
                        batch_id TEXT NULL,
                        category INTEGER NOT NULL,
                        event_type TEXT NOT NULL,
                        status INTEGER NOT NULL,
                        is_problem INTEGER NOT NULL,
                        search TEXT NULL,
                        json TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ix_activity_event_ts ON activity_event(ts);
                    CREATE INDEX IF NOT EXISTS ix_activity_event_instance ON activity_event(instance_id, ts);
                    CREATE INDEX IF NOT EXISTS ix_activity_event_item ON activity_event(item_id, ts);
                    CREATE INDEX IF NOT EXISTS ix_activity_event_type ON activity_event(event_type, ts);

                    CREATE TABLE IF NOT EXISTS request_run (
                        run_id TEXT NOT NULL PRIMARY KEY,
                        instance_id TEXT NULL,
                        item_id TEXT NULL,
                        batch_id TEXT NULL,
                        started INTEGER NOT NULL,
                        completed INTEGER NULL,
                        outcome INTEGER NOT NULL,
                        search TEXT NULL,
                        json TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ix_request_run_started ON request_run(started);
                    CREATE INDEX IF NOT EXISTS ix_request_run_completed ON request_run(completed);
                    CREATE INDEX IF NOT EXISTS ix_request_run_item ON request_run(item_id, started);
                    CREATE INDEX IF NOT EXISTS ix_request_run_instance ON request_run(instance_id, started);

                    CREATE TABLE IF NOT EXISTS status_snapshot (
                        instance_id TEXT NOT NULL,
                        ts INTEGER NOT NULL,
                        json TEXT NOT NULL,
                        PRIMARY KEY (instance_id, ts)
                    );
                    """);

                await ExecuteAsync(connection, $"PRAGMA user_version={SchemaVersion};");

                _isInitialised = true;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Record an activity event. An event already recorded (the same id) is left as it is, so an instance sending
        /// held events again after a reconnect does not duplicate them.
        /// </summary>
        public async Task<bool> AddEventAsync(ActivityEvent activityEvent)
        {
            await InitAsync();
            await _writeLock.WaitAsync();

            try
            {
                await using var connection = await OpenAsync();
                await using var cmd = connection.CreateCommand();

                cmd.CommandText = """
                    INSERT OR IGNORE INTO activity_event (id, ts, instance_id, item_id, run_id, batch_id, category, event_type, status, is_problem, search, json)
                    VALUES ($id, $ts, $instance, $item, $run, $batch, $category, $type, $status, $problem, $search, $json);
                    """;

                cmd.Parameters.AddWithValue("$id", activityEvent.Id);
                cmd.Parameters.AddWithValue("$ts", activityEvent.Timestamp.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$instance", (object?)activityEvent.InstanceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$item", (object?)activityEvent.ManagedItemId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$run", (object?)activityEvent.RunId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$batch", (object?)activityEvent.BatchId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$category", (int)activityEvent.Category);
                cmd.Parameters.AddWithValue("$type", activityEvent.EventType);
                cmd.Parameters.AddWithValue("$status", (int)activityEvent.Status);
                cmd.Parameters.AddWithValue("$problem", activityEvent.IsProblem ? 1 : 0);
                cmd.Parameters.AddWithValue("$search", ToSearchText(activityEvent.Title, activityEvent.Detail, activityEvent.ItemTitle, activityEvent.Actor));
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(activityEvent, JsonOptions.DefaultJsonSerializerOptions));

                return await cmd.ExecuteNonQueryAsync() > 0;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Query activity events, newest first. The optional filter decides which events the caller may see; it is
        /// given only the columns needed to decide that, so the whole of each event is only read for the page returned.
        /// </summary>
        public async Task<(List<ActivityEvent> Results, long Total)> QueryEventsAsync(ActivityQuery query, Func<ActivityRecordScope, bool>? filter = null)
        {
            await InitAsync();

            var where = new List<string>();
            var parameters = new Dictionary<string, object>();

            AddTimeRange(where, parameters, "ts", query.From, query.To);
            AddEquals(where, parameters, "instance_id", query.InstanceId);
            AddEquals(where, parameters, "item_id", query.ManagedItemId);
            AddEquals(where, parameters, "run_id", query.RunId);
            AddEquals(where, parameters, "batch_id", query.BatchId);
            AddKeyword(where, parameters, query.Keyword);

            if (query.Categories?.Count > 0)
            {
                where.Add($"category IN ({string.Join(",", query.Categories.Select(c => (int)c))})");
            }

            if (query.ProblemsOnly)
            {
                where.Add("is_problem = 1");
            }

            var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 500);
            var pageIndex = Math.Max(0, query.PageIndex);

            await using var connection = await OpenAsync();

            var ids = await QueryPageIdsAsync(connection, "activity_event", "id", "ts DESC, id DESC", where, parameters, pageIndex, pageSize, filter,
                r => new ActivityRecordScope(r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), (ActivityCategory)r.GetInt32(3)),
                "id, instance_id, item_id, category");

            var results = await ReadByIdsAsync<ActivityEvent>(connection, "activity_event", "id", ids.PageIds);

            return (results.OrderByDescending(e => e.Timestamp).ThenByDescending(e => e.Id).ToList(), ids.Total);
        }

        /// <summary>
        /// Get the events of the given types in a period, oldest first
        /// </summary>
        public async Task<List<ActivityEvent>> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, IEnumerable<string> eventTypes, string? instanceId = null)
        {
            await InitAsync();

            var where = new List<string>();
            var parameters = new Dictionary<string, object>();

            AddTimeRange(where, parameters, "ts", from, to);
            AddEquals(where, parameters, "instance_id", instanceId);

            var types = eventTypes.ToList();
            var typeParams = types.Select((t, i) => $"$type{i}").ToList();

            for (var i = 0; i < types.Count; i++)
            {
                parameters[typeParams[i]] = types[i];
            }

            if (types.Count > 0)
            {
                where.Add($"event_type IN ({string.Join(",", typeParams)})");
            }

            await using var connection = await OpenAsync();
            await using var cmd = connection.CreateCommand();

            cmd.CommandText = $"SELECT json FROM activity_event {ToWhereClause(where)} ORDER BY ts ASC, id ASC;";
            AddParameters(cmd, parameters);

            return await ReadJsonAsync<ActivityEvent>(cmd);
        }

        /// <summary>
        /// Get the most recent event of each of the given types for each instance, e.g. the last connection change
        /// </summary>
        public async Task<List<ActivityEvent>> GetLatestEventsByInstanceAsync(IEnumerable<string> eventTypes, DateTimeOffset? before = null)
        {
            await InitAsync();

            var types = eventTypes.ToList();

            if (types.Count == 0)
            {
                return [];
            }

            await using var connection = await OpenAsync();
            await using var cmd = connection.CreateCommand();

            var typeParams = types.Select((t, i) => $"$type{i}").ToList();

            for (var i = 0; i < types.Count; i++)
            {
                cmd.Parameters.AddWithValue(typeParams[i], types[i]);
            }

            var beforeClause = before.HasValue ? "AND ts < $before" : "";

            if (before.HasValue)
            {
                cmd.Parameters.AddWithValue("$before", before.Value.ToUnixTimeMilliseconds());
            }

            cmd.CommandText = $"""
                SELECT e.json FROM activity_event e
                INNER JOIN (
                    SELECT instance_id, MAX(ts) AS max_ts FROM activity_event
                    WHERE event_type IN ({string.Join(",", typeParams)}) AND instance_id IS NOT NULL {beforeClause}
                    GROUP BY instance_id
                ) latest ON latest.instance_id = e.instance_id AND latest.max_ts = e.ts
                WHERE e.event_type IN ({string.Join(",", typeParams)});
                """;

            var events = await ReadJsonAsync<ActivityEvent>(cmd);

            // two events with the same timestamp resolve to the one with the latest id
            return events
                .GroupBy(e => e.InstanceId)
                .Select(g => g.OrderByDescending(e => e.Id).First())
                .ToList();
        }

        /// <summary>
        /// Add or replace a request run
        /// </summary>
        public async Task UpsertRunAsync(RequestRun run)
        {
            await InitAsync();
            await _writeLock.WaitAsync();

            try
            {
                await using var connection = await OpenAsync();
                await using var cmd = connection.CreateCommand();

                cmd.CommandText = """
                    INSERT OR REPLACE INTO request_run (run_id, instance_id, item_id, batch_id, started, completed, outcome, search, json)
                    VALUES ($run, $instance, $item, $batch, $started, $completed, $outcome, $search, $json);
                    """;

                cmd.Parameters.AddWithValue("$run", run.RunId);
                cmd.Parameters.AddWithValue("$instance", (object?)run.InstanceId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$item", (object?)run.ManagedItemId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$batch", (object?)run.BatchId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$started", run.Started.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$completed", run.Completed.HasValue ? run.Completed.Value.ToUnixTimeMilliseconds() : DBNull.Value);
                cmd.Parameters.AddWithValue("$outcome", (int)run.Outcome);
                cmd.Parameters.AddWithValue("$search", ToSearchText(run.ItemTitle, run.Message, run.TriggeredBy));
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(run, JsonOptions.DefaultJsonSerializerOptions));

                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Get a request run, including its messages
        /// </summary>
        public async Task<RequestRun?> GetRunAsync(string runId)
        {
            await InitAsync();

            await using var connection = await OpenAsync();
            var results = await ReadByIdsAsync<RequestRun>(connection, "request_run", "run_id", [runId]);

            return results.FirstOrDefault();
        }

        /// <summary>
        /// Query request runs, newest first. Messages are left out of the results; fetch a single run for those.
        /// </summary>
        public async Task<(List<RequestRun> Results, long Total)> QueryRunsAsync(RequestRunQuery query, Func<ActivityRecordScope, bool>? filter = null)
        {
            await InitAsync();

            var where = new List<string>();
            var parameters = new Dictionary<string, object>();

            AddTimeRange(where, parameters, "started", query.From, query.To);
            AddEquals(where, parameters, "instance_id", query.InstanceId);
            AddEquals(where, parameters, "item_id", query.ManagedItemId);
            AddEquals(where, parameters, "batch_id", query.BatchId);
            AddKeyword(where, parameters, query.Keyword);

            if (query.Outcomes?.Count > 0)
            {
                where.Add($"outcome IN ({string.Join(",", query.Outcomes.Select(o => (int)o))})");
            }

            var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 500);
            var pageIndex = Math.Max(0, query.PageIndex);

            await using var connection = await OpenAsync();

            var ids = await QueryPageIdsAsync(connection, "request_run", "run_id", "started DESC, run_id DESC", where, parameters, pageIndex, pageSize, filter,
                r => new ActivityRecordScope(r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), ActivityCategory.Request),
                "run_id, instance_id, item_id");

            var results = await ReadByIdsAsync<RequestRun>(connection, "request_run", "run_id", ids.PageIds);

            foreach (var run in results)
            {
                run.Messages = [];
            }

            return (results.OrderByDescending(r => r.Started).ThenByDescending(r => r.RunId).ToList(), ids.Total);
        }

        /// <summary>
        /// The outcome, completion time and subject of each run completed in a period, for daily totals
        /// </summary>
        public async Task<List<(DateTimeOffset Completed, RequestState Outcome, ActivityRecordScope Scope)>> GetRunOutcomesAsync(DateTimeOffset from, DateTimeOffset to)
        {
            await InitAsync();

            await using var connection = await OpenAsync();
            await using var cmd = connection.CreateCommand();

            cmd.CommandText = "SELECT completed, outcome, instance_id, item_id FROM request_run WHERE completed >= $from AND completed < $to;";
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());

            var results = new List<(DateTimeOffset, RequestState, ActivityRecordScope)>();

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    (RequestState)reader.GetInt32(1),
                    new ActivityRecordScope(reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), ActivityCategory.Request)));
            }

            return results;
        }

        /// <summary>
        /// The time and subject of each event of a type in a period, for daily totals
        /// </summary>
        public async Task<List<(DateTimeOffset Timestamp, ActivityRecordScope Scope)>> GetEventTimesAsync(string eventType, DateTimeOffset from, DateTimeOffset to)
        {
            await InitAsync();

            await using var connection = await OpenAsync();
            await using var cmd = connection.CreateCommand();

            cmd.CommandText = "SELECT ts, instance_id, item_id, category FROM activity_event WHERE event_type = $type AND ts >= $from AND ts < $to;";
            cmd.Parameters.AddWithValue("$type", eventType);
            cmd.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());

            var results = new List<(DateTimeOffset, ActivityRecordScope)>();

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add((
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    new ActivityRecordScope(reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), (ActivityCategory)reader.GetInt32(3))));
            }

            return results;
        }

        /// <summary>
        /// Record an instance's status summary as it stood at a point in time
        /// </summary>
        public async Task AddStatusSnapshotAsync(string instanceId, DateTimeOffset taken, StatusSummary summary)
        {
            await InitAsync();
            await _writeLock.WaitAsync();

            try
            {
                await using var connection = await OpenAsync();
                await using var cmd = connection.CreateCommand();

                cmd.CommandText = "INSERT OR REPLACE INTO status_snapshot (instance_id, ts, json) VALUES ($instance, $ts, $json);";
                cmd.Parameters.AddWithValue("$instance", instanceId);
                cmd.Parameters.AddWithValue("$ts", taken.ToUnixTimeMilliseconds());
                cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(summary, JsonOptions.DefaultJsonSerializerOptions));

                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// The time of the most recent status snapshot for each instance
        /// </summary>
        public async Task<Dictionary<string, DateTimeOffset>> GetLatestSnapshotTimesAsync()
        {
            await InitAsync();

            await using var connection = await OpenAsync();
            await using var cmd = connection.CreateCommand();

            cmd.CommandText = "SELECT instance_id, MAX(ts) FROM status_snapshot GROUP BY instance_id;";

            var results = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results[reader.GetString(0)] = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
            }

            return results;
        }

        /// <summary>
        /// For each instance, the most recent status snapshot taken at or before the given time
        /// </summary>
        public async Task<Dictionary<string, StatusSummary>> GetStatusSnapshotsAtAsync(DateTimeOffset at)
        {
            await InitAsync();

            await using var connection = await OpenAsync();
            await using var cmd = connection.CreateCommand();

            cmd.CommandText = """
                SELECT s.instance_id, s.json FROM status_snapshot s
                INNER JOIN (
                    SELECT instance_id, MAX(ts) AS max_ts FROM status_snapshot WHERE ts <= $at GROUP BY instance_id
                ) latest ON latest.instance_id = s.instance_id AND latest.max_ts = s.ts;
                """;
            cmd.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds());

            var results = new Dictionary<string, StatusSummary>(StringComparer.OrdinalIgnoreCase);

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var summary = JsonSerializer.Deserialize<StatusSummary>(reader.GetString(1), JsonOptions.DefaultJsonSerializerOptions);

                if (summary != null)
                {
                    results[reader.GetString(0)] = summary;
                }
            }

            return results;
        }

        /// <summary>
        /// Remove everything recorded before the given time. Returns the number of records removed.
        /// </summary>
        public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff)
        {
            await InitAsync();
            await _writeLock.WaitAsync();

            try
            {
                await using var connection = await OpenAsync();
                await using var cmd = connection.CreateCommand();

                cmd.CommandText = """
                    DELETE FROM activity_event WHERE ts < $cutoff;
                    DELETE FROM request_run WHERE started < $cutoff;
                    DELETE FROM status_snapshot WHERE ts < $cutoff;
                    """;
                cmd.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());

                var removed = await cmd.ExecuteNonQueryAsync();

                if (removed > 0)
                {
                    await ExecuteAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE);");
                }

                return removed;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout=5000;";
            await cmd.ExecuteNonQueryAsync();

            return connection;
        }

        private static async Task ExecuteAsync(SqliteConnection connection, string sql)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Find the ids of one page of matching records, and the total number of matches. With a filter, candidates are
        /// read in order (only the columns the filter needs) and counted in memory; without one the database does both.
        /// </summary>
        private static async Task<(List<string> PageIds, long Total)> QueryPageIdsAsync(
            SqliteConnection connection,
            string table,
            string idColumn,
            string orderBy,
            List<string> where,
            Dictionary<string, object> parameters,
            int pageIndex,
            int pageSize,
            Func<ActivityRecordScope, bool>? filter,
            Func<SqliteDataReader, ActivityRecordScope> readScope,
            string scopeColumns)
        {
            var whereClause = ToWhereClause(where);

            if (filter == null)
            {
                long total;

                await using (var countCmd = connection.CreateCommand())
                {
                    countCmd.CommandText = $"SELECT COUNT(*) FROM {table} {whereClause};";
                    AddParameters(countCmd, parameters);
                    total = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);
                }

                await using var pageCmd = connection.CreateCommand();
                pageCmd.CommandText = $"SELECT {idColumn} FROM {table} {whereClause} ORDER BY {orderBy} LIMIT $limit OFFSET $offset;";
                AddParameters(pageCmd, parameters);
                pageCmd.Parameters.AddWithValue("$limit", pageSize);
                pageCmd.Parameters.AddWithValue("$offset", pageIndex * pageSize);

                var ids = new List<string>();

                await using var reader = await pageCmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    ids.Add(reader.GetString(0));
                }

                return (ids, total);
            }
            else
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT {scopeColumns} FROM {table} {whereClause} ORDER BY {orderBy};";
                AddParameters(cmd, parameters);

                var ids = new List<string>();
                long matched = 0;
                var skip = (long)pageIndex * pageSize;

                await using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    if (!filter(readScope(reader)))
                    {
                        continue;
                    }

                    if (matched >= skip && ids.Count < pageSize)
                    {
                        ids.Add(reader.GetString(0));
                    }

                    matched++;
                }

                return (ids, matched);
            }
        }

        private static async Task<List<T>> ReadByIdsAsync<T>(SqliteConnection connection, string table, string idColumn, List<string> ids)
        {
            if (ids.Count == 0)
            {
                return [];
            }

            await using var cmd = connection.CreateCommand();

            var idParams = ids.Select((id, i) => $"$id{i}").ToList();

            for (var i = 0; i < ids.Count; i++)
            {
                cmd.Parameters.AddWithValue(idParams[i], ids[i]);
            }

            cmd.CommandText = $"SELECT json FROM {table} WHERE {idColumn} IN ({string.Join(",", idParams)});";

            return await ReadJsonAsync<T>(cmd);
        }

        private static async Task<List<T>> ReadJsonAsync<T>(SqliteCommand cmd)
        {
            var results = new List<T>();

            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var item = JsonSerializer.Deserialize<T>(reader.GetString(0), JsonOptions.DefaultJsonSerializerOptions);

                if (item != null)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        private static void AddTimeRange(List<string> where, Dictionary<string, object> parameters, string column, DateTimeOffset? from, DateTimeOffset? to)
        {
            if (from.HasValue)
            {
                where.Add($"{column} >= $from");
                parameters["$from"] = from.Value.ToUnixTimeMilliseconds();
            }

            if (to.HasValue)
            {
                where.Add($"{column} < $to");
                parameters["$to"] = to.Value.ToUnixTimeMilliseconds();
            }
        }

        private static void AddEquals(List<string> where, Dictionary<string, object> parameters, string column, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var name = $"${column}";
                where.Add($"{column} = {name}");
                parameters[name] = value;
            }
        }

        private static void AddKeyword(List<string> where, Dictionary<string, object> parameters, string? keyword)
        {
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                where.Add("search LIKE $keyword ESCAPE '\\'");

                var escaped = keyword.Trim().ToLowerInvariant()
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal);

                parameters["$keyword"] = $"%{escaped}%";
            }
        }

        private static string ToWhereClause(List<string> where) => where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;

        private static void AddParameters(SqliteCommand cmd, Dictionary<string, object> parameters)
        {
            foreach (var p in parameters)
            {
                cmd.Parameters.AddWithValue(p.Key, p.Value);
            }
        }

        private static string ToSearchText(params string?[] values)
        {
            return string.Join(" ", values.Where(v => !string.IsNullOrWhiteSpace(v))).ToLower(CultureInfo.InvariantCulture);
        }
    }
}
