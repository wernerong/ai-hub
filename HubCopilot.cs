namespace AIHub;

internal sealed partial class HubForm
{
    private CopilotPage? copilotPage;
    private bool copilotShutdownReady;

    private async Task ShowCopilot()
    {
        copilotShutdownReady = false;
        if (page == HubPage.Copilot && copilotPage?.Parent == empty)
        {
            await ReconcileCopilotWorkspace(); copilotPage.Focus(); return;
        }
        page = HubPage.Copilot; focusPresentation = false;
        activation.Cancel(); taskbar?.Cancel(); taskbar?.SetTarget(0); dock.HideAll();
        SetSplit(null); ClearContent();
        if (copilotPage is null)
        {
            copilotPage = new CopilotPage(Path.GetDirectoryName(usagePreferencesPath)!, isolated: isolated);
            copilotPage.StateChanged += () => { if (!closing && !IsDisposed) UpdateUsage(); };
        }
        await ReconcileCopilotWorkspace();
        selected = "copilot";
        copilotPage.Dock = DockStyle.Fill;
        empty.Padding = Padding.Empty; empty.Controls.Add(copilotPage);
        copilotPage.Visible = true;
        empty.Visible = true; empty.BringToFront();
        heading.Text = "Copilot   /   GitHub models · " + (ActiveProject?.Name ?? "Personal workspace");
        detach.Enabled = false;
        status.Text = "Copilot runs inside AI Hub. Choose a folder and connect to start a chat.";
        RememberSession(); UpdateNavigation();
    }

    private Task SetCopilotWorkspace() => copilotPage?.SetWorkspace(projects.ActiveProjectId ?? "personal",
        ActiveProject?.Name ?? "Personal workspace",
        ActiveProject?.Resources.FirstOrDefault(resource => Path.IsPathFullyQualified(resource) && Directory.Exists(resource))) ?? Task.CompletedTask;

    private async Task ReconcileCopilotWorkspace()
    {
        await SetCopilotWorkspace();
        if (copilotPage is null || projects.Warning is not null) return;
        // Retry a previously failed archive on the next explicit visit, only
        // when the project inventory loaded successfully.
        copilotPage.ArchiveMissingWorkspaces(projects.Projects.Select(project => project.Id));
        await SetCopilotWorkspace();
    }
}
