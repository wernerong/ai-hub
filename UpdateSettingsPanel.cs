namespace AIHub;

internal sealed class UpdateSettingsPanel : FlowLayoutPanel
{
    private readonly AppUpdater updater;
    private readonly Label status;
    private readonly Button check, restart;
    internal UpdateSettingsPanel(AppUpdater updater, Action restartApp)
    {
        this.updater = updater;
        FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoSize = true;
        Margin = new Padding(0, 0, 0, 24);
        Controls.Add(new Label { Text = "Software updates", AutoSize = true, Font = new Font("Segoe UI Semibold", 24), Margin = new Padding(0, 0, 0, 10) });
        var automatic = new CheckBox { Text = "Automatically download updates and install when AI Hub closes", AutoSize = true,
            Checked = updater.Automatic, AccessibleName = "Automatic software updates" };
        bool loading = false;
        automatic.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            try { updater.SetAutomatic(automatic.Checked); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                loading = true; automatic.Checked = updater.Automatic; loading = false;
                MessageBox.Show(this, "Could not save update preferences.", "AI Hub");
            }
        };
        Controls.Add(automatic);
        Controls.Add(new Label { Text = "Checks the public GitHub releases at startup and every six hours.\nYour app windows are restored before an update installs. Restart now to install sooner.",
            AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = UsageStyle.Muted, Margin = new Padding(0, 6, 0, 10) });
        status = new Label { AutoSize = true, MaximumSize = new Size(680, 0), AccessibleName = "Software update status", Margin = new Padding(0, 0, 0, 10) };
        Controls.Add(status);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        check = new Button { Text = "Check for updates", AutoSize = true, FlatStyle = FlatStyle.Flat, AccessibleName = "Check for software updates" };
        check.Click += async (_, _) => await updater.CheckAsync(true);
        restart = new Button { Text = "Restart to update", AutoSize = true, FlatStyle = FlatStyle.Flat };
        restart.Click += (_, _) => restartApp();
        actions.Controls.Add(check); actions.Controls.Add(restart); Controls.Add(actions);
        updater.Changed += UpdateState;
        UpdateState();
    }
    private void UpdateState()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { BeginInvoke((Action)UpdateState); return; }
        status.Text = updater.Ready && !updater.Automatic ? "An update is ready. Choose Restart to update to install it." : updater.Status;
        check.Enabled = !updater.Checking && !updater.Ready;
        restart.Visible = updater.Ready;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) updater.Changed -= UpdateState;
        base.Dispose(disposing);
    }
}
