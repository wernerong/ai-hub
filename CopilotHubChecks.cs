using System.Reflection;

namespace AIHub;

internal static class CopilotHubChecks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    internal static async Task Run(string folder, List<string> lines)
    {
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            lines.Add("PASS " + label);
        }
        string root = Path.Combine(folder, "copilot-hub-" + Guid.NewGuid().ToString("N"));
        string firstFolder = Path.Combine(root, "first-project"), secondFolder = Path.Combine(root, "second-project");
        Directory.CreateDirectory(firstFolder); Directory.CreateDirectory(secondFolder);
        var seedProjects = new WorkspaceStore(Path.Combine(root, "workspaces.json"));
        var firstProject = seedProjects.SaveProject(new("", "First project", ["copilot"], "copilot", "", [firstFolder], null));
        var secondProject = seedProjects.SaveProject(new("", "Second project", ["copilot"], "copilot", "", [secondFolder], null));
        var seedChats = new CopilotChatStore(Path.Combine(root, "copilot-chats.json"));
        CopilotChat Seed(string workspace, string name, string workingFolder) => seedChats.Save(new(
            Guid.NewGuid().ToString("N"), workspace, name, workingFolder, null, null, DateTimeOffset.UtcNow,
            [new("assistant", name + " history", DateTimeOffset.UtcNow)]));
        var personal = Seed("personal", "Personal chat", root);
        var first = Seed(firstProject.Id, "First project chat", firstFolder);
        var second = Seed(secondProject.Id, "Second project chat", secondFolder);
        using var hub = new HubForm(Path.Combine(root, "windows.json"), isolated: true);
        // Never show this fixture: HubForm.Shown scans the desktop and starts live
        // services. We exercise native navigation without any provider processes.
        CopilotPage? panel = null;
        try
        {
            var entry = AppEntry.All.Single(a => a.Key == "copilot");
            await CallTask(hub, "Select", entry, null, false, false);
            panel = Field<CopilotPage>(hub, "copilotPage");
            var input = panel.Controls.Find("CopilotInput", true).OfType<TextBox>().Single();
            var parent = panel.Parent;
            Check(parent is not null && Field<string>(hub, "selected") == "copilot"
                && Field<CopilotChat>(panel, "chat").Id == personal.Id,
                "Hub selects its native Copilot page and personal history without attaching an external window");
            input.Text = "Keep this unsent draft when navigating.";
            int parentChanges = 0;
            panel.ParentChanged += (_, _) => parentChanges++;
            var permission = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            SetField(panel, "pendingPermission", permission);
            await CallTask(hub, "Select", entry, null, false, false);
            Check(ReferenceEquals(panel, Field<CopilotPage>(hub, "copilotPage")) && ReferenceEquals(parent, panel.Parent)
                && parentChanges == 0 && input.Text == "Keep this unsent draft when navigating."
                && !permission.Task.IsCompleted,
                "Reselecting Copilot preserves its page, draft and pending permission without detaching the panel");
            SetField(panel, "pendingPermission", null); permission.TrySetResult(null);
            Call(hub, "ShowSettings");
            Check(!panel.IsDisposed && panel.Parent is null && input.Text == "Keep this unsent draft when navigating.",
                "Opening Settings releases the Copilot page from view without disposing its draft");
            await CallTask(hub, "Select", entry, null, false, false);
            Check(ReferenceEquals(panel, Field<CopilotPage>(hub, "copilotPage")) && ReferenceEquals(parent, panel.Parent)
                && input.Text == "Keep this unsent draft when navigating.",
                "Returning from Settings restores the same Copilot page and unsent draft");

            var projects = Field<WorkspaceStore>(hub, "projects");
            projects.SaveSession(firstProject.Id, "copilot", null);
            await CallTask(hub, "SetCopilotWorkspace");
            Check(Field<CopilotChat>(panel, "chat").Id == first.Id && Field<string>(panel, "workspaceKey") == firstProject.Id
                && Field<TextBox>(panel, "folder").Text == firstFolder && input.Text.Length == 0,
                "Hub project binding loads that project's Copilot history and working folder without leaking the personal draft");
            projects.SaveSession(secondProject.Id, "copilot", null);
            await CallTask(hub, "SetCopilotWorkspace");
            Check(Field<CopilotChat>(panel, "chat").Id == second.Id && Field<TextBox>(panel, "folder").Text == secondFolder,
                "Switching Copilot project scope loads the second project's own conversation");
            projects.SaveSession(null, "copilot", null);
            await CallTask(hub, "SetCopilotWorkspace");
            Check(Field<CopilotChat>(panel, "chat").Id == personal.Id
                && new CopilotChatStore(Path.Combine(root, "copilot-chats.json")).ForWorkspace(firstProject.Id).Single().Id == first.Id,
                "Returning to personal Copilot history preserves the other projects' saved chats");

            var catalog = Field<AppCatalog>(hub, "catalog");
            catalog.Remove("claude"); catalog.Remove("copilot");
            var installed = new[] { new InstalledApp("OpenAI", "fixture-openai"), new InstalledApp("Claude", "fixture-claude") };
            AppEntry[] Discover() => AppCatalog.Discover(installed, [], [], [], catalog.Removed.Select(a => a.Key));
            Check(Discover().Select(a => a.Key).SequenceEqual(new[] { "openai" }),
                "Removing built-in Copilot preserves unrelated app choices and earlier removals");
            catalog.Restore("copilot");
            Check(Discover().Select(a => a.Key).ToHashSet().SetEquals(["copilot", "openai"])
                && catalog.IsRemoved("claude") && !new AppCatalog(Path.Combine(root, "connections.json")).IsRemoved("copilot"),
                "Restoring Copilot persists its visibility while retaining other removed-app preferences");
            Check(Field<object?>(panel, "agent") is null && Field<object?>(panel, "installation") is null,
                "Native Copilot navigation checks never start the runtime, authenticate or send model requests");
        }
        finally
        {
            if (panel is not null) { await panel.ShutdownAsync(); panel.Dispose(); }
            // A never-shown Form does not run FormClosed cleanup.
            foreach (string name in new[] { "timer", "usageTimer", "updateTimer", "navigationHints", "usageNotifications", "usage", "usageReaders", "updater" })
                if (Field<object?>(hub, name) is IDisposable resource) resource.Dispose();
        }
    }

    private static T Field<T>(object value, string name) => (T)(value.GetType().GetField(name, Instance)
        ?? throw new InvalidOperationException("Missing fixture field: " + name)).GetValue(value)!;
    private static void SetField(object value, string name, object? fieldValue) => (value.GetType().GetField(name, Instance)
        ?? throw new InvalidOperationException("Missing fixture field: " + name)).SetValue(value, fieldValue);
    private static object? Call(object value, string name, params object?[] arguments) => (value.GetType().GetMethod(name, Instance)
        ?? throw new InvalidOperationException("Missing fixture method: " + name)).Invoke(value, arguments);
    private static Task CallTask(object value, string name, params object?[] arguments) => (Task)(Call(value, name, arguments)
        ?? throw new InvalidOperationException("Fixture method did not return a task: " + name));
}
