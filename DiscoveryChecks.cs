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
