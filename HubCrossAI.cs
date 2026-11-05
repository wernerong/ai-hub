namespace AIHub;

internal sealed partial class HubForm
{
    private readonly ContextShelfStore contextShelf;
    private readonly UsageHistoryStore usageHistory;

    private async Task ShowContextShelf()
    {
        if (busy || closing || modalOpen || openingProject) return;
        CancelStartupResume();
        string? target;
        SuspendForDialog();
        try
        {
            using var dialog = new ContextShelfDialog(contextShelf, projects.ActiveProjectId,
                ActiveProject?.Name ?? "Personal workspace", ActiveProject?.Notes ?? "", catalog.Available, selected);
            dialog.ShowDialog(this); target = dialog.SelectedTargetKey;
        }
        finally { modalOpen = false; }
        if (target is not null && catalog.Available.FirstOrDefault(a => a.Key == target) is { } entry) await Select(entry);
        else if (!pageOpen && selected is not null) QueueSelectedPresentation(true);
    }

    private void ShowUsageHistory()
    {
        if (busy || closing || modalOpen || openingProject) return;
        CancelStartupResume(); SuspendForDialog();
        try
        {
            using var dialog = new UsageHistoryDialog(usageHistory, usage.Providers, () => usage.States);
            dialog.ShowDialog(this);
        }
        finally { modalOpen = false; if (!pageOpen && selected is not null) QueueSelectedPresentation(true); }
    }
}
