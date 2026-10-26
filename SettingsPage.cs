using AIHub.Usage;
using Microsoft.Win32;

namespace AIHub;

internal sealed class SettingsPage : UserControl
{
    private sealed record Choice(int? Percent)
    {
        public override string ToString() => Percent is int p ? $"{p}%" : "Keep current zoom";
    }
    internal SettingsPage(ZoomSettings settings, Action<string, int?> save, Func<AppEntry, Task> open,
        UsagePreferences usageSettings, UsageProvider[] providers, Action<UsagePreferences> saveUsage)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(19, 22, 27);
        ForeColor = Color.FromArgb(236, 240, 244);
        Font = new Font("Segoe UI", 10);
        AutoScroll = true;
        Padding = new Padding(30, 24, 30, 24);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        stack.Controls.Add(new Label { Text = "Usage & alerts", UseMnemonic = false, AutoSize = true, Font = new Font("Segoe UI Semibold", 24), Margin = new Padding(0, 0, 0, 12) });
        var usagePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            Width = 680, Margin = new Padding(0, 0, 0, 24) };
        var enabled = new Dictionary<string, CheckBox>();
        foreach (var provider in providers)
        {
            var check = new CheckBox { Text = "Monitor " + provider.Name, AutoSize = true, Checked = usageSettings.IsEnabled(provider.Id), Margin = new Padding(0, 8, 0, 0) };
            enabled.Add(provider.Id, check); usagePanel.Controls.Add(check);
            usagePanel.Controls.Add(new Label { Text = provider.Scope, AutoSize = true, ForeColor = UsageStyle.Muted, Margin = new Padding(22, 0, 0, 4) });
        }
        var lowAlerts = new CheckBox { Text = "Notify when an allowance is low", AutoSize = true, Checked = usageSettings.LowAlerts, Margin = new Padding(0, 16, 0, 4) };
        var resetAlerts = new CheckBox { Text = "Notify when a fresh reading confirms an allowance reset", AutoSize = true, Checked = usageSettings.ResetAlerts, Margin = new Padding(0, 4, 0, 4) };
        usagePanel.Controls.Add(lowAlerts); usagePanel.Controls.Add(resetAlerts);
        var thresholdRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 6) };
        thresholdRow.Controls.Add(new Label { Text = "Low allowance: at or below", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        var threshold = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 100 - usageSettings.UsedThreshold, Width = 65, AccessibleName = "Low allowance remaining percent" };
        thresholdRow.Controls.Add(threshold);
        thresholdRow.Controls.Add(new Label { Text = "% remaining", AutoSize = true, Margin = new Padding(8, 6, 0, 0) });
        usagePanel.Controls.Add(thresholdRow);
        bool loading = false;
        void SaveUsage()
        {
            if (loading) return;
            var proposed = new UsagePreferences { Providers = enabled.ToDictionary(x => x.Key, x => x.Value.Checked),
                LowAlerts = lowAlerts.Checked, ResetAlerts = resetAlerts.Checked, UsedThreshold = 100 - (int)threshold.Value };
            try { saveUsage(proposed); usageSettings = proposed; }
            catch (Exception ex)
            {
                loading = true;
                foreach (var (id, check) in enabled) check.Checked = usageSettings.IsEnabled(id);
                lowAlerts.Checked = usageSettings.LowAlerts; resetAlerts.Checked = usageSettings.ResetAlerts;
                threshold.Value = 100 - usageSettings.UsedThreshold;
                loading = false;
                MessageBox.Show(this, "Could not save usage preferences.\n\n" + ex.Message, "AI Hub");
            }
        }
        foreach (var check in enabled.Values) check.CheckedChanged += (_, _) => SaveUsage();
        lowAlerts.CheckedChanged += (_, _) => SaveUsage(); resetAlerts.CheckedChanged += (_, _) => SaveUsage();
        threshold.ValueChanged += (_, _) => SaveUsage();
        var startup = new CheckBox { Text = "Start AI Hub when I sign in to Windows", AutoSize = true, Checked = HubStartup.Enabled, Margin = new Padding(0, 12, 0, 6) };
        startup.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            try { HubStartup.Set(startup.Checked); }
            catch (Exception ex)
            {
                loading = true; startup.Checked = HubStartup.Enabled; loading = false;
                MessageBox.Show(this, "Could not change startup.\n\n" + ex.Message, "AI Hub");
            }
        };
        usagePanel.Controls.Add(startup);
        usagePanel.Controls.Add(new Label { Text = "Usage refreshes every minute using the providers' existing logins.\nClose the standalone AI Usage Widget when using AI Hub for monitoring to avoid duplicate refreshes.",
            AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = UsageStyle.Muted, Margin = new Padding(0, 8, 0, 4) });
        stack.Controls.Add(usagePanel);
        stack.Controls.Add(new Label { Text = "App content zoom", AutoSize = true, Font = new Font("Segoe UI Semibold", 24), Margin = new Padding(0, 0, 0, 10) });
        stack.Controls.Add(new Label { Text = "Choose a size for each AI. Changes are saved automatically.\nApply & open previews the selected level in that app.", UseMnemonic = false, AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Color.FromArgb(151, 160, 172), Margin = new Padding(0, 0, 0, 22) });
        foreach (var app in AppEntry.All)
        {
            var card = new Panel { Size = new Size(680, 116), BackColor = Color.FromArgb(26, 30, 36), Margin = new Padding(0, 0, 0, 14) };
            card.Controls.Add(new Label { Text = app.Name, Location = new Point(18, 14), Size = new Size(300, 26), Font = new Font("Segoe UI Semibold", 13), ForeColor = app.Accent });
            card.Controls.Add(new Label { Text = app.Key == "openai" ? "Shared by ChatGPT, Codex and Dot" : app.Description, Location = new Point(18, 42), Size = new Size(330, 22), ForeColor = Color.FromArgb(151, 160, 172) });
            var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, IntegralHeight = false, DropDownHeight = 200, Location = new Point(18, 74), Width = 190, AccessibleName = app.Name + " zoom ratio" };
            picker.Items.Add(new Choice(null));
            foreach (int value in ZoomLevels.For(app.Key)) picker.Items.Add(new Choice(value));
            picker.SelectedItem = picker.Items.Cast<Choice>().First(c => c.Percent == settings.Get(app.Key));
            var reset = Button("100%", new Point(222, 72), new Size(90, 30));
            reset.AccessibleName = "Reset " + app.Name + " zoom to 100%";
            reset.Click += (_, _) => picker.SelectedItem = picker.Items.Cast<Choice>().First(c => c.Percent == 100);
            var apply = Button("Apply & open", new Point(474, 40), new Size(184, 40));
            apply.AccessibleName = "Apply zoom and open " + app.Name;
            apply.Click += async (_, _) => await open(app);
            bool rollingBack = false;
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (rollingBack) return;
                try { save(app.Key, ((Choice)picker.SelectedItem!).Percent); }
                catch (Exception ex)
                {
                    // Roll the visible choice back when persistence fails.
                    var current = settings.Get(app.Key);
                    rollingBack = true;
                    if (((Choice)picker.SelectedItem!).Percent != current)
                        picker.SelectedItem = picker.Items.Cast<Choice>().First(c => c.Percent == current);
                    rollingBack = false;
                    MessageBox.Show(this, "Could not save zoom settings.\n\n" + ex.Message, "AI Hub");
                }
            };
            card.Controls.Add(picker);
            card.Controls.Add(reset);
            card.Controls.Add(apply);
            stack.Controls.Add(card);
        }
        stack.Controls.Add(new Label { Text = "Keep current zoom leaves an app's existing size alone.\nIn OpenAI, zoom follows the focused view; click the chat area first if a browser pane is active.", AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Color.FromArgb(151, 160, 172), Margin = new Padding(0, 6, 0, 10) });
        Controls.Add(stack);
        // Runtime-created pages need their design DPI after Font and children
        // have been initialized, just like the main hub form.
        AutoScaleDimensions = new SizeF(96, 96);
        PerformAutoScale();
    }
    private static Button Button(string text, Point location, Size size)
    {
        var button = new Button { Text = text, UseMnemonic = false, Location = location, Size = size, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(40, 48, 56), ForeColor = Color.FromArgb(236, 240, 244), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderColor = Color.FromArgb(69, 82, 92);
        return button;
    }
}

internal static class HubStartup
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue("AIHub") is string; } }
    internal static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue("AIHub", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("AIHub", false);
    }
}
