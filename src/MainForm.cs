using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Windows.Forms;

internal sealed partial class MainForm : Form
{
    private const string Upstream = "https://github.com/Flowseal/zapret-discord-youtube";
    private const string SearchHint = "Профиль или ALT12";
    private DateTime nextServiceCheck;
    private readonly Panel pages = new Panel { Dock = DockStyle.Fill };
    private readonly WorkspaceButton[] navigation = new WorkspaceButton[7];
    private readonly Control[] views = new Control[7];
    private Icon appIcon;
    private bool serviceRunning;
    private string serviceStrategy;
    private bool filteringProfiles;
    private static readonly Color Background = WorkspaceStyle.Canvas;
    private static readonly Color Surface = WorkspaceStyle.Card;
    private static readonly Color Input = WorkspaceStyle.Field;
    private static readonly Color Border = WorkspaceStyle.Line;
    private static readonly Color TextColor = WorkspaceStyle.Text;
    private static readonly Color Muted = WorkspaceStyle.Muted;
    private static Color Accent { get { return WorkspaceStyle.Highlight; } }
    private static Color AccentLight { get { return WorkspaceStyle.Mix(WorkspaceStyle.Ice, WorkspaceStyle.Highlight, 0.16f); } }
    private static readonly Color Positive = WorkspaceStyle.Mint;

    private readonly ListBox profiles = new WorkspaceListBox();
    private readonly TextBox profileSearch = new WorkspaceTextBox();
    private readonly TextBox addresses = new WorkspaceTextBox();
    private readonly ListBox entries = new WorkspaceListBox();
    private readonly TextBox journal = new WorkspaceTextBox();
    private readonly Label status = new Label();
    private readonly Label folderLabel = new Label();
    private readonly ToolTip workspaceHints = new ToolTip();
    private readonly Label profileCaption = Caption("ПРОФИЛИ FLOWSEAL", AccentLight);
    private readonly Label preview = new Label();
    private readonly Label domainsStatus = new Label();
    private readonly Label serviceStatus = new Label();
    private readonly List<string> journalEntries = new List<string>();
    private readonly Timer timer = new Timer();
    private TextBox journalSearch;
    private string journalFilter = "";
    private readonly Preferences preferences;
    private readonly bool isAdmin;
    private Button start, stop, service, openFolder, browse, refresh, add, remove, reload, restore, clearSearch;
    private Session session;
    private DateTime started;
    private string runningProfile;
    private bool hadProcess;
    private bool restartNeeded;
    private string[] availableProfiles = new string[0];
    private Image banner;

    public MainForm(string initialFolder) : this(initialFolder, LoadPreferences()) { }

    private static Preferences LoadPreferences()
    {
        try { return Preferences.Load(); }
        catch (IOException) { return new Preferences(); }
        catch (UnauthorizedAccessException) { return new Preferences(); }
    }

    private MainForm(string initialFolder, Preferences settings)
    {
        Text = "Reze Zapret";
        appIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        if (appIcon != null) Icon = appIcon;
        Font = new Font("Segoe UI", 10);
        ForeColor = TextColor;
        BackColor = Background;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 680);
        ClientSize = new Size(1060, 730);
        DoubleBuffered = true;
        Padding = new Padding(1);
        using (var identity = WindowsIdentity.GetCurrent())
            isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        preferences = settings;
        usedFlowsealFolders.UnionWith(preferences.FlowsealFolders);

        KeyPreview = true;
        Controls.Add(CreateWorkspace());
        LoadJournal();

        string candidate = initialFolder ?? preferences.Folder;
        Guard(delegate
        {
            if (!ProfileFiles.IsBundle(candidate)) candidate = FindBundle();
            if (candidate != null) LoadFolder(candidate, false);
        });
        timer.Interval = 1000;
        timer.Tick += delegate { RefreshServiceStatus(); Guard(CheckSession); };
        RefreshServiceStatus();
        timer.Start();
        UpdatePreview();
        RefreshControls();
        Shown += delegate { InitializeTray(); InitializeUpdates(); };
        Log("Приложение открыто.");
    }

    private static string FindBundle()
    {
        string applicationDir = AppDomain.CurrentDomain.BaseDirectory;
        if (ProfileFiles.IsBundle(applicationDir)) return applicationDir;
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(desktop)) return null;
        return Directory.EnumerateDirectories(desktop, "zapret-discord-youtube*")
            .Where(ProfileFiles.IsBundle).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private void Browse()
    {
        if (session != null || AltTestsRunning) return;
        using (var dialog = new FolderBrowserDialog { Description = "Выберите папку со скачанной сборкой Flowseal", ShowNewFolderButton = false })
        {
            if (Directory.Exists(preferences.Folder)) dialog.SelectedPath = preferences.Folder;
            if (dialog.ShowDialog(this) == DialogResult.OK) LoadFolder(dialog.SelectedPath, true);
        }
    }

    private void LoadFolder(string folder, bool persist)
    {
        if (session != null || AltTestsRunning) return;
        if (!ProfileFiles.IsBundle(folder)) throw new IOException("В папке нужны service.bat, bin\\winws.exe и папка lists.");
        string[] available = ProfileFiles.Profiles(folder);
        string last = preferences.Profile;
        preferences.Folder = Path.GetFullPath(folder);
        preferences.FlowsealFolders.Add(preferences.Folder);
        folderLabel.Text = new DirectoryInfo(preferences.Folder).Name;
        workspaceHints.SetToolTip(folderLabel, preferences.Folder);
        availableProfiles = available;
        preferences.Profile = available.Contains(last) ? last : available.FirstOrDefault();
        FilterProfiles();
        LoadEntries();
        RefreshControls();
        if (persist)
        {
            preferences.FlowsealFolders.Add(preferences.Folder);
            SavePreferences();
        }
        Log("Найдено профилей: " + available.Length + ".");
    }

    private void FilterProfiles()
    {
        string query = profileSearch.Text.Trim();
        string[] matches = availableProfiles.Where(name =>
            (query.StartsWith("#", StringComparison.Ordinal) ? preferences.ProfileTags.ContainsKey(name) &&
                preferences.ProfileTags[name].Any(tag => String.Equals(tag, query.Substring(1), StringComparison.OrdinalIgnoreCase)) :
                name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (!favoritesOnly.Selected || preferences.Favorites.Contains(name)))
            .OrderByDescending(preferences.Favorites.Contains).ToArray();
        filteringProfiles = true;
        profiles.BeginUpdate();
        try
        {
            profiles.Items.Clear();
            profiles.Items.AddRange(matches);
            if (matches.Length > 0) profiles.SelectedIndex = Math.Max(0, Array.IndexOf(matches, preferences.Profile));
        }
        finally
        {
            profiles.EndUpdate();
            filteringProfiles = false;
        }
        RefreshListScrollBar(profiles);
        profileCaption.Text = matches.Length > 0 ? "Найдено · " + matches.Length :
            favoritesOnly.Selected && query.Length == 0 ? "Нет избранных" : "Нет совпадений";
        RefreshControls();
    }

    private void SavePreferences()
    {
        try { preferences.Save(); }
        catch (IOException ex) { Log("Не удалось сохранить настройки. " + ex.Message); }
        catch (UnauthorizedAccessException ex) { Log("Не удалось сохранить настройки. " + ex.Message); }
    }

    private void StartProfile()
    {
        string profile = profiles.SelectedItem as string;
        if (session != null || AltTestsRunning || !ProfileFiles.IsProfile(profile) || !ProfileFiles.IsBundle(preferences.Folder)) return;
        if (!File.Exists(Path.Combine(preferences.Folder, profile))) throw new IOException("Профиль удалён. Обновите список.");
        string validation = ProfileFiles.ValidateProfile(preferences.Folder, profile);
        if (validation != null) throw new IOException("Профиль не прошёл проверку: " + validation);
        preferences.Profile = profile;
        if (!isAdmin)
        {
            if (MessageBox.Show(this, "Открыть Reze Zapret с правами администратора для запуска профиля?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SavePreferences();
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--folder \"" + preferences.Folder.TrimEnd('\\') + "\"")
            { UseShellExecute = true, Verb = "runas" });
            Close();
            return;
        }
        if (HasExternalProcess())
        {
            MessageBox.Show(this, "Ранее запущенный winws.exe активен. Остановите его обычным способом перед выбором нового профиля.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        session = Session.Start(preferences.Folder, profile);
        runningProfile = profile;
        started = DateTime.UtcNow;
        hadProcess = false;
        restartNeeded = false;
        SavePreferences();
        Log("Запущен батник " + profile + ".");
        CheckSession();
    }

    private void StopProfile()
    {
        if (session == null) return;
        session.Dispose();
        session = null;
        restartNeeded = false;
        Log("Сеанс " + runningProfile + " остановлен.");
        RefreshControls();
    }

    private void CheckSession()
    {
        if (session == null) return;
        bool running = false;
        var found = Process.GetProcessesByName("winws");
        try
        {
            foreach (Process process in found)
            {
                try { if (session.Contains(process)) running = true; }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        finally { foreach (Process process in found) process.Dispose(); }
        if (running && !hadProcess)
        {
            preferences.LastWorkingProfile = runningProfile;
            preferences.LastWorkingFolder = preferences.Folder;
            SavePreferences();
            Log("winws.exe работает. Профиль " + runningProfile + ". Последний рабочий профиль сохранён.");
        }
        bool completed = !session.IsActive;
        if (completed)
        {
            Log(hadProcess || running ? "Сеанс завершился." : "Батник завершился до обнаружения winws.exe. Проверьте вывод в его окне или откройте диагностику Flowseal.");
            session.Dispose();
            session = null;
            restartNeeded = false;
        }
        else if (!running && DateTime.UtcNow - started > TimeSpan.FromSeconds(30) && !hadProcess)
        {
            status.Text = "Ожидание winws.exe · проверьте окно профиля";
            return;
        }
        hadProcess = running;
        RefreshControls();
    }

    private static bool HasExternalProcess()
    {
        var found = Process.GetProcessesByName("winws");
        foreach (Process process in found) process.Dispose();
        return found.Length > 0;
    }

    private void RefreshServiceStatus()
    {
        if (DateTime.UtcNow < nextServiceCheck) return;
        nextServiceCheck = DateTime.UtcNow.AddSeconds(3);
        string state;
        string strategy = null;
        bool active = false;
        try
        {
            using (var controller = new ServiceController("zapret"))
            {
                var current = controller.Status;
                active = current == ServiceControllerStatus.Running;
                state = active ? "работает" : current == ServiceControllerStatus.Stopped ? "остановлена" :
                    current == ServiceControllerStatus.Paused ? "на паузе" : "меняет состояние";
            }
        }
        catch (InvalidOperationException error)
        {
            var cause = error.InnerException as Win32Exception;
            state = cause != null && cause.NativeErrorCode == 1060 ? "не установлена" : "статус недоступен";
        }
        catch (Win32Exception) { state = "статус недоступен"; }
        if (active)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret"))
                {
                    if (key != null) strategy = key.GetValue("zapret-discord-youtube") as string;
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        serviceRunning = active;
        serviceStrategy = strategy;
        serviceStatus.Text = "Служба zapret · " + state;
        serviceStatus.ForeColor = active ? Positive : Muted;
        RefreshTrayText();
        if (session == null) RefreshControls();
    }

    private void RunService()
    {
        if (session != null || AltTestsRunning) return;
        if (!ProfileFiles.IsBundle(preferences.Folder)) throw new IOException("Папка Flowseal недоступна.");
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /v:off /c .\\service.bat")
        { UseShellExecute = true, WorkingDirectory = preferences.Folder };
        if (!isAdmin) info.Verb = "runas";
        Process.Start(info);
        Log("Открыто штатное меню Flowseal.");
    }

    private string SelectedList { get { return ProfileFiles.ListPath(preferences.Folder, excludeDomains); } }

    private string[] NormalizedEntries()
    {
        var domains = new System.Collections.Generic.List<string>();
        string[] lines = addresses.Lines;
        for (int index = 0; index < lines.Length; index++)
        {
            if (String.IsNullOrWhiteSpace(lines[index])) continue;
            try { domains.Add(ProfileFiles.Normalize(lines[index])); }
            catch (FormatException ex) { throw new FormatException("Строка " + (index + 1) + ". " + ex.Message, ex); }
        }
        return domains.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void UpdatePreview()
    {
        if (add == null) return;
        try
        {
            string[] values = NormalizedEntries();
            preview.ForeColor = Muted;
            preview.Text = values.Length == 0 ? "Адрес сайта преобразуется в домен." : "Будут добавлены: " + String.Join(", ", values);
            add.Enabled = values.Length > 0 && ProfileFiles.IsBundle(preferences.Folder);
        }
        catch (FormatException ex) { preview.ForeColor = Accent; preview.Text = ex.Message; add.Enabled = false; }
    }

    private void LoadEntries()
    {
        storedDomains = new string[0];
        entries.Items.Clear();
        domainsStatus.Text = "Выберите папку в разделе «Профили».";
        if (ProfileFiles.IsBundle(preferences.Folder))
        {
            storedDomains = ProfileFiles.Entries(SelectedList);
            domainsStatus.Text = Path.GetFileName(SelectedList);
            if (restartNeeded) domainsStatus.Text += " · требуется перезапуск";
        }
        ApplyDomainFilter();
    }

    private void AddDomains()
    {
        string[] domains = NormalizedEntries();
        if (domains.Length == 0) return;
        string other = ProfileFiles.ListPath(preferences.Folder, !excludeDomains);
        if (ProfileFiles.Entries(other).Intersect(domains, StringComparer.OrdinalIgnoreCase).Any() &&
            MessageBox.Show(this, "Часть доменов уже есть в другом списке. Добавить их и сюда?", Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        ChangeDomains(domains, false);
        addresses.Clear();
    }

    private void RemoveDomains()
    {
        string[] values = entries.SelectedItems.Cast<string>().ToArray();
        if (values.Length > 0) ChangeDomains(values, true);
    }

    private void ChangeDomains(string[] values, bool deleting)
    {
        int count = DomainHistory.Update(preferences.Folder, excludeDomains, values, deleting).Count;
        if (count != 0 && session != null) restartNeeded = true;
        LoadEntries();
        Log(count == 0 ? "Список уже содержит эти записи." : (deleting ? "Удалено записей: " : "Добавлено записей: ") + count + ".");
        if (count != 0) domainsStatus.Text += " · сохранено";
    }

    private void RestoreDomains()
    {
        string path = SelectedList;
        if (MessageBox.Show(this, "Вернуть предыдущую версию списка «" + Path.GetFileName(path) + "»?", Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (DomainHistory.RestoreBackup(preferences.Folder, excludeDomains).Changed && session != null) restartNeeded = true;
        LoadEntries();
        Log("Восстановлен список " + Path.GetFileName(path) + ".");
    }

    private void RefreshControls()
    {
        if (start == null || service == null || restore == null) return;
        bool configured = ProfileFiles.IsBundle(preferences.Folder);
        bool canChangeProfile = session == null && !AltTestsRunning;
        browse.Enabled = canChangeProfile;
        profiles.Enabled = configured && canChangeProfile;
        profileSearch.Enabled = configured && canChangeProfile;
        clearSearch.Enabled = profileSearch.Enabled && profileSearch.TextLength > 0;
        start.Enabled = configured && canChangeProfile && !serviceRunning && profiles.SelectedIndex >= 0;
        start.Text = "Запустить";
        workspaceHints.SetToolTip(start, isAdmin ? "Запустить выбранный профиль" : "Запустить выбранный профиль с запросом UAC");
        stop.Enabled = session != null && !AltTestsRunning;
        service.Enabled = configured && canChangeProfile;
        refresh.Enabled = configured && canChangeProfile;
        openFolder.Enabled = configured;
        reload.Enabled = configured;
        restore.Enabled = configured && File.Exists(ProfileFiles.BackupPath(SelectedList));
        remove.Enabled = copyDomains.Enabled = configured && entries.SelectedItems.Count > 0;
        exportDomains.Enabled = configured && storedDomains.Length > 0;
        importDomains.Enabled = pasteDomains.Enabled = historyDomains.Enabled = undoDomains.Enabled = configured && !AltTestsRunning;
        domainSearch.Enabled = configured;
        favoritesOnly.Enabled = configured && session == null;
        string currentProfile = profiles.SelectedItem as string;
        bool starred = currentProfile != null && preferences.Favorites.Contains(currentProfile);
        favorite.Enabled = configured && currentProfile != null && session == null;
        compareProfile.Enabled = configured && currentProfile != null &&
            String.Equals(preferences.LastWorkingFolder, preferences.Folder, StringComparison.OrdinalIgnoreCase) &&
            ProfileFiles.IsProfile(preferences.LastWorkingProfile) && session == null;
        validateProfile.Enabled = configured && currentProfile != null && session == null;
        favorite.Text = starred ? "В избранном" : "В избранное";
        favorite.Glyph = starred ? "star-filled" : "star";
        favorite.Selected = starred;
        selectedProfile.Text = ProfileTitle(currentProfile);
        profileHint.Text = session != null ? "Закрытие приложения остановит этот сеанс." :
            serviceRunning ? "Системная служба уже запущена. Управление — в меню Flowseal." :
            "Запуск профиля требует прав администратора.";
        status.ForeColor = (session != null && hadProcess) || serviceRunning ? Positive : Muted;
        status.Text = session == null ? (serviceRunning ? "Zapret работает как служба Windows" +
            (String.IsNullOrWhiteSpace(serviceStrategy) ? "" : " · " + serviceStrategy) :
            configured ? "Готово к запуску" : "Укажите папку Flowseal") :
            (hadProcess ? "Работает · " : "Запуск · ") + runningProfile + (restartNeeded ? " · требуется перезапуск" : "");
        UpdatePreview();
        RefreshAltTests();
        RefreshUpdateControls();
        RefreshDashboard();
        RefreshTrayProfiles();
        RefreshTrayText();
    }

    private void Log(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   " + message;
        journalEntries.Add(line);
        while (journalEntries.Count > 600) journalEntries.RemoveAt(0);
        try { AltLab.Save(AltLab.StatePath("action-log.json"), journalEntries); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        RefreshJournalView();
    }

    private void ClearJournal()
    {
        journalEntries.Clear();
        try { AltLab.Save(AltLab.StatePath("action-log.json"), journalEntries); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        RefreshJournalView();
    }

    private void RefreshJournalView()
    {
        if (journal == null) return;
        string text = String.Join(Environment.NewLine, journalEntries.Where(line => String.IsNullOrEmpty(journalFilter) ||
            line.IndexOf(journalFilter, StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray());
        if (journal.Text != text) journal.Text = text;
        journal.SelectionStart = journal.TextLength;
        journal.ScrollToCaret();
    }

    private void LoadJournal()
    {
        try
        {
            var saved = AltLab.Load<List<string>>(AltLab.StatePath("action-log.json"));
            if (saved != null) journalEntries.AddRange(saved.Skip(Math.Max(0, saved.Count - 600)));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        RefreshJournalView();
    }

    private void Guard(System.Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (!(ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception ||
                  ex is ArgumentException || ex is FormatException)) throw;
            if (ex is Win32Exception && ((Win32Exception)ex).NativeErrorCode == 1223)
            {
                Log("Операция отменена в запросе Windows.");
                return;
            }
            Log(ex.Message);
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        HideToTrayIfConfigured(e);
        base.OnFormClosing(e);
        if (e.Cancel) return;
        timer.Stop();
        StopAltTests();
        StopUpdates();
        StopProfile();
        SavePreferences();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopAltTests();
            StopUpdates();
            timer.Dispose();
            workspaceHints.Dispose();
            DisposeTray();
            if (session != null) session.Dispose();
            if (banner != null) banner.Dispose();
        }
        base.Dispose(disposing);
        if (disposing && appIcon != null)
        {
            appIcon.Dispose();
            appIcon = null;
        }
    }

    private static Label Caption(string value, Color? color = null)
    {
        return new Label { Text = value, ForeColor = color ?? TextColor,
            Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 0), AutoEllipsis = true };
    }

    private static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static FlowLayoutPanel Row()
    {
        return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
    }

    private static void ConfigureText(TextBox box)
    {
        box.Dock = DockStyle.Fill;
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.WordWrap = true;
        box.ForeColor = TextColor;
        box.BackColor = Input;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = new Font("Segoe UI", 10);
        box.Margin = new Padding(0, 2, 0, 2);
    }

}
