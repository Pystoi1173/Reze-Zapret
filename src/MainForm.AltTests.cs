using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private const int AltStartupTimeoutMs = 10000;
    private const string AltSettingsFile = "alt-settings.json", AltHistoryFile = "alt-history.json";
    private ListBox altProfiles;
    private ComboBox altSets, altModes;
    private DataGridView altTable;
    private Label altStatus, altSelection, altRunInfo, altDetail;
    private ProgressBar altProgress;
    private ComboBox altFilter;
    private Button altCurrent, altCompare, altCancel, altHistoryButton, altCsv, altLaunch, altEditSites;
    private int altCompletedMeasurements, altTotalMeasurements;
    private string altFolder;
    private string[] altCatalog;
    private bool altRendering, altUpdatingSettings;
    private CancellationTokenSource altCancellation;
    private Session altSession;
    private AltLabSettings altSettings = new AltLabSettings();
    private readonly List<AltLabRun> altHistory = new List<AltLabRun>();
    private AltLabRun altShownRun, altBeforeRun;
    private string altSortSite;
    private bool altSortDescending;

    private bool AltTestsRunning { get { return altCancellation != null; } }
    private string AltLastSummary { get; set; }

    internal AltLabSettings ExportAltLabSettings()
    {
        return AltLab.NormalizeSettings(altSettings);
    }

    internal void ImportAltLabSettings(AltLabSettings settings)
    {
        if (AltTestsRunning) throw new IOException("Завершите проверку ALT перед импортом настроек.");
        ApplyAltSettings(settings);
    }

    private Control CreateAltTests()
    {
        var split = Split(32);
        var setup = Card(split, 0, new Padding(0, 0, 7, 0));
        SetRows(setup, 33, 24, 32, 41, 24, 32, 31, -1, 56, 41, 41);
        setup.Controls.Add(Heading("ALT-лаборатория", 14, TextColor), 0, 0);
        setup.Controls.Add(Heading("Набор сайтов", 9, Muted), 0, 1);
        altSets = AltCombo("Набор сайтов");
        foreach (string set in AltLab.Sets) altSets.Items.Add(AltLab.SetTitle(set));
        setup.Controls.Add(altSets, 0, 2);
        altEditSites = Action("Править мои сайты", EditAltSites);
        altEditSites.Dock = DockStyle.Fill;
        setup.Controls.Add(altEditSites, 0, 3);
        setup.Controls.Add(Heading("Повторы на сайт", 9, Muted), 0, 4);
        altModes = AltCombo("Число повторов на сайт");
        altModes.Items.AddRange(new object[] { "Быстрый · 1", "Подробный · 3" });
        setup.Controls.Add(altModes, 0, 5);
        setup.Controls.Add(Heading("Профили · Ctrl / Shift", 9, Muted), 0, 6);
        altProfiles = new WorkspaceListBox { SelectionMode = SelectionMode.MultiExtended };
        ConfigureList(altProfiles, "Локальные профили для сравнения");
        altProfiles.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index >= 0 && e.Index < altProfiles.Items.Count)
                DrawListRow(altProfiles, e, ProfileTitle((string)altProfiles.Items[e.Index]), "profiles");
        };
        altProfiles.SelectedIndexChanged += delegate { UpdateAltTestActions(); };
        setup.Controls.Add(altProfiles, 0, 7);
        altSelection = Heading("", 9, Muted);
        setup.Controls.Add(altSelection, 0, 8);
        altCurrent = Action("Проверить текущее", delegate { RunAltTests(false); }, true);
        altCompare = Action("Сравнить с базой", delegate { RunAltTests(true); });
        altCurrent.Dock = altCompare.Dock = DockStyle.Fill;
        setup.Controls.Add(altCurrent, 0, 9);
        setup.Controls.Add(altCompare, 0, 10);

        var report = Card(split, 1, new Padding(7, 0, 0, 0));
        SetRows(report, 33, 48, 30, -1, 67, 68, 28, 43, 43);
        report.Controls.Add(Heading("Результаты HTTPS", 15, TextColor), 0, 0);
        altRunInfo = Heading("Успехи / попытки · медиана успехов. Нажмите заголовок для сортировки.", 9, Muted);
        altRunInfo.AutoEllipsis = false;
        altRunInfo.Padding = new Padding(0, 3, 0, 3);
        report.Controls.Add(altRunInfo, 0, 1);
        var progressRow = Split(70);
        altProgress = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1, Style = ProgressBarStyle.Continuous, Margin = new Padding(0, 7, 8, 7) };
        altFilter = AltCombo("Фильтр результатов ALT");
        altFilter.Items.AddRange(new object[] { "Все результаты", "Только успехи", "Есть ошибки" });
        altFilter.SelectedIndex = 0;
        altFilter.SelectedIndexChanged += delegate { RenderAltTable(); };
        progressRow.Controls.Add(altProgress, 0, 0);
        progressRow.Controls.Add(altFilter, 1, 0);
        report.Controls.Add(progressRow, 0, 2);
        altTable = CreateAltTable();
        report.Controls.Add(altTable, 0, 3);
        altDetail = Heading("Выберите ячейку, чтобы увидеть отдельные попытки.", 9, TextColor);
        altDetail.AutoEllipsis = false;
        altDetail.Padding = new Padding(0, 3, 0, 3);
        report.Controls.Add(altDetail, 0, 4);
        var legend = Heading(AltLab.Legend, 9, Muted);
        legend.AutoEllipsis = false;
        legend.Padding = new Padding(0, 3, 0, 3);
        report.Controls.Add(legend, 0, 5);
        altStatus = Heading("Готово к проверке", 9, AccentLight);
        report.Controls.Add(altStatus, 0, 6);
        altCancel = AltCompactAction("Отмена", StopAltTests);
        altHistoryButton = AltCompactAction("История", ShowAltHistory);
        altCsv = AltCompactAction("CSV", SaveAltCsv);
        altLaunch = AltCompactAction("Запустить профиль", LaunchAltSelectedProfile);
        report.Controls.Add(Pair(altCancel, altHistoryButton), 0, 7);
        var reportActions = Split(50);
        reportActions.BackColor = Surface;
        altCsv.Dock = altLaunch.Dock = DockStyle.Fill;
        altCsv.Margin = new Padding(0, 4, 4, 4);
        altLaunch.Margin = new Padding(4, 4, 0, 4);
        reportActions.Controls.Add(altCsv, 0, 0);
        reportActions.Controls.Add(altLaunch, 1, 0);
        report.Controls.Add(reportActions, 0, 8);
        workspaceHints.SetToolTip(altHistoryButton, "Выбрать сохранённый запуск или сравнить два запуска");
        workspaceHints.SetToolTip(altLaunch, "Запустить профиль строки обычным способом из текущей папки Flowseal");
        workspaceHints.SetToolTip(altTable, "Нажмите заголовок: сначала доля успехов, затем медиана. Нажмите ячейку для подробностей.");

        LoadAltState();
        SyncAltSettingsControls();
        altSets.SelectedIndexChanged += delegate { SaveAltControlSettings(); };
        altModes.SelectedIndexChanged += delegate { SaveAltControlSettings(); };
        RenderAltTable();
        RefreshAltTests();
        return split;
    }

    private Button AltCompactAction(string title, System.Action handler)
    {
        var button = Action(title, handler);
        button.MinimumSize = new Size(0, 32);
        return button;
    }

    private static ComboBox AltCombo(string name)
    {
        return new WorkspaceComboBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0), BackColor = Input,
            ForeColor = TextColor, FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = name };
    }

    private DataGridView CreateAltTable()
    {
        var table = new DataGridView { Dock = DockStyle.Fill, Margin = Padding.Empty, BackgroundColor = Input,
            BorderStyle = BorderStyle.None, GridColor = Border, EnableHeadersVisualStyles = false,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, AccessibleName = "Сравнение результатов HTTPS" };
        table.ColumnHeadersDefaultCellStyle.BackColor = Surface;
        table.ColumnHeadersDefaultCellStyle.ForeColor = TextColor;
        table.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
        table.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextColor;
         table.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
         table.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
         table.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
         table.ColumnHeadersHeight = Math.Max(32, table.Font.Height + 12);
        table.DefaultCellStyle.BackColor = Input;
        table.DefaultCellStyle.ForeColor = TextColor;
        table.DefaultCellStyle.SelectionBackColor = WorkspaceStyle.Raised;
        table.DefaultCellStyle.SelectionForeColor = AccentLight;
        table.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        table.DefaultCellStyle.Padding = new Padding(4, 3, 4, 3);
        table.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
        {
            string site = table.Columns[e.ColumnIndex].Name;
            bool descending = site == altSortSite ? !altSortDescending : site.Length > 0;
            altSortSite = site;
            altSortDescending = descending;
            RenderAltTable();
        };
        table.SelectionChanged += delegate { if (!altRendering) UpdateAltCellDetails(); };
        return table;
    }

    private void LoadAltState()
    {
        try
        {
            var stored = AltLab.Load<AltLabSettings>(AltLab.StatePath(AltSettingsFile));
            if (stored != null) altSettings = AltLab.NormalizeSettings(stored);
        }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException || error is FormatException)) throw;
            altStatus.Text = "Настройки ALT не прочитаны · " + error.Message;
        }
        try
        {
            var stored = AltLab.Load<List<AltLabRun>>(AltLab.StatePath(AltHistoryFile));
            if (stored == null) return;
            altHistory.AddRange(stored.Where(AltLab.ValidRun).OrderByDescending(run => run.DateUtc).Take(AltLab.MaxHistory));
            altShownRun = altHistory.FirstOrDefault();
            if (altShownRun != null)
            {
                AltLastSummary = SummarizeAltRun(altShownRun);
            }
        }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException)) throw;
            altStatus.Text = "История ALT не прочитана · " + error.Message;
        }
    }

    private void ApplyAltSettings(AltLabSettings settings)
    {
        AltLabSettings normalized = AltLab.NormalizeSettings(settings);
        AltLab.Save(AltLab.StatePath(AltSettingsFile), normalized);
        altSettings = normalized;
        SyncAltSettingsControls();
        if (altShownRun == null) RenderAltTable();
        UpdateAltTestActions();
    }

    private void SyncAltSettingsControls()
    {
        if (altSets == null) return;
        altUpdatingSettings = true;
        try
        {
            altSets.SelectedIndex = Array.IndexOf(AltLab.Sets, altSettings.SelectedSet);
            altModes.SelectedIndex = altSettings.Detailed ? 1 : 0;
            altEditSites.Text = "Мои сайты · " + altSettings.MySites.Length + "/" + AltLab.MaxCustomSites;
        }
        finally { altUpdatingSettings = false; }
    }

    private void SaveAltControlSettings()
    {
        if (altUpdatingSettings || AltTestsRunning || altSets.SelectedIndex < 0) return;
        try
        {
            ApplyAltSettings(new AltLabSettings { SelectedSet = AltLab.Sets[altSets.SelectedIndex],
                Detailed = altModes.SelectedIndex == 1, MySites = altSettings.MySites });
        }
        catch (Exception error)
        {
            if (!(error is IOException || error is UnauthorizedAccessException)) throw;
            SyncAltSettingsControls();
            altStatus.Text = "Настройки ALT не сохранены · " + error.Message;
        }
    }

    private void EditAltSites()
    {
        if (AltTestsRunning) return;
        using (var dialog = AltDialog("Мои сайты", new Size(500, 270)))
        {
            var slots = Grid();
            slots.Padding = new Padding(15);
            SetRows(slots, 29, 55, -1, 42);
            slots.Controls.Add(Heading("Мои сайты", 15, TextColor), 0, 0);
            slots.Controls.Add(Heading("До 3 публичных доменов, по одному в строке. Проверяется главная страница HTTPS на порту 443. " +
                "Прямые IP-адреса и непубличные DNS-адреса блокируются.", 9, Muted), 0, 1);
            var input = new WorkspaceTextBox { AccessibleName = "Публичные домены", Lines = altSettings.MySites, MaxLength = 1024 };
            ConfigureText(input);
            slots.Controls.Add(input, 0, 2);
            var save = Action("Сохранить", delegate
            {
                ApplyAltSettings(new AltLabSettings { SelectedSet = altSettings.SelectedSet, Detailed = altSettings.Detailed, MySites = input.Lines });
                dialog.DialogResult = DialogResult.OK;
            }, true);
            var cancel = Action("Отмена", delegate { dialog.DialogResult = DialogResult.Cancel; });
            dialog.CancelButton = cancel;
            slots.Controls.Add(ButtonRow(save, cancel), 0, 3);
            dialog.Controls.Add(slots);
            dialog.ShowDialog(this);
        }
    }

    private static Form AltDialog(string title, Size size)
    {
        return new Form { Text = title, ClientSize = size, MinimumSize = size, StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            ShowInTaskbar = false, BackColor = Surface, ForeColor = TextColor };
    }

    private void RefreshAltTests()
    {
        if (altProfiles == null) return;
        string folder = preferences.Folder;
        if (!AltTestsRunning && (!ReferenceEquals(altCatalog, availableProfiles) ||
            !String.Equals(altFolder, folder, StringComparison.OrdinalIgnoreCase)))
        {
            var selection = String.Equals(altFolder, folder, StringComparison.OrdinalIgnoreCase) ?
                altProfiles.SelectedItems.Cast<string>().ToArray() : new string[0];
            altFolder = folder;
            altCatalog = availableProfiles;
            altProfiles.BeginUpdate();
            try
            {
                altProfiles.Items.Clear();
                altProfiles.Items.AddRange(altCatalog);
                foreach (string name in selection)
                {
                    int index = altProfiles.Items.IndexOf(name);
                    if (index >= 0) altProfiles.SetSelected(index, true);
                }
            }
            finally { altProfiles.EndUpdate(); }
        }
        RefreshListScrollBar(altProfiles);
        UpdateAltTestActions();
    }

    private void UpdateAltTestActions()
    {
        if (altLaunch == null) return;
        bool busy = AltTestsRunning;
        bool hasSites = AltLab.SitesFor(altSettings).Length > 0;
        int count = altProfiles.SelectedItems.Count;
        altProfiles.Enabled = !busy && altProfiles.Items.Count > 0;
        altSets.Enabled = altModes.Enabled = altEditSites.Enabled = !busy;
        altCurrent.Enabled = !busy && hasSites;
        altCancel.Enabled = busy && !altCancellation.IsCancellationRequested;
        altHistoryButton.Enabled = !busy && altHistory.Count > 0;
        altCsv.Enabled = !busy && altShownRun != null && altShownRun.Rows.Count > 0;
        altLaunch.Enabled = CanLaunchAltRow(SelectedAltRow());
        altCompare.Enabled = !busy && hasSites && isAdmin && session == null && !serviceRunning && count > 0 && count <= AltLab.MaxProfiles;
        altSelection.Text = !hasSites ? "Добавьте публичный домен в «Мои сайты»." :
            session != null || serviceRunning ? "Для сравнения завершите сеанс или остановите службу вручную." :
            !isAdmin ? "Для сравнения откройте приложение от имени администратора." :
            altProfiles.Items.Count == 0 ? "Выберите папку Flowseal в разделе «Профили»." :
            count > AltLab.MaxProfiles ? "Для сравнения выберите до " + AltLab.MaxProfiles + " профилей." :
            "Выбрано " + count + " из " + AltLab.MaxProfiles + ". Сначала замер без профиля.";
    }

    private async void RunAltTests(bool compare)
    {
        if (AltTestsRunning || Disposing || IsDisposed || altProfiles == null) return;
        string folder = preferences.Folder;
        string[] selection = compare ? altProfiles.SelectedItems.Cast<string>().ToArray() : new string[0];
        string[] sites = AltLab.SitesFor(altSettings);
        if (sites.Length == 0)
        {
            altStatus.Text = "В наборе «Мои сайты» пока нет доменов.";
            return;
        }
        if (compare && (!isAdmin || session != null || serviceRunning || selection.Length == 0 || selection.Length > AltLab.MaxProfiles ||
            !String.Equals(altFolder, folder, StringComparison.OrdinalIgnoreCase) || !ProfileFiles.IsBundle(folder))) return;

        var cancellation = new CancellationTokenSource();
        altCancellation = cancellation;
        CancellationToken token = cancellation.Token;
        var run = new AltLabRun { DateUtc = DateTime.UtcNow, Version = "?", BundleFolder = folder,
            Set = altSettings.SelectedSet, Attempts = altSettings.Detailed ? AltLab.DetailedAttempts : 1, Sites = sites };
        altCompletedMeasurements = 0;
        altTotalMeasurements = (compare ? 1 : 1) * run.Attempts * sites.Length;
        if (altProgress != null) { altProgress.Maximum = Math.Max(1, altTotalMeasurements); altProgress.Value = 0; }
        altShownRun = run;
        altBeforeRun = null;
        altSortSite = null;
        try
        {
            run.Version = ReadAltVersion(folder);
            var baseline = new AltLabRow { Key = "baseline", Title = "База · без профиля" };
            if (compare)
            {
                run.Rows.Add(baseline);
                run.Rows.AddRange(selection.Select(profile => new AltLabRow { Key = "profile:" + profile,
                    Title = ProfileTitle(profile), Profile = profile }));
            }
            else run.Rows.Add(CreateAltCurrentRow());
            RenderAltTable();
            RefreshControls();
            if (compare)
            {
                await MeasureAltConfiguration(run, baseline, true, token);
                foreach (AltLabRow row in run.Rows.Skip(1))
                {
                    await Task.Delay(350, token);
                    await MeasureAltProfile(run, row, token);
                }
            }
            else await MeasureAltConfiguration(run, run.Rows[0], false, token);
            token.ThrowIfCancellationRequested();
            if (!Disposing && !IsDisposed)
            {
                altHistory.Insert(0, run);
                if (altHistory.Count > AltLab.MaxHistory) altHistory.RemoveAt(altHistory.Count - 1);
                AltLastSummary = SummarizeAltRun(run);
                altStatus.Text = "Проверка завершена";
                try { AltLab.Save(AltLab.StatePath(AltHistoryFile), altHistory); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                    altStatus.Text = "Проверка завершена · история доступна до закрытия приложения";
                    Log("Не удалось записать историю ALT. " + error.Message);
                }
                RefreshDashboard();
                NotifyWorkspace("Тест ALT завершён", AltLastSummary, false);
            }
        }
        catch (OperationCanceledException)
        {
            if (!Disposing && !IsDisposed) altStatus.Text = "Проверка отменена · эта попытка не сохранена в истории";
        }
        catch (Exception error)
        {
            if (!Disposing && !IsDisposed) altStatus.Text = error.Message;
        }
        finally
        {
            CloseAltSession();
            altCancellation = null;
            cancellation.Dispose();
            if (!Disposing && !IsDisposed) RefreshControls();
        }
    }

    private static string ReadAltVersion(string folder)
    {
        try
        {
            Version version = FlowsealRelease.InstalledVersion(folder);
            return version == null ? "?" : version.ToString();
        }
        catch (IOException) { return "?"; }
        catch (UnauthorizedAccessException) { return "?"; }
    }

    private AltLabRow CreateAltCurrentRow()
    {
        nextServiceCheck = DateTime.MinValue;
        RefreshServiceStatus();
        bool idle;
        string serviceState = ReadAltServiceState(out idle);
        AltWinws winws = ReadAltWinws(session);
        string note = "Служба zapret " + serviceState;
        string key = "current:" + serviceState;
        if (serviceState == "работает" && !String.IsNullOrWhiteSpace(serviceStrategy))
        {
            key += ":" + serviceStrategy;
            note += " · " + serviceStrategy;
        }
        if ((winws & AltWinws.Owned) != 0 || session != null)
        {
            key += ":" + runningProfile;
            note += " · сеанс " + ProfileTitle(runningProfile);
        }
        if ((winws & AltWinws.Other) != 0)
        {
            key += ":external";
            note += " · сторонний winws.exe";
        }
        return new AltLabRow { Key = key, Title = "Текущее подключение", Profile = session == null ? null : runningProfile, Note = note };
    }

    private static string SummarizeAltRun(AltLabRun run)
    {
        var samples = run.Rows.SelectMany(row => row.Sites.Values).SelectMany(list => list).ToList();
        return run.DateUtc.ToLocalTime().ToString("dd.MM HH:mm") + " · " + AltLab.SetTitle(run.Set) + " · HTTP 2xx " +
            samples.Count(sample => sample.IsSuccess()) + "/" + samples.Count;
    }

    private void StopAltTests()
    {
        var cancellation = altCancellation;
        if (cancellation == null || cancellation.IsCancellationRequested) return;
        if (!Disposing && !IsDisposed)
        {
            altStatus.Text = "Отмена…";
            altCancel.Enabled = false;
        }
        cancellation.Cancel();
        CloseAltSession();
    }

    private void CloseAltSession()
    {
        if (altSession == null) return;
        altSession.Dispose();
        altSession = null;
    }

    private async Task MeasureAltProfile(AltLabRun run, AltLabRow row, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        row.Note = "Профиль выбран · запустите его кнопкой «Запустить профиль», затем выполните отдельную проверку текущего подключения.";
        RenderAltTable();
        await Task.Delay(0, cancellation);
    }

    private async Task MeasureAltConfiguration(AltLabRun run, AltLabRow row, bool compare, CancellationToken cancellation)
    {
        for (int attempt = 1; attempt <= run.Attempts; attempt++)
        foreach (string site in run.Sites)
        {
            cancellation.ThrowIfCancellationRequested();
            if (compare && !CheckAltConfiguration(row)) return;
            altStatus.Text = row.Title + " · " + AltLab.SiteTitle(site) + " · " + attempt + "/" + run.Attempts;
            AltLabSample result = await AltLab.MeasureAsync(site, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (compare && !CheckAltConfiguration(row)) return;
            List<AltLabSample> samples;
            if (!row.Sites.TryGetValue(site, out samples)) row.Sites.Add(site, samples = new List<AltLabSample>());
            samples.Add(result);
            altCompletedMeasurements++;
            if (altProgress != null) altProgress.Value = Math.Min(altProgress.Maximum, altCompletedMeasurements);
            RenderAltTable();
        }
    }

    private bool CheckAltConfiguration(AltLabRow row)
    {
        if (RequireAltIsolation(altSession) || altSession == null && row.Profile == null) return true;
        row.Note = "Профиль прерван · winws.exe завершился. Учтены только предыдущие замеры.";
        RenderAltTable();
        return false;
    }

    private void RenderAltTable()
    {
        if (altTable == null) return;
        AltViewRow selected = SelectedAltRow();
        string selectedKey = selected == null ? null : selected.Key;
        string selectedSite = altTable.CurrentCell == null ? "" : altTable.Columns[altTable.CurrentCell.ColumnIndex].Name;
        string[] sites = altShownRun == null ? AltLab.SitesFor(altSettings) : altBeforeRun == null ? altShownRun.Sites :
            altShownRun.Sites.Union(altBeforeRun.Sites).ToArray();
        var rows = new List<AltViewRow>();
        if (altShownRun != null) rows.AddRange(altShownRun.Rows.Select(row => new AltViewRow { After = row }));
        if (altBeforeRun != null)
        {
            foreach (AltLabRow before in altBeforeRun.Rows)
            {
                AltViewRow match = rows.FirstOrDefault(row => String.Equals(row.Key, before.Key, StringComparison.OrdinalIgnoreCase));
                if (match == null) rows.Add(new AltViewRow { Before = before });
                else match.Before = before;
            }
        }
        if (altSortSite != null && (altSortSite.Length == 0 || sites.Contains(altSortSite)))
            rows.Sort(CompareAltRows);
        if (altFilter != null && altFilter.SelectedIndex > 0)
            rows = rows.Where(AltMatchesFilter).ToList();

        altRendering = true;
        altTable.SuspendLayout();
        try
        {
            altTable.Rows.Clear();
            if (altTable.Columns.Count != sites.Length + 1 || !altTable.Columns.Cast<DataGridViewColumn>().Skip(1).Select(col => col.Name).SequenceEqual(sites))
            {
                altTable.Columns.Clear();
                AddAltColumn("", "Профиль", 150);
                foreach (string site in sites) AddAltColumn(site, AltLab.SiteTitle(site), 105);
            }
            foreach (DataGridViewColumn column in altTable.Columns)
                column.HeaderCell.SortGlyphDirection = column.Name == altSortSite ?
                    (altSortDescending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            foreach (AltViewRow row in rows)
            {
                var values = new List<object> { row.Title };
                foreach (string site in sites)
                {
                    string after = AltCellText(row.After, site);
                    values.Add(altBeforeRun == null ? after : AltCellText(row.Before, site) + "\n→ " + after);
                }
                var displayed = altTable.Rows[altTable.Rows.Add(values.ToArray())];
                displayed.Tag = row;
                displayed.Cells[0].ToolTipText = AltNotes(row);
                for (int i = 0; i < sites.Length; i++)
                    displayed.Cells[i + 1].ToolTipText = AltAttempts(row, sites[i]);
                if (String.Equals(row.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    int index = Array.IndexOf(sites, selectedSite);
                    altTable.CurrentCell = displayed.Cells[index + 1];
                }
            }
        }
        finally
        {
            altTable.ResumeLayout();
            altRendering = false;
        }
        altRunInfo.Text = altShownRun == null ? "Успехи / попытки · медиана успехов. Нажмите заголовок для сортировки." :
            altBeforeRun == null ? altShownRun + " · " + altShownRun.Attempts + " повт.\r\nFlowseal — версия выбранной сборки; столбцы сортируются." :
            "До: " + altBeforeRun + " · " + altBeforeRun.Attempts + " повт.\r\nПосле: " + altShownRun + " · " + altShownRun.Attempts + " повт.";
        UpdateAltCellDetails();
    }

    private void AddAltColumn(string site, string title, int width)
    {
        // A fixed nonempty name is required for the first DataGridView column.
        altTable.Columns.Add(new DataGridViewTextBoxColumn { Name = site, HeaderText = title,
            SortMode = DataGridViewColumnSortMode.Programmatic, MinimumWidth = width,
            FillWeight = site.Length == 0 ? 145 : 100, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
    }

    private int CompareAltRows(AltViewRow left, AltViewRow right)
    {
        int order;
        if (altSortSite.Length == 0) order = StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title);
        else
        {
            order = AltLabStats.Compare(AltStats(left.After, altSortSite), AltStats(right.After, altSortSite));
            if (order == 0 && altBeforeRun != null)
                order = AltLabStats.Compare(AltStats(left.Before, altSortSite), AltStats(right.Before, altSortSite));
        }
        return order == 0 ? StringComparer.CurrentCultureIgnoreCase.Compare(left.Title, right.Title) : altSortDescending ? -order : order;
    }

    private static AltLabStats AltStats(AltLabRow row, string site)
    {
        return new AltLabStats(row == null ? null : row.Samples(site));
    }

    private static string AltCellText(AltLabRow row, string site)
    {
        AltLabStats stats = AltStats(row, site);
        return stats.Total == 0 ? "—" : stats.CountText + " · " + stats.TimeText;
    }

    private bool AltMatchesFilter(AltViewRow row)
    {
        bool success = row.After != null && row.After.Sites.Values.SelectMany(list => list).Any(sample => sample.IsSuccess());
        bool error = row.After == null || row.After.Sites.Values.SelectMany(list => list).Any(sample => !sample.IsSuccess());
        return altFilter.SelectedIndex == 1 ? success && !error : error;
    }

    private AltViewRow SelectedAltRow()
    {
        return altTable == null || altTable.CurrentRow == null ? null : altTable.CurrentRow.Tag as AltViewRow;
    }

    private string AltAttempts(AltViewRow row, string site)
    {
        if (altBeforeRun == null) return DescribeAltSamples(row.After, site);
        return "До: " + DescribeAltSamples(row.Before, site) + "\r\nПосле: " + DescribeAltSamples(row.After, site);
    }

    private static string DescribeAltSamples(AltLabRow row, string site)
    {
        IList<AltLabSample> samples = row == null ? null : row.Samples(site);
        return samples == null || samples.Count == 0 ? "—" : String.Join("; ", samples.Select(sample => sample.Describe()));
    }

    private string AltNotes(AltViewRow row)
    {
        if (altBeforeRun == null) return row.After == null ? "" : row.After.Note ?? "";
        return "До: " + (row.Before == null ? "—" : row.Before.Note ?? "") + "\r\nПосле: " + (row.After == null ? "—" : row.After.Note ?? "");
    }

    private void UpdateAltCellDetails()
    {
        AltViewRow row = SelectedAltRow();
        if (row == null) altDetail.Text = "Выберите ячейку, чтобы увидеть отдельные попытки.";
        else
        {
            string site = altTable.CurrentCell == null ? "" : altTable.Columns[altTable.CurrentCell.ColumnIndex].Name;
            if (site.Length == 0)
            {
                string note = AltNotes(row);
                altDetail.Text = note.Length == 0 ? row.Title + " · выберите ячейку сайта для подробностей." : note;
            }
            else altDetail.Text = AltLab.SiteTitle(site) + " · " + AltAttempts(row, site);
        }
        workspaceHints.SetToolTip(altDetail, altDetail.Text);
        UpdateAltTestActions();
    }

    private sealed class AltViewRow
    {
        internal AltLabRow Before, After;
        internal string Key { get { return (After ?? Before).Key; } }
        internal string Title { get { return (After ?? Before).Title; } }
    }

    private void ShowAltHistory()
    {
        if (AltTestsRunning || altHistory.Count == 0) return;
        using (var dialog = AltDialog("История ALT", new Size(515, 290)))
        {
            var grid = Grid();
            grid.Padding = new Padding(15);
            SetRows(grid, 31, 26, 34, 26, 34, -1, 42);
            grid.Controls.Add(Heading("Сохранённые проверки", 15, TextColor), 0, 0);
            grid.Controls.Add(Heading("После · выбранная проверка", 9, Muted), 0, 1);
            var after = AltCombo("Проверка после");
            after.Items.AddRange(altHistory.Cast<object>().ToArray());
            after.SelectedItem = altShownRun;
            if (after.SelectedIndex < 0) after.SelectedIndex = 0;
            grid.Controls.Add(after, 0, 2);
            grid.Controls.Add(Heading("До · более ранняя проверка", 9, Muted), 0, 3);
            var before = AltCombo("Проверка до");
            grid.Controls.Add(before, 0, 4);
            System.Action fillBefore = delegate
            {
                var selected = before.SelectedItem as AltLabRun ?? altBeforeRun;
                before.Items.Clear();
                before.Items.Add("Без сравнения");
                if (after.SelectedIndex >= 0)
                    before.Items.AddRange(altHistory.Skip(after.SelectedIndex + 1).Cast<object>().ToArray());
                if (selected != null) before.SelectedItem = selected;
                if (before.SelectedIndex < 0) before.SelectedIndex = 0;
            };
            fillBefore();
            after.SelectedIndexChanged += delegate { fillBefore(); };
            grid.Controls.Add(Heading("До " + AltLab.MaxHistory + " запусков. Таблица сопоставляет одинаковые профили и сайты. " +
                "Для сравнения времени выбирайте одинаковые наборы и число повторов.", 9, Muted), 0, 5);
            var show = Action("Показать", delegate { dialog.DialogResult = DialogResult.OK; }, true);
            var cancel = Action("Отмена", delegate { dialog.DialogResult = DialogResult.Cancel; });
            dialog.AcceptButton = show;
            dialog.CancelButton = cancel;
            grid.Controls.Add(ButtonRow(show, cancel), 0, 6);
            dialog.Controls.Add(grid);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            altShownRun = (AltLabRun)after.SelectedItem;
            altBeforeRun = before.SelectedItem as AltLabRun;
            altSortSite = null;
            altStatus.Text = altBeforeRun == null ? "История · выбранный запуск" : "История · сравнение до → после";
            RenderAltTable();
        }
    }

    private void SaveAltCsv()
    {
        if (altShownRun == null || AltTestsRunning) return;
        using (var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", DefaultExt = "csv",
            FileName = "reze-alt-" + altShownRun.DateUtc.ToLocalTime().ToString("yyyy-MM-dd-HHmmss") + ".csv" })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(dialog.FileName, AltLab.Csv(altBeforeRun, altShownRun), new UTF8Encoding(true));
                altStatus.Text = "CSV сохранён";
            }
        }
    }

    private bool CanLaunchAltRow(AltViewRow row)
    {
        if (row == null || AltTestsRunning || session != null || serviceRunning) return false;
        AltLabRun source = row.After != null ? altShownRun : altBeforeRun;
        return source != null && String.Equals(source.BundleFolder, preferences.Folder, StringComparison.OrdinalIgnoreCase) &&
            ProfileFiles.IsProfile((row.After ?? row.Before).Profile) &&
            availableProfiles.Contains((row.After ?? row.Before).Profile, StringComparer.OrdinalIgnoreCase);
    }

    private void LaunchAltSelectedProfile()
    {
        AltViewRow row = SelectedAltRow();
        if (!CanLaunchAltRow(row)) return;
        bool idle;
        string state = ReadAltServiceState(out idle);
        if (!idle)
        {
            altStatus.Text = "Запуск недоступен · служба zapret " + state;
            return;
        }
        string profile = (row.After ?? row.Before).Profile;
        string localName = availableProfiles.First(name => String.Equals(name, profile, StringComparison.OrdinalIgnoreCase));
        if (!profiles.Items.Contains(localName))
        {
            favoritesOnly.Selected = false;
            profileSearch.Clear();
            FilterProfiles();
        }
        int index = profiles.Items.IndexOf(localName);
        if (index < 0) return;
        profiles.SelectedIndex = index;
        StartProfile();
        if (session != null) altStatus.Text = "Запущен сеанс · " + ProfileTitle(localName);
    }

    private static bool RequireAltIsolation(Session owned)
    {
        bool serviceIdle;
        string state = ReadAltServiceState(out serviceIdle);
        if (!serviceIdle) throw new InvalidOperationException("Сравнение недоступно: служба zapret " + state + ".");
        AltWinws winws = ReadAltWinws(owned);
        if ((winws & AltWinws.Other) != 0)
            throw new InvalidOperationException("Сравнение недоступно, пока запущен сторонний winws.exe.");
        return (winws & AltWinws.Owned) != 0;
    }

    private static string ReadAltServiceState(out bool idle)
    {
        idle = false;
        try
        {
            using (var controller = new ServiceController("zapret"))
            {
                var state = controller.Status;
                idle = state == ServiceControllerStatus.Stopped;
                return idle ? "остановлена" : state == ServiceControllerStatus.Running ? "работает" :
                    state == ServiceControllerStatus.Paused ? "на паузе" : "меняет состояние";
            }
        }
        catch (InvalidOperationException error)
        {
            var cause = error.InnerException as Win32Exception;
            if (cause != null && cause.NativeErrorCode == 1060) { idle = true; return "не установлена"; }
        }
        catch (Win32Exception error)
        {
            if (error.NativeErrorCode == 1060) { idle = true; return "не установлена"; }
        }
        return "не удалось проверить";
    }

    [Flags]
    private enum AltWinws { None = 0, Owned = 1, Other = 2 }

    private static AltWinws ReadAltWinws(Session owned)
    {
        AltWinws result = AltWinws.None;
        var processes = Process.GetProcessesByName("winws");
        try
        {
            foreach (Process process in processes)
            {
                try { result |= owned != null && owned.Contains(process) ? AltWinws.Owned : AltWinws.Other; }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { result |= AltWinws.Other; }
            }
        }
        finally { foreach (Process process in processes) process.Dispose(); }
        return result;
    }
}
