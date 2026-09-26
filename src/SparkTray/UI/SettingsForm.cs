using SparkTray.Config;
using SparkTray.Logging;
using SparkTray.NvidiaSyncImport;

namespace SparkTray.UI;

/// <summary>
/// Plain code-built WinForms dialog (no designer/.resx) - deliberately simple static
/// layout, which keeps this AOT/trim-friendly and easy to read without a designer file.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly IAppLogger _logger;

    private readonly TextBox _hostnameBox = new();
    private readonly NumericUpDown _intervalBox = new() { Minimum = 5, Maximum = 3600 };
    private readonly NumericUpDown _timeoutBox = new() { Minimum = 1, Maximum = 60 };
    // AutoSize on a CheckBox inside these nested Table/FlowLayoutPanels hits the same
    // measurement-timing problem the GroupBoxes did (see CreateGroup) - the control gets
    // assigned a Bounds narrower than its actual text needs and the label just clips,
    // rather than wrapping. An explicit fixed width sidesteps it entirely.
    private const int CheckboxWidth = 400;

    private readonly CheckBox _icmpCheckBox = new()
    {
        Text = "Also check ICMP ping (informational only)",
        AutoSize = false,
        Width = CheckboxWidth,
        Height = 20,
    };

    private readonly TextBox _sshUserBox = new();
    private readonly NumericUpDown _sshPortBox = new() { Minimum = 1, Maximum = 65535 };
    private readonly TextBox _sshKeyPathBox = new();
    private readonly Button _browseKeyButton = new() { Text = "Browse…" };
    private readonly Button _importButton = new() { Text = "Import from NVIDIA Sync" };
    private readonly Label _importStatusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private readonly TextBox _shutdownCommandBox = new();
    // Confirmed CheckBox reliably renders neither wrapped nor explicit "\n" multi-line
    // text in this layout - a short one-line label is the robust option; the full
    // explanation lives in the README instead of fighting the control for two lines.
    private readonly CheckBox _promptForSudoPasswordCheckBox = new()
    {
        Text = "Ask for sudo password each time",
        AutoSize = false,
        Width = CheckboxWidth,
        Height = 20,
    };
    private readonly TextBox _ignoreListBox = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };

    private readonly CheckBox _loggingCheckBox = new()
    {
        Text = "Write a troubleshooting log file",
        AutoSize = false,
        Width = CheckboxWidth,
        Height = 20,
    };
    private readonly CheckBox _autostartCheckBox = new()
    {
        Text = "Start with Windows",
        AutoSize = false,
        Width = CheckboxWidth,
        Height = 20,
    };

    public AppSettings Result { get; private set; }

    public SettingsForm(AppSettings current, IAppLogger logger)
    {
        _logger = logger;
        Result = Clone(current);

        Text = "DGX Spark Status Tray - Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(480, 620);
        Padding = new Padding(12);
        AutoScroll = true;

        // A single-column TableLayoutPanel with Dock=Fill + AutoSize=true is a classic
        // WinForms trap: the Percent(100) column has no real width to measure against until
        // the panel itself has been sized, but AutoSize wants to size the panel from its
        // children first - so on the first layout pass every AutoSize child (the GroupBoxes,
        // and every Label inside them) gets measured against a near-zero width and wraps its
        // text one character per line. A plain top-down FlowLayoutPanel sidesteps that
        // circular dependency entirely - it just stacks children at their own sizes.
        var layout = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Top,
        };

        layout.Controls.Add(BuildTargetGroup());
        layout.Controls.Add(BuildSshGroup());
        layout.Controls.Add(BuildShutdownGroup());
        layout.Controls.Add(BuildMiscGroup());
        layout.Controls.Add(BuildButtonsPanel());

        Controls.Add(layout);

        LoadFrom(Result);
    }

    private const int GroupWidth = 440;

    // AutoSize=true plus an explicit Width fights itself (AutoSize wins and recomputes
    // width from content). Pinning Minimum/MaximumSize.Width to the same value instead
    // gives every group a stable, known width from the very first layout pass - which is
    // exactly what the inner TableLayoutPanels' Percent(100) columns need to measure
    // against correctly - while still letting height grow to fit content.
    private static GroupBox CreateGroup(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MinimumSize = new Size(GroupWidth, 0),
        MaximumSize = new Size(GroupWidth, 0),
        Padding = new Padding(8),
        Margin = new Padding(0, 0, 0, 8),
    };

    private GroupBox BuildTargetGroup()
    {
        var group = CreateGroup("Target");
        var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _hostnameBox.Width = 260;
        AddRow(panel, "Hostname or IP:", _hostnameBox);
        AddRow(panel, "Check every (seconds):", _intervalBox);
        AddRow(panel, "Check timeout (seconds):", _timeoutBox);
        panel.Controls.Add(_icmpCheckBox);
        panel.SetColumnSpan(_icmpCheckBox, 2);

        group.Controls.Add(panel);
        return group;
    }

    private GroupBox BuildSshGroup()
    {
        var group = CreateGroup("SSH (used for shutdown and the pre-shutdown check)");
        var panel = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _sshUserBox.Width = 200;
        panel.Controls.Add(new Label { Text = "Username:", AutoSize = true, Anchor = AnchorStyles.Left });
        panel.Controls.Add(_sshUserBox);
        panel.Controls.Add(new Label());

        panel.Controls.Add(new Label { Text = "Port:", AutoSize = true, Anchor = AnchorStyles.Left });
        panel.Controls.Add(_sshPortBox);
        panel.Controls.Add(new Label());

        _sshKeyPathBox.Width = 260;
        panel.Controls.Add(new Label { Text = "Private key file:", AutoSize = true, Anchor = AnchorStyles.Left });
        panel.Controls.Add(_sshKeyPathBox);
        panel.Controls.Add(_browseKeyButton);

        panel.Controls.Add(_importButton);
        panel.SetColumnSpan(_importButton, 2);
        panel.Controls.Add(new Label());

        panel.Controls.Add(_importStatusLabel);
        panel.SetColumnSpan(_importStatusLabel, 3);

        _browseKeyButton.Click += (_, _) => BrowseForKey();
        _importButton.Click += (_, _) => ImportFromNvidiaSync();

        group.Controls.Add(panel);
        return group;
    }

    private GroupBox BuildShutdownGroup()
    {
        var group = CreateGroup("Shutdown");
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top };

        panel.Controls.Add(new Label { Text = "Shutdown command:", AutoSize = true });
        _shutdownCommandBox.Width = 400;
        panel.Controls.Add(_shutdownCommandBox);

        _promptForSudoPasswordCheckBox.Margin = new Padding(0, 6, 0, 0);
        panel.Controls.Add(_promptForSudoPasswordCheckBox);

        panel.Controls.Add(new Label
        {
            Text = "Process names to ignore before warning about active work (one per line):",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
        });
        _ignoreListBox.Width = 400;
        _ignoreListBox.Height = 120;
        panel.Controls.Add(_ignoreListBox);

        group.Controls.Add(panel);
        return group;
    }

    private GroupBox BuildMiscGroup()
    {
        var group = CreateGroup("General");
        // WrapContents defaults to true, which - combined with the AutoSize timing quirk
        // these nested panels keep hitting - wraps into a second column instead of
        // stacking, exactly like the outer layout panel needed this same fix for.
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
        panel.Controls.Add(_autostartCheckBox);
        panel.Controls.Add(_loggingCheckBox);
        group.Controls.Add(panel);
        return group;
    }

    private FlowLayoutPanel BuildButtonsPanel()
    {
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
        okButton.Click += (_, _) => SaveTo(Result);

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            MinimumSize = new Size(GroupWidth, 0),
            MaximumSize = new Size(GroupWidth, 0),
            Margin = new Padding(0, 4, 0, 0),
        };
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        return panel;
    }

    private static void AddRow(TableLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        panel.Controls.Add(control);
    }

    private void BrowseForKey()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select SSH private key file",
            Filter = "All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _sshKeyPathBox.Text = dialog.FileName;
        }
    }

    private void ImportFromNvidiaSync()
    {
        var import = NvidiaSyncImporter.TryImport(_logger);
        if (import is null)
        {
            _importStatusLabel.Text = "NVIDIA Sync config not found - enter details manually.";
            return;
        }

        _hostnameBox.Text = import.Hostname;
        _sshUserBox.Text = import.Username;
        _sshPortBox.Value = import.Port;
        _sshKeyPathBox.Text = import.PrivateKeyPath;
        _importStatusLabel.Text = "Imported from NVIDIA Sync.";
    }

    private void LoadFrom(AppSettings settings)
    {
        _hostnameBox.Text = settings.Target.Hostname;
        _intervalBox.Value = Math.Clamp(settings.Target.CheckIntervalSeconds, (int)_intervalBox.Minimum, (int)_intervalBox.Maximum);
        _timeoutBox.Value = Math.Clamp(settings.Target.CheckTimeoutSeconds, (int)_timeoutBox.Minimum, (int)_timeoutBox.Maximum);
        _icmpCheckBox.Checked = settings.Target.EnableIcmpProbe;

        _sshUserBox.Text = settings.Ssh.Username;
        _sshPortBox.Value = Math.Clamp(settings.Ssh.Port, (int)_sshPortBox.Minimum, (int)_sshPortBox.Maximum);
        _sshKeyPathBox.Text = settings.Ssh.PrivateKeyPath;

        _shutdownCommandBox.Text = settings.Shutdown.Command;
        _promptForSudoPasswordCheckBox.Checked = settings.Shutdown.PromptForSudoPassword;
        _ignoreListBox.Text = string.Join(Environment.NewLine, settings.Shutdown.ProcessIgnoreList);

        _loggingCheckBox.Checked = settings.Logging.Enabled;
        _autostartCheckBox.Checked = settings.StartWithWindows;
    }

    private void SaveTo(AppSettings settings)
    {
        settings.Target.Hostname = _hostnameBox.Text.Trim();
        settings.Target.CheckIntervalSeconds = (int)_intervalBox.Value;
        settings.Target.CheckTimeoutSeconds = (int)_timeoutBox.Value;
        settings.Target.EnableIcmpProbe = _icmpCheckBox.Checked;

        settings.Ssh.Host = settings.Target.Hostname;
        settings.Ssh.Username = _sshUserBox.Text.Trim();
        settings.Ssh.Port = (int)_sshPortBox.Value;
        settings.Ssh.PrivateKeyPath = _sshKeyPathBox.Text.Trim();

        settings.Shutdown.Command = _shutdownCommandBox.Text.Trim();
        settings.Shutdown.PromptForSudoPassword = _promptForSudoPasswordCheckBox.Checked;
        settings.Shutdown.ProcessIgnoreList = _ignoreListBox.Text
            .Split('\n')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        settings.Logging.Enabled = _loggingCheckBox.Checked;
        settings.StartWithWindows = _autostartCheckBox.Checked;
    }

    private static AppSettings Clone(AppSettings source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        Target = new TargetSettings
        {
            Hostname = source.Target.Hostname,
            CheckIntervalSeconds = source.Target.CheckIntervalSeconds,
            CheckTimeoutSeconds = source.Target.CheckTimeoutSeconds,
            EnableIcmpProbe = source.Target.EnableIcmpProbe,
        },
        Ssh = new SshSettings
        {
            Host = source.Ssh.Host,
            Port = source.Ssh.Port,
            Username = source.Ssh.Username,
            PrivateKeyPath = source.Ssh.PrivateKeyPath,
            ConnectTimeoutSeconds = source.Ssh.ConnectTimeoutSeconds,
        },
        Shutdown = new ShutdownSettings
        {
            Command = source.Shutdown.Command,
            PromptForSudoPassword = source.Shutdown.PromptForSudoPassword,
            ProcessIgnoreList = new List<string>(source.Shutdown.ProcessIgnoreList),
        },
        Logging = new LoggingSettings { Enabled = source.Logging.Enabled },
        StartWithWindows = source.StartWithWindows,
    };
}
