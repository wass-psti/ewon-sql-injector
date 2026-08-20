using System.Globalization;
using EwonSqlInjector.Models;
using Npgsql;

namespace EwonSqlInjector.Database;

public sealed record ImportResult(int Inserted, int SkippedDuplicates);

public sealed class PostgresImporter
{
    // Canonical schema supplied for the MCWD historian tables.
    // The m3ph spelling is accepted as a compatibility alias because
    // it appeared in the earlier pgAdmin screenshot, while the requested
    // canonical/display name is m3p.
    private static readonly TargetColumnSpec[] TargetSchema =
    [
        new("ID", "bigint"),

        new("rec_Turbidity_NTU", "numeric"),
        new("rec_FreeChlorine_ppm", "numeric"),
        new("rec_AcidBase_pH", "numeric"),

        new(
            "rec_FlwMtr_A_Flowrate_m3p",
            "numeric",
            ["rec_FlwMtr_A_Flowrate_m3ph"]),

        new("rec_FlwMtr_A_Tot_m3", "numeric"),

        new(
            "rec_FlwMtr_B_Flowrate_m3p",
            "numeric",
            ["rec_FlwMtr_B_Flowrate_m3ph"]),

        new("rec_FlwMtr_B_Tot_m3", "numeric"),
        new("rec_Pressure_A", "numeric"),
        new("rec_Pressure_B", "numeric"),
        new("rec_DATE", "text"),
        new("rec_TS", "timestamp without time zone")
    ];

    public async Task TestConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT 1;", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<List<string>> GetCompatiblePublicTablesAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        Dictionary<string, Dictionary<string, ColumnInfo>> tables =
            await LoadPublicTableSchemasAsync(connection, cancellationToken);

        return tables
            .Where(entry => IsCompatible(entry.Value))
            .Select(entry => entry.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ImportResult> ImportAsync(
        string connectionString,
        string tableName,
        IReadOnlyList<EwonRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("No PostgreSQL target table was selected.");

        if (records.Count == 0)
            throw new InvalidOperationException("There are no parsed Ewon records to inject.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        Dictionary<string, ColumnInfo> mapped =
            await LoadAndValidateTargetColumnsAsync(
                connection,
                tableName,
                cancellationToken);

        await ValidateIdGenerationAsync(connection, tableName, cancellationToken);

        static string Quote(string identifier) =>
            "\"" + identifier.Replace("\"", "\"\"") + "\"";

        string Col(string canonicalName) =>
            Quote(mapped[canonicalName].ActualName);

        string qualifiedTable = $"{Quote("public")}.{Quote(tableName)}";

        string sql = $"""
            INSERT INTO {qualifiedTable}
            (
                {Col("rec_Turbidity_NTU")},
                {Col("rec_FreeChlorine_ppm")},
                {Col("rec_AcidBase_pH")},
                {Col("rec_FlwMtr_A_Flowrate_m3p")},
                {Col("rec_FlwMtr_A_Tot_m3")},
                {Col("rec_FlwMtr_B_Flowrate_m3p")},
                {Col("rec_FlwMtr_B_Tot_m3")},
                {Col("rec_Pressure_A")},
                {Col("rec_Pressure_B")},
                {Col("rec_DATE")},
                {Col("rec_TS")}
            )
            SELECT
                @turbidity,
                @free_chlorine,
                @ph,
                @flow_a,
                @total_a,
                @flow_b,
                @total_b,
                @pressure_a,
                @pressure_b,
                @rec_date,
                @rec_ts
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM {qualifiedTable}
                WHERE {Col("rec_TS")} = @rec_ts
            );
            """;

        int inserted = 0;
        int skipped = 0;

        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (EwonRecord record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var command =
                    new NpgsqlCommand(sql, connection, transaction);

                // Values are already rounded by EwonTxtParser, but rounding
                // again here protects the database boundary.
                command.Parameters.AddWithValue(
                    "turbidity",
                    Round2(record.Turbidity));

                command.Parameters.AddWithValue(
                    "free_chlorine",
                    Round2(record.FreeChlorine));

                command.Parameters.AddWithValue(
                    "ph",
                    Round2(record.PH));

                command.Parameters.AddWithValue(
                    "flow_a",
                    Round2(record.LeftFlowRate));

                command.Parameters.AddWithValue(
                    "total_a",
                    Round2(record.LeftTotal));

                command.Parameters.AddWithValue(
                    "flow_b",
                    Round2(record.RightFlowRate));

                command.Parameters.AddWithValue(
                    "total_b",
                    Round2(record.RightTotal));

                command.Parameters.AddWithValue(
                    "pressure_a",
                    Round2(record.PressureA));

                command.Parameters.AddWithValue(
                    "pressure_b",
                    Round2(record.PressureB));

                command.Parameters.AddWithValue(
                    "rec_date",
                    record.Timestamp.ToString(
                        "MM/dd/yyyy HH:mm:ss",
                        CultureInfo.InvariantCulture));

                command.Parameters.AddWithValue(
                    "rec_ts",
                    record.Timestamp);

                int affected =
                    await command.ExecuteNonQueryAsync(cancellationToken);

                if (affected == 1)
                    inserted++;
                else
                    skipped++;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return new ImportResult(inserted, skipped);
    }

    private static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static async Task<Dictionary<string, Dictionary<string, ColumnInfo>>>
        LoadPublicTableSchemasAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                c.table_name,
                c.column_name,
                c.data_type,
                c.is_nullable,
                c.column_default,
                c.is_identity
            FROM information_schema.columns c
            INNER JOIN information_schema.tables t
                ON t.table_schema = c.table_schema
               AND t.table_name = c.table_name
            WHERE c.table_schema = 'public'
              AND t.table_type = 'BASE TABLE'
            ORDER BY c.table_name, c.ordinal_position;
            """;

        var result =
            new Dictionary<string, Dictionary<string, ColumnInfo>>(
                StringComparer.OrdinalIgnoreCase);

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            string tableName = reader.GetString(0);
            string columnName = reader.GetString(1);
            string dataType = reader.GetString(2);
            string isNullable = reader.GetString(3);
            string? defaultValue =
                reader.IsDBNull(4) ? null : reader.GetString(4);
            string isIdentity = reader.GetString(5);

            if (!result.TryGetValue(
                tableName,
                out Dictionary<string, ColumnInfo>? columns))
            {
                columns =
                    new Dictionary<string, ColumnInfo>(
                        StringComparer.OrdinalIgnoreCase);

                result[tableName] = columns;
            }

            columns[columnName] =
                new ColumnInfo(
                    columnName,
                    dataType,
                    isNullable,
                    defaultValue,
                    isIdentity);
        }

        return result;
    }

    private static bool IsCompatible(
        Dictionary<string, ColumnInfo> columns)
    {
        foreach (TargetColumnSpec spec in TargetSchema)
        {
            if (!TryResolveColumn(columns, spec, out ColumnInfo? actual))
                return false;

            if (!actual.DataType.Equals(
                    spec.ExpectedDataType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<Dictionary<string, ColumnInfo>>
        LoadAndValidateTargetColumnsAsync(
            NpgsqlConnection connection,
            string tableName,
            CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                column_name,
                data_type,
                is_nullable,
                column_default,
                is_identity
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table
            ORDER BY ordinal_position;
            """;

        var columns =
            new Dictionary<string, ColumnInfo>(
                StringComparer.OrdinalIgnoreCase);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", tableName);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            string name = reader.GetString(0);
            string dataType = reader.GetString(1);
            string isNullable = reader.GetString(2);
            string? defaultValue =
                reader.IsDBNull(3) ? null : reader.GetString(3);
            string isIdentity = reader.GetString(4);

            columns[name] =
                new ColumnInfo(
                    name,
                    dataType,
                    isNullable,
                    defaultValue,
                    isIdentity);
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Table public.{tableName} was not found.");
        }

        var mapped =
            new Dictionary<string, ColumnInfo>(
                StringComparer.OrdinalIgnoreCase);

        foreach (TargetColumnSpec spec in TargetSchema)
        {
            if (!TryResolveColumn(columns, spec, out ColumnInfo? actual))
            {
                string acceptedNames =
                    string.Join(
                        ", ",
                        new[] { spec.CanonicalName }
                            .Concat(spec.Aliases));

                throw new InvalidOperationException(
                    $"Table public.{tableName} is incompatible. " +
                    $"Missing required column. Accepted name(s): " +
                    $"{acceptedNames}.");
            }

            if (!actual.DataType.Equals(
                    spec.ExpectedDataType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Table public.{tableName} column " +
                    $"'{actual.ActualName}' has PostgreSQL type " +
                    $"'{actual.DataType}', but '{spec.ExpectedDataType}' " +
                    $"is required.");
            }

            mapped[spec.CanonicalName] = actual;
        }

        return mapped;
    }

    private static bool TryResolveColumn(
        Dictionary<string, ColumnInfo> columns,
        TargetColumnSpec spec,
        out ColumnInfo? column)
    {
        if (columns.TryGetValue(spec.CanonicalName, out column))
            return true;

        foreach (string alias in spec.Aliases)
        {
            if (columns.TryGetValue(alias, out column))
                return true;
        }

        column = null;
        return false;
    }

    private static async Task ValidateIdGenerationAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                column_name,
                is_nullable,
                column_default,
                is_identity
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table
              AND lower(column_name) = 'id'
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table", tableName);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Table public.{tableName} does not contain the required ID column.");
        }

        string isNullable = reader.GetString(1);
        string? defaultValue =
            reader.IsDBNull(2) ? null : reader.GetString(2);
        string isIdentity = reader.GetString(3);

        if (isNullable.Equals("NO", StringComparison.OrdinalIgnoreCase) &&
            defaultValue is null &&
            !isIdentity.Equals("YES", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Table public.{tableName} has a required ID column, " +
                "but PostgreSQL is not configured to generate it automatically. " +
                "The injector will not invent historian IDs.");
        }
    }

    private sealed record TargetColumnSpec(
        string CanonicalName,
        string ExpectedDataType,
        string[] Aliases)
    {
        public TargetColumnSpec(
            string canonicalName,
            string expectedDataType)
            : this(canonicalName, expectedDataType, [])
        {
        }
    }

    private sealed record ColumnInfo(
        string ActualName,
        string DataType,
        string IsNullable,
        string? DefaultValue,
        string IsIdentity);
}
