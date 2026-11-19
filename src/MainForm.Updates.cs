using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private enum UpdateAction { Inspect, Check, Automatic, Download, Select, Rollback }

    private sealed class LocalFlowsealBundle
    {
        public string Folder;
        public Version Version;
        public FlowsealRelease.Prepared Prepared;
        public bool FromSelectedFolder;
        public bool PreviouslyUsed;
        public bool IsServiceFolder;
        public bool HasOwnLists { get { return PreviouslyUsed || IsServiceFolder; } }

        public override string ToString()
        {
            string version = Version == null ? "Версия неизвестна" : Version.ToString();
            return version + " · " + (IsServiceFolder ? "Папка службы" : PreviouslyUsed ? "Предыдущая" :
                FromSelectedFolder ? "Подготовлена" : "Скачана");
        }
    }

    private readonly System.Windows.Forms.Timer updateTimer = new System.Windows.Forms.Timer { Interval = 2000 };
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
    private readonly HashSet<Version> notifiedFlowsealReleases = new HashSet<Version>();
    private readonly HashSet<string> usedFlowsealFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly ListBox localFlowsealList = new WorkspaceListBox();
    private WorkspaceButton autoUpdatesButton, checkUpdateButton, downloadUpdateButton, selectUpdateButton, rollbackUpdateButton, cancelUpdateButton, inspectUpdatesButton;
    private Label updateStatus, selectedVersionLabel, serviceVersionLabel, releaseNotesTitle, localVersionsTitle;
    private TextBox selectedVersionPath, serviceVersionPath, localVersionPath, releaseNotes;
    private LinkLabel officialReleaseLink;
    private CancellationTokenSource updateOperation;
    private UpdateAction currentUpdateAction;
    private bool updatesInitialized, updatesStopped, updateInspectionPending, serviceInspectionRunning;
    private string updateFolder;
    private DateTime nextUpdateCheck, nextUpdateServiceInspection;
    private Version selectedFlowsealVersion;
    private FlowsealRelease latestFlowsealRelease;
    private FlowsealRelease.Prepared[] stagedFlowsealReleases = new FlowsealRelease.Prepared[0];
    private FlowsealRelease.ServiceInfo flowsealService;

    private bool UpdateOperationInProgress { get { return updateOperation != null; } }
    private Version FlowsealServiceVersion { get { return flowsealService == null ? null : flowsealService.Version; } }
    private string FlowsealServicePath { get { return flowsealService == null ? null : flowsealService.ImagePath; } }
    private int StagedFlowsealCount { get { return stagedFlowsealReleases.Count(item => !SameUpdatePath(item.Folder, updateFolder)); } }

    private Control CreateUpdates()
    {
        var card = new WorkspaceCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(15) };
        var body = Grid();
        body.BackColor = Surface;
        card.Controls.Add(body);
        SetRows(body, 34, 27, 27, 30, 42, 30, -1, 42, 54);
        body.Controls.Add(Heading("Версии Flowseal", 16, TextColor), 0, 0);
        var versions = Split(50);
        serviceVersionLabel = Heading("Служба zapret", 10, AccentLight);
        selectedVersionLabel = Heading("Выбранная сборка", 10, AccentLight);
        versions.Controls.Add(serviceVersionLabel, 0, 0);
        versions.Controls.Add(selectedVersionLabel, 1, 0);
        body.Controls.Add(versions, 0, 1);
        var paths = Split(50);
        serviceVersionPath = UpdatePathBox("Путь исполняемого файла службы zapret");
        serviceVersionPath.Margin = new Padding(0, 0, 8, 0);
        selectedVersionPath = UpdatePathBox("Путь выбранной сборки");
        paths.Controls.Add(serviceVersionPath, 0, 0);
        paths.Controls.Add(selectedVersionPath, 1, 0);
        body.Controls.Add(paths, 0, 2);
        var schedule = Heading("Автозагрузка проверяет GitHub при запуске и каждые 6 часов.", 9, Muted);
        body.Controls.Add(schedule, 0, 3);

        autoUpdatesButton = Action("Автозагрузка", ToggleAutoUpdates);
        autoUpdatesButton.MinimumSize = new Size(156, 32);
        autoUpdatesButton.Glyph = "switch";
        autoUpdatesButton.AccessibleRole = AccessibleRole.CheckButton;
        checkUpdateButton = Action("Проверить GitHub", delegate { RunUpdate(UpdateAction.Check); });
        downloadUpdateButton = Action("Скачать ZIP", delegate { RunUpdate(UpdateAction.Download); });
        cancelUpdateButton = Action("Отменить", CancelUpdate);
        body.Controls.Add(ButtonRow(autoUpdatesButton, checkUpdateButton, downloadUpdateButton, cancelUpdateButton), 0, 4);

        var captions = Split(52);
        localVersionsTitle = Heading("ЛОКАЛЬНЫЕ СБОРКИ", 8.5f, Accent);
        releaseNotesTitle = Heading("СТАБИЛЬНЫЙ РЕЛИЗ GITHUB", 8.5f, Accent);
        captions.Controls.Add(localVersionsTitle, 0, 0);
        captions.Controls.Add(releaseNotesTitle, 1, 0);
        body.Controls.Add(captions, 0, 5);
        var catalog = Split(52);
        var local = Grid();
        local.Margin = new Padding(0, 0, 10, 0);
        SetRows(local, -1, 25);
        ConfigureList(localFlowsealList, "Локальные сборки Flowseal");
        localFlowsealList.DrawItem += delegate(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= localFlowsealList.Items.Count) return;
            DrawListRow(localFlowsealList, e, localFlowsealList.Items[e.Index].ToString(), "folder");
        };
        localFlowsealList.SelectedIndexChanged += delegate { RefreshLocalVersionSelection(); };
        local.Controls.Add(localFlowsealList, 0, 0);
        localVersionPath = UpdatePathBox("Путь выделенной локальной сборки");
        localVersionPath.Margin = new Padding(0, 4, 0, 0);
        local.Controls.Add(localVersionPath, 0, 1);
        catalog.Controls.Add(local, 0, 0);
        releaseNotes = new WorkspaceTextBox { ReadOnly = true, AccessibleName = "Описание последнего релиза Flowseal" };
        ConfigureText(releaseNotes);
        releaseNotes.Text = "Нажмите «Проверить GitHub», чтобы загрузить описание релиза.";
        releaseNotes.Margin = Padding.Empty;
        catalog.Controls.Add(releaseNotes, 1, 0);
        body.Controls.Add(catalog, 0, 6);

        var catalogActions = Split(52);
        selectUpdateButton = Action("Выбрать папку", delegate { RunUpdate(UpdateAction.Select); }, true);
        selectUpdateButton.MinimumSize = new Size(123, 32);
        rollbackUpdateButton = Action("Откатить", delegate { RunUpdate(UpdateAction.Rollback); });
        rollbackUpdateButton.MinimumSize = new Size(105, 32);
        inspectUpdatesButton = Action("Перечитать", delegate { nextUpdateServiceInspection = DateTime.MinValue; InspectUpdateService(); RunUpdate(UpdateAction.Inspect); });
        catalogActions.Controls.Add(ButtonRow(selectUpdateButton, rollbackUpdateButton, inspectUpdatesButton), 0, 0);
        officialReleaseLink = new LinkLabel { Dock = DockStyle.Fill, Text = "Открыть на GitHub", LinkColor = AccentLight,
            ActiveLinkColor = TextColor, LinkBehavior = LinkBehavior.HoverUnderline, TextAlign = ContentAlignment.MiddleLeft };
        officialReleaseLink.LinkClicked += delegate
        {
            Guard(delegate { Process.Start(Upstream + "/releases/" + (latestFlowsealRelease == null ? "latest" : "tag/" + latestFlowsealRelease.Tag)); });
        };
        catalogActions.Controls.Add(officialReleaseLink, 1, 0);
        body.Controls.Add(catalogActions, 0, 7);
        var footer = Grid();
        SetRows(footer, 20, 34);
        updateStatus = Heading("Проверка GitHub ещё не выполнялась.", 9, TextColor);
        footer.Controls.Add(updateStatus, 0, 0);
        var footerHint = Heading("Папка меняет настройки Reze.\r\nСлужба переключается вручную в «Профили» → «Меню Flowseal».", 8.5f, Muted);
        footerHint.AutoEllipsis = false;
        footerHint.AutoSize = false;
        footerHint.Padding = new Padding(0, 3, 0, 3);
        footer.Controls.Add(footerHint, 0, 1);
        body.Controls.Add(footer, 0, 8);
        RefreshUpdateControls();
        return card;
    }

    private static TextBox UpdatePathBox(string accessibleName)
    {
        var box = new WorkspaceTextBox { ReadOnly = true, AccessibleName = accessibleName };
        ConfigureText(box);
        box.Multiline = false;
        box.Font = new Font("Segoe UI", 9);
        box.Margin = Padding.Empty;
        return box;
    }

    private void InitializeUpdates()
    {
        if (updatesInitialized || updatesStopped || updateStatus == null) return;
        updatesInitialized = true;
        updateInspectionPending = true;
        updateTimer.Tick += UpdateTimerTick;
        updateTimer.Start();
        InspectUpdateService();
        RunScheduledUpdate();
    }

    private void StopUpdates()
    {
        if (updatesStopped) return;
        updatesStopped = true;
        updateTimer.Stop();
        updateTimer.Tick -= UpdateTimerTick;
        updateTimer.Dispose();
        if (UpdateOperationInProgress) updateOperation.Cancel();
    }

    private void UpdateTimerTick(object sender, EventArgs e)
    {
        RefreshUpdateControls();
        InspectUpdateService();
        RunScheduledUpdate();
    }

    private async void InspectUpdateService()
    {
        if (updatesStopped || serviceInspectionRunning || DateTime.UtcNow < nextUpdateServiceInspection) return;
        serviceInspectionRunning = true;
        nextUpdateServiceInspection = DateTime.UtcNow.AddSeconds(10);
        try
        {
            var current = await Task.Run(() => FlowsealRelease.ReadServiceInfo());
            if (updatesStopped) return;
            var old = flowsealService;
            flowsealService = current;
            if (old == null || old.Version != current.Version || !SameUpdatePath(old.BundleFolder, current.BundleFolder))
                updateInspectionPending = true;
            RefreshUpdateControls();
        }
        finally { serviceInspectionRunning = false; }
    }

    private void RunScheduledUpdate()
    {
        if (updatesStopped || UpdateOperationInProgress) return;
        if (preferences.AutoUpdates && DateTime.UtcNow >= nextUpdateCheck)
            RunUpdate(UpdateAction.Automatic);
        else if (updateInspectionPending)
            RunUpdate(UpdateAction.Inspect);
    }

    private void ToggleAutoUpdates()
    {
        preferences.AutoUpdates = !preferences.AutoUpdates;
        SavePreferences();
        if (preferences.AutoUpdates) nextUpdateCheck = DateTime.MinValue;
        else if (UpdateOperationInProgress && currentUpdateAction == UpdateAction.Automatic) CancelUpdate();
        RefreshUpdateControls();
        if (updatesInitialized) RunScheduledUpdate();
    }

    private void CancelUpdate()
    {
        if (!UpdateOperationInProgress || updateOperation.IsCancellationRequested) return;
        updateStatus.Text = "Отмена…";
        updateOperation.Cancel();
        RefreshUpdateControls();
    }

    private bool UpdateAvailable
    {
        get { return latestFlowsealRelease != null && (selectedFlowsealVersion == null || latestFlowsealRelease.Version > selectedFlowsealVersion); }
    }

    private void RefreshUpdateControls()
    {
        if (updatesStopped || updateStatus == null) return;
        if (!SameUpdatePath(updateFolder, preferences.Folder))
        {
            if (!String.IsNullOrEmpty(updateFolder))
            {
                usedFlowsealFolders.Add(updateFolder);
                preferences.FlowsealFolders.Add(updateFolder);
            }
            updateFolder = preferences.Folder;
            selectedFlowsealVersion = null;
            updateInspectionPending = true;
            nextUpdateCheck = DateTime.MinValue;
            if (UpdateOperationInProgress) updateOperation.Cancel();
            SetLocalVersions(new LocalFlowsealBundle[0], null);
        }
        autoUpdatesButton.Text = preferences.AutoUpdates ? "Автозагрузка · вкл" : "Автозагрузка · выкл";
        autoUpdatesButton.Selected = preferences.AutoUpdates;
        checkUpdateButton.Enabled = !UpdateOperationInProgress;
        inspectUpdatesButton.Enabled = !UpdateOperationInProgress;
        downloadUpdateButton.Enabled = !UpdateOperationInProgress && UpdateAvailable;
        cancelUpdateButton.Enabled = UpdateOperationInProgress && !updateOperation.IsCancellationRequested;
        RefreshLocalVersionSelection();
        string local = selectedFlowsealVersion == null ? (String.IsNullOrEmpty(updateFolder) ? "не выбрана" : "версия неизвестна") : selectedFlowsealVersion.ToString();
        selectedVersionLabel.Text = "Сборка Reze · " + local;
        SetUpdatePath(selectedVersionPath, updateFolder, "Папка не выбрана");
        string serviceVersion = FlowsealServiceVersion == null ? "версия неизвестна" : FlowsealServiceVersion.ToString();
        serviceVersionLabel.Text = flowsealService == null ? "Служба zapret · проверка…" : !flowsealService.Installed ? "Служба zapret · не установлена" :
            "Служба zapret · " + serviceVersion + (serviceRunning ? " · работает" : "");
        SetUpdatePath(serviceVersionPath, FlowsealServicePath, flowsealService != null && !flowsealService.Installed ? "—" : "Путь недоступен");
        releaseNotesTitle.Text = latestFlowsealRelease == null ? "СТАБИЛЬНЫЙ РЕЛИЗ GITHUB" : "GITHUB · " + latestFlowsealRelease.Tag +
            (latestFlowsealRelease.PublishedUtc == DateTime.MinValue ? "" : " · " + latestFlowsealRelease.PublishedUtc.ToLocalTime().ToString("dd.MM.yyyy"));
    }

    private static bool SameUpdatePath(string first, string second)
    {
        return String.Equals(first ?? String.Empty, second ?? String.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private void SetUpdatePath(TextBox box, string path, string emptyText)
    {
        string value = String.IsNullOrEmpty(path) ? emptyText : path;
        string display = value;
        if (!String.IsNullOrEmpty(path))
        {
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(trimmed);
            if (!String.IsNullOrEmpty(name)) display = name;
        }
        if (box.Text != display) box.Text = display;
        workspaceHints.SetToolTip(box, value);
    }

    private void RefreshLocalVersionSelection()
    {
        if (selectUpdateButton == null) return;
        var item = localFlowsealList.SelectedItem as LocalFlowsealBundle;
        bool canSelect = item != null && (item.FromSelectedFolder || item.HasOwnLists);
        bool canRollback = item != null && !item.FromSelectedFolder && item.HasOwnLists;
        bool idle = !UpdateOperationInProgress && session == null && !AltTestsRunning;
        selectUpdateButton.Enabled = idle && canSelect;
        rollbackUpdateButton.Enabled = idle && canRollback;
        selectUpdateButton.Text = item != null && !item.FromSelectedFolder && item.HasOwnLists ? "Вернуть папку" : "Выбрать папку";
        SetUpdatePath(localVersionPath, item == null ? null : item.Folder, "Нет других локальных сборок");
        workspaceHints.SetToolTip(selectUpdateButton, item != null && !canSelect ?
            "Эта копия подготовлена для другой сборки. Для переноса текущих списков скачайте релиз заново." :
            "Меняет выбранную папку Reze. Служба Windows переключается вручную.");
        workspaceHints.SetToolTip(rollbackUpdateButton, canRollback ?
            "Вернёт Reze на эту проверенную локальную сборку. Служба Windows не переключается автоматически." :
            "Выберите предыдущую локальную сборку, чтобы включить откат.");
    }

    private void SetLocalVersions(LocalFlowsealBundle[] items, string preferredPath)
    {
        var previous = localFlowsealList.SelectedItem as LocalFlowsealBundle;
        string path = preferredPath ?? (previous == null ? null : previous.Folder);
        localFlowsealList.BeginUpdate();
        try
        {
            localFlowsealList.Items.Clear();
            localFlowsealList.Items.AddRange(items);
            int index = Array.FindIndex(items, item => SameUpdatePath(item.Folder, path));
            if (items.Length > 0) localFlowsealList.SelectedIndex = Math.Max(0, index);
        }
        finally { localFlowsealList.EndUpdate(); }
        RefreshListScrollBar(localFlowsealList);
        localVersionsTitle.Text = "ЛОКАЛЬНЫЕ СБОРКИ · " + items.Length;
        RefreshLocalVersionSelection();
    }

    private static LocalFlowsealBundle[] ReadLocalVersions(FlowsealRelease.Prepared[] staged, string source,
        string[] previous, string serviceFolder, CancellationToken token)
    {
        var history = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
        var used = new Queue<string>(previous);
        if (!String.IsNullOrEmpty(source)) used.Enqueue(source);
        while (used.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string folder = used.Dequeue();
            var origin = staged.FirstOrDefault(item => SameUpdatePath(item.Folder, folder));
            if (origin != null && !String.IsNullOrEmpty(origin.Source) && history.Add(origin.Source)) used.Enqueue(origin.Source);
        }
        var results = new List<LocalFlowsealBundle>();
        foreach (var item in staged)
        {
            token.ThrowIfCancellationRequested();
            if (SameUpdatePath(item.Folder, source)) continue;
            results.Add(new LocalFlowsealBundle { Folder = item.Folder, Version = item.Version, Prepared = item,
                FromSelectedFolder = SameUpdatePath(item.Source, source), PreviouslyUsed = history.Contains(item.Folder),
                IsServiceFolder = SameUpdatePath(item.Folder, serviceFolder) });
        }
        if (!String.IsNullOrEmpty(serviceFolder)) history.Add(serviceFolder);
        foreach (string folder in history)
        {
            token.ThrowIfCancellationRequested();
            if (SameUpdatePath(folder, source) || results.Any(item => SameUpdatePath(item.Folder, folder))) continue;
            try
            {
                Version version;
                if (FlowsealRelease.TryReadLocalBundle(folder, token, out version))
                    results.Add(new LocalFlowsealBundle { Folder = folder, Version = version, PreviouslyUsed = true,
                        IsServiceFolder = SameUpdatePath(folder, serviceFolder) });
            }
            catch (Exception ex) { if (!FlowsealRelease.IsLocalReadError(ex)) throw; }
        }
        return results.OrderByDescending(item => item.FromSelectedFolder).ThenByDescending(item => item.PreviouslyUsed || item.IsServiceFolder)
            .ThenByDescending(item => item.Version).ToArray();
    }

    private void ShowLatestRelease(FlowsealRelease release, Version installed)
    {
        latestFlowsealRelease = release;
        string notes = release.Notes.Replace("\0", "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).Trim();
        string text = notes.Length == 0 ? "В этом релизе нет описания." : notes;
        if (releaseNotes.Text != text) releaseNotes.Text = text;
        RefreshUpdateControls();
        if (installed != null && release.Version > installed && notifiedFlowsealReleases.Add(release.Version))
            NotifyWorkspace("Обновления Flowseal", "Доступен Flowseal " + release.Tag + ".", true);
    }

    private async void RunUpdate(UpdateAction action)
    {
        if (updatesStopped || UpdateOperationInProgress) return;
        RefreshUpdateControls();
        if (action == UpdateAction.Download && !UpdateAvailable) return;
        if (action == UpdateAction.Select && !selectUpdateButton.Enabled) return;
        if (action == UpdateAction.Rollback && !rollbackUpdateButton.Enabled) return;
        string source = preferences.Folder;
        var selection = localFlowsealList.SelectedItem as LocalFlowsealBundle;
        var release = latestFlowsealRelease;
        var operation = new CancellationTokenSource();
        var token = operation.Token;
        updateOperation = operation;
        currentUpdateAction = action;
        if (action != UpdateAction.Select) updateInspectionPending = false;
        if (action == UpdateAction.Check || action == UpdateAction.Automatic) nextUpdateCheck = DateTime.UtcNow + UpdateInterval;
        if (action == UpdateAction.Check || action == UpdateAction.Automatic) updateStatus.Text = "Проверяем стабильный релиз Flowseal…";
        if (action == UpdateAction.Select) updateStatus.Text = "Проверяем выбранную папку…";
        if (action == UpdateAction.Rollback) updateStatus.Text = "Проверяем сборку для отката…";
        if (action == UpdateAction.Download) updateStatus.Text = "Скачиваем и проверяем официальный ZIP…";
        RefreshUpdateControls();
        bool select = false;
        try
        {
            if (action == UpdateAction.Select || action == UpdateAction.Rollback)
            {
                if (selection == null) throw new IOException("Выберите локальную сборку.");
                if (action == UpdateAction.Rollback && selection.FromSelectedFolder)
                    throw new IOException("Для отката выберите предыдущую локальную сборку.");
                string prompt = action == UpdateAction.Rollback ?
                    "Откатить Reze на эту локальную сборку? Настройки службы Windows не изменятся.\n\n" + selection.Folder :
                    "Выбрать эту папку с её собственными списками? Настройки службы Windows не изменятся.\n\n" + selection.Folder;
                if (!selection.FromSelectedFolder && MessageBox.Show(this, prompt,
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    if (UpdateStillCurrent(source, CancellationToken.None)) updateStatus.Text = "Папка не изменена.";
                    return;
                }
                if (!UpdateStillCurrent(source, token)) return;
                await Task.Run(delegate
                {
                    if (selection.FromSelectedFolder) FlowsealRelease.ValidateSelection(selection.Prepared, source, token);
                    else FlowsealRelease.ValidateExistingSelection(selection.Folder, selection.Version, selection.Prepared, token);
                }, token);
                if (!UpdateStillCurrent(source, token)) return;
                if (session != null || AltTestsRunning) throw new IOException("Завершите сеанс профиля или тест ALT перед выбором другой папки.");
                select = true;
            }
            else
            {
                string staging = FlowsealRelease.StagingRoot();
                string serviceFolder = flowsealService == null ? null : flowsealService.BundleFolder;
                string[] previous = usedFlowsealFolders.ToArray();
                Version installed = await Task.Run(() => FlowsealRelease.InstalledVersion(source), token);
                if (!UpdateStillCurrent(source, token)) return;
                selectedFlowsealVersion = installed;
                var staged = await Task.Run(() => FlowsealRelease.ReadStaged(staging, token), token);
                var versions = await Task.Run(() => ReadLocalVersions(staged, source, previous, serviceFolder, token), token);
                if (!UpdateStillCurrent(source, token)) return;
                stagedFlowsealReleases = staged;
                SetLocalVersions(versions, null);
                RefreshUpdateControls();
                if (action == UpdateAction.Inspect) return;
                if (action == UpdateAction.Check || action == UpdateAction.Automatic)
                {
                    release = await FlowsealRelease.GetLatestAsync(token);
                    if (!UpdateStillCurrent(source, token)) return;
                    ShowLatestRelease(release, installed);
                }
                if (!UpdateAvailable)
                {
                    updateStatus.Text = installed > release.Version ? "Выбранная сборка новее стабильного релиза GitHub." : "Уже выбрана актуальная сборка Flowseal.";
                    return;
                }
                bool ready = versions.Any(item => item.FromSelectedFolder && item.Version == release.Version &&
                    String.Equals(item.Prepared.Digest, release.Digest, StringComparison.OrdinalIgnoreCase));
                if (action == UpdateAction.Check || (action == UpdateAction.Automatic && ready))
                {
                    updateStatus.Text = ready ? "Релиз " + release.Tag + " уже подготовлен." : "Доступен релиз " + release.Tag + ". Нажмите «Скачать ZIP».";
                    return;
                }
                if (action == UpdateAction.Automatic && installed == null && !String.IsNullOrEmpty(source))
                {
                    updateStatus.Text = "Версия выбранной сборки неизвестна. Релиз можно скачать вручную.";
                    return;
                }
                updateStatus.Text = "Скачиваем и проверяем официальный ZIP " + release.Tag + "…";
                var result = await Task.Run(() => release.StageAsync(staging, source, token), token);
                if (!UpdateStillCurrent(source, token)) return;
                staged = new[] { result }.Concat(staged).ToArray();
                versions = await Task.Run(() => ReadLocalVersions(staged, source, previous, serviceFolder, token), token);
                if (!UpdateStillCurrent(source, token)) return;
                stagedFlowsealReleases = staged;
                SetLocalVersions(versions, result.Folder);
                updateStatus.Text = "Flowseal " + release.Tag + " готов. Пользовательские списки и их копии перенесены.";
                Log("Подготовлена отдельная папка Flowseal " + release.Tag + ": " + result.Folder);
            }
        }
        catch (OperationCanceledException)
        {
            if (UpdateStillCurrent(source, CancellationToken.None)) updateStatus.Text = "Операция отменена.";
        }
        catch (Exception ex)
        {
            if (!(FlowsealRelease.IsLocalReadError(ex) || ex is InvalidDataException || ex is HttpRequestException)) throw;
            if (UpdateStillCurrent(source, CancellationToken.None))
            {
                updateStatus.Text = token.IsCancellationRequested ? "Операция отменена." : ex.Message;
                if (!token.IsCancellationRequested) Log("Обновления Flowseal. " + ex.Message);
            }
        }
        finally
        {
            updateOperation = null;
            operation.Dispose();
            if (!updatesStopped) RefreshUpdateControls();
        }
        if (select && !updatesStopped && !token.IsCancellationRequested && SameUpdatePath(source, preferences.Folder))
        {
            Guard(delegate
            {
                if (session != null || AltTestsRunning || !SameUpdatePath(source, preferences.Folder)) return;
                LoadFolder(selection.Folder, true);
                RefreshUpdateControls();
                selectedFlowsealVersion = selection.Version;
                RefreshUpdateControls();
                updateStatus.Text = action == UpdateAction.Rollback ?
                    "Откат выполнен. Служба Windows продолжает использовать свою папку." :
                    "Выбрана папка Flowseal. Служба Windows продолжает использовать свою папку.";
                Log(action == UpdateAction.Rollback ? "Выполнен откат локальной сборки Flowseal: " + selection.Folder :
                    "Выбрана папка Flowseal: " + selection.Folder);
                RunScheduledUpdate();
            });
        }
    }

    private bool UpdateStillCurrent(string source, CancellationToken token)
    {
        if (updatesStopped || !SameUpdatePath(source, preferences.Folder)) return false;
        token.ThrowIfCancellationRequested();
        return true;
    }
}
