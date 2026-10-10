using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace EnterpriseInventory.IntegrationTests.Persistence;

/// <summary>
/// Runs a committed sqlcmd script the way <c>sqlcmd -b</c> does: <c>$(Name)</c> variables replaced, batch by batch
/// split on GO, stopping at the first error. Returns every row the batches selected.
/// </summary>
internal static partial class SqlcmdScript
{
    public static async Task<IReadOnlyList<string[]>> RunAsync(
        string connectionString, string relativePath, IReadOnlyDictionary<string, string>? variables = null)
    {
        var text = Variable().Replace(
            await File.ReadAllTextAsync(Path.Combine(DeploymentScript.RepositoryRoot(), relativePath)),
            match => variables is not null && variables.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"{relativePath} needs the sqlcmd variable {match.Groups[1].Value}."));
        var rows = new List<string[]>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var batch in Batch().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)))
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // A committed deployment script; the variables are test values.
            command.CommandText = batch;
#pragma warning restore CA2100
            command.CommandTimeout = 120;
            await using var reader = await command.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                {
                    rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty)]);
                }
            }
            while (await reader.NextResultAsync());
        }

        return rows;
    }

    [GeneratedRegex(@"\$\((\w+)\)")]
    private static partial Regex Variable();

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline)]
    private static partial Regex Batch();
}
