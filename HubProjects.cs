using System.Diagnostics;
using System.Text.Json;

namespace AIHub;

internal sealed partial class HubForm
{
    private readonly WorkspaceStore projects;
    private readonly string commandSettingsPath;
    private Button projectButton = null!, commandsButton = null!;
    private CommandShortcut? commandShortcut;
    private Keys commandKey = Keys.Space;
    private bool modalOpen, openingProject, sessionReady;
    private bool startupResumeCanceled, lastMaximized;
    private string? connectionIssue;
    private ProjectWorkspace? ActiveProject => projects.Projects.FirstOrDefault(p => p.Id == projects.ActiveProjectId);

    private Control CreateProjectBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(20, 3, 16, 3), BackColor = Surface };
        projectButton = MakeButton("Projects", async () => await ShowProjects());
        projectButton.Dock = DockStyle.Fill; projectButton.TextAlign = ContentAlignment.MiddleLeft;
        projectButton.AccessibleName = "Manage saved projects";
        commandsButton = MakeButton("Commands  ·  Ctrl+Alt+Space", async () => await ShowCommands());
        commandsButton.Dock = DockStyle.Right; commandsButton.Width = 240;
        commandsButton.AccessibleName = "Open command palette";
        var contextButton = MakeButton("Context shelf", async () => await ShowContextShelf());
        contextButton.Dock = DockStyle.Right; contextButton.Width = 125; contextButton.AccessibleName = "Open context shelf and handoff";
        layoutButton = MakeButton("Split view", async () => await ShowSplitOptions());
        layoutButton.Dock = DockStyle.Right; layoutButton.Width = 125; layoutButton.AccessibleName = "Choose workspace layout";
        bar.Controls.Add(projectButton); bar.Controls.Add(layoutButton); bar.Controls.Add(contextButton); bar.Controls.Add(commandsButton);
        UpdateProjectBar();
        return bar;
    }

    private void UpdateProjectBar()
    {
        if (projectButton is null) return;
        projectButton.Text = ActiveProject is { } project ? "PROJECT   /   " + project.Name : "PROJECTS   /   Personal workspace";
        navigationHints.SetToolTip(projectButton, "Save apps, notes and links in a project. Manage projects and session resume.");
    }

    private WorkspaceBounds CaptureLayout()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height, DeviceDpi,
            WindowState == FormWindowState.Maximized || WindowState == FormWindowState.Minimized && lastMaximized);
    }

    private void ApplyLayout(WorkspaceBounds? saved)
    {
        if (saved is null) return;
        var screen = Screen.FromRectangle(new Rectangle(saved.X, saved.Y, saved.Width, saved.Height));
        MinimumSize = new Size(Math.Min(1000, screen.WorkingArea.Width), Math.Min(700, screen.WorkingArea.Height));
        WindowState = FormWindowState.Normal;
        Bounds = WorkspacePlacement.Fit(saved, screen.WorkingArea, DeviceDpi);
        if (saved.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void RememberSession()
    {
        if (!sessionReady || openingProject) return;
        try { projects.SaveSession(projects.ActiveProjectId, selected, CaptureLayout(), split); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { status.Text = "Could not save the workspace: " + ex.Message; }
    }

    private async Task ResumeSession()
    {
        if (startupResumeCanceled || closing || IsDisposed) return;
        if (projects.Warning is { } warning) status.Text = "Could not load workspaces: " + warning;
        if (!projects.ResumeOnStartup)
        {
            sessionReady = WorkspaceStartup.TryStartPersonal(projects, out var error);
            if (error is not null) status.Text = error;
            UpdateProjectBar(); return;
        }
        ApplyLayout(projects.LastLayout);
        var lastSplit = projects.LastSplit;
        if (ActiveProject is { } project)
        {
            string? last = projects.LastSelectedAppKey;
            await OpenProject(project, restoreLayout: false);
            if (lastSplit is null && last != selected && catalog.Available.FirstOrDefault(a => a.Key == last) is { } lastApp)
                await Select(lastApp, automatic: true);
        }
        else if (lastSplit is null && projects.LastSelectedAppKey is { } key && catalog.Available.FirstOrDefault(a => a.Key == key) is { } entry)
        {
            connectionIssue = null;
            await Select(entry, automatic: true);
            if (connectionIssue is not null) ShowWelcome(connectionIssue);
        }
        if (lastSplit is not null) await OpenSplit(lastSplit, automatic: true);
        sessionReady = true;
        UpdateProjectBar(); RememberSession();
    }

    private async Task ShowProjects()
    {
        if (busy || closing || modalOpen || openingProject) return;
        CancelStartupResume();
        RememberSession();
        var previousIds = projects.Projects.Select(p => p.Id).ToArray();
        string? openId;
        SuspendForDialog();
        try
        {
            using var dialog = new WorkspaceDialog(projects, catalog.Available,
                dock.Windows.Where(p => p.Value.Matches()).Select(p => p.Key).ToArray(), selected, CaptureLayout(), split);
            dialog.ShowDialog(this);
            openId = dialog.OpenProjectId;
        }
        finally { modalOpen = false; UpdateProjectBar(); }
        var cleanupErrors = new List<string>();
        foreach (var removed in previousIds.Where(id => projects.Projects.All(p => p.Id != id)))
            try { contextShelf.RemoveProject(removed); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { cleanupErrors.Add(ex.Message); }
        if (openId is not null && projects.Projects.FirstOrDefault(p => p.Id == openId) is { } project) await OpenProject(project);
        else if (selected is not null && !pageOpen) QueueSelectedPresentation(true);
        else if (!pageOpen && split is null) ShowWelcome();
        if (cleanupErrors.Count > 0)
        {
            SuspendForDialog();
            try { MessageBox.Show(this, "The project was deleted, but its saved context could not be removed from context-shelf.json.\n\n" + string.Join("\n", cleanupErrors),
                "Could not remove project context", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { modalOpen = false; if (selected is not null && !pageOpen) QueueSelectedPresentation(true); }
        }
    }

    private async Task OpenProject(ProjectWorkspace project, bool restoreLayout = true)
    {
        if (busy || closing || openingProject) return;
        RememberSession();
        var issues = new List<string>();
        try
        {
            WorkspaceSessionSwitch.Begin(projects, project.Id, project.SelectedAppKey, project.Layout, project.Split, () =>
            {
                openingProject = true; activation.Cancel(); focusPresentation = false;
                // A failed target save must leave the previous workspace intact.
                dock.RestoreAll(); appliedZoom.Clear(); selected = null; SetSplit(null);
            });
            if (restoreLayout) ApplyLayout(project.Layout);
            UpdateProjectBar();
            await RefreshDiscovery(true);
            foreach (var key in project.AppKeys.OrderBy(key => key == project.SelectedAppKey ? 1 : 0))
            {
                if (catalog.Available.FirstOrDefault(a => a.Key == key) is not { } entry)
                {
                    issues.Add($"{AppEntry.All.FirstOrDefault(a => a.Key == key)?.Name ?? key} is unavailable or removed.");
                    continue;
                }
                connectionIssue = null;
                await Select(entry, automatic: true, fromProject: true);
                if (connectionIssue is not null) issues.Add(connectionIssue);
            }
            if (project.Split is { } layout)
            {
                SetSplit(layout); page = HubPage.Workspace;
                selected = dock.Has(layout.FocusedAppKey) ? layout.FocusedAppKey : new[] { layout.FirstAppKey, layout.SecondAppKey }.FirstOrDefault(dock.Has);
                if (selected is not null) split = layout with { FocusedAppKey = selected };
                UpdateSplitChrome(); UpdateWorkspaceHeading(); UpdateNavigation();
            }
            else if (selected is null) ShowWelcome(issues.Count > 0 ? string.Join("\n", issues) : "Your project is ready. Select an AI or manage the project's notes and links.");
        }
        catch (Exception ex)
        {
            issues.Add("Could not finish opening the project: " + ex.Message);
            if (selected is null) ShowWelcome(issues[^1]);
        }
        finally
        {
            openingProject = false;
            if (!pageOpen && split is not null) PresentWorkspace();
            if (selected is not null && !pageOpen) QueueSelectedPresentation(true);
            RememberSession(); UpdateProjectBar();
        }
        if (issues.Count > 0)
        {
            status.Text = "Could not reconnect every project app. " + string.Join(" ", issues);
            navigationHints.SetToolTip(projectButton, string.Join("\n", issues));
        }
    }

    private void SuspendForDialog()
    {
        modalOpen = true; activation.Cancel(); taskbar?.Cancel(); taskbar?.SetTarget(0); focusPresentation = false;
        dock.HideAll();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    private void AddProjectOverview(FlowLayoutPanel content)
    {
        if (ActiveProject is not { } project) return;
        content.Controls.Add(new Label { Text = project.Name, AutoSize = true, ForeColor = UsageStyle.Good,
            Font = new Font("Segoe UI Semibold", 16), Margin = new Padding(0, 0, 0, 10) });
        if (!string.IsNullOrWhiteSpace(project.Notes)) content.Controls.Add(new Label { Text = project.Notes.Length > 500 ? project.Notes[..500] + "…" : project.Notes,
            UseMnemonic = false, AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Muted, Margin = new Padding(0, 0, 0, 12) });
        foreach (var resource in project.Resources)
        {
            var button = MakeButton("Open   " + resource, () => OpenResource(resource));
            button.Width = 660; button.TextAlign = ContentAlignment.MiddleLeft; button.Margin = new Padding(0, 0, 0, 8);
            content.Controls.Add(button);
        }
    }

    private void OpenResource(string value)
    {
        try
        {
            if (Directory.Exists(value) || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
            else throw new InvalidOperationException("This folder is unavailable. Update its path in Projects.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Project resource", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }

    private IEnumerable<HubCommand> Commands()
    {
        yield return new("projects", "Manage projects", "Save apps, notes, folders and links", "workspace create save resume");
        yield return new("personal", "Open personal workspace", "Leave the current project and return to your apps", "home default");
        yield return new("split", "Choose split view", "Show two AIs side by side or stacked; return to one AI", "layout dual compare panes");
        if (split is not null) yield return new("swap-panes", "Swap split panes", "Move the first and second AI to opposite sides", "layout reverse left right top bottom");
        yield return new("context", "Open context shelf and handoff", "Choose snippets, instructions and file references; preview before copying", "clipboard transfer draft context files");
        yield return new("history", "Show usage history", "Local quota timeline and estimates for each product", "consumption resets trends retention");
        foreach (var project in projects.Projects)
            yield return new("project:" + project.Id, "Open project: " + project.Name, "Restore saved apps and layout", project.Name + " " + project.Notes);
        foreach (var app in catalog.Available)
            yield return new("app:" + app.Key, "Open " + app.Name, app.Description, "AI switch focus " + app.Key);
        if (ActiveProject is { } active)
        {
            yield return new("notes", "Copy project notes", active.Name, "clipboard instructions context");
            for (int i = 0; i < active.Resources.Length; i++)
                yield return new("resource:" + i, "Open project resource", active.Resources[i], "folder link " + active.Name);
        }
        yield return new("connect", "Connect or reconnect an AI", "Choose an app or a dedicated browser window", "browser add repair");
        yield return new("usage", "Show usage overview", "Remaining allowance and resets", "quota monitor");
        yield return new("refresh", "Refresh usage", "Request fresh provider readings", "quota monitor");
        yield return new("settings", "Open settings", "Updates, usage alerts and zoom", "preferences");
        yield return new("shortcut", "Change command shortcut", "Choose a global shortcut or disable it", "hotkey keyboard");
        if (selected is not null) yield return new("pop", "Pop out selected AI", "Return the app to its original window", "detach");
        if (dock.Windows.Count > 0) yield return new("restore", "Restore all AI windows", "Return connected apps to the desktop", "detach");
    }

    private async Task ShowCommands()
    {
        if (busy || closing || modalOpen || openingProject) return;
        CancelStartupResume();
        string? command;
        SuspendForDialog();
        try
        {
            using var dialog = new CommandPaletteDialog(Commands());
            dialog.ShowDialog(this); command = dialog.SelectedCommandId;
        }
        finally { modalOpen = false; }
        try
        {
            switch (command)
            {
                case "projects": await ShowProjects(); break;
                case "personal":
                    RememberSession();
                    WorkspaceSessionSwitch.Begin(projects, null, null, CaptureLayout(), null, () =>
                    {
                        dock.RestoreAll(); appliedZoom.Clear(); selected = null; SetSplit(null);
                    });
                    UpdateProjectBar(); ShowWelcome(); break;
                case "split": await ShowSplitOptions(); break;
                case "context": await ShowContextShelf(); break;
                case "history": ShowUsageHistory(); break;
                case "swap-panes":
                    if (split is { } current) { SetSplit(current with { FirstAppKey = current.SecondAppKey, SecondAppKey = current.FirstAppKey, Ratio = 100 - current.Ratio }); PresentWorkspace(); RememberSession(); } break;
                case "connect": await ConnectAI(); break;
                case "usage": ShowUsage(); break;
                case "refresh": await usage.RefreshAsync(true); break;
                case "settings": ShowSettings(); break;
                case "shortcut": ChangeCommandShortcut(); break;
                case "pop": PopOut(); break;
                case "restore": RestoreAll(); break;
                case "notes": if (ActiveProject is { } active && active.Notes.Length > 0) Clipboard.SetText(active.Notes); break;
                case { } id when id.StartsWith("project:"):
                    if (projects.Projects.FirstOrDefault(p => p.Id == id[8..]) is { } project) await OpenProject(project); break;
                case { } id when id.StartsWith("app:"):
                    if (catalog.Available.FirstOrDefault(a => a.Key == id[4..]) is { } app) await Select(app); break;
                case { } id when id.StartsWith("resource:"):
                    if (int.TryParse(id[9..], out int index) && ActiveProject is { } owner && index >= 0 && index < owner.Resources.Length) OpenResource(owner.Resources[index]); break;
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "AI Hub commands", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally { if (!pageOpen && selected is not null) QueueSelectedPresentation(true); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K)) { _ = ShowCommands(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void CancelStartupResume()
    {
        // A deliberate action during discovery takes priority over saved state.
        if (sessionReady) return;
        startupResumeCanceled = true;
        // Clear the previous active identity before a manual action can save
        // this new session. Failed persistence must not enable later writes
        // against a project that was never restored.
        sessionReady = WorkspaceStartup.TryStartPersonal(projects, out var error);
        if (error is not null) status.Text = error;
        UpdateProjectBar();
    }

    private void InitializeCommands()
    {
        try
        {
            if (File.Exists(commandSettingsPath)) commandKey = JsonSerializer.Deserialize<Keys>(File.ReadAllText(commandSettingsPath));
            if (commandKey is not (Keys.None or Keys.Space or Keys.K or Keys.P)) commandKey = Keys.Space;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { commandKey = Keys.Space; }
        commandShortcut = new CommandShortcut(Handle);
        bool registered = commandKey == Keys.None || commandShortcut.TryRegister(commandKey, Keys.Control | Keys.Alt);
        UpdateCommandLabel(registered);
    }

    private void ChangeCommandShortcut()
    {
        SuspendForDialog();
        try
        {
            using var dialog = new ShortcutDialog(commandKey);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var chosen = dialog.SelectedKey;
            if (commandShortcut?.TryRegister(chosen, Keys.Control | Keys.Alt) != true)
                throw new InvalidOperationException("That shortcut is already in use. Choose another shortcut or use the Commands button.");
            try
            {
                File.WriteAllText(commandSettingsPath + ".tmp", JsonSerializer.Serialize(chosen));
                File.Move(commandSettingsPath + ".tmp", commandSettingsPath, true);
                commandKey = chosen;
            }
            catch { commandShortcut.TryRegister(commandKey, Keys.Control | Keys.Alt); throw; }
            UpdateCommandLabel(true);
        }
        finally { modalOpen = false; }
    }

    private void UpdateCommandLabel(bool registered)
    {
        commandsButton.Text = registered && commandKey != Keys.None ? "Commands  ·  Ctrl+Alt+" + commandKey : "Commands  ·  Ctrl+K";
        navigationHints.SetToolTip(commandsButton, registered ? "Search apps, projects and actions. Ctrl+K also works while AI Hub has focus."
            : "The global shortcut is unavailable. Use this button, then Change command shortcut.");
    }
}

internal static class WorkspaceSessionSwitch
{
    internal static void Begin(WorkspaceStore projects, string? projectId, string? selectedKey,
        WorkspaceBounds? layout, WorkspaceSplit? split, Action releaseCurrent)
    {
        // Commit the destination before releasing native windows or clearing
        // runtime state. If persistence fails, later autosaves still describe
        // the previous workspace rather than an uncompleted switch.
        projects.SaveSession(projectId, selectedKey, layout, split);
        releaseCurrent();
    }
}

internal static class WorkspaceStartup
{
    internal static bool TryStartPersonal(WorkspaceStore projects, out string? error)
    {
        try { projects.SaveSession(null, null, null); error = null; return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { error = "Could not save the workspace: " + ex.Message; return false; }
    }
}

internal static class WorkspacePlacement
{
    internal static Rectangle Fit(WorkspaceBounds saved, Rectangle area, int dpi)
    {
        double scale = (double)dpi / Math.Max(48, saved.Dpi);
        int width = Math.Clamp((int)Math.Round(saved.Width * scale), Math.Min(1000, area.Width), area.Width);
        int height = Math.Clamp((int)Math.Round(saved.Height * scale), Math.Min(700, area.Height), area.Height);
        return new(Math.Clamp(saved.X, area.Left, area.Right - width), Math.Clamp(saved.Y, area.Top, area.Bottom - height), width, height);
    }
}
