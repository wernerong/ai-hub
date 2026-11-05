using AIHub.Usage;

namespace AIHub;

internal static class DiscoveryChecks
{
    internal static void Run(string folder, List<string> lines)
    {
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); lines.Add("PASS " + message); }
        InstalledApp[] three = [new("ChatGPT", "chatgpt.lnk"), new("Grok", "grok.lnk"), new("Grok Bot", "bot.lnk")];
        var found = AppCatalog.Discover(three, [], [], []);
        Check(found.Select(a => a.Key).SequenceEqual(new[] { "openai", "grok", "grokbot" }), "Only the installed AI apps appear; absent Claude is omitted");
        Check(AppCatalog.Discover([], [], [], []).Length == 0, "A new machine has no hard-coded provider placeholders");
        var claude = new WindowInfo(1, 11, "Claude", "My conversation");
        Check(AppCatalog.Discover([], [claude], [], []).Single().Key == "claude", "An open AI app is discovered without an installed shortcut");
        Check(AppCatalog.Discover([], [], [], ["claude"]).Single().Key == "claude", "A hidden hosted app stays connected during navigation");
        Check(AppCatalog.Discover([], [], [], []).Length == 0 && found.Length == 3, "Closed open-only apps disappear while installed apps stay available");
        var grok = AppEntry.All.Single(a => a.Key == "grok");
        Check(grok.Matches(new(1, 11, "chrome", "Grok - Google Chrome")) && grok.Matches(new(1, 11, "msedge", "Research - Grok"))
            && !grok.Matches(new(1, 11, "notepad", "Grok")) && !grok.Matches(new(1, 11, "chrome", "Grok Bot")), "Browser detection uses provider titles and browser identity without confusing Grok Bot");
        string path = Path.Combine(folder, "connection-test.json");
        File.Delete(path);
        File.Delete(Path.ChangeExtension(path, "hidden.json"));
        var catalog = new AppCatalog(path);
        var native = new WindowInfo(1, 11, "ExampleAI", "Question one", Environment.ProcessPath);
        var custom = catalog.Connect("Example AI", native);
        Check(custom.Custom && custom.Matches(native with { Title = "Question two" }) && !custom.Matches(native with { Executable = @"C:\Other\ExampleAI.exe" }), "Custom desktop connections follow executable identity across title changes");
        var browser = native with { ProcessName = "chrome", Title = "My web AI" };
        // Use a different executable identity from the desktop fixture.
        browser = browser with { Executable = @"C:\ExampleBrowser\chrome.exe" };
        var web = catalog.Connect("Web AI", browser);
        Check(web.Matches(browser) && !web.Matches(browser with { Title = "An unrelated tab" }) && web.LaunchTarget is null, "Custom web connections never adopt a different browser tab or launch a bare browser");
        var reload = new AppCatalog(path);
        Check(reload.Connections.Length == 2 && reload.Connections.Single(c => c.Key == web.Key).WindowTitle == browser.Title, "Named custom connections survive reload");
        Check(AppCatalog.Discover([], [], reload.Connections.Select(c => c.Entry), []).Select(a => a.Key).ToHashSet().SetEquals([custom.Key, web.Key]),
            "Saved desktop and browser connections stay available while their windows are closed");
        Check(!AppCatalog.Discover([], [], reload.Connections.Select(c => c.Entry), [], [web.Key]).Any(a => a.Key == web.Key),
            "A removed saved browser stays hidden even though closed connections remain discoverable");
        var changedBrowser = browser with { Title = "A renamed conversation", ProcessName = "msedge", Executable = @"C:\ExampleBrowser\msedge.exe" };
        web = reload.Reconnect(web.Key, changedBrowser);
        Check(web.Name == "Web AI" && reload.Connections.Length == 2 && web.Matches(changedBrowser) && !web.Matches(browser),
            "Explicit browser reconnection preserves the name and key while replacing the old window identity");
        var reconnected = new AppCatalog(path).Connections.Single(c => c.Key == web.Key).Entry;
        Check(reconnected.Matches(changedBrowser) && !reconnected.Matches(changedBrowser with { Title = "Unrelated page" }),
            "Browser reassignment survives restart without broadening matching to unrelated windows");
        string beforeInvalidReconnect = File.ReadAllText(path);
        bool rejectedNative = false, rejectedMissing = false;
        try { reload.Reconnect(web.Key, native); } catch (InvalidOperationException) { rejectedNative = true; }
        try { reload.Reconnect("custom-missing", changedBrowser); } catch (InvalidOperationException) { rejectedMissing = true; }
        Check(rejectedNative && rejectedMissing && File.ReadAllText(path) == beforeInvalidReconnect,
            "Invalid or missing browser assignments are rejected without changing saved connections");
        var otherBrowser = changedBrowser with { Title = "Another saved AI" };
        var other = reload.Connect("Other web AI", otherBrowser);
        bool rejectedDuplicate = false;
        try { reload.Reconnect(web.Key, otherBrowser); } catch (InvalidOperationException) { rejectedDuplicate = true; }
        Check(rejectedDuplicate && reload.Connections.Length == 3 && reload.Connections.Single(c => c.Key == web.Key).Entry.Matches(changedBrowser),
            "Reconnecting cannot take a window already assigned to another custom connection");
        reload.Forget(other.Key);
        reload.Remove(web.Key); reload.Reconnect(web.Key, changedBrowser);
        Check(!new AppCatalog(path).IsRemoved(web.Key) && reload.Connections.Length == 2,
            "Explicitly reconnecting a removed browser restores the existing connection without duplication");
        using (var reconnectDialog = new ConnectionDialog([native, changedBrowser], [], [], web))
        {
            var picker = Descendants(reconnectDialog).OfType<ListBox>().Single();
            Check(picker.Items.Count == 1 && ((WindowInfo)picker.Items[0]!).ProcessName == "msedge"
                && Descendants(reconnectDialog).OfType<TextBox>().Single().Text == "Web AI",
                "The reconnection picker limits choices to browser windows and retains the saved name");
        }
        reload.Remove("claude"); reload.Remove(custom.Key);
        reload = new AppCatalog(path);
        Check(reload.IsRemoved("claude") && reload.IsRemoved(custom.Key) && reload.Removed.Length == 2,
            "Removing built-in and custom apps persists across restarts and keeps their add-back entries");
        var removedKeys = reload.Removed.Select(a => a.Key).ToArray();
        Check(AppCatalog.Discover([.. three, new("Claude", "claude.lnk")], [claude, native], [custom], ["claude", custom.Key], removedKeys)
            .Select(a => a.Key).SequenceEqual(new[] { "openai", "grok", "grokbot" }),
            "Removed apps stay out of discovery even when installed, open or previously attached; other apps remain");
        reload.Restore("claude");
        Check(!new AppCatalog(path).IsRemoved("claude") && reload.IsRemoved(custom.Key), "Adding a built-in app back persists without restoring other removed apps");
        Check(reload.Connect("Example AI", native).Key == custom.Key && reload.Connections.Length == 2
            && !new AppCatalog(path).IsRemoved(custom.Key), "Reconnecting a removed custom app restores it without duplicating the saved connection");
        reload.Forget(custom.Key);
        Check(new AppCatalog(path).Connections.Single().Key == web.Key, "Forgetting a connection preserves other saved connections");
        Check(!ZoomLevels.Supports(web.Key) && !ZoomLevels.Supports("claude"), "Unknown app zoom stays under the app's own control");
        using var readers = new UsageReaders();
        using var page = new UsagePage([], () => { }, () => { }, () => { });
        page.Configure(readers.Providers.Take(1).ToArray()); page.Update([new("codex", true)], false);
        Check(Descendants(page).OfType<UsageCard>().Count() == 1, "A single enabled monitor creates exactly one usage card");
        page.Configure(readers.Providers); page.Update(PreviewStates(), false);
        Check(Descendants(page).OfType<UsageCard>().Count() == 3, "Usage cards expand dynamically for all available readers");
        page.Configure([]);
        Check(!Descendants(page).OfType<UsageCard>().Any(), "Disabling all monitors removes their cards and supports an empty dashboard");
        using var strip = new UsageStrip([], () => { });
        strip.Configure(readers.Providers.Take(1).ToArray()); strip.Update([new("codex", true)]);
        Check(strip.Controls.Count == 1, "Compact usage strip adapts to one monitor"); strip.Configure([]);
        Check(strip.Controls.Count == 0 && !strip.Visible, "No monitor means no empty usage strip");
        using var fallback = new ProviderButton(web, () => { });
        Check(fallback.AccessibleName == "Open Web AI", "Custom apps get a usable icon and accessible navigation label");
        File.Delete(path);
        File.Delete(Path.ChangeExtension(path, "hidden.json"));
    }
    private static IEnumerable<Control> Descendants(Control control) => control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));
    internal static UsageState[] PreviewStates()
    {
        var now = DateTimeOffset.Now;
        return [new("codex", true, new("Codex", [new("5 hours", 28, now.AddHours(3)), new("Weekly", 64, now.AddDays(4))], "0 free resets", now)),
            new("grok", true, new("Grok CLI", [new("Weekly", 86, now.AddDays(2))], "Prepaid balance · US$20.00", now)),
            new("grokbot", true, new("Grok Bot", [new("Weekly", 44, now.AddDays(5))], "Spend · $12.50 / $40.00", now))];
    }
    internal static void ShowPreview(string folder)
    {
        Directory.CreateDirectory(folder);
        using var readers = new UsageReaders();
        using var form = new Form { Text = "AI Hub · Design preview · Sample data", BackColor = UsageStyle.Background, Size = new Size(1200, 900), StartPosition = FormStartPosition.CenterScreen };
        var page = new UsagePage(readers.Providers, () => { }, () => form.Close(), () => { }); form.Controls.Add(page);
        form.Shown += (_, _) =>
        {
            var area = Screen.FromControl(form).WorkingArea;
            form.Size = new Size(Math.Min(form.Width, area.Width - 40), Math.Min(form.Height, area.Height - 40));
            form.Location = new Point(area.Left + (area.Width - form.Width) / 2, area.Top + (area.Height - form.Height) / 2);
            int dark = 1; Native.DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            page.Update(PreviewStates(), false);
            using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height); form.DrawToBitmap(image, form.ClientRectangle);
            image.Save(Path.Combine(folder, "usage-preview.png"));
        };
        form.AutoScaleMode = AutoScaleMode.Dpi; form.AutoScaleDimensions = new SizeF(96, 96); form.PerformAutoScale();
        Application.Run(form);
    }
}
