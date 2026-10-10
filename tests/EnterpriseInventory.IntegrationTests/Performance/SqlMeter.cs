using System.Data.Common;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EnterpriseInventory.IntegrationTests.Performance;

/// <summary>
/// Counts the SQL commands EF Core sends while it is recording. Added to the API's context in tests; requests are
/// measured one at a time.
/// </summary>
internal sealed class SqlMeter : DbCommandInterceptor
{
    private readonly Lock _lock = new();
    private readonly List<string> _commands = [];
    private bool _recording;

    public void Start()
    {
        lock (_lock)
        {
            _commands.Clear();
            _recording = true;
        }
    }

    public IReadOnlyList<string> Stop()
    {
        lock (_lock)
        {
            _recording = false;
            return [.. _commands];
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    private void Record(DbCommand command)
    {
        lock (_lock)
        {
            if (_recording)
            {
                _commands.Add(command.CommandText);
            }
        }
    }
}

/// <summary>
/// What SQL Server spent on one database's statements, from its plan cache (<c>sys.dm_exec_query_stats</c>): pages
/// read and CPU time, per statement. Two snapshots around a request give that request's share; the account needs
/// VIEW SERVER STATE, as a test server's administrator has.
/// </summary>
internal sealed class ServerStatistics(string connectionString)
{
    private const string Query = """
        SELECT CONVERT(varchar(200), qs.sql_handle, 1) + ':' + CONVERT(varchar(12), qs.statement_start_offset) + ':' + CONVERT(varchar(200), qs.plan_handle, 1),
               qs.execution_count, qs.total_logical_reads, qs.total_worker_time,
               SUBSTRING(st.text, qs.statement_start_offset / 2 + 1,
                   (CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(st.text) ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2 + 1)
        FROM sys.dm_exec_query_stats AS qs
        CROSS APPLY sys.dm_exec_plan_attributes(qs.plan_handle) AS pa
        CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
        WHERE pa.attribute = 'dbid' AND CONVERT(int, pa.value) = DB_ID()
        """;

    public async Task<Dictionary<string, Statement>> SnapshotAsync()
    {
        var statements = new Dictionary<string, Statement>(StringComparer.Ordinal);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(Query, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statements[reader.GetString(0)] = new Statement(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetString(4));
        }

        return statements;
    }

    /// <summary>The statements run between two snapshots, with what they cost in that time.</summary>
    public static ServerCost Difference(Dictionary<string, Statement> before, Dictionary<string, Statement> after)
    {
        var ran = new List<Statement>();
        foreach (var (key, now) in after)
        {
            // The snapshot's own statement.
            if (now.Text.Contains("sys.dm_exec_query_stats", StringComparison.Ordinal))
            {
                continue;
            }

            var earlier = before.GetValueOrDefault(key);
            var executions = now.Executions - (earlier?.Executions ?? 0);
            if (executions > 0)
            {
                ran.Add(new Statement(executions, now.LogicalReads - (earlier?.LogicalReads ?? 0), now.WorkerMicroseconds - (earlier?.WorkerMicroseconds ?? 0), now.Text));
            }
        }

        return new ServerCost([.. ran.OrderByDescending(s => s.LogicalReads)]);
    }

    internal sealed record Statement(long Executions, long LogicalReads, long WorkerMicroseconds, string Text);
}

/// <summary>What one request's statements cost SQL Server.</summary>
internal sealed record ServerCost(IReadOnlyList<ServerStatistics.Statement> Statements)
{
    public long LogicalReads => Statements.Sum(s => s.LogicalReads);

    public double CpuMilliseconds => Statements.Sum(s => s.WorkerMicroseconds) / 1000.0;
}

/// <summary>Elapsed times of repeated calls, in milliseconds.</summary>
internal static class Timings
{
    public static async Task<(double Median, double P95)> MeasureAsync(int runs, Func<Task> call)
    {
        var elapsed = new List<double>(runs);
        for (var i = 0; i < runs; i++)
        {
            var watch = Stopwatch.StartNew();
            await call();
            elapsed.Add(watch.Elapsed.TotalMilliseconds);
        }

        elapsed.Sort();
        return (elapsed[elapsed.Count / 2], elapsed[(int)Math.Ceiling(elapsed.Count * 0.95) - 1]);
    }
}
