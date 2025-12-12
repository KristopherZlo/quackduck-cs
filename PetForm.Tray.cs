using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using QuackDuck.DebugUI;

namespace QuackDuck;

internal sealed partial class PetForm
{
    private NotifyIcon? trayIcon;
    private ContextMenuStrip? trayMenu;
    private ToolStripMenuItem? trayToggleItem;
    private Icon? visibleIcon;
    private Icon? hiddenIcon;

    private void InitializeTrayIcon()
    {
        visibleIcon = LoadIcon("white-quackduck-visible.ico");
        hiddenIcon = LoadIcon("white-quackduck-hidden.ico");

        trayMenu = BuildTrayMenu();
        trayIcon = new NotifyIcon
        {
            Visible = true,
            Icon = visibleIcon,
            Text = "QuackDuck Pet",
            ContextMenuStrip = trayMenu
        };

        trayIcon.MouseClick += OnTrayIconClick;
        UpdateTrayIcon();
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        trayToggleItem = new ToolStripMenuItem("Hide", null, (_, _) => TogglePauseFromTray());
        var settingsItem = new ToolStripMenuItem("Settings", null, (_, _) => OpenSettings());
        var updatesItem = new ToolStripMenuItem("Check for updates", null, (_, _) => Log("Update check is not implemented yet."));
        var coffeeItem = new ToolStripMenuItem("Buy me a coffee", null, (_, _) => OpenCoffeeLink());
        var debugItem = new ToolStripMenuItem("Debug", null, (_, _) => DebugHost.Start(debugState));

        menu.Items.Add(trayToggleItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(updatesItem);
        menu.Items.Add(coffeeItem);
        menu.Items.Add(debugItem);
        return menu;
    }

    private void OnTrayIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            TogglePauseFromTray();
        }
    }

    private void TogglePauseFromTray()
    {
        if (isPausedHidden)
        {
            ResumePet();
        }
        else
        {
            PausePet();
        }

        UpdateTrayIcon();
    }

    private void PausePet()
    {
        isPausedHidden = true;
        animationTimer.Stop();
        energyTimer.Stop();
        Hide();
        nameOverlay?.Hide();
        UpdateTrayIcon();
        Log("Pet paused/hidden from tray");
    }

    private void ResumePet()
    {
        isPausedHidden = false;
        Show();
        KeepPetInBoundsAndApply(workingArea);
        animationTimer.Start();
        energyTimer.Start();
        UpdateNameOverlay();
        UpdateTrayIcon();
        Log("Pet resumed/shown from tray");
    }

    private void UpdateTrayIcon()
    {
        if (trayIcon is null)
        {
            return;
        }

        trayIcon.Icon = isPausedHidden ? hiddenIcon ?? trayIcon.Icon : visibleIcon ?? trayIcon.Icon;
        if (trayToggleItem is not null)
        {
            trayToggleItem.Text = isPausedHidden ? "Show" : "Hide";
        }
    }

    private void OpenCoffeeLink()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://www.buymeacoffee.com/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"Failed to open link: {ex.Message}");
        }
    }

    private Icon? LoadIcon(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var runtimePath = Path.Combine(baseDir, "assets", "images", fileName);
        var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "assets", "images", fileName));
        var path = File.Exists(runtimePath) ? runtimePath : devPath;
        if (!File.Exists(path))
        {
            Log($"Tray icon not found: {fileName}");
            return null;
        }

        try
        {
            return new Icon(path);
        }
        catch (Exception ex)
        {
            Log($"Failed to load icon {fileName}: {ex.Message}");
            return null;
        }
    }
}
