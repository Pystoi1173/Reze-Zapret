using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private NotifyIcon trayIcon;
    private ToolStripMenuItem trayProfilesMenu;
    private bool trayExitRequested;

    private void InitializeTray()
    {
        if (!preferences.TrayEnabled || appIcon == null || trayIcon != null) return;
        trayIcon = new NotifyIcon { Icon = appIcon, Text = "Reze Zapret", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть Reze Zapret", null, delegate { ShowFromTray(); });
        menu.Items.Add("Проверить подключение", null, delegate { ShowFromTray(); SelectView(3); RunCurrentAltFromDashboard(); });
        trayProfilesMenu = new ToolStripMenuItem("Запустить профиль");
        menu.Items.Add(trayProfilesMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, delegate { trayExitRequested = true; Close(); });
        trayIcon.ContextMenuStrip = menu;
        trayIcon.DoubleClick += delegate { ShowFromTray(); };
        RefreshTrayText();
        RefreshTrayProfiles();
    }

    private void RefreshTrayProfiles()
    {
        if (trayProfilesMenu == null) return;
        trayProfilesMenu.DropDownItems.Clear();
        trayProfilesMenu.Enabled = availableProfiles.Length > 0 && session == null && !AltTestsRunning && !serviceRunning;
        foreach (string profile in availableProfiles)
        {
            string selected = profile;
            var item = new ToolStripMenuItem(ProfileTitle(profile)) { Checked = String.Equals(profile, preferences.Profile, StringComparison.OrdinalIgnoreCase) };
            item.Click += delegate
            {
                PickProfile(selected);
                ShowFromTray();
                Guard(StartProfile);
            };
            trayProfilesMenu.DropDownItems.Add(item);
        }
        var restore = new ToolStripMenuItem("Восстановить последний рабочий");
        restore.Enabled = String.Equals(preferences.LastWorkingFolder, preferences.Folder, StringComparison.OrdinalIgnoreCase) &&
            !String.IsNullOrEmpty(preferences.LastWorkingProfile) && Array.Exists(availableProfiles,
            profile => String.Equals(profile, preferences.LastWorkingProfile, StringComparison.OrdinalIgnoreCase));
        restore.Click += delegate { PickProfile(preferences.LastWorkingProfile); ShowFromTray(); Guard(StartProfile); };
        trayProfilesMenu.DropDownItems.Add(new ToolStripSeparator());
        trayProfilesMenu.DropDownItems.Add(restore);
    }

    private void RefreshTray()
    {
        if (preferences.TrayEnabled) InitializeTray();
        else DisposeTray();
    }

    private void DisposeTray()
    {
        if (trayIcon == null) return;
        var menu = trayIcon.ContextMenuStrip;
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayIcon = null;
        if (menu != null) menu.Dispose();
    }

    private void NotifyWorkspace(string title, string message, bool update)
    {
        if (trayIcon == null || String.IsNullOrWhiteSpace(message) ||
            (update ? !preferences.UpdateNotifications : !preferences.TestNotifications)) return;
        trayIcon.ShowBalloonTip(3500, title, message, ToolTipIcon.Info);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void RefreshTrayText()
    {
        if (trayIcon == null) return;
        string state = serviceRunning ? "служба работает" : "служба остановлена";
        string profile = profiles == null ? "" : profiles.SelectedItem as string;
        trayIcon.Text = (String.IsNullOrEmpty(profile) ? "Reze Zapret" : "Reze · " + ProfileTitle(profile)) + " · " + state;
    }

    private void HideToTrayIfConfigured(FormClosingEventArgs e)
    {
        if (trayExitRequested || !preferences.TrayEnabled || !preferences.MinimizeToTray ||
            e.CloseReason != CloseReason.UserClosing || trayIcon == null) return;
        e.Cancel = true;
        Hide();
    }
}
