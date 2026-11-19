using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private static readonly string[] SettingsAccentNames = { "ice", "lavender", "mint" };
    private static readonly string[] SettingsAccentTitles = { "Лёд", "Лаванда", "Мята" };
    private CheckBox settingsCompactHeader, settingsTray, settingsMinimize, settingsTestNotice, settingsUpdateNotice;
    private ComboBox settingsAccent, settingsDensity;
    private WorkspaceScalePicker settingsScale;
    private bool syncingSettings;

    private Control CreateSettings()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = false, BackColor = Background };
        var card = new WorkspaceCard { Dock = DockStyle.Top, AutoSize = true, Margin = Padding.Empty, Padding = new Padding(16) };
        var body = Grid();
        body.Dock = DockStyle.Top;
        body.AutoSize = true;
        body.BackColor = Surface;
        SetRows(body, 36, 36, 36, 34, 36, 36, 36, 36, 34, 54, 44, 44);
        body.Controls.Add(Heading("Оформление", 15, TextColor), 0, 0);
        settingsCompactHeader = SettingsCheck(body, 1, "Компактная шапка", value => preferences.CompactHeader = value);
        settingsScale = new WorkspaceScalePicker { Minimum = Preferences.MinUiScale, Maximum = Preferences.MaxUiScale,
            BackColor = Surface, AccessibleName = "Масштаб интерфейса, проценты" };
        settingsScale.ValueChanged += delegate { ChangeSetting(delegate { preferences.UiScale = (int)settingsScale.Value; }); };
        settingsAccent = new WorkspaceComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Input, ForeColor = TextColor, AccessibleName = "Акцент интерфейса" };
        settingsAccent.Items.AddRange(SettingsAccentTitles);
        settingsAccent.SelectedIndexChanged += delegate
        {
            if (settingsAccent.SelectedIndex >= 0)
                ChangeSetting(delegate { preferences.AccentName = SettingsAccentNames[settingsAccent.SelectedIndex]; });
        };
        settingsDensity = new WorkspaceComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = Input, ForeColor = TextColor, AccessibleName = "Плотность интерфейса" };
        settingsDensity.Items.AddRange(new object[] { "Компактная", "Обычная", "Просторная" });
        settingsDensity.SelectedIndexChanged += delegate
        {
            if (settingsDensity.SelectedIndex >= 0)
                ChangeSetting(delegate { preferences.Density = settingsDensity.SelectedIndex == 0 ? "compact" : settingsDensity.SelectedIndex == 2 ? "spacious" : "normal"; });
        };
        var displayOptions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty };
        displayOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        displayOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        displayOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        displayOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        displayOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 8));
        displayOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        settingsScale.Dock = settingsAccent.Dock = DockStyle.Fill;
        settingsScale.Margin = new Padding(0, 1, 0, 0);
        displayOptions.Controls.Add(Heading("Масштаб (%)", 9.5f, TextColor), 0, 0);
        displayOptions.Controls.Add(settingsScale, 1, 0);
        var accentLabel = Heading("Акцент", 9.5f, TextColor);
        accentLabel.Margin = new Padding(12, 0, 0, 0);
        displayOptions.Controls.Add(accentLabel, 2, 0);
        displayOptions.Controls.Add(settingsAccent, 3, 0);
        displayOptions.Controls.Add(Heading("Плотность", 9.5f, TextColor), 4, 0);
        settingsDensity.Dock = DockStyle.Fill;
        displayOptions.Controls.Add(settingsDensity, 5, 0);
        body.Controls.Add(displayOptions, 0, 2);

        body.Controls.Add(Heading("Трей и уведомления", 15, TextColor), 0, 3);
        settingsTray = SettingsCheck(body, 4, "Показывать значок в трее", value => preferences.TrayEnabled = value);
        settingsMinimize = SettingsCheck(body, 5, "Скрывать окно в трей при закрытии", value => preferences.MinimizeToTray = value);
        settingsTestNotice = SettingsCheck(body, 6, "Уведомлять о завершении тестов ALT", value => preferences.TestNotifications = value);
        settingsUpdateNotice = SettingsCheck(body, 7, "Уведомлять об обновлениях Flowseal", value => preferences.UpdateNotifications = value);

        body.Controls.Add(Heading("Перенос настроек", 15, TextColor), 0, 8);
        var transferHint = Heading("JSON переносит параметры, избранное, заметки, теги, историю сборок, набор ALT и доменные списки выбранной сборки.", 9, Muted);
        transferHint.AutoEllipsis = false;
        transferHint.Padding = new Padding(0, 5, 0, 5);
        body.Controls.Add(transferHint, 0, 9);
        body.Controls.Add(Pair(Action("Экспорт JSON", ExportPortable, true), Action("Импорт JSON", ImportPortable)), 0, 10);
        body.Controls.Add(Pair(Action("Заметки профилей", EditSettingsProfileNote), Action("Открыть гид · F1", ShowGuide)), 0, 11);
        card.Controls.Add(body);
        scroll.Controls.Add(card);
        RefreshSettings();
        return scroll;
    }

    private CheckBox SettingsCheck(TableLayoutPanel body, int row, string text, Action<bool> change)
    {
        var check = new WorkspaceCheckBox { Text = text, Dock = DockStyle.Fill, ForeColor = TextColor, BackColor = Surface, Margin = Padding.Empty,
            Font = new Font("Segoe UI", 9.5f) };
        check.CheckedChanged += delegate { ChangeSetting(delegate { change(check.Checked); }); };
        body.Controls.Add(check, 0, row);
        return check;
    }

    private void ChangeSetting(Action change)
    {
        if (syncingSettings) return;
        change();
        SavePreferences();
        ApplyWorkspacePreferences();
        RefreshTray();
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        if (settingsAccent == null) return;
        syncingSettings = true;
        try
        {
            settingsCompactHeader.Checked = preferences.CompactHeader;
            settingsScale.Value = Preferences.ClampUiScale(preferences.UiScale);
            settingsAccent.SelectedIndex = Array.IndexOf(SettingsAccentNames, Preferences.NormalizeAccent(preferences.AccentName));
            settingsDensity.SelectedIndex = Preferences.NormalizeDensity(preferences.Density) == "compact" ? 0 : Preferences.NormalizeDensity(preferences.Density) == "spacious" ? 2 : 1;
            settingsTray.Checked = preferences.TrayEnabled;
            settingsMinimize.Checked = preferences.MinimizeToTray;
            settingsTestNotice.Checked = preferences.TestNotifications;
            settingsUpdateNotice.Checked = preferences.UpdateNotifications;
            settingsMinimize.Enabled = settingsTestNotice.Enabled = settingsUpdateNotice.Enabled = preferences.TrayEnabled;
        }
        finally { syncingSettings = false; }
    }

    private void EditSettingsProfileNote()
    {
        string[] names = availableProfiles.Concat(preferences.ProfileNotes.Keys).Concat(preferences.ProfileTags.Keys).Concat(preferences.Favorites)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0) throw new IOException("Выберите папку Flowseal, чтобы добавить заметку к профилю.");
        using (var dialog = new Form { Text = "Заметки и теги профилей", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(460, 275), MinimumSize = new Size(380, 250), BackColor = Background, ForeColor = TextColor,
            Font = Font, ShowInTaskbar = false, Padding = new Padding(14) })
        {
            var body = Grid();
            SetRows(body, 34, 32, 36, 26, 34, 42);
            var choice = new WorkspaceComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Input, ForeColor = TextColor, AccessibleName = "Профиль" };
            choice.Items.AddRange(names);
            var note = new WorkspaceTextBox { AccessibleName = "Заметка" };
            ConfigureText(note);
            var tags = new WorkspaceTextBox { AccessibleName = "Теги профиля" };
            tags.Multiline = false;
            tags.ScrollBars = ScrollBars.None;
            tags.BorderStyle = BorderStyle.FixedSingle;
            choice.SelectedIndexChanged += delegate
            {
                string saved;
                note.Text = preferences.ProfileNotes.TryGetValue((string)choice.SelectedItem, out saved) ? saved : "";
                string[] selectedTags;
                tags.Text = preferences.ProfileTags.TryGetValue((string)choice.SelectedItem, out selectedTags) ? String.Join(" ", selectedTags.Select(tag => "#" + tag).ToArray()) : "";
            };
            choice.SelectedIndex = Math.Max(0, Array.FindIndex(names, name => String.Equals(name, preferences.Profile, StringComparison.OrdinalIgnoreCase)));
            string[] savedTags;
            tags.Text = preferences.ProfileTags.TryGetValue((string)choice.SelectedItem, out savedTags) ? String.Join(" ", savedTags.Select(tag => "#" + tag).ToArray()) : "";
            body.Controls.Add(choice, 0, 0);
            body.Controls.Add(Heading("Заметка профиля", 9, Muted), 0, 1);
            body.Controls.Add(note, 0, 2);
            body.Controls.Add(Heading("Теги через пробел, например #видео #игры", 9, Muted), 0, 3);
            body.Controls.Add(tags, 0, 4);
            var save = Action("Сохранить", delegate { dialog.DialogResult = DialogResult.OK; }, true);
            var cancel = Action("Отмена", delegate { dialog.DialogResult = DialogResult.Cancel; });
            body.Controls.Add(Pair(save, cancel), 0, 5);
            dialog.CancelButton = cancel;
            dialog.Controls.Add(body);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string profile = (string)choice.SelectedItem;
            if (String.IsNullOrWhiteSpace(note.Text)) preferences.ProfileNotes.Remove(profile);
            else preferences.ProfileNotes[profile] = note.Text;
            string[] normalizedTags = Preferences.NormalizeTags(tags.Text);
            if (normalizedTags.Length == 0) preferences.ProfileTags.Remove(profile);
            else preferences.ProfileTags[profile] = normalizedTags;
            SavePreferences();
            FilterProfiles();
        }
    }

    private void ExportPortable()
    {
        using (var dialog = new SaveFileDialog { Filter = "Настройки Reze Zapret (*.json)|*.json", DefaultExt = "json",
            FileName = "reze-settings.json", RestoreDirectory = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            string path = Path.GetFullPath(dialog.FileName);
            string appSettings = Path.GetDirectoryName(AltLab.StatePath("alt-settings.json"));
            if (!String.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Path.GetDirectoryName(path), appSettings, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Выберите файл .json вне папки служебных настроек Reze Zapret.");
            PortableDomainLists domains = ProfileFiles.IsBundle(preferences.Folder) ?
                PortableSettings.CaptureDomains(SnapshotPortableDomainLists()) : null;
            PortableSettings.Write(path, PortableSettings.Capture(preferences, ExportAltLabSettings(), domains));
            Log("Настройки экспортированы · " + Path.GetFileName(path));
        }
    }

    private void ImportPortable()
    {
        if (AltTestsRunning) throw new IOException("Завершите тест ALT перед импортом настроек.");
        using (var dialog = new OpenFileDialog { Filter = "Настройки Reze Zapret (*.json)|*.json", RestoreDirectory = true })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            PortableSettingsDocument document = PortableSettings.Read(dialog.FileName);
            string folder = preferences.Folder;
            DomainListsSnapshot current = null, merged = null;
            int included = 0, excluded = 0;
            if (document.Domains != null && (document.Domains.Include.Length > 0 || document.Domains.Exclude.Length > 0))
            {
                if (!ProfileFiles.IsBundle(folder)) throw new IOException("Выберите папку Flowseal для импорта доменных списков.");
                current = SnapshotPortableDomainLists();
                merged = PortableSettings.MergeDomains(current, document.Domains, out included, out excluded);
            }
            string selected = availableProfiles.FirstOrDefault(name => String.Equals(name, document.SelectedProfile, StringComparison.OrdinalIgnoreCase));
            if (MessageBox.Show(this, ImportSettingsSummary(document, selected, included, excluded), "Импорт настроек Reze Zapret",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (AltTestsRunning) throw new IOException("Завершите тест ALT перед импортом настроек.");
            if (current != null && (!String.Equals(folder, preferences.Folder, StringComparison.OrdinalIgnoreCase) ||
                !current.Equivalent(SnapshotPortableDomainLists())))
                throw new IOException("Доменные списки изменились после предпросмотра. Повторите импорт.");

            ApplyPortableSettings(document, current, merged);
            Log("Настройки импортированы · " + Path.GetFileName(dialog.FileName));
        }
    }

    private void ApplyPortableSettings(PortableSettingsDocument document, DomainListsSnapshot domainsBefore, DomainListsSnapshot domainsAfter)
    {
        var previous = PortableSettings.Capture(preferences, ExportAltLabSettings(), null);
        string previousProfile = preferences.Profile;
        bool savedPreferences = false, savedAlt = false, attemptedDomains = false;
        try
        {
            PortableSettings.ApplyPreferences(preferences, document);
            string selected = availableProfiles.FirstOrDefault(name => String.Equals(name, document.SelectedProfile, StringComparison.OrdinalIgnoreCase));
            if (selected != null) preferences.Profile = selected;
            preferences.Save();
            savedPreferences = true;
            ImportAltLabSettings(document.Alt);
            savedAlt = true;
            if (domainsAfter != null && !domainsBefore.Equivalent(domainsAfter))
            {
                attemptedDomains = true;
                RestorePortableDomainLists(domainsAfter);
            }
        }
        catch (Exception error)
        {
            PortableSettings.ApplyPreferences(preferences, previous);
            preferences.Profile = previousProfile;
            if (savedPreferences) RestoreAfterFailedImport(preferences.Save);
            if (savedAlt) RestoreAfterFailedImport(delegate { ImportAltLabSettings(previous.Alt); });
            if (attemptedDomains) RestoreAfterFailedImport(delegate { RestorePortableDomainLists(domainsBefore); });
            if (error is InvalidOperationException) throw new IOException("Не удалось применить настройки.", error);
            throw;
        }

        if (previous.Preferences.AutoUpdates != preferences.AutoUpdates)
        {
            if (preferences.AutoUpdates) nextUpdateCheck = DateTime.MinValue;
            else if (UpdateOperationInProgress && currentUpdateAction == UpdateAction.Automatic) CancelUpdate();
        }
        ApplyWorkspacePreferences();
        RefreshTray();
        RefreshSettings();
        FilterProfiles();
    }

    private void RestoreAfterFailedImport(Action restore)
    {
        try { restore(); }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException)) throw;
            Log("Не удалось отменить часть импорта · " + error.Message);
        }
    }

    private static string ImportSettingsSummary(PortableSettingsDocument document, string selected, int included, int excluded)
    {
        var values = document.Preferences;
        string accent = SettingsAccentTitles[Array.IndexOf(SettingsAccentNames, values.AccentName)];
        string domains = document.Domains == null ? "Доменный раздел отсутствует." :
            "Включения: " + document.Domains.Include.Length + " в файле, добавится " + included + ".\r\n" +
            "Исключения: " + document.Domains.Exclude.Length + " в файле, добавится " + excluded + ".";
        return "Будут заменены настройки приложения, избранное и заметки профилей.\r\n\r\n" +
            "Масштаб: " + values.UiScale + "%. Акцент: " + accent + ".\r\n" +
            "Компактная шапка: " + SettingsSwitch(values.CompactHeader) + ". Значок в трее: " + SettingsSwitch(values.TrayEnabled) + ".\r\n" +
            "Скрытие при закрытии: " + SettingsSwitch(values.MinimizeToTray) + ". Автозагрузка: " + SettingsSwitch(values.AutoUpdates) + ".\r\n" +
            "Уведомления ALT: " + SettingsSwitch(values.TestNotifications) + ". Обновления: " + SettingsSwitch(values.UpdateNotifications) + ".\r\n" +
            "Профиль: " + (selected == null ? "текущий" : ProfileTitle(selected)) + ". Избранное: " + document.Favorites.Length + ". Заметки: " + document.ProfileNotes.Count + ". Теги: " + (document.ProfileTags == null ? 0 : document.ProfileTags.Count) + ". Сборки: " + (values.FlowsealFolders == null ? 0 : values.FlowsealFolders.Length) + ".\r\n" +
            "ALT: «" + AltLab.SetTitle(document.Alt.SelectedSet) + "», " + (document.Alt.Detailed ? "подробный режим" : "быстрый режим") +
            ", своих сайтов: " + document.Alt.MySites.Length + ".\r\n\r\n" + domains + "\r\n\r\nПрименить настройки?";
    }

    private static string SettingsSwitch(bool value) { return value ? "вкл." : "выкл."; }
}
