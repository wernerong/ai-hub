namespace AIHub;

internal sealed class WorkspaceDialog : Form
{
    private readonly WorkspaceStore store;
    private readonly AppEntry[] available;
    private readonly string[] connectedKeys;
    private readonly string? selectedKey;
    private readonly WorkspaceBounds currentLayout;
    private readonly ListBox projects;
    private readonly TextBox name, notes, resources;
    private readonly CheckedListBox apps;
    private readonly CheckBox resume;
    private readonly Label status;
    private readonly Button save, open, delete;
    private string? editingId;
    private string? editingSelectedKey;
    private bool loading, dirty, acceptingClose;
    private int previousIndex;
    internal string? OpenProjectId { get; private set; }

    internal WorkspaceDialog(WorkspaceStore store, AppEntry[] available, string[] connectedKeys, string? selectedKey, WorkspaceBounds currentLayout)
    {
        this.store = store; this.available = available; this.connectedKeys = connectedKeys;
        this.selectedKey = selectedKey; this.currentLayout = currentLayout;
        Text = "AI Hub · Projects"; Size = new Size(990, 800); MinimumSize = new Size(780, 650);
        StartPosition = FormStartPosition.CenterParent; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 10); MinimizeBox = false; MaximizeBox = false; Padding = new Padding(22);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        shell.ColumnStyles.Add(new(SizeType.Absolute, 235)); shell.ColumnStyles.Add(new(SizeType.Percent, 100));
        shell.RowStyles.Add(new(SizeType.Absolute, 62)); shell.RowStyles.Add(new(SizeType.Percent, 100)); shell.RowStyles.Add(new(SizeType.Absolute, 100));
        var intro = new Label { Dock = DockStyle.Fill, Text = "PROJECTS\nSave the AI apps, notes and resources for each kind of work.", ForeColor = UsageStyle.Muted };
        shell.Controls.Add(intro, 0, 0); shell.SetColumnSpan(intro, 2);
        projects = new ListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink,
            BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, AccessibleName = "Saved projects", Margin = new Padding(0, 0, 18, 0) };
        shell.Controls.Add(projects, 0, 1);
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        editor.ColumnStyles.Add(new(SizeType.Percent, 100));
        foreach (float height in new[] { 25f, 34f, 25f }) editor.RowStyles.Add(new(SizeType.Absolute, height));
        editor.RowStyles.Add(new(SizeType.Percent, 35)); editor.RowStyles.Add(new(SizeType.Absolute, 25));
        editor.RowStyles.Add(new(SizeType.Percent, 35)); editor.RowStyles.Add(new(SizeType.Absolute, 25)); editor.RowStyles.Add(new(SizeType.Percent, 30));
        name = TextEntry("Project name", false); name.MaxLength = WorkspaceStore.MaxNameLength;
        apps = new CheckedListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink,
            BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true, IntegralHeight = false, AccessibleName = "Project AI apps" };
        notes = TextEntry("Project notes", true); notes.MaxLength = WorkspaceStore.MaxNotesLength;
        notes.PlaceholderText = "Goals, instructions and useful context. These notes stay in AI Hub.";
        resources = TextEntry("Project links and folders", true); resources.MaxLength = WorkspaceStore.MaxResources * 2050;
        resources.PlaceholderText = "One https link or full folder path per line. Resources are not opened automatically.";
        editor.Controls.Add(Caption("Name"), 0, 0); editor.Controls.Add(name, 0, 1);
        editor.Controls.Add(Caption("AI apps · unavailable apps stay saved"), 0, 2); editor.Controls.Add(apps, 0, 3);
        editor.Controls.Add(Caption("Project notes"), 0, 4); editor.Controls.Add(notes, 0, 5);
        editor.Controls.Add(Caption("Links and folders"), 0, 6); editor.Controls.Add(resources, 0, 7);
        shell.Controls.Add(editor, 1, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(0, 10, 0, 0) };
        footer.RowStyles.Add(new(SizeType.Absolute, 25)); footer.RowStyles.Add(new(SizeType.Absolute, 23)); footer.RowStyles.Add(new(SizeType.Percent, 100));
        resume = new CheckBox { AutoSize = true, Text = "Resume the last workspace when AI Hub starts (saved automatically)", AccessibleName = "Resume workspace on startup", ForeColor = UsageStyle.Ink };
        footer.Controls.Add(resume, 0, 0);
        status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = UsageStyle.Muted, Font = new Font("Segoe UI", 9) };
        footer.Controls.Add(status, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        save = UsageStyle.Button("Save current", SaveClicked); save.Width = 155;
        open = UsageStyle.Button("Open workspace", OpenClicked); open.Width = 165;
        delete = UsageStyle.Button("Delete project", DeleteClicked); delete.Width = 145;
        var close = UsageStyle.Button("Close", Close); close.Width = 105; close.DialogResult = DialogResult.Cancel;
        foreach (var button in new[] { save, open, delete, close }) { button.Margin = new Padding(0, 0, 8, 0); actions.Controls.Add(button); }
        footer.Controls.Add(actions, 0, 2); shell.Controls.Add(footer, 0, 2); shell.SetColumnSpan(footer, 2);
        Controls.Add(shell); CancelButton = close;
        name.TextChanged += (_, _) => Changed(); notes.TextChanged += (_, _) => Changed(); resources.TextChanged += (_, _) => Changed();
        resume.CheckedChanged += (_, _) => SaveResumePreference(); apps.ItemCheck += (_, _) => Changed();
        projects.SelectedIndexChanged += (_, _) => SelectProject();
        FormClosing += (_, e) => { if (!acceptingClose && !LeaveEdits()) { e.Cancel = true; DialogResult = DialogResult.None; } };
        Shown += (_, _) =>
        {
            var working = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, working.Width), Math.Min(Height, working.Height));
            int dark = 1; Native.DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        };
        ReloadProjects(store.ActiveProjectId);
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }

    private static Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, ForeColor = UsageStyle.Muted };
    private static TextBox TextEntry(string title, bool multiline) => new()
    {
        Dock = DockStyle.Fill, AccessibleName = title, Multiline = multiline, AcceptsReturn = multiline,
        ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink,
        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 7)
    };
    private void ReloadProjects(string? selectId)
    {
        loading = true;
        projects.Items.Clear(); projects.Items.Add(new ProjectChoice(null, "+ New project"));
        foreach (var project in store.Projects) projects.Items.Add(new ProjectChoice(project.Id, project.Name));
        int index = projects.Items.Cast<ProjectChoice>().ToList().FindIndex(p => p.Id == selectId);
        projects.SelectedIndex = Math.Max(0, index); previousIndex = projects.SelectedIndex; loading = false;
        LoadEditor(selectId is null ? null : store.Projects.FirstOrDefault(p => p.Id == selectId));
    }
    private void SelectProject()
    {
        if (loading || projects.SelectedItem is not ProjectChoice chosen) return;
        int requestedIndex = projects.SelectedIndex;
        if (!LeaveEdits()) { loading = true; projects.SelectedIndex = previousIndex; loading = false; return; }
        // Saving edits rebuilds the list, so reselect by stable identity.
        loading = true;
        int index = projects.Items.Cast<ProjectChoice>().ToList().FindIndex(p => p.Id == chosen.Id);
        projects.SelectedIndex = index >= 0 ? index : Math.Min(requestedIndex, projects.Items.Count - 1);
        previousIndex = projects.SelectedIndex; loading = false;
        LoadEditor(chosen.Id is null ? null : store.Projects.FirstOrDefault(p => p.Id == chosen.Id));
    }
    private void LoadEditor(ProjectWorkspace? project)
    {
        loading = true; editingId = project?.Id; editingSelectedKey = project?.SelectedAppKey;
        name.Text = project?.Name ?? ""; notes.Text = project?.Notes ?? ""; resources.Lines = project?.Resources ?? [];
        resume.Checked = store.ResumeOnStartup;
        var chosen = (project?.AppKeys ?? connectedKeys).ToHashSet(StringComparer.Ordinal);
        apps.Items.Clear();
        foreach (var entry in available) apps.Items.Add(new AppChoice(entry.Key, entry.Name), chosen.Contains(entry.Key));
        foreach (var key in chosen.Where(key => available.All(a => a.Key != key)))
            apps.Items.Add(new AppChoice(key, (AppEntry.All.FirstOrDefault(a => a.Key == key)?.Name ?? key) + " · unavailable"), true);
        dirty = false; loading = false; UpdateActions();
    }
    private void Changed() { if (!loading) { dirty = true; UpdateActions(); } }
    private void SaveResumePreference()
    {
        if (loading) return;
        try
        {
            store.SetResumeOnStartup(resume.Checked);
            status.Text = dirty ? "Startup preference saved. Project edits are still unsaved." : "Startup preference saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            loading = true; resume.Checked = store.ResumeOnStartup; loading = false;
            MessageBox.Show(this, ex.Message, "Could not save startup preference", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    private void UpdateActions()
    {
        bool writable = store.Warning is null;
        save.Enabled = writable; delete.Enabled = writable && editingId is not null;
        open.Enabled = editingId is not null && !dirty;
        resume.Enabled = writable;
        status.ForeColor = writable ? UsageStyle.Muted : UsageStyle.Warning;
        status.Text = store.Warning ?? (dirty ? "Unsaved changes. Save current captures the window layout; then open the workspace."
            : editingId is null ? "Choose apps and enter a name to save this workspace." : "Open reconnects the saved AI apps. Notes and resources remain local.");
    }
    private bool SaveEditor()
    {
        try
        {
            var keys = apps.CheckedItems.Cast<AppChoice>().Select(a => a.Key).ToArray();
            string? active = selectedKey is not null && keys.Contains(selectedKey) ? selectedKey
                : editingSelectedKey is not null && keys.Contains(editingSelectedKey) ? editingSelectedKey : keys.FirstOrDefault();
            var saved = store.SaveProject(new(editingId ?? "", name.Text, keys, active, notes.Text,
                resources.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray(), currentLayout));
            ReloadProjects(saved.Id); status.Text = "Project saved. Open workspace to use it."; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Could not save project", MessageBoxButtons.OK, MessageBoxIcon.Information); return false;
        }
    }
    private void SaveClicked() => SaveEditor();
    private bool LeaveEdits()
    {
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "Save changes to this project before continuing?", "Unsaved project changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && SaveEditor();
    }
    private void OpenClicked()
    {
        if (editingId is null || dirty) return;
        OpenProjectId = editingId; acceptingClose = true; DialogResult = DialogResult.OK;
    }
    private void DeleteClicked()
    {
        if (editingId is null) return;
        if (MessageBox.Show(this, "Delete this saved project? Your AI apps, chats and linked folders stay in place.", "Delete project", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { store.RemoveProject(editingId); ReloadProjects(null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, "Could not delete project", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    private sealed record ProjectChoice(string? Id, string Name) { public override string ToString() => Name; }
    private sealed record AppChoice(string Key, string Name) { public override string ToString() => Name; }
}
