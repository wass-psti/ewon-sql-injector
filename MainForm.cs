using System.Globalization;
using EwonSqlInjector.Database;
using EwonSqlInjector.Models;
using EwonSqlInjector.Parsing;
using Npgsql;

namespace EwonSqlInjector;

public sealed class MainForm : Form
{
    private readonly TextBox _host = new() { Text = "localhost" };
    private readonly NumericUpDown _port = new()
    {
        Minimum = 1,
        Maximum = 65535,
        Value = 5432
    };
    private readonly TextBox _database = new() { Text = "db_EwonCMWD" };
    private readonly TextBox _username = new() { Text = "postgres" };
    private readonly TextBox _password = new()
    {
        UseSystemPasswordChar = true
    };

    private readonly Button _testConnection = new()
    {
        Text = "Test Connection",
        AutoSize = true
    };

    private readonly Button _loadTables = new()
    {
        Text = "Load Compatible Tables",
        AutoSize = true
    };

    private readonly ComboBox _targetTable = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 300
    };

    private readonly TextBox _filePath = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill
    };

    private readonly Button _selectFile = new()
    {
        Text = "Select Exported TXT",
        AutoSize = true
    };

    private readonly Button _parsePreview = new()
    {
        Text = "Parse / Preview",
        AutoSize = true
    };

    private readonly Button _processExport = new()
    {
        Text = "Process Export && Inject",
        AutoSize = true
    };

    private readonly Button _injectParsed = new()
    {
        Text = "Inject Parsed Data",
        AutoSize = true,
        Enabled = false
    };

    private readonly CheckBox _autoInject = new()
    {
        Text = "Automatically inject after successful parse",
        Checked = true,
        AutoSize = true
    };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };

    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9F)
    };

    private readonly ToolStripStatusLabel _status = new()
    {
        Text = "Ready"
    };

    private readonly ToolStripProgressBar _progress = new()
    {
        Style = ProgressBarStyle.Marquee,
        MarqueeAnimationSpeed = 30,
        Visible = false
    };

    private readonly EwonTxtParser _parser = new();
    private readonly PostgresImporter _importer = new();

    private List<EwonRecord> _parsedRecords = [];
    private string? _selectedFile;

    public MainForm()
    {
        Text = "Ewon SQL Injector - PostgreSQL";
        Width = 1450;
        Height = 850;
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        BuildPreviewColumns();
        WireEvents();

        Log("Application started.");
        Log("Local mode: select a TXT file already exported from the Ewon device.");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };

        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Controls.Add(root);

        root.Controls.Add(BuildDatabaseGroup(), 0, 0);
        root.Controls.Add(BuildFileGroup(), 0, 1);

        var previewGroup = new GroupBox
        {
            Text = "Parsed Data Preview (first 500 rows)",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        previewGroup.Controls.Add(_grid);
        root.Controls.Add(previewGroup, 0, 2);

        var logGroup = new GroupBox
        {
            Text = "Operation Log",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 3);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(new ToolStripStatusLabel { Spring = true });
        statusStrip.Items.Add(_progress);
        root.Controls.Add(statusStrip, 0, 4);
    }

    private Control BuildDatabaseGroup()
    {
        var group = new GroupBox
        {
            Text = "PostgreSQL Connection and Target",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 8,
            RowCount = 2
        };

        for (int i = 0; i < 8; i++)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(new Label { Text = "Host", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_host, 1, 0);
        layout.Controls.Add(new Label { Text = "Port", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 0);
        layout.Controls.Add(_port, 3, 0);
        layout.Controls.Add(new Label { Text = "Database", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 0);
        layout.Controls.Add(_database, 5, 0);
        layout.Controls.Add(_testConnection, 6, 0);
        layout.Controls.Add(_loadTables, 7, 0);

        layout.Controls.Add(new Label { Text = "Username", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_username, 1, 1);
        layout.Controls.Add(new Label { Text = "Password", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1);
        layout.Controls.Add(_password, 3, 1);
        layout.Controls.Add(new Label { Text = "Target Table", AutoSize = true, Anchor = AnchorStyles.Left }, 4, 1);
        layout.Controls.Add(_targetTable, 5, 1);
        layout.SetColumnSpan(_targetTable, 3);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildFileGroup()
    {
        var group = new GroupBox
        {
            Text = "Ewon TXT Export",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 6,
            RowCount = 2
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 1; i < 6; i++)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        layout.Controls.Add(_filePath, 0, 0);
        layout.Controls.Add(_selectFile, 1, 0);
        layout.Controls.Add(_parsePreview, 2, 0);
        layout.Controls.Add(_injectParsed, 3, 0);
        layout.Controls.Add(_processExport, 4, 0);
        layout.SetColumnSpan(_processExport, 2);

        layout.Controls.Add(_autoInject, 0, 1);
        layout.SetColumnSpan(_autoInject, 6);

        group.Controls.Add(layout);
        return group;
    }

    private void BuildPreviewColumns()
    {
        _grid.Columns.Clear();

        AddTextColumn(
            "ID",
            nameof(DatabasePreviewRow.ID));

        AddNumericColumn(
            "rec_Turbidity_NTU",
            nameof(DatabasePreviewRow.rec_Turbidity_NTU));

        AddNumericColumn(
            "rec_FreeChlorine_ppm",
            nameof(DatabasePreviewRow.rec_FreeChlorine_ppm));

        AddNumericColumn(
            "rec_AcidBase_pH",
            nameof(DatabasePreviewRow.rec_AcidBase_pH));

        AddNumericColumn(
            "rec_FlwMtr_A_Flowrate_m3p",
            nameof(DatabasePreviewRow.rec_FlwMtr_A_Flowrate_m3p));

        AddNumericColumn(
            "rec_FlwMtr_A_Tot_m3",
            nameof(DatabasePreviewRow.rec_FlwMtr_A_Tot_m3));

        AddNumericColumn(
            "rec_FlwMtr_B_Flowrate_m3p",
            nameof(DatabasePreviewRow.rec_FlwMtr_B_Flowrate_m3p));

        AddNumericColumn(
            "rec_FlwMtr_B_Tot_m3",
            nameof(DatabasePreviewRow.rec_FlwMtr_B_Tot_m3));

        AddNumericColumn(
            "rec_Pressure_A",
            nameof(DatabasePreviewRow.rec_Pressure_A));

        AddNumericColumn(
            "rec_Pressure_B",
            nameof(DatabasePreviewRow.rec_Pressure_B));

        AddTextColumn(
            "rec_DATE",
            nameof(DatabasePreviewRow.rec_DATE));

        var timestampColumn = new DataGridViewTextBoxColumn
        {
            HeaderText = "rec_TS",
            DataPropertyName = nameof(DatabasePreviewRow.rec_TS),
            Name = "rec_TS"
        };

        timestampColumn.DefaultCellStyle.Format =
            "yyyy-MM-dd HH:mm:ss";

        _grid.Columns.Add(timestampColumn);
    }

    private void AddNumericColumn(
        string header,
        string dataPropertyName)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = dataPropertyName,
            Name = header
        };

        column.DefaultCellStyle.Format = "0.00";
        column.DefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleRight;

        _grid.Columns.Add(column);
    }

    private void AddTextColumn(
        string header,
        string dataPropertyName)
    {
        _grid.Columns.Add(
            new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = dataPropertyName,
                Name = header
            });
    }

    private void WireEvents()
    {
        _testConnection.Click += async (_, _) => await TestConnectionAsync();
        _loadTables.Click += async (_, _) => await LoadTablesAsync();
        _selectFile.Click += (_, _) => SelectFile();
        _parsePreview.Click += async (_, _) => await ParseSelectedFileAsync();
        _injectParsed.Click += async (_, _) => await InjectCurrentRecordsAsync();
        _processExport.Click += async (_, _) => await ProcessExportAndInjectAsync();

        _targetTable.SelectedIndexChanged += (_, _) =>
        {
            _injectParsed.Enabled =
                !_progress.Visible &&
                _parsedRecords.Count > 0 &&
                _targetTable.SelectedItem is not null;
        };
    }

    private string BuildConnectionString()
    {
        if (string.IsNullOrWhiteSpace(_host.Text))
            throw new InvalidOperationException("PostgreSQL host is required.");

        if (string.IsNullOrWhiteSpace(_database.Text))
            throw new InvalidOperationException("Database name is required.");

        if (string.IsNullOrWhiteSpace(_username.Text))
            throw new InvalidOperationException("Database username is required.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = _host.Text.Trim(),
            Port = (int)_port.Value,
            Database = _database.Text.Trim(),
            Username = _username.Text.Trim(),
            Password = _password.Text,
            Timeout = 5,
            CommandTimeout = 30,
            Pooling = true
        };

        return builder.ConnectionString;
    }

    private async Task TestConnectionAsync()
    {
        await RunBusyAsync("Testing PostgreSQL connection...", async () =>
        {
            await _importer.TestConnectionAsync(BuildConnectionString());
            Log("PostgreSQL connection test succeeded.");
            MessageBox.Show(
                this,
                "Connection successful.",
                "PostgreSQL",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    private async Task LoadTablesAsync()
    {
        await RunBusyAsync("Loading compatible PostgreSQL tables...", async () =>
        {
            List<string> tables =
                await _importer.GetCompatiblePublicTablesAsync(BuildConnectionString());

            _targetTable.Items.Clear();

            foreach (string table in tables)
                _targetTable.Items.Add(table);

            if (_targetTable.Items.Count > 0)
                _targetTable.SelectedIndex = 0;

            Log($"Loaded {tables.Count} compatible public table(s).");

            if (tables.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No compatible tables were found in schema public.\n\n" +
                    "The selected table must contain all expected rec_* columns " +
                    "shown in the MCWD schema.",
                    "No Compatible Tables",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        });
    }

    private void SelectFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Ewon TXT Export",
            Filter = "Ewon text export (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _selectedFile = dialog.FileName;
        _filePath.Text = _selectedFile;
        _parsedRecords = [];
        _grid.DataSource = null;
        _injectParsed.Enabled = false;

        Log($"Selected file: {_selectedFile}");
    }

    private async Task ParseSelectedFileAsync()
    {
        if (!EnsureFileSelected())
            return;

        await RunBusyAsync("Parsing Ewon TXT export...", async () =>
        {
            await ParseInternalAsync();
        });
    }

    private async Task ProcessExportAndInjectAsync()
    {
        if (_selectedFile is null)
        {
            SelectFile();
            if (_selectedFile is null)
                return;
        }

        if (_autoInject.Checked && _targetTable.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                "Load the PostgreSQL tables and select a target table before " +
                "using automatic injection.",
                "Target Table Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        await RunBusyAsync("Processing Ewon export...", async () =>
        {
            await ParseInternalAsync();

            if (_autoInject.Checked)
                await InjectInternalAsync();
        });
    }

    private async Task ParseInternalAsync()
    {
        if (_selectedFile is null)
            throw new InvalidOperationException("No Ewon TXT file is selected.");

        Log("Parsing started.");

        List<EwonRecord> records = await _parser.ParseAsync(_selectedFile);
        _parsedRecords = records;

        _grid.DataSource = records
            .Take(500)
            .Select(x => new DatabasePreviewRow
            {
                // ID is generated by PostgreSQL during insertion.
                ID = null,

                rec_Turbidity_NTU = x.Turbidity,
                rec_FreeChlorine_ppm = x.FreeChlorine,
                rec_AcidBase_pH = x.PH,
                rec_FlwMtr_A_Flowrate_m3p = x.LeftFlowRate,
                rec_FlwMtr_A_Tot_m3 = x.LeftTotal,
                rec_FlwMtr_B_Flowrate_m3p = x.RightFlowRate,
                rec_FlwMtr_B_Tot_m3 = x.RightTotal,
                rec_Pressure_A = x.PressureA,
                rec_Pressure_B = x.PressureB,

                rec_DATE = x.Timestamp.ToString(
                    "MM/dd/yyyy HH:mm:ss",
                    CultureInfo.InvariantCulture),

                rec_TS = x.Timestamp
            })
            .ToList();

        Log($"Parsing successful: {records.Count} record(s).");

        if (records.Count > 500)
            Log("Preview limited to the first 500 rows.");

        _injectParsed.Enabled =
            _targetTable.SelectedItem is not null;
    }

    private async Task InjectCurrentRecordsAsync()
    {
        if (_parsedRecords.Count == 0)
        {
            MessageBox.Show(
                this,
                "Parse an Ewon TXT export before injecting.",
                "No Parsed Data",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_targetTable.SelectedItem is null)
        {
            MessageBox.Show(
                this,
                "Load and select a compatible PostgreSQL table.",
                "Target Table Required",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        await RunBusyAsync("Injecting parsed data into PostgreSQL...", async () =>
        {
            await InjectInternalAsync();
        });
    }

    private async Task InjectInternalAsync()
    {
        if (_targetTable.SelectedItem is not string table)
            throw new InvalidOperationException("No target PostgreSQL table is selected.");

        Log($"Injection started -> public.{table}");

        ImportResult result = await _importer.ImportAsync(
            BuildConnectionString(),
            table,
            _parsedRecords);

        Log(
            $"Injection completed. Inserted: {result.Inserted}; " +
            $"duplicate timestamps skipped: {result.SkippedDuplicates}.");

        MessageBox.Show(
            this,
            $"Injection completed successfully.\n\n" +
            $"Table: public.{table}\n" +
            $"Inserted: {result.Inserted}\n" +
            $"Skipped duplicates: {result.SkippedDuplicates}",
            "Ewon SQL Injector",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private bool EnsureFileSelected()
    {
        if (_selectedFile is not null)
            return true;

        SelectFile();
        return _selectedFile is not null;
    }

    private async Task RunBusyAsync(string statusText, Func<Task> action)
    {
        SetBusy(true, statusText);

        try
        {
            await action();
            _status.Text = "Ready";
        }
        catch (Exception ex)
        {
            _status.Text = "Error";
            Log($"ERROR: {ex.Message}");

            MessageBox.Show(
                this,
                ex.Message,
                "Operation Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, _status.Text);
        }
    }

    private void SetBusy(bool busy, string statusText)
    {
        _status.Text = statusText;
        _progress.Visible = busy;

        // Prevents double-click/re-entry while a file is being parsed/imported.
        _processExport.Enabled = !busy;
        _selectFile.Enabled = !busy;
        _parsePreview.Enabled = !busy;
        _testConnection.Enabled = !busy;
        _loadTables.Enabled = !busy;
        _targetTable.Enabled = !busy;

        _injectParsed.Enabled =
            !busy &&
            _parsedRecords.Count > 0 &&
            _targetTable.SelectedItem is not null;
    }

    private void Log(string message)
    {
        _log.AppendText(
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
