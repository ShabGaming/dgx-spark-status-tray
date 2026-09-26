using SparkTray.Config;
using SparkTray.Logging;
using SparkTray.Ssh;

namespace SparkTray.UI;

public sealed class ShutdownConfirmForm : Form
{
    private readonly AppSettings _settings;
    private readonly ISshCommandRunner _sshRunner;
    private readonly IAppLogger _logger;

    private readonly Label _statusLabel = new() { AutoSize = true, Text = "Checking for active work…" };
    private readonly TextBox _detailsBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Width = 420,
        Height = 180,
    };

    private readonly Button _proceedButton = new() { Text = "Shut down", Width = 100, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Cancel", Width = 100 };

    public ShutdownConfirmForm(AppSettings settings, ISshCommandRunner sshRunner, IAppLogger logger)
    {
        _settings = settings;
        _sshRunner = sshRunner;
        _logger = logger;

        Text = "Shut down DGX Spark";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 280);
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(_statusLabel, 0, 0);
        layout.Controls.Add(_detailsBox, 0, 1);

        var buttonPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        buttonPanel.Controls.Add(_cancelButton);
        buttonPanel.Controls.Add(_proceedButton);
        layout.Controls.Add(buttonPanel, 0, 2);

        Controls.Add(layout);

        _cancelButton.Click += (_, _) => Close();
        _proceedButton.Click += async (_, _) => await ProceedAsync();

        Shown += async (_, _) => await RunPreShutdownCheckAsync();
    }

    private async Task RunPreShutdownCheckAsync()
    {
        var connection = new SshConnectionInfo(_settings.Ssh.Host, _settings.Ssh.Port, _settings.Ssh.Username, _settings.Ssh.PrivateKeyPath);
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.Ssh.ConnectTimeoutSeconds));

        var gpuTask = GpuActivityCheck.RunAsync(_sshRunner, connection, timeout, CancellationToken.None);
        var processTask = ProcessActivityCheck.RunAsync(_sshRunner, connection, _settings.Shutdown.ProcessIgnoreList, timeout, CancellationToken.None);

        await Task.WhenAll(gpuTask, processTask);
        var gpu = gpuTask.Result;
        var processes = processTask.Result;

        var lines = new List<string>();
        var somethingRunning = false;

        if (!gpu.Available)
        {
            lines.Add($"GPU status: unavailable ({gpu.Error})");
        }
        else
        {
            if (gpu.UtilizationSummary is { Length: > 0 })
            {
                lines.Add($"GPU utilization/memory: {gpu.UtilizationSummary}");
            }

            if (gpu.Processes.Count > 0)
            {
                somethingRunning = true;
                lines.Add("");
                lines.Add("Active GPU processes:");
                foreach (var p in gpu.Processes)
                {
                    lines.Add($"  pid {p.Pid}  {p.ProcessName}  ({p.UsedMemory})");
                }
            }
        }

        if (!processes.Available)
        {
            lines.Add($"Process check: unavailable ({processes.Error})");
        }
        else if (processes.ActiveProcessNames.Count > 0)
        {
            somethingRunning = true;
            lines.Add("");
            lines.Add("Other processes not in the ignore list:");
            foreach (var name in processes.ActiveProcessNames)
            {
                lines.Add($"  {name}");
            }
        }

        var checkFailed = !gpu.Available && !processes.Available;

        if (checkFailed)
        {
            _statusLabel.Text = "Could not check for active work. Shut down anyway?";
        }
        else if (somethingRunning)
        {
            _statusLabel.Text = "Possible active work detected - shut down anyway?";
        }
        else
        {
            _statusLabel.Text = "No active GPU or process work detected.";
        }

        _detailsBox.Text = lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "(nothing to report)";
        _proceedButton.Enabled = true;
    }

    private async Task ProceedAsync()
    {
        string? sudoPassword = null;
        if (_settings.Shutdown.PromptForSudoPassword)
        {
            using var promptForm = new SudoPasswordPromptForm();
            if (promptForm.ShowDialog(this) != DialogResult.OK || promptForm.Password.Length == 0)
            {
                return; // user cancelled or left it blank - stay on the confirmation dialog
            }

            sudoPassword = promptForm.Password;
        }

        _proceedButton.Enabled = false;
        _cancelButton.Enabled = false;
        _statusLabel.Text = "Sending shutdown command…";

        var connection = new SshConnectionInfo(_settings.Ssh.Host, _settings.Ssh.Port, _settings.Ssh.Username, _settings.Ssh.PrivateKeyPath);
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _settings.Ssh.ConnectTimeoutSeconds));
        var executor = new ShutdownExecutor(_sshRunner, _logger);

        await executor.ShutdownAsync(connection, _settings.Shutdown.Command, timeout, CancellationToken.None, sudoPassword);

        MessageBox.Show(this, "Shutdown command sent.", "DGX Spark Status Tray",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
}
