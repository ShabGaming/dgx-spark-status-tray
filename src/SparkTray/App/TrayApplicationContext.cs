using SparkTray.Config;
using SparkTray.Logging;
using SparkTray.Monitoring;
using SparkTray.NvidiaSyncImport;
using SparkTray.Ssh;
using SparkTray.UI;

namespace SparkTray.App;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly TrayIcons _icons;
    private readonly ToolStripMenuItem _statusItem;
    private readonly SettingsStore _settingsStore;
    private readonly IAppLogger _logger;
    private readonly HealthCheckService _healthCheck;
    private readonly Control _uiMarshal = new();

    private AppSettings _settings;
    private HealthState _lastNotifiedState = HealthState.Unknown;
    private Func<ISshCommandRunner> _sshRunnerFactory;

    public TrayApplicationContext(SettingsStore settingsStore, IAppLogger logger, Func<ISshCommandRunner> sshRunnerFactory)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _sshRunnerFactory = sshRunnerFactory;
        _settings = _settingsStore.Load();
        _icons = new TrayIcons();

        // Force the control's window handle to exist so BeginInvoke works from the
        // health-check timer thread, without ever showing an actual window.
        _ = _uiMarshal.Handle;

        var mdnsResolver = new MdnsResolver(_logger);
        var tcpProbe = new TcpProbe();
        _healthCheck = new HealthCheckService(() => _settings, mdnsResolver, tcpProbe, _logger);
        _healthCheck.StatusChanged += OnHealthStatusChanged;

        _statusItem = new ToolStripMenuItem("Status: Unknown") { Enabled = false };
        var checkNowItem = new ToolStripMenuItem("Check now", null, (_, _) => _healthCheck.CheckNow());
        var settingsItem = new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings());
        var shutdownItem = new ToolStripMenuItem("Shut down Spark…", null, (_, _) => OpenShutdownConfirmation());
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(checkNowItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(shutdownItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _icons.For(HealthState.Unknown),
            Text = "DGX Spark Status Tray",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();

        _healthCheck.Start();

        MaybeOfferNvidiaSyncImport();
    }

    private void OnHealthStatusChanged(object? sender, HealthStatus status)
    {
        _uiMarshal.BeginInvoke(() =>
        {
            _notifyIcon.Icon = _icons.For(status.State);
            _statusItem.Text = BuildStatusText(status);

            if (status.State is HealthState.Online or HealthState.Offline && status.State != _lastNotifiedState)
            {
                ShowTransitionBalloon(status.State);
            }

            if (status.State is HealthState.Online or HealthState.Offline)
            {
                _lastNotifiedState = status.State;
            }
        });
    }

    private static string BuildStatusText(HealthStatus status)
    {
        var when = status.LastCheckedUtc is { } t ? $" ({t.ToLocalTime():HH:mm:ss})" : "";
        return $"Status: {status.State}{when}";
    }

    private void ShowTransitionBalloon(HealthState state)
    {
        var (title, text, icon) = state switch
        {
            HealthState.Online => ("DGX Spark is online", "The Spark is now reachable.", ToolTipIcon.Info),
            HealthState.Offline => ("DGX Spark is offline", "The Spark stopped responding.", ToolTipIcon.Warning),
            _ => ("", "", ToolTipIcon.None),
        };

        if (title.Length == 0)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(4000);
    }

    private void MaybeOfferNvidiaSyncImport()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Target.Hostname))
        {
            return; // already configured, nothing to auto-fill
        }

        var import = NvidiaSyncImporter.TryImport(_logger);
        if (import is null)
        {
            return;
        }

        _settings.Target.Hostname = import.Hostname;
        _settings.Ssh.Host = import.Hostname;
        _settings.Ssh.Username = import.Username;
        _settings.Ssh.Port = import.Port;
        _settings.Ssh.PrivateKeyPath = import.PrivateKeyPath;
        _settingsStore.Save(_settings);
        _healthCheck.Restart();
    }

    private void OpenSettings()
    {
        using var form = new SettingsForm(_settings, _logger);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _settings = form.Result;
            _settingsStore.Save(_settings);
            AutostartManager.SetEnabled(_settings.StartWithWindows);
            _healthCheck.Restart();
        }
    }

    private void OpenShutdownConfirmation()
    {
        if (string.IsNullOrWhiteSpace(_settings.Ssh.Host) || string.IsNullOrWhiteSpace(_settings.Ssh.PrivateKeyPath))
        {
            MessageBox.Show("Configure the SSH connection in Settings before shutting down the Spark.",
                "DGX Spark Status Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ShutdownConfirmForm(_settings, _sshRunnerFactory(), _logger);
        form.ShowDialog();
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _healthCheck.Dispose();
        _icons.Dispose();
        Application.Exit();
    }
}
