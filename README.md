# Ewon SQL Injector

Windows Forms prototype for processing an Ewon TXT export and inserting the parsed
measurements into the existing PostgreSQL historian tables.

## Important: current operating mode

This build uses **local-export mode**:

1. Export the `.txt` file from Ewon using the site's existing method.
2. Open the exported file in this application.
3. Parse and preview the records.
4. Select a compatible PostgreSQL `public` table.
5. Inject the records.

Directly downloading/exporting a file from the Ewon itself is **not** implemented
yet because the Ewon model, firmware, IP, authentication, and export mechanism
(HTTP/EBD/FTP/etc.) have not yet been supplied.

## Expected Ewon TXT columns

- TimeInt
- TimeStr
- AI1_Turbidity
- AI2_FreeChlorine
- AI3_pH
- FM_Left_FlowRate
- FM_Left_Tot1_Log
- FM_Right_FlowRate
- FM_Right_Tot1_Log
- Pressure_A_psi
- Pressure_B_psi

The file is semicolon-delimited and `TimeStr` is interpreted as
`dd/MM/yyyy HH:mm:ss`.

## Database mapping

| Ewon TXT | PostgreSQL |
|---|---|
| AI1_Turbidity | rec_Turbidity_NTU |
| AI2_FreeChlorine | rec_FreeChlorine_ppm |
| AI3_pH | rec_AcidBase_pH |
| FM_Left_FlowRate | rec_FlwMtr_A_Flowrate_m3p |
| FM_Left_Tot1_Log | rec_FlwMtr_A_Tot_m3 |
| FM_Right_FlowRate | rec_FlwMtr_B_Flowrate_m3p |
| FM_Right_Tot1_Log | rec_FlwMtr_B_Tot_m3 |
| Pressure_A_psi | rec_Pressure_A |
| Pressure_B_psi | rec_Pressure_B |
| TimeStr (formatted) | rec_DATE |
| TimeStr (DateTime) | rec_TS |

`TimeInt` is parsed and shown for traceability but is not inserted because the
shown target table does not contain a corresponding TimeInt column.

No engineering-unit conversion is performed. Numeric values are inserted exactly
as exported.

## Duplicate handling

If `rec_TS` already exists in the selected table, that source row is skipped.

This is application-level duplicate protection. For strict database-level
protection, the production database should ideally also have an appropriate
UNIQUE constraint/index agreed with the database owner.

## ID handling

The application intentionally does not generate the database `ID`. It expects the
existing PostgreSQL table to generate `ID` through an identity/sequence/default.
If `ID` is required but has no generator, injection is stopped before any row is
written.

## Build

Requires the .NET 10 SDK.

```powershell
dotnet restore
dotnet run
```

## Publish portable Windows x64 EXE

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output:

```text
bin\Release\net10.0-windows\win-x64\publish\EwonSqlInjector.exe
```

The publish is self-contained, so the target Windows x64 PC does not need a
separate .NET runtime installation.

## PostgreSQL package

The application uses `Npgsql` to connect to PostgreSQL.

## Before live deployment

Verify with the senior engineer / DBA:

- PostgreSQL host/IP and port
- Database name
- Application database account and permissions
- Which historian table corresponds to each site/Ewon
- Whether `ID` is sequence/identity-generated
- Whether `rec_TS` is the correct duplicate key
- Whether `rec_DATE` must retain a particular text format
- Whether Pressure_A/B should remain in the exported unit or be converted
- Ewon model, firmware, IP, and actual export mechanism
- Required store-and-forward/retry behavior during database outages

Do not hard-code production passwords in source code or commit them to GitHub.


## v2 schema alignment

The preview now mirrors the PostgreSQL historian schema order exactly:

`ID`, `rec_Turbidity_NTU`, `rec_FreeChlorine_ppm`, `rec_AcidBase_pH`,
`rec_FlwMtr_A_Flowrate_m3p`, `rec_FlwMtr_A_Tot_m3`,
`rec_FlwMtr_B_Flowrate_m3p`, `rec_FlwMtr_B_Tot_m3`,
`rec_Pressure_A`, `rec_Pressure_B`, `rec_DATE`, `rec_TS`.

All numeric Ewon measurements are rounded to two decimal places before they are
shown and inserted.

Compatible-table discovery validates both column names and PostgreSQL data types.
