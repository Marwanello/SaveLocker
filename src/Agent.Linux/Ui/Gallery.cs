using System.Numerics;
using ImGuiNET;

namespace SaveLocker.Agent.Linux.Ui;

/// <summary>
/// Every widget in every state, on one screen. Reached with <c>savelocker ui --gallery</c>.
///
/// This is the Phase B verification gate, and it earns its keep afterwards: paired with
/// <c>--screenshot</c> it renders the whole component set to a PNG in one shot, so a theme change
/// can be reviewed against the entire vocabulary at once instead of hunting states across four
/// screens. It is a development surface, not a user-facing one — nothing links to it.
/// </summary>
static class Gallery
{
    private static bool _toggleA = true;
    private static bool _toggleB;
    private static bool _checkA = true;
    private static bool _checkB;
    private static int _seconds = 8;

    public static void Draw()
    {
        Widgets.Text("Component gallery", Theme.TextPrimary, Theme.Title);
        Widgets.Text(Theme.FontsLoaded
                ? "Inter + JetBrains Mono baked."
                : "FALLBACK BITMAP FONT - the TTFs did not load.",
            Theme.FontsLoaded ? Theme.TextMuted : Theme.AccentAmber);
        Widgets.Gap(Theme.Space.Md);

        Widgets.TwoColumn("gallery", 0.5f, DrawLeft, DrawRight);
    }

    private static void DrawLeft()
    {
        Widgets.SectionHeader("Buttons");
        Widgets.PillButton("Primary", Widgets.ButtonKind.Primary, Icons.Check);
        ImGui.SameLine();
        Widgets.PillButton("Secondary");
        ImGui.SameLine();
        Widgets.PillButton("Danger", Widgets.ButtonKind.Danger, Icons.Trash);
        Widgets.PillButton("Ghost", Widgets.ButtonKind.Ghost);
        ImGui.SameLine();
        Widgets.PillButton("Disabled", Widgets.ButtonKind.Secondary, enabled: false);
        ImGui.SameLine();
        Widgets.PillButton("Copy", Widgets.ButtonKind.Secondary, Icons.Copy);

        Widgets.SectionHeader("Stat tiles");
        Widgets.StatTile("12", "Games Tracked", Theme.AccentGreen, 150, 96);
        ImGui.SameLine(0, Theme.Space.Md);
        Widgets.StatTile("348", "Saves Backed Up", Theme.TextPrimary, 150, 96);
        ImGui.SameLine(0, Theme.Space.Md);
        Widgets.StatTile("4m ago", "Last Sync", Theme.TextMuted, 150, 96);

        Widgets.SectionHeader("Badges and status");
        Widgets.StatusDot(Theme.AccentGreen);
        ImGui.SameLine(0, Theme.Space.Sm);
        ImGui.AlignTextToFramePadding();
        Widgets.Text("CONNECTED", Theme.AccentGreen, Theme.BodyStrong);
        ImGui.SameLine(0, Theme.Space.Lg);
        Widgets.Badge("192.168.68.55:5080", Theme.AccentGreen, Icons.Server, mono: true);
        Widgets.Badge("already tracked", Theme.AccentGreen, Icons.Check);
        ImGui.SameLine(0, Theme.Space.Sm);
        Widgets.Badge("no save folder", Theme.AccentAmber, Icons.AlertTriangle);

        Widgets.SectionHeader("Input");
        Widgets.Toggle("Start on boot", ref _toggleA);
        Widgets.Toggle("Second toggle (off)", ref _toggleB);
        Widgets.Gap(Theme.Space.Sm);
        Widgets.Stepper("Settle seconds", ref _seconds, 0, 120);
    }

    private static void DrawRight()
    {
        Widgets.SectionHeader("Banner");
        Widgets.Banner("warn", "Save conflict risk - Hollow Knight",
            "WIDEBOY already has this game checked out. You launched without pulling their latest save.",
            Theme.AccentAmber, Icons.AlertTriangle, dismissible: true);

        Widgets.SectionHeader("List rows");
        Widgets.ListRow("r1", "Hollow Knight", "~/.local/share/Steam/.../unity3d/Team Cherry",
            Icons.Folder, "synced", Theme.AccentGreen);
        Widgets.ListRow("r2", "Hades", "No save folder set", Icons.AlertTriangle,
            "needs setup", Theme.AccentAmber);
        Widgets.ListRow("r3", "Selected row", "This one is the current selection",
            Icons.Monitor, selected: true);
        Widgets.ListRow("r4", "Disabled row", "Already tracked", Icons.Check,
            chevron: false, enabled: false);

        Widgets.SectionHeader("Checkboxes");
        Widgets.CheckRow("c1", ref _checkA);
        ImGui.SameLine(0, Theme.Space.Md);
        ImGui.AlignTextToFramePadding();
        Widgets.Text("Ticked", Theme.TextPrimary);
        ImGui.SameLine(0, Theme.Space.Lg);
        Widgets.CheckRow("c2", ref _checkB);
        ImGui.SameLine(0, Theme.Space.Md);
        ImGui.AlignTextToFramePadding();
        Widgets.Text("Unticked", Theme.TextPrimary);

        Widgets.SectionHeader("Icons");
        DrawIconStrip();

        Widgets.SectionHeader("Spinner and mono");
        var dl = ImGui.GetWindowDrawList();
        Icons.Spinner(dl, ImGui.GetCursorScreenPos(), 22f, Theme.AccentGreen, 2.5f);
        ImGui.Dummy(new Vector2(22, 22));
        ImGui.SameLine(0, Theme.Space.Md);
        ImGui.AlignTextToFramePadding();
        Widgets.Text("Scanning...", Theme.TextMuted);
        Widgets.Text("savelocker run -- %command%", Theme.AccentGreen, Theme.Mono);
    }

    private static void DrawIconStrip()
    {
        var set = new (string Name, Icons.Glyph Glyph)[]
        {
            ("Monitor", Icons.Monitor), ("Plus", Icons.Plus), ("Settings", Icons.Settings),
            ("Shield", Icons.Shield), ("Server", Icons.Server), ("Cpu", Icons.Cpu),
            ("Alert", Icons.AlertTriangle), ("Folder", Icons.Folder), ("Check", Icons.Check),
            ("X", Icons.X), ("Chevron", Icons.ChevronRight), ("Copy", Icons.Copy),
            ("Trash", Icons.Trash), ("Search", Icons.Search), ("Drive", Icons.HardDrive),
            ("Cloud", Icons.Cloud), ("Branch", Icons.GitBranch),
        };

        for (int i = 0; i < set.Length; i++)
        {
            if (i % 8 != 0) ImGui.SameLine(0, Theme.Space.Md);
            Icons.Draw(set[i].Glyph, 24f, Theme.TextPrimary);
        }

        // Same glyphs at the smallest and largest sizes used, to catch stroke-weight breakdown.
        Widgets.Gap(Theme.Space.Sm);
        foreach (var size in new[] { 14f, 18f, 24f, 32f, 40f })
        {
            Icons.Draw(Icons.Shield, size, Theme.AccentGreen);
            ImGui.SameLine(0, Theme.Space.Md);
        }
        Icons.Draw(Icons.Settings, 40f, Theme.AccentAmber);
    }
}
