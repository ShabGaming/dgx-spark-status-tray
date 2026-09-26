namespace SparkTray.UI;

/// <summary>
/// Small modal that asks for the remote account's sudo password, right before a shutdown -
/// never saved anywhere, just held in a local variable for that one attempt and handed to
/// <see cref="Ssh.ShutdownExecutor"/> to pipe over stdin to "sudo -S".
/// </summary>
public sealed class SudoPasswordPromptForm : Form
{
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true, Width = 260 };

    public string Password => _passwordBox.Text;

    public SudoPasswordPromptForm()
    {
        Text = "Sudo password required";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(320, 120);
        Padding = new Padding(12);

        var label = new Label
        {
            Text = "Enter the sudo password for the Spark to proceed with shutdown:",
            AutoSize = true,
            MaximumSize = new Size(296, 0),
        };

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Bottom,
        };
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(okButton);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(_passwordBox, 0, 1);
        layout.Controls.Add(buttonPanel, 0, 2);
        Controls.Add(layout);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
