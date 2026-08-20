using System.Globalization;
using System.Text;
using EwonSqlInjector.Models;

namespace EwonSqlInjector.Parsing;

public sealed class EwonTxtParser
{
    private static readonly string[] RequiredHeaders =
    [
        "TimeInt",
        "TimeStr",
        "AI1_Turbidity",
        "AI2_FreeChlorine",
        "AI3_pH",
        "FM_Left_FlowRate",
        "FM_Left_Tot1_Log",
        "FM_Right_FlowRate",
        "FM_Right_Tot1_Log",
        "Pressure_A_psi",
        "Pressure_B_psi"
    ];

    private static readonly string[] SupportedDateFormats =
    [
        "dd/MM/yyyy HH:mm:ss",
        "d/M/yyyy H:mm:ss",
        "dd/MM/yyyy HH:mm:ss.fff",
        "d/M/yyyy H:mm:ss.fff"
    ];

    public async Task<List<EwonRecord>> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("No Ewon export file was selected.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("The selected Ewon export file does not exist.", filePath);

        var records = new List<EwonRecord>();

        using var reader = new StreamReader(
            filePath,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        string? headerLine = await reader.ReadLineAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(headerLine))
            throw new FormatException("The Ewon export file is empty.");

        List<string> headers = SplitSemicolonLine(headerLine);

        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++)
        {
            string header = headers[i].Trim();
            if (!headerIndex.TryAdd(header, i))
                throw new FormatException($"Duplicate header '{header}' was found.");
        }

        foreach (string required in RequiredHeaders)
        {
            if (!headerIndex.ContainsKey(required))
                throw new FormatException($"Required Ewon column '{required}' is missing.");
        }

        int lineNumber = 1;

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? line = await reader.ReadLineAsync(cancellationToken);
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
                continue;

            List<string> fields = SplitSemicolonLine(line);

            string Get(string name)
            {
                int index = headerIndex[name];
                if (index >= fields.Count)
                {
                    throw new FormatException(
                        $"Line {lineNumber}: expected column '{name}', but the row has only {fields.Count} field(s).");
                }

                return fields[index].Trim();
            }

            long timeInt = ParseLong(Get("TimeInt"), "TimeInt", lineNumber);
            string timeStr = Get("TimeStr");

            DateTime timestamp = ParseDateTime(timeStr, lineNumber);

            records.Add(new EwonRecord
            {
                TimeInt = timeInt,
                TimeStr = timeStr,
                Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified),

                Turbidity = ParseDecimal(Get("AI1_Turbidity"), "AI1_Turbidity", lineNumber),
                FreeChlorine = ParseDecimal(Get("AI2_FreeChlorine"), "AI2_FreeChlorine", lineNumber),
                PH = ParseDecimal(Get("AI3_pH"), "AI3_pH", lineNumber),
                LeftFlowRate = ParseDecimal(Get("FM_Left_FlowRate"), "FM_Left_FlowRate", lineNumber),
                LeftTotal = ParseDecimal(Get("FM_Left_Tot1_Log"), "FM_Left_Tot1_Log", lineNumber),
                RightFlowRate = ParseDecimal(Get("FM_Right_FlowRate"), "FM_Right_FlowRate", lineNumber),
                RightTotal = ParseDecimal(Get("FM_Right_Tot1_Log"), "FM_Right_Tot1_Log", lineNumber),
                PressureA = ParseDecimal(Get("Pressure_A_psi"), "Pressure_A_psi", lineNumber),
                PressureB = ParseDecimal(Get("Pressure_B_psi"), "Pressure_B_psi", lineNumber)
            });
        }

        if (records.Count == 0)
            throw new FormatException("The file contains a header but no data rows.");

        return records;
    }

    private static long ParseLong(string value, string column, int lineNumber)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
            return result;

        throw new FormatException(
            $"Line {lineNumber}: '{value}' is not a valid integer for {column}.");
    }

    private static decimal ParseDecimal(string value, string column, int lineNumber)
    {
        if (decimal.TryParse(
            value,
            NumberStyles.Float | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out decimal result))
        {
            // The MCWD historian import stores the Ewon measurements
            // at two-decimal precision.
            return Math.Round(result, 2, MidpointRounding.AwayFromZero);
        }

        throw new FormatException(
            $"Line {lineNumber}: '{value}' is not a valid numeric value for {column}.");
    }

    private static DateTime ParseDateTime(string value, int lineNumber)
    {
        if (DateTime.TryParseExact(
            value,
            SupportedDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime result))
        {
            return result;
        }

        throw new FormatException(
            $"Line {lineNumber}: '{value}' is not a supported Ewon TimeStr value. " +
            "Expected day/month/year, for example 20/08/2026 15:00:01.");
    }

    // Small CSV-style parser for Ewon's semicolon-delimited export.
    // Handles quoted fields and escaped double-quotes.
    private static List<string> SplitSemicolonLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (c == ';' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (inQuotes)
            throw new FormatException("The file contains an unterminated quoted field.");

        fields.Add(current.ToString());
        return fields;
    }
}
