namespace AIHub;

internal sealed class SettingsPage : UserControl
{
    private sealed record Choice(int? Percent)
    {
        public override string ToString() => Percent is int p ? $"{p}%" : "Keep current zoom";
    }
    internal SettingsPage(ZoomSettings settings, Action<string, int?> save, Func<AppEntry, Task> open)
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
