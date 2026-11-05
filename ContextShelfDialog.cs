using System.Runtime.InteropServices;

namespace AIHub;

internal sealed class ContextShelfDialog : Form
{
    private readonly ContextShelfStore store;
    private readonly string? projectId;
    private readonly string projectName, projectNotes;
    private readonly CheckedListBox shelf;
    private readonly ComboBox target;
    private readonly TextBox goal, decisions, evidence, constraints, nextStep, preview;
    private readonly CheckBox includeNotes;
    private readonly Label status;
    private readonly Button edit, remove, saveDraft, copy, copyOpen;
    private readonly Panel editorPages, composePage, previewPage;
    private readonly Button composeButton, previewButton;
    private bool loading, dirty, acceptingClose, validPreview;
    internal string? SelectedTargetKey { get; private set; }

    internal ContextShelfDialog(ContextShelfStore store, string? projectId, string projectName, string projectNotes,
        AppEntry[] available, string? selectedKey)
    {
        this.store = store; this.projectId = projectId; this.projectName = projectName; this.projectNotes = projectNotes;
        Text = "AI Hub · Context shelf · " + projectName; ClientSize = new Size(1060, 730); MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent; BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink;
        Font = new Font("Segoe UI", 10); Padding = new Padding(20); MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 32)); layout.ColumnStyles.Add(new(SizeType.Percent, 68));
        layout.RowStyles.Add(new(SizeType.Absolute, 64)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 92));
        var intro = new Label { Dock = DockStyle.Fill, Text = "CONTEXT SHELF  /  " + projectName + "\nSelect saved context, prepare a handoff, then copy the exact preview.",
            ForeColor = UsageStyle.Muted, AutoEllipsis = true, UseMnemonic = false };
        layout.Controls.Add(intro, 0, 0); layout.SetColumnSpan(intro, 2);
        var library = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = new Padding(0, 0, 16, 0) };
        library.RowStyles.Add(new(SizeType.Absolute, 26)); library.RowStyles.Add(new(SizeType.Percent, 100));
        library.RowStyles.Add(new(SizeType.Absolute, 94)); library.RowStyles.Add(new(SizeType.Absolute, 58));
        library.Controls.Add(Caption("CHECK ITEMS TO INCLUDE"), 0, 0);
        shelf = new CheckedListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink,
            BorderStyle = BorderStyle.FixedSingle, CheckOnClick = true, IntegralHeight = false, HorizontalScrollbar = true,
            AccessibleName = "Saved context items", AccessibleDescription = "Check an item to include it in the preview. Space toggles the selected item." };
        library.Controls.Add(shelf, 0, 1);
        var shelfActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 6, 0, 0) };
        shelfActions.ColumnStyles.Add(new(SizeType.Percent, 50)); shelfActions.ColumnStyles.Add(new(SizeType.Percent, 50));
        shelfActions.RowStyles.Add(new(SizeType.Percent, 50)); shelfActions.RowStyles.Add(new(SizeType.Percent, 50));
        var add = UsageStyle.Button("Add text", () => EditEntry(null, [])); add.Dock = DockStyle.Fill;
        var files = UsageStyle.Button("Add files", AddFiles); files.Dock = DockStyle.Fill;
        edit = UsageStyle.Button("Edit", () => { if (shelf.SelectedItem is ShelfChoice choice) EditEntry(choice.Entry, []); }); edit.Dock = DockStyle.Fill;
        remove = UsageStyle.Button("Remove", RemoveEntry); remove.Dock = DockStyle.Fill;
        shelfActions.Controls.Add(add, 0, 0); shelfActions.Controls.Add(files, 1, 0); shelfActions.Controls.Add(edit, 0, 1); shelfActions.Controls.Add(remove, 1, 1);
        library.Controls.Add(shelfActions, 0, 2);
        library.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Files are references only. Attach them manually in the target AI. No files are read or uploaded here.", ForeColor = UsageStyle.Muted, Font = new Font("Segoe UI", 9) }, 0, 3);
        layout.Controls.Add(library, 0, 1);

        var handoff = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        handoff.RowStyles.Add(new(SizeType.Absolute, 35)); handoff.RowStyles.Add(new(SizeType.Absolute, 29)); handoff.RowStyles.Add(new(SizeType.Percent, 100));
        var destination = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        destination.ColumnStyles.Add(new(SizeType.Absolute, 90)); destination.ColumnStyles.Add(new(SizeType.Percent, 100));
        destination.Controls.Add(Caption("Target AI"), 0, 0);
        target = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(AppEntry.Name),
            BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, AccessibleName = "Handoff target AI" };
        target.Items.AddRange(available.Cast<object>().ToArray());
        if (available.Length > 0) target.SelectedIndex = Math.Max(0, Array.FindIndex(available, a => a.Key == selectedKey));
        destination.Controls.Add(target, 1, 0); handoff.Controls.Add(destination, 0, 0);
        includeNotes = new CheckBox { AutoSize = true, Text = "Include project notes", Enabled = !string.IsNullOrWhiteSpace(projectNotes),
            Checked = false, ForeColor = UsageStyle.Ink, AccessibleName = "Include project notes in handoff" };
        handoff.Controls.Add(includeNotes, 0, 1);
        var editor = new Panel { Dock = DockStyle.Fill, BackColor = UsageStyle.Background };
        var modes = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false };
        composeButton = UsageStyle.Button("Compose", () => ShowEditorPage(false)); composeButton.Width = 115;
        previewButton = UsageStyle.Button("Exact preview", () => ShowEditorPage(true)); previewButton.Width = 145;
        modes.Controls.Add(composeButton); modes.Controls.Add(previewButton);
        editorPages = new Panel { Dock = DockStyle.Fill };
        composePage = new Panel { Dock = DockStyle.Fill, BackColor = UsageStyle.Background, Padding = new Padding(10, 0, 10, 0) };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10 };
        goal = Editor("Handoff goal"); decisions = Editor("Handoff decisions"); evidence = Editor("Handoff evidence");
        constraints = Editor("Handoff constraints"); nextStep = Editor("Handoff next step");
        var sections = new[] { ("Goal", goal), ("Decisions", decisions), ("Evidence", evidence), ("Constraints", constraints), ("Next step", nextStep) };
        for (int i = 0; i < sections.Length; i++)
        {
            fields.RowStyles.Add(new(SizeType.Absolute, 23)); fields.RowStyles.Add(new(SizeType.Percent, 20));
            fields.Controls.Add(Caption(sections[i].Item1), 0, i * 2); fields.Controls.Add(sections[i].Item2, 0, i * 2 + 1);
        }
        composePage.Controls.Add(fields);
        previewPage = new Panel { Dock = DockStyle.Fill, BackColor = UsageStyle.Background, Padding = new Padding(10, 0, 10, 0) };
        preview = Editor("Exact handoff preview"); preview.ReadOnly = true; preview.MaxLength = HandoffText.MaxLength;
        previewPage.Controls.Add(preview); editorPages.Controls.Add(composePage); editorPages.Controls.Add(previewPage);
        editor.Controls.Add(editorPages); editor.Controls.Add(modes); ShowEditorPage(false);
        handoff.Controls.Add(editor, 0, 2); layout.Controls.Add(handoff, 1, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 10, 0, 0) };
        footer.RowStyles.Add(new(SizeType.Percent, 100)); footer.RowStyles.Add(new(SizeType.Absolute, 44));
        status = new Label { Dock = DockStyle.Fill, ForeColor = UsageStyle.Muted, AutoEllipsis = true, Font = new Font("Segoe UI", 9), AccessibleName = "Context shelf status" };
        footer.Controls.Add(status, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        saveDraft = UsageStyle.Button("Save draft", () => SaveTemplate()); saveDraft.Width = 130;
        copy = UsageStyle.Button("Copy", () => CopyHandoff(false)); copy.Width = 105;
        copyOpen = UsageStyle.Button("Copy & open target", () => CopyHandoff(true)); copyOpen.Width = 205;
        var close = UsageStyle.Button("Close", Close); close.Width = 100; close.DialogResult = DialogResult.Cancel;
        foreach (var button in new[] { saveDraft, copy, copyOpen, close }) { button.Margin = new Padding(0, 0, 10, 0); actions.Controls.Add(button); }
        footer.Controls.Add(actions, 0, 1); layout.Controls.Add(footer, 0, 2); layout.SetColumnSpan(footer, 2);
        Controls.Add(layout); CancelButton = close;
        loading = true;
        var draft = store.TemplateFor(projectId);
        goal.Text = draft.Goal; decisions.Text = draft.Decisions; evidence.Text = draft.Evidence; constraints.Text = draft.Constraints; nextStep.Text = draft.NextStep;
        loading = false;
        foreach (var (_, box) in sections) box.TextChanged += (_, _) => { if (!loading) dirty = true; RefreshPreview(); };
        shelf.SelectedIndexChanged += (_, _) => UpdateActions();
        shelf.ItemCheck += (_, _) => { if (!loading && IsHandleCreated) BeginInvoke((Action)RefreshPreview); };
        target.SelectedIndexChanged += (_, _) => RefreshPreview(); includeNotes.CheckedChanged += (_, _) => RefreshPreview();
        add.Enabled = files.Enabled = store.Warning is null;
        LoadEntries(); RefreshPreview();
        Shown += (_, _) => FitAndTheme(this);
        FormClosing += (_, e) =>
        {
            if (acceptingClose || !dirty) return;
            var answer = MessageBox.Show(this, "Save this handoff draft before closing?", "Unsaved handoff", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel || answer == DialogResult.Yes && !SaveTemplate()) { e.Cancel = true; DialogResult = DialogResult.None; }
        };
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
    }

    private HandoffTemplate Draft() => new(projectId, goal.Text, decisions.Text, evidence.Text, constraints.Text, nextStep.Text);
    private void LoadEntries(string? includeId = null)
    {
        var checkedIds = shelf.CheckedItems.Cast<ShelfChoice>().Select(c => c.Entry.Id).ToHashSet(StringComparer.Ordinal);
        if (includeId is not null) checkedIds.Add(includeId);
        loading = true; shelf.Items.Clear();
        foreach (var entry in store.EntriesFor(projectId)) shelf.Items.Add(new ShelfChoice(entry), checkedIds.Contains(entry.Id));
        if (shelf.Items.Count > 0) shelf.SelectedIndex = Math.Max(0, shelf.Items.Cast<ShelfChoice>().ToList().FindIndex(c => c.Entry.Id == includeId));
        loading = false; RefreshPreview();
    }
    private void RefreshPreview()
    {
        if (loading) return;
        try
        {
            preview.Text = HandoffText.Render(projectName, (target.SelectedItem as AppEntry)?.Name, Draft(),
                shelf.CheckedItems.Cast<ShelfChoice>().Select(c => c.Entry), includeNotes.Checked ? projectNotes : null);
            validPreview = true;
            status.ForeColor = UsageStyle.Muted;
            status.Text = store.Warning ?? $"{shelf.CheckedItems.Count} selected items · {preview.Text.Length:N0} characters. Copy does not paste or send to another app.";
        }
        catch (InvalidOperationException ex) { preview.Text = ""; validPreview = false; status.Text = ex.Message; status.ForeColor = UsageStyle.Warning; }
        UpdateActions();
    }
    private void UpdateActions()
    {
        bool writable = store.Warning is null;
        edit.Enabled = remove.Enabled = writable && shelf.SelectedItem is ShelfChoice;
        saveDraft.Enabled = writable;
        bool content = shelf.CheckedItems.Count > 0 || includeNotes.Checked || new[] { goal, decisions, evidence, constraints, nextStep }.Any(t => !string.IsNullOrWhiteSpace(t.Text));
        copy.Enabled = writable && validPreview && content; copyOpen.Enabled = copy.Enabled && target.SelectedItem is AppEntry;
    }
    private bool SaveTemplate()
    {
        try { store.SaveTemplate(Draft()); dirty = false; status.Text = "Handoff draft saved for " + projectName + "."; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { MessageBox.Show(this, ex.Message, "Could not save handoff", MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
    }
    private void CopyHandoff(bool openTarget)
    {
        RefreshPreview();
        if (!copy.Enabled || openTarget && target.SelectedItem is not AppEntry) return;
        if (!SaveTemplate()) return;
        try
        {
            // Explicit user action only. Never read the clipboard or send a
            // keyboard shortcut to an AI app from this dialog.
            Clipboard.SetText(preview.Text);
            if (openTarget)
            {
                SelectedTargetKey = ((AppEntry)target.SelectedItem!).Key;
                acceptingClose = true; DialogResult = DialogResult.OK;
            }
            else { ShowEditorPage(true); status.Text = "Copied exactly as previewed. Paste it yourself; attach referenced files manually."; }
        }
        catch (ExternalException) { status.Text = "The clipboard is busy. Nothing was opened. Try Copy again."; status.ForeColor = UsageStyle.Warning; }
    }
    private void AddFiles()
    {
        using var chooser = new OpenFileDialog { Title = "Choose files to reference — contents are not read", Multiselect = true, CheckFileExists = true };
        if (chooser.ShowDialog(this) == DialogResult.OK) EditEntry(null, chooser.FileNames);
    }
    private void ShowEditorPage(bool showPreview)
    {
        composePage.Visible = !showPreview; previewPage.Visible = showPreview;
        (showPreview ? previewPage : composePage).BringToFront();
        composeButton.ForeColor = showPreview ? UsageStyle.Ink : UsageStyle.Good;
        previewButton.ForeColor = showPreview ? UsageStyle.Good : UsageStyle.Ink;
        composeButton.FlatAppearance.BorderColor = showPreview ? UsageStyle.Line : UsageStyle.Good;
        previewButton.FlatAppearance.BorderColor = showPreview ? UsageStyle.Good : UsageStyle.Line;
    }
    private void EditEntry(ContextShelfEntry? entry, string[] addedFiles)
    {
        using var editor = new ShelfEntryDialog(store, projectId, entry, addedFiles);
        if (editor.ShowDialog(this) == DialogResult.OK) LoadEntries(editor.SavedEntryId);
    }
    private void RemoveEntry()
    {
        if (shelf.SelectedItem is not ShelfChoice choice) return;
        if (MessageBox.Show(this, "Remove this item from the context shelf? Referenced files stay in place.", "Remove shelf item", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { store.RemoveEntry(choice.Entry.Id); LoadEntries(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { MessageBox.Show(this, ex.Message, "Could not remove shelf item", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    private static Label Caption(string text) => new() { Dock = DockStyle.Fill, Text = text, ForeColor = UsageStyle.Muted, TextAlign = ContentAlignment.MiddleLeft };
    private static TextBox Editor(string accessibleName) => new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
        BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, BorderStyle = BorderStyle.FixedSingle, AccessibleName = accessibleName, MaxLength = ContextShelfStore.MaxFieldLength };
    private static void FitAndTheme(Form form)
    {
        var area = Screen.FromControl(form).WorkingArea;
        form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
        form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
        form.Location = new Point(Math.Clamp(form.Left, area.Left, area.Right - form.Width), Math.Clamp(form.Top, area.Top, area.Bottom - form.Height));
        int dark = 1; Native.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
    }
    private sealed record ShelfChoice(ContextShelfEntry Entry)
    { public override string ToString() => Entry.Title + (Entry.Files.Length > 0 ? $"  [{Entry.Files.Length} file refs]" : ""); }

    private sealed class ShelfEntryDialog : Form
    {
        private readonly TextBox title, text;
        private readonly ListBox files;
        internal string? SavedEntryId { get; private set; }
        internal ShelfEntryDialog(ContextShelfStore store, string? projectId, ContextShelfEntry? existing, string[] addedFiles)
        {
            Text = existing is null ? "Add context" : "Edit context"; ClientSize = new Size(700, 580); MinimumSize = new Size(540, 420);
            BackColor = UsageStyle.Background; ForeColor = UsageStyle.Ink; Font = new Font("Segoe UI", 10); Padding = new Padding(18);
            StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
            foreach (float height in new[] { 24f, 34f, 24f }) layout.RowStyles.Add(new(SizeType.Absolute, height));
            layout.RowStyles.Add(new(SizeType.Percent, 65)); layout.RowStyles.Add(new(SizeType.Absolute, 24));
            layout.RowStyles.Add(new(SizeType.Percent, 35)); layout.RowStyles.Add(new(SizeType.Absolute, 90));
            title = new TextBox { Dock = DockStyle.Fill, MaxLength = ContextShelfStore.MaxTitleLength, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, AccessibleName = "Context item title" };
            text = Editor("Context item text"); text.MaxLength = ContextShelfStore.MaxTextLength;
            files = new ListBox { Dock = DockStyle.Fill, BackColor = UsageStyle.Surface, ForeColor = UsageStyle.Ink, BorderStyle = BorderStyle.FixedSingle,
                IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Referenced file paths" };
            title.Text = existing?.Title ?? (addedFiles.Length == 1 ? Path.GetFileName(addedFiles[0]) : addedFiles.Length > 0 ? "Reference files" : "");
            if (title.Text.Length > ContextShelfStore.MaxTitleLength) title.Text = title.Text[..ContextShelfStore.MaxTitleLength];
            text.Text = existing?.Text ?? ""; files.Items.AddRange((existing?.Files ?? addedFiles).Cast<object>().ToArray());
            layout.Controls.Add(Caption("Title"), 0, 0); layout.Controls.Add(title, 0, 1); layout.Controls.Add(Caption("Text or instructions"), 0, 2); layout.Controls.Add(text, 0, 3);
            layout.Controls.Add(Caption("File references · attach manually in the AI app"), 0, 4); layout.Controls.Add(files, 0, 5);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            footer.RowStyles.Add(new(SizeType.Percent, 50)); footer.RowStyles.Add(new(SizeType.Percent, 50));
            var fileActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            fileActions.Controls.Add(UsageStyle.Button("Add file references", () =>
            {
                using var chooser = new OpenFileDialog { Multiselect = true, CheckFileExists = true, Title = "Choose references — files are not read" };
                if (chooser.ShowDialog(this) != DialogResult.OK) return;
                foreach (string path in chooser.FileNames) if (!files.Items.Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase)) files.Items.Add(path);
            }));
            fileActions.Controls.Add(UsageStyle.Button("Remove reference", () => { if (files.SelectedIndex >= 0) files.Items.RemoveAt(files.SelectedIndex); }));
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var save = UsageStyle.Button("Save item", () =>
            {
                try
                {
                    var saved = store.SaveEntry(new(existing?.Id ?? "", projectId, title.Text, text.Text, files.Items.Cast<string>().ToArray()));
                    SavedEntryId = saved.Id; DialogResult = DialogResult.OK;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                { MessageBox.Show(this, ex.Message, "Could not save context", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            });
            var cancel = UsageStyle.Button("Cancel", () => DialogResult = DialogResult.Cancel); cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(save); actions.Controls.Add(cancel); footer.Controls.Add(fileActions, 0, 0); footer.Controls.Add(actions, 0, 1);
            layout.Controls.Add(footer, 0, 6); Controls.Add(layout); CancelButton = cancel;
            Shown += (_, _) => { FitAndTheme(this); title.Focus(); };
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); PerformAutoScale();
        }
    }
}
