using System.Drawing;
using System.Windows.Forms;

namespace SaveLocker.Agent;

/// <summary>
/// Test-only: <c>SaveLocker.Agent.exe fake-game</c> (tests/testenv.ps1's "Conflict Game" entry).
/// Windows analogue of Agent.Linux's <c>savelocker fake-game</c> — stands in for a real game so
/// the Playnite plugin's launch gate and sync-before/after-play can be exercised end to end
/// without needing an actual title. Closing this window (Exit, Escape, or the titlebar) is what a
/// real game's process exit looks like to SaveLocker — the whole point of a real window instead
/// of a message box.
///
/// WARNING: this runs as <c>SaveLocker.Agent.exe</c> — the exact same process name as the real
/// installed tray (<see cref="Program"/>'s normal entry point), and <c>GameActivity.IsActive</c>
/// matches purely on <c>Process.ProcessName</c> with no PID/path disambiguation. Never configure a
/// tracked game's <c>ProcessNames</c> as <c>SaveLocker.Agent</c> to detect this fixture — it would
/// also match the real tray any time it happens to be running, permanently masking that game's
/// "exited" state.
/// </summary>
static class FakeGame
{
    public static int Run()
    {
        using var form = new Form
        {
            Text = "Conflict Game",
            ClientSize = new Size(640, 400),
            StartPosition = FormStartPosition.CenterScreen,
            BackColor = Color.FromArgb(28, 28, 36),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            KeyPreview = true,
        };

        var label = new Label
        {
            Text = "Conflict Game is running\n\n" +
                   "This stands in for a real game — closing this window\n" +
                   "is what SaveLocker sees as the game exiting.",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11),
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(0, 60, form.ClientSize.Width, 140),
        };

        var exitButton = new Button
        {
            Text = "Exit",
            Size = new Size(140, 44),
            Location = new Point((form.ClientSize.Width - 140) / 2, 260),
        };
        exitButton.Click += (_, _) => form.Close();
        form.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) form.Close(); };

        form.Controls.Add(label);
        form.Controls.Add(exitButton);

        Application.Run(form);
        return 0;
    }
}
