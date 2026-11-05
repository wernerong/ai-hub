namespace AIHub;

internal sealed partial class HubForm
{
    private WorkspaceSplit? split;
    private SplitContainer splitContainer = null!;
    private Panel firstPane = null!, secondPane = null!;
    private Button firstFocus = null!, secondFocus = null!, layoutButton = null!;
    private Label firstPlaceholder = null!, secondPlaceholder = null!;
    private bool changingSplit;

    private Control CreateSplitHost()
    {
        splitContainer = new SplitContainer { Dock = DockStyle.Fill, BackColor = UsageStyle.Line,
            SplitterWidth = 8, Panel1MinSize = 0, Panel2MinSize = 0, Visible = false, TabStop = false };
        firstPane = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        secondPane = new Panel { Dock = DockStyle.Fill, BackColor = Background };
        firstFocus = MakeButton("First AI", async () => await SelectSplitPane(true));
        secondFocus = MakeButton("Second AI", async () => await SelectSplitPane(false));
        firstPlaceholder = new Label { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter };
        secondPlaceholder = new Label { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleCenter };
        firstPane.Controls.Add(firstPlaceholder); secondPane.Controls.Add(secondPlaceholder);
        foreach (var (panel, host, button) in new[] { (splitContainer.Panel1, firstPane, firstFocus), (splitContainer.Panel2, secondPane, secondFocus) })
        {
            button.Dock = DockStyle.Top; button.Height = 36; button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 8, 0);
            panel.Controls.Add(host); panel.Controls.Add(button);
            host.Resize += (_, _) => ResizeSelected();
        }
        splitContainer.Resize += (_, _) => FitSplitter();
        splitContainer.SplitterMoved += (_, _) =>
        {
            if (changingSplit || split is null) return;
            int span = SplitSpan;
            if (span > 0) split = split with { Ratio = Math.Clamp((int)Math.Round(100d * splitContainer.SplitterDistance / span), 20, 80) };
            FitSplitter(); ResizeSelected(); RememberSession();
        };
        return splitContainer;
    }

    private int SplitSpan => Math.Max(0, (splitContainer.Orientation == Orientation.Vertical
        ? splitContainer.ClientSize.Width : splitContainer.ClientSize.Height) - splitContainer.SplitterWidth);

    private void FitSplitter()
    {
        if (changingSplit || split is null || splitContainer.IsDisposed) return;
        changingSplit = true;
        try
        {
            var orientation = split.Stacked ? Orientation.Horizontal : Orientation.Vertical;
            if (splitContainer.Orientation != orientation) splitContainer.Orientation = orientation;
            int span = SplitSpan;
            if (span > 1) splitContainer.SplitterDistance = Math.Clamp((int)Math.Round(span * split.Ratio / 100d), 0, span);
        }
        finally { changingSplit = false; }
    }

    private void SetSplit(WorkspaceSplit? layout)
    {
        split = layout;
        FitSplitter(); UpdateSplitChrome();
    }

    private void UpdateSplitChrome()
    {
        if (splitContainer is null) return;
        splitContainer.Visible = split is not null && !pageOpen;
        if (layoutButton is not null) layoutButton.Text = split is null ? "Split view" : "Layout · 2 AIs";
        if (split is null) return;
        foreach (var (key, button, placeholder, position) in new[] {
            (split.FirstAppKey, firstFocus, firstPlaceholder, split.Stacked ? "Top" : "Left"),
            (split.SecondAppKey, secondFocus, secondPlaceholder, split.Stacked ? "Bottom" : "Right") })
        {
            string name = catalog.Available.FirstOrDefault(a => a.Key == key)?.Name ?? AppEntry.All.FirstOrDefault(a => a.Key == key)?.Name ?? key;
            bool active = selected == key;
            button.Text = position + "  /  " + name + (active ? "  ·  FOCUSED" : "");
            button.AccessibleName = "Focus " + position.ToLowerInvariant() + " AI: " + name;
            button.ForeColor = active ? UsageStyle.Good : Ink;
            button.FlatAppearance.BorderColor = active ? UsageStyle.Good : UsageStyle.Line;
            button.FlatAppearance.BorderSize = active ? 1 : 0;
            placeholder.Text = name + " is not connected.\n\nClick the pane header to connect it, or choose another AI in Split view.";
        }
        if (!pageOpen) { empty.Visible = false; splitContainer.BringToFront(); }
    }

    private nint PaneHandle(string key) => split is not null && split.Contains(key)
        ? (key == split.FirstAppKey ? firstPane.Handle : secondPane.Handle) : viewport.Handle;

    private DockPlacement[] VisiblePlacements() => split is { } layout
        ? [new(layout.FirstAppKey, firstPane.Handle), new(layout.SecondAppKey, secondPane.Handle)]
        : selected is { } key ? [new(key, viewport.Handle)] : [];

    private nint[] VisibleWindowHandles() => VisiblePlacements().Where(p => dock.Has(p.Key))
        .Select(p => (nint)dock.Windows[p.Key].Handle).ToArray();

    private bool WorkspaceOwnsForeground() => Native.GetForegroundWindow() == Handle
        || VisiblePlacements().Any(p => dock.IsForeground(p.Key));

    private void PresentWorkspace(bool raise = true, bool activate = false)
    {
        if (WindowState == FormWindowState.Minimized || modalOpen || pageOpen || closing) { dock.HideAll(); return; }
        UpdateSplitChrome();
        dock.Present(VisiblePlacements(), selected, raise, activate);
    }

    private void ObservePaneFocus()
    {
        if (split is null || pageOpen || busy || modalOpen || openingProject) return;
        string? focused = new[] { split.FirstAppKey, split.SecondAppKey }.FirstOrDefault(dock.IsForeground);
        if (focused is null || focused == selected) return;
        selected = focused; split = split with { FocusedAppKey = focused };
        UpdateWorkspaceHeading(); UpdateNavigation(); UpdateSplitChrome(); RememberSession();
    }

    private void UpdateWorkspaceHeading()
    {
        if (selected is { } key && catalog.Available.FirstOrDefault(a => a.Key == key) is { } entry)
            heading.Text = entry.Name + "   /   " + (split is null ? entry.Description : "Focused AI · select either pane to switch");
        else if (split is not null) heading.Text = "Split workspace   /   Connect an AI in either pane";
        detach.Enabled = selected is not null && dock.Has(selected);
    }

    private async Task SelectSplitPane(bool first)
    {
        if (split is null || busy || modalOpen || openingProject || closing) return;
        string key = first ? split.FirstAppKey : split.SecondAppKey;
        if (catalog.Available.FirstOrDefault(a => a.Key == key) is { } entry) await Select(entry);
        else status.Text = "This AI is unavailable or removed. Choose another AI in Split view or add it back with +.";
    }

    private async Task ShowSplitOptions()
    {
        if (busy || modalOpen || openingProject || closing) return;
        CancelStartupResume();
        WorkspaceSplit? choice;
        SuspendForDialog();
        try
        {
            using var dialog = new SplitViewDialog(catalog.Available, split, selected);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            choice = dialog.Selection;
        }
        finally { modalOpen = false; if (!pageOpen && selected is not null) QueueSelectedPresentation(true); }
        if (choice is null)
        {
            SetSplit(null);
            if (selected is not null) { page = HubPage.Workspace; empty.Visible = false; PresentWorkspace(true, true); }
            else ShowWelcome();
            UpdateWorkspaceHeading(); RememberSession(); return;
        }
        await OpenSplit(choice, automatic: false);
    }

    private async Task OpenSplit(WorkspaceSplit layout, bool automatic)
    {
        if (busy || openingProject || closing) return;
        openingProject = true; activation.Cancel(); focusPresentation = false; dock.HideAll();
        var issues = new List<string>();
        try
        {
            page = HubPage.Workspace; SetSplit(layout);
            foreach (string key in new[] { layout.FirstAppKey, layout.SecondAppKey })
            {
                if (catalog.Available.FirstOrDefault(a => a.Key == key) is not { } entry) { issues.Add(key + " is unavailable or removed."); continue; }
                connectionIssue = null;
                await Select(entry, automatic: automatic, fromProject: true);
                if (connectionIssue is not null) issues.Add(connectionIssue);
            }
            split = layout;
            selected = dock.Has(layout.FocusedAppKey) ? layout.FocusedAppKey
                : new[] { layout.FirstAppKey, layout.SecondAppKey }.FirstOrDefault(dock.Has);
            if (selected is not null) split = split with { FocusedAppKey = selected };
            UpdateSplitChrome(); UpdateWorkspaceHeading(); UpdateNavigation();
        }
        finally
        {
            openingProject = false;
            if (WindowState != FormWindowState.Minimized) PresentWorkspace();
            QueueSelectedPresentation(true); RememberSession();
        }
        if (issues.Count > 0) status.Text = "Could not connect every split pane. " + string.Join(" ", issues);
    }

    private void HandleClosedSelection()
    {
        if (split is not null)
        {
            selected = new[] { split.FirstAppKey, split.SecondAppKey }.FirstOrDefault(dock.Has);
            if (selected is not null) split = split with { FocusedAppKey = selected };
            UpdateSplitChrome(); UpdateWorkspaceHeading(); UpdateNavigation();
        }
        else { selected = null; ShowWelcome("The app window closed. Select its tab to open it again."); }
        RememberSession();
    }

    private void LeaveSplitAfterRemoving(string key)
    {
        if (split is null || !split.Contains(key)) return;
        string other = split.Other(key);
        SetSplit(null); selected = dock.Has(other) ? other : null;
        if (!pageOpen && selected is not null) { empty.Visible = false; PresentWorkspace(); UpdateWorkspaceHeading(); }
    }
}

internal static class SplitSelection
{
    internal static WorkspaceSplit Place(WorkspaceSplit layout, string key) => layout.Contains(key)
        ? layout with { FocusedAppKey = key }
        : layout.FocusedAppKey == layout.SecondAppKey ? layout with { SecondAppKey = key, FocusedAppKey = key }
        : layout with { FirstAppKey = key, FocusedAppKey = key };
}

internal sealed class SplitViewDialog : Form
{
    private sealed record Choice(string Key, string Name) { public override string ToString() => Name; }
    internal WorkspaceSplit? Selection { get; private set; }
    internal SplitViewDialog(AppEntry[] apps, WorkspaceSplit? current, string? selected)
    {
        Text = "Workspace layout"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 370); MinimumSize = new Size(540, 370); MaximizeBox = false; MinimizeBox = false;
        BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink; Font = new Font("Segoe UI", 10); Padding = new Padding(22);
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        content.RowStyles.Add(new(SizeType.Absolute, 66));
        content.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = UsageStyle.Muted,
            Text = "TWO AIs, ONE WORKSPACE\nChoose different AIs. Click either pane to focus it; sidebar selections replace the focused pane." });
        var first = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "First split AI" };
        var second = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Second split AI" };
        Choice[] choices = apps.Select(a => new Choice(a.Key, a.Name)).ToArray(); first.Items.AddRange(choices); second.Items.AddRange(choices);
        if (choices.Length > 0)
        {
            first.SelectedIndex = Math.Max(0, Array.FindIndex(choices, c => c.Key == (current?.FirstAppKey ?? selected)));
            int index = Array.FindIndex(choices, c => c.Key == current?.SecondAppKey);
            second.SelectedIndex = index >= 0 ? index : Array.FindIndex(choices, c => c.Key != ((Choice)first.SelectedItem!).Key);
        }
        var stacked = new CheckBox { AutoSize = true, Text = "Stack vertically (top and bottom)", Checked = current?.Stacked == true };
        foreach (var control in new Control[] { new Label { Text = "First AI", AutoSize = true }, first, new Label { Text = "Second AI", AutoSize = true }, second, stacked }) content.Controls.Add(control);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var apply = UsageStyle.Button("Open split", () =>
        {
            if (first.SelectedItem is not Choice a || second.SelectedItem is not Choice b || a.Key == b.Key) return;
            string focus = current is not null && (current.FocusedAppKey == a.Key || current.FocusedAppKey == b.Key) ? current.FocusedAppKey : a.Key;
            Selection = new(a.Key, b.Key, focus, stacked.Checked, current?.Ratio ?? 50); DialogResult = DialogResult.OK;
        });
        var single = UsageStyle.Button("Single AI", () => { Selection = null; DialogResult = DialogResult.OK; });
        var cancel = UsageStyle.Button("Cancel", () => DialogResult = DialogResult.Cancel); cancel.DialogResult = DialogResult.Cancel;
        apply.Width = 140; single.Width = 120; cancel.Width = 100;
        void RefreshApply() => apply.Enabled = first.SelectedItem is Choice a && second.SelectedItem is Choice b && a.Key != b.Key;
        first.SelectedIndexChanged += (_, _) => RefreshApply(); second.SelectedIndexChanged += (_, _) => RefreshApply(); RefreshApply();
        actions.Controls.Add(apply); actions.Controls.Add(single); actions.Controls.Add(cancel);
        Controls.Add(content); Controls.Add(actions); AcceptButton = apply; CancelButton = cancel;
        Shown += (_, _) => { int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }
}
