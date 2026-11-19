using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private readonly TextBox domainSearch = new WorkspaceTextBox();
    private readonly Label selectedProfile = new Label();
    private readonly Label profileHint = new Label();
    private readonly Label domainListCaption = Caption("Сохранённые домены", TextColor);
    private WorkspaceButton favorite, favoritesOnly, compareProfile, validateProfile;
    private Button copyDomains, exportDomains, importDomains, pasteDomains, historyDomains, undoDomains, checkDomain;
    private WorkspaceButton includeMode, excludeMode;
    private string[] storedDomains = new string[0];
    private bool excludeDomains;
    private int activeView;
    private TableLayoutPanel workspaceContent;
    private Control workspaceBanner, compactHeader;
    private Label compactTitle;
    private static readonly string[] ViewTitles = { "Профили", "Свои сервисы", "Журнал", "Тесты ALT", "Обновления", "Обзор", "Настройки" };
    private static readonly string[] ViewGlyphs = { "profiles", "domains", "journal", "tests", "updates", "home", "settings" };

    private const int SetCueBanner = 0x1501;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int bytes);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr option, string text);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (Environment.OSVersion.Version.Major < 6) return;
        int enabled = 1;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
    }

    private Control CreateWorkspace()
    {
        WorkspaceStyle.SetAccent(preferences.AccentName);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 193));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var rail = Grid();
        rail.BackColor = WorkspaceStyle.Rail;
        rail.Padding = new Padding(13, 22, 13, 16);
        SetRows(rail, 70, 29, 43, 43, 43, 43, 43, 43, 43, -1, 25, 30, 39);
        rail.Paint += delegate(object sender, PaintEventArgs e)
        {
            using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, rail.Width - 1, 0, rail.Width - 1, rail.Height);
        };
        rail.Controls.Add(CreateBrand(), 0, 0);
        rail.Controls.Add(Heading("РАБОЧАЯ ОБЛАСТЬ", 8, Muted), 0, 1);
        string[] labels = ViewTitles;
        string[] glyphs = ViewGlyphs;
        views[0] = CreateProfiles();
        views[1] = CreateDomains();
        views[2] = CreateJournal();
        views[3] = CreateAltTests();
        views[4] = CreateUpdates();
        views[5] = CreateDashboard();
        views[6] = CreateSettings();
        int row = 2;
        foreach (int page in new[] { 5, 0, 3, 1, 4, 2, 6 })
        {
            int index = page;
            var button = Action(labels[index], delegate { SelectView(index); });
            button.Navigation = true;
            button.Glyph = glyphs[index];
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 0, 5);
            navigation[index] = button;
            rail.Controls.Add(button, 0, row++);
            pages.Controls.Add(views[index]);
        }
        rail.Controls.Add(Heading("ALT + 1…7   ·   CTRL + K", 8, Muted), 0, 9);
        rail.Controls.Add(TextLink("Документация Flowseal", Upstream), 0, 10);
        var help = Action("Справка · F1", ShowGuide);
        help.Glyph = "journal";
        help.Dock = DockStyle.Fill;
        help.MinimumSize = new Size(0, 26);
        help.Margin = Padding.Empty;
        rail.Controls.Add(help, 0, 11);
        root.Controls.Add(rail, 0, 0);

        var content = workspaceContent = Grid();
        content.Padding = new Padding(22, 18, 22, 18);
        SetRows(content, 54, 210, -1, 35);
        workspaceBanner = CreateBanner();
        compactHeader = CreateCompactHeader();
        content.Controls.Add(compactHeader, 0, 0);
        content.Controls.Add(workspaceBanner, 0, 1);
        content.SizeChanged += delegate { RefreshWorkspaceHeader(); };
        var footer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(0, 1, 0, 0), BackColor = Background };
        footer.Paint += delegate(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(Background);
            using (var line = new Pen(Border)) e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };
        status.Dock = DockStyle.Fill;
        status.Padding = new Padding(23, 0, 4, 0);
        status.Margin = Padding.Empty;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Font = new Font("Segoe UI Semibold", 9);
        status.AutoEllipsis = true;
        status.Paint += delegate(object sender, PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            float r = 3 * scale;
            float x = 8 * scale;
            float y = status.Height / 2f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var halo = new SolidBrush(Color.FromArgb(37, status.ForeColor)))
                e.Graphics.FillEllipse(halo, x - r * 1.9f, y - r * 1.9f, r * 3.8f, r * 3.8f);
            using (var dot = new SolidBrush(status.ForeColor)) e.Graphics.FillEllipse(dot, x - r, y - r, r * 2, r * 2);
        };
        footer.Controls.Add(status);
        content.Controls.Add(pages, 0, 2);
        content.Controls.Add(footer, 0, 3);
        root.Controls.Add(content, 1, 0);
        SelectView(5);
        return root;
    }

    private Control CreateBrand()
    {
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 43));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        SetRows(brand, 26, 20);
        var picture = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 6) };
        picture.Paint += delegate(object sender, PaintEventArgs e)
        {
            int side = Math.Min(picture.Width, picture.Height) - 2;
            if (side < 8) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var tile = new Rectangle(1, (picture.Height - side) / 2, side, side);
            using (var shape = WorkspaceStyle.Rounded(tile, 9 * e.Graphics.DpiX / 96f))
            using (var fill = new SolidBrush(WorkspaceStyle.Mix(WorkspaceStyle.Rail, WorkspaceStyle.Highlight, 0.13f)))
            using (var line = new Pen(WorkspaceStyle.Line))
            {
                e.Graphics.FillPath(fill, shape);
                e.Graphics.DrawPath(line, shape);
            }
            if (appIcon != null)
            {
                var artwork = Rectangle.Inflate(tile, -3, -3);
                e.Graphics.DrawIcon(appIcon, artwork);
            }
        };
        brand.Controls.Add(picture, 0, 0);
        brand.SetRowSpan(picture, 2);
        brand.Controls.Add(Heading("Reze Zapret", 12, TextColor), 1, 0);
        brand.Controls.Add(Heading("Flowseal", 9, Muted), 1, 1);
        return brand;
    }

    private void SelectView(int index)
    {
        activeView = index;
        for (int i = 0; i < views.Length; i++)
        {
            views[i].Visible = i == index;
            navigation[i].Selected = i == index;
        }
        views[index].BringToFront();
        RefreshWorkspaceHeader();
        if (index == 5) RefreshDashboard();
        if (IsHandleCreated && ContainsFocus) navigation[index].Focus();
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Alt | Keys.D1: SelectView(0); return true;
            case Keys.Alt | Keys.D2: SelectView(1); return true;
            case Keys.Alt | Keys.D3: SelectView(2); return true;
            case Keys.Alt | Keys.D4: SelectView(3); return true;
            case Keys.Alt | Keys.D5: SelectView(4); return true;
            case Keys.Alt | Keys.D6: SelectView(5); return true;
            case Keys.Alt | Keys.D7: SelectView(6); return true;
            case Keys.F1: ShowGuide(); return true;
            case Keys.Control | Keys.K: ShowCommandPalette(); return true;
            case Keys.Control | Keys.F:
                if (activeView == 0 && profileSearch.Enabled) { profileSearch.Focus(); profileSearch.SelectAll(); return true; }
                if (activeView == 1 && domainSearch.Enabled) { domainSearch.Focus(); domainSearch.SelectAll(); return true; }
                break;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private Control CreateBanner()
    {
        var panel = new WorkspaceCard { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "Reze Zapret" };
        using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("Reze.Banner"))
            if (stream != null) using (Image source = Image.FromStream(stream)) banner = new Bitmap(source);
        panel.Paint += delegate(object sender, PaintEventArgs e)
        {
            if (panel.Width < 4 || panel.Height < 4) return;
            Graphics g = e.Graphics;
            float scale = g.DpiX / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = panel.ClientRectangle;
            int inset = (int)(13 * scale);
            int left = (int)(25 * scale);
            int middle = bounds.Height / 2;
            int textWidth = bounds.Width - left - inset;
            using (var shape = WorkspaceStyle.Rounded(new RectangleF(0.5f, 0.5f, bounds.Width - 1.5f, bounds.Height - 1.5f), 18 * scale))
            {
                GraphicsState state = g.Save();
                g.SetClip(shape);
                using (var fill = new LinearGradientBrush(bounds, Color.FromArgb(38, 41, 79), Color.FromArgb(14, 16, 33), 22f))
                    g.FillRectangle(fill, bounds);
                using (var ring = new Pen(Color.FromArgb(29, WorkspaceStyle.Highlight)))
                {
                    for (int diameter = 160; diameter <= 340; diameter += 45)
                    {
                        float size = diameter * scale;
                        g.DrawEllipse(ring, -size * 0.43f, bounds.Bottom - size * 0.45f, size, size);
                    }
                }
                if (banner != null && bounds.Width > 140 * scale && bounds.Height > inset * 2 + 2)
                {
                    int imageWidth = bounds.Width * 61 / 100;
                    float fit = Math.Min((imageWidth - inset) / (float)banner.Width, (bounds.Height - inset * 2) / (float)banner.Height);
                    var art = new Size(Math.Max(1, (int)(banner.Width * fit)), Math.Max(1, (int)(banner.Height * fit)));
                    var target = new Rectangle(bounds.Right - inset - art.Width, (bounds.Height - art.Height) / 2, art.Width, art.Height);
                    using (var frame = new Pen(Color.FromArgb(90, WorkspaceStyle.Highlight))) g.DrawRectangle(frame, Rectangle.Inflate(target, 1, 1));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(banner, target);
                    textWidth = target.Left - left - inset;
                }
                g.Restore(state);
                using (var pen = new Pen(WorkspaceStyle.Mix(Border, WorkspaceStyle.Highlight, 0.25f))) g.DrawPath(pen, shape);
            }
            bool small = bounds.Height < 155 * scale;
            using (var title = new Font("Segoe UI Semibold", small ? 23 : 29))
            using (var detail = new Font("Segoe UI", 10))
            using (var line = new SolidBrush(WorkspaceStyle.Highlight))
            {
                const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                g.FillRectangle(line, left, middle - (int)(39 * scale), (int)(27 * scale), Math.Max(1, (int)(2 * scale)));
                TextRenderer.DrawText(g, "Reze Zapret", title, new Rectangle(left, middle - (int)(25 * scale), Math.Max(0, textWidth), (int)(52 * scale)), TextColor, flags);
                TextRenderer.DrawText(g, "Панель управления Flowseal", detail, new Rectangle(left + 1, middle + (int)(26 * scale), Math.Max(0, textWidth), (int)(23 * scale)), AccentLight, flags);
            }
        };
        if (banner != null)
        {
            panel.Cursor = Cursors.Hand;
            workspaceHints.SetToolTip(panel, "Открыть иллюстрацию целиком");
            panel.Click += delegate { ViewArtwork(); };
        }
        return panel;
    }

    private void ViewArtwork()
    {
        using (var gallery = new Form { Text = "Reze Zapret · Иллюстрация", BackColor = Background,
            StartPosition = FormStartPosition.CenterParent, ClientSize = banner.Size, MinimumSize = new Size(360, 240),
            ShowInTaskbar = false, KeyPreview = true, Icon = appIcon })
        {
            gallery.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = banner, SizeMode = PictureBoxSizeMode.Zoom });
            gallery.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) gallery.Close(); };
            gallery.ShowDialog(this);
        }
    }

    private Control CreateProfiles()
    {
        var split = Split(54);
        var catalog = Card(split, 0, new Padding(0, 0, 7, 0));
        SetRows(catalog, 36, 42, -1, 34);
        catalog.Controls.Add(Heading("Профили", 16, TextColor), 0, 0);
        clearSearch = Action("Сбросить", delegate { profileSearch.Clear(); profileSearch.Focus(); });
        workspaceHints.SetToolTip(profileSearch, "Введите имя профиля или #тег для фильтрации");
        catalog.Controls.Add(SearchRow(profileSearch, SearchHint, clearSearch), 0, 1);
        profileSearch.TextChanged += delegate { FilterProfiles(); };
        ConfigureList(profiles, "Профили Flowseal");
        profiles.HorizontalScrollbar = false;
        profiles.DrawItem += DrawProfile;
        profiles.SelectedIndexChanged += delegate
        {
            if (filteringProfiles) return;
            if (profiles.SelectedItem != null) preferences.Profile = (string)profiles.SelectedItem;
            RefreshControls();
        };
        catalog.Controls.Add(profiles, 0, 2);
        favoritesOnly = (WorkspaceButton)Action("Избранные", delegate
        {
            favoritesOnly.Selected = !favoritesOnly.Selected;
            FilterProfiles();
        });
        var catalogFooter = Split(54);
        catalogFooter.Margin = new Padding(0, 6, 0, 0);
        catalogFooter.Controls.Add(profileCaption, 0, 0);
        favoritesOnly.Dock = DockStyle.Right;
        favoritesOnly.Margin = Padding.Empty;
        catalogFooter.Controls.Add(favoritesOnly, 1, 0);
        catalog.Controls.Add(catalogFooter, 0, 3);

        var details = Card(split, 1, new Padding(7, 0, 0, 0));
        SetRows(details, 26, 31, 42, 42, 27, 42, 52, 42, 42, -1);
        details.Controls.Add(Heading("ВЫБРАННЫЙ ПРОФИЛЬ", 8.5f, Accent), 0, 0);
        StyleHeading(selectedProfile, 15, TextColor);
        details.Controls.Add(selectedProfile, 0, 1);
        favorite = (WorkspaceButton)Action("В избранное", ToggleFavorite);
        favorite.MinimumSize = new Size(146, 32);
        compareProfile = Action("Сравнить с рабочим", ShowProfileDifference);
        compareProfile.MinimumSize = new Size(146, 32);
        validateProfile = Action("Проверить", ValidateSelectedProfile);
        validateProfile.MinimumSize = new Size(100, 32);
        details.Controls.Add(ButtonRow(favorite, compareProfile, validateProfile), 0, 2);
        start = Action("Запустить", StartProfile, true);
        start.MinimumSize = new Size(0, 32);
        stop = Action("Остановить", StopProfile);
        stop.MinimumSize = new Size(0, 32);
        details.Controls.Add(Pair(start, stop), 0, 3);
        StyleHeading(serviceStatus, 9, Muted);
        details.Controls.Add(serviceStatus, 0, 4);
        StyleHeading(profileHint, 9, Muted);
        details.Controls.Add(profileHint, 0, 5);
        var folder = Grid();
        SetRows(folder, 21, -1);
        folder.Controls.Add(Heading("ПАПКА FLOWSEAL", 8.5f, Accent), 0, 0);
        folderLabel.Text = "Выберите папку со сборкой";
        StyleHeading(folderLabel, 9, Muted);
        folder.Controls.Add(folderLabel, 0, 1);
        details.Controls.Add(folder, 0, 6);
        browse = Action("Выбрать папку", Browse);
        openFolder = Action("Открыть папку", delegate { Process.Start(preferences.Folder); });
        refresh = Action("Обновить", delegate { LoadFolder(preferences.Folder, true); });
        service = Action("Меню Flowseal", RunService);
        details.Controls.Add(Pair(browse, openFolder), 0, 7);
        details.Controls.Add(Pair(refresh, service), 0, 8);
        return split;
    }

    private Control CreateDomains()
    {
        var split = Split(45);
        var editor = Card(split, 0, new Padding(0, 0, 7, 0));
        SetRows(editor, 37, 34, 28, -1, 34, 42, 42, 42, 42);
        editor.Controls.Add(Heading("Добавить домены", 15, TextColor), 0, 0);
        includeMode = Action("Включения", delegate { SelectDomainMode(false); });
        excludeMode = Action("Исключения", delegate { SelectDomainMode(true); });
        includeMode.MinimumSize = excludeMode.MinimumSize = new Size(0, 25);
        includeMode.Selected = true;
        editor.Controls.Add(Pair(includeMode, excludeMode), 0, 1);
        editor.Controls.Add(Heading("Домен или ссылка, по одному на строку", 9, Muted), 0, 2);
        ConfigureText(addresses);
        addresses.AccessibleName = "URL или домены";
        addresses.TextChanged += delegate { UpdatePreview(); };
        addresses.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; if (add.Enabled) add.PerformClick(); }
        };
        editor.Controls.Add(addresses, 0, 3);
        StyleHeading(preview, 9, Muted);
        editor.Controls.Add(preview, 0, 4);
        add = Action("Добавить", AddDomains, true);
        exportDomains = Action("Экспорт", ExportDomains);
        importDomains = Action("Импорт", ShowDomainImport);
        pasteDomains = Action("Из буфера", ShowDomainClipboardImport);
        historyDomains = Action("История", ShowDomainHistory);
        undoDomains = Action("Отменить", UndoLatestDomains);
        checkDomain = Action("Проверить домен", CheckEnteredDomain);
        editor.Controls.Add(Pair(add, pasteDomains), 0, 5);
        editor.Controls.Add(Pair(importDomains, exportDomains), 0, 6);
        editor.Controls.Add(Pair(historyDomains, undoDomains), 0, 7);
        editor.Controls.Add(checkDomain, 0, 8);

        var saved = Card(split, 1, new Padding(7, 0, 0, 0));
        SetRows(saved, 37, 42, -1, 26, 42, 42);
        StyleHeading(domainListCaption, 15, TextColor);
        saved.Controls.Add(domainListCaption, 0, 0);
        saved.Controls.Add(SearchRow(domainSearch, "Поиск по доменам", null), 0, 1);
        domainSearch.TextChanged += delegate { ApplyDomainFilter(); };
        ConfigureList(entries, "Сохранённые домены");
        entries.SelectionMode = SelectionMode.MultiExtended;
        entries.DrawItem += DrawDomain;
        entries.SelectedIndexChanged += delegate { RefreshControls(); };
        saved.Controls.Add(entries, 0, 2);
        StyleHeading(domainsStatus, 9, Muted);
        saved.Controls.Add(domainsStatus, 0, 3);
        remove = Action("Удалить выбранные", RemoveDomains);
        copyDomains = Action("Копировать", CopyDomains);
        reload = Action("Перечитать", LoadEntries);
        restore = Action("Вернуть копию", RestoreDomains);
        saved.Controls.Add(Pair(remove, copyDomains), 0, 4);
        saved.Controls.Add(Pair(reload, restore), 0, 5);
        return split;
    }

    private Control CreateJournal()
    {
        var card = new WorkspaceCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16) };
        var body = Grid();
        body.BackColor = Surface;
        card.Controls.Add(body);
        SetRows(body, 43, 38, -1, 46);
        body.Controls.Add(Heading("Журнал действий", 16, TextColor), 0, 0);
        journalSearch = new WorkspaceTextBox();
        body.Controls.Add(SearchRow(journalSearch, "Поиск по журналу", null), 0, 1);
        journalSearch.TextChanged += delegate { journalFilter = journalSearch.Text.Trim(); RefreshJournalView(); };
        ConfigureText(journal);
        journal.Font = new Font("Consolas", 10);
        journal.ReadOnly = true;
        journal.AccessibleName = "Журнал действий";
        body.Controls.Add(journal, 0, 2);
        var actions = ButtonRow(
            Action("Сохранить .txt", ExportJournal, true),
            Action("Скопировать", delegate { if (journal.TextLength > 0) Clipboard.SetText(journal.Text); }),
            Action("Очистить", ClearJournal));
        body.Controls.Add(actions, 0, 3);
        return card;
    }

    private void ToggleFavorite()
    {
        string name = profiles.SelectedItem as string;
        if (name == null) return;
        if (!preferences.Favorites.Add(name)) preferences.Favorites.Remove(name);
        preferences.Profile = name;
        SavePreferences();
        FilterProfiles();
    }

    private void SelectDomainMode(bool exclude)
    {
        if (excludeDomains == exclude) return;
        excludeDomains = exclude;
        includeMode.Selected = !exclude;
        excludeMode.Selected = exclude;
        LoadEntries();
        UpdatePreview();
    }

    private void ApplyDomainFilter()
    {
        string term = domainSearch.Text.Trim();
        string[] visible = storedDomains.Where(value => value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        string[] selected = entries.SelectedItems.Cast<string>().ToArray();
        entries.BeginUpdate();
        try
        {
            entries.Items.Clear();
            entries.Items.AddRange(visible);
            foreach (string value in selected)
            {
                int index = entries.Items.IndexOf(value);
                if (index >= 0) entries.SetSelected(index, true);
            }
        }
        finally { entries.EndUpdate(); }
        RefreshListScrollBar(entries);
        domainListCaption.Text = term.Length > 0 ? "Найдено · " + visible.Length + " / " + storedDomains.Length :
            "Сохранённые · " + storedDomains.Length;
        RefreshControls();
    }

    private void CopyDomains()
    {
        string[] selected = entries.SelectedItems.Cast<string>().ToArray();
        if (selected.Length == 0) return;
        Clipboard.SetText(String.Join(Environment.NewLine, selected));
        Log("Скопировано доменов: " + selected.Length + ".");
    }

    private void ExportDomains()
    {
        if (storedDomains.Length == 0) return;
        using (var dialog = new SaveFileDialog { Filter = "Текстовый файл|*.txt", DefaultExt = "txt",
            FileName = excludeDomains ? "reze-exclusions.txt" : "reze-inclusions.txt" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (ProfileFiles.IsBundle(preferences.Folder) && new[] { false, true }.Select(exclude => ProfileFiles.ListPath(preferences.Folder, exclude))
                .Any(path => String.Equals(path, dialog.FileName, StringComparison.OrdinalIgnoreCase) ||
                             String.Equals(ProfileFiles.BackupPath(path), dialog.FileName, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Выберите другой файл для экспорта.");
            File.WriteAllLines(dialog.FileName, storedDomains, new System.Text.UTF8Encoding(false));
            Log("Экспортировано доменов: " + storedDomains.Length + ".");
        }
    }

    private void ExportJournal()
    {
        if (journal.TextLength == 0) return;
        using (var dialog = new SaveFileDialog { Filter = "Текстовый файл|*.txt", DefaultExt = "txt",
            FileName = "reze-zapret-" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt" })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK) WriteJournal(dialog.FileName);
        }
    }

    private void WriteJournal(string path)
    {
        File.WriteAllText(path, journal.Text, new System.Text.UTF8Encoding(true));
        Log("Журнал сохранён: " + Path.GetFileName(path));
    }

    private void DrawProfile(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= profiles.Items.Count) return;
        string name = (string)profiles.Items[e.Index];
        string[] tags;
        string label = ProfileTitle(name);
        if (preferences.ProfileTags.TryGetValue(name, out tags) && tags.Length > 0)
            label += "  · " + String.Join(" ", tags.Select(tag => "#" + tag).ToArray());
        DrawListRow(profiles, e, label, preferences.Favorites.Contains(name) ? "star-filled" : "profiles");
    }

    private void DrawDomain(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= entries.Items.Count) return;
        DrawListRow(entries, e, (string)entries.Items[e.Index], "domains");
    }

    private static void DrawListRow(ListBox list, DrawItemEventArgs e, string label, string glyph)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var workspaceList = list as WorkspaceListBox;
        bool hovered = workspaceList != null && workspaceList.HoveredIndex == e.Index;
        Rectangle row = Rectangle.Inflate(e.Bounds, -3, -2);
        float scale = e.Graphics.DpiX / 96f;
        if (row.Width < 40 || row.Height < 6) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var back = new SolidBrush(Input)) e.Graphics.FillRectangle(back, e.Bounds);
        using (var shape = WorkspaceStyle.Rounded(row, 7 * scale))
        using (var fill = new SolidBrush(selected ? WorkspaceStyle.Mix(Input, WorkspaceStyle.Highlight, 0.19f) : hovered ? WorkspaceStyle.Mix(Input, WorkspaceStyle.Highlight, 0.08f) : Input))
        {
            e.Graphics.FillPath(fill, shape);
            if (selected && (e.State & DrawItemState.Focus) != 0)
                using (var line = new Pen(WorkspaceStyle.Highlight)) e.Graphics.DrawPath(line, shape);
        }
        int iconSize = (int)(17 * scale);
        WorkspaceStyle.Glyph(e.Graphics, glyph, row.X + (int)(8 * scale), row.Y + (row.Height - iconSize) / 2,
            selected || hovered || glyph == "star-filled" ? WorkspaceStyle.Highlight : Muted, scale);
        int left = (int)(34 * scale);
        int rightInset = (int)(8 * scale);
        var workspace = list as WorkspaceListBox;
        if (workspace != null) rightInset += workspace.TextRightInset;
        TextRenderer.DrawText(e.Graphics, label, e.Font, new Rectangle(row.X + left, row.Y, Math.Max(0, row.Width - left - rightInset), row.Height),
            selected ? AccentLight : TextColor, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static string ProfileTitle(string file)
    {
        if (file == null) return "Профиль не выбран";
        return Path.GetFileNameWithoutExtension(file);
    }

    private void ValidateSelectedProfile()
    {
        string profile = profiles.SelectedItem as string;
        if (String.IsNullOrEmpty(profile)) return;
        string validation = ProfileFiles.ValidateProfile(preferences.Folder, profile);
        if (validation == null)
        {
            profileHint.Text = "Проверка пройдена · профиль готов к запуску.";
            Log("Проверен профиль " + profile + " · ошибок не найдено.");
            MessageBox.Show(this, "Профиль прошёл проверку и готов к запуску.\n\n" + profile, Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            profileHint.Text = "Проверка профиля · " + validation;
            Log("Проверка профиля " + profile + " · " + validation);
            MessageBox.Show(this, validation, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowProfileDifference()
    {
        string current = profiles.SelectedItem as string;
        string previous = preferences.LastWorkingProfile;
        if (String.IsNullOrEmpty(current) || !ProfileFiles.IsProfile(previous) || String.Equals(current, previous, StringComparison.OrdinalIgnoreCase)) return;
        string first = File.ReadAllText(Path.Combine(preferences.Folder, previous), Encoding.Default).Replace("\r\n", "\n").Replace('\r', '\n');
        string second = File.ReadAllText(Path.Combine(preferences.Folder, current), Encoding.Default).Replace("\r\n", "\n").Replace('\r', '\n');
        string[] before = first.Split('\n');
        string[] after = second.Split('\n');
        var lines = new List<string> { "Сравнение профилей", "Рабочий: " + ProfileTitle(previous), "Выбранный: " + ProfileTitle(current), "" };
        foreach (string line in before.Except(after).Take(80)) lines.Add("− " + line.TrimEnd());
        foreach (string line in after.Except(before).Take(80)) lines.Add("+ " + line.TrimEnd());
        if (lines.Count == 4) lines.Add("Различий не найдено.");
        using (var dialog = new Form { Text = "Различия профилей", StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(720, 500),
            MinimumSize = new Size(560, 360), BackColor = Background, ForeColor = TextColor, ShowInTaskbar = false, Icon = appIcon })
        {
            var box = new WorkspaceTextBox { Dock = DockStyle.Fill, ReadOnly = true, Text = String.Join(Environment.NewLine, lines.ToArray()), AccessibleName = "Различия профилей" };
            ConfigureText(box);
            dialog.Controls.Add(box);
            dialog.ShowDialog(this);
        }
    }

    private WorkspaceButton Action(string title, System.Action handler, bool emphasis = false)
    {
        var button = new WorkspaceButton { Text = title, Primary = emphasis, Font = new Font("Segoe UI Semibold", 9.5f),
            Margin = new Padding(0, 4, 7, 4), Padding = new Padding(9, 0, 9, 0),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(emphasis ? 115 : 94, 32) };
        button.Click += delegate { Guard(handler); };
        return button;
    }

    private LinkLabel TextLink(string title, string url)
    {
        var link = new LinkLabel { Dock = DockStyle.Fill, Text = title, LinkColor = Muted, ActiveLinkColor = AccentLight,
            Margin = new Padding(2, 4, 0, 0), Font = new Font("Segoe UI", 9), LinkBehavior = LinkBehavior.HoverUnderline };
        link.LinkClicked += delegate { Guard(delegate { Process.Start(url); }); };
        return link;
    }

    private static Control SearchRow(TextBox field, string hint, Button reset)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Input, Margin = new Padding(0, 2, 0, 7) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 33));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var icon = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        icon.Paint += delegate(object sender, PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            WorkspaceStyle.Glyph(e.Graphics, "search", (int)(9 * scale), (icon.Height - (int)(17 * scale)) / 2, Muted, scale);
        };
        row.Controls.Add(icon, 0, 0);
        ConfigureText(field);
        field.Multiline = false;
        field.ScrollBars = ScrollBars.None;
        field.BorderStyle = BorderStyle.None;
        field.AccessibleName = hint;
        field.Dock = DockStyle.Top;
        field.Margin = new Padding(0, 7, 6, 0);
        field.HandleCreated += delegate { SendMessage(field.Handle, SetCueBanner, new IntPtr(1), hint); };
        icon.Click += delegate { field.Focus(); };
        row.Controls.Add(field, 1, 0);
        if (reset != null)
        {
            reset.MinimumSize = new Size(78, 26);
            reset.Dock = DockStyle.Fill;
            reset.Margin = new Padding(0, 2, 2, 2);
            row.Controls.Add(reset, 2, 0);
        }
        return row;
    }

    private static void ConfigureList(ListBox list, string name)
    {
        list.Dock = DockStyle.Fill;
        list.Margin = Padding.Empty;
        list.AccessibleName = name;
        list.BackColor = Input;
        list.ForeColor = TextColor;
        list.IntegralHeight = false;
        list.BorderStyle = BorderStyle.None;
        list.Font = new Font("Segoe UI", 10);
        list.DrawMode = DrawMode.OwnerDrawFixed;
        list.ItemHeight = 31;
        list.HandleCreated += delegate
        {
            using (Graphics graphics = list.CreateGraphics()) list.ItemHeight = (int)Math.Round(31 * graphics.DpiY / 96f);
            var workspace = list as WorkspaceListBox;
            if (workspace != null) workspace.RefreshScrollBar();
        };
    }

    private static Label Heading(string text, float size, Color color)
    {
        var label = new Label { Text = text };
        StyleHeading(label, size, color);
        return label;
    }

    private static void StyleHeading(Label label, float size, Color color)
    {
        label.Dock = DockStyle.Fill;
        label.Margin = Padding.Empty;
        label.ForeColor = color;
        label.Font = new Font(size >= 14 || size < 9 ? "Segoe UI Semibold" : "Segoe UI", size);
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.AutoEllipsis = true;
        label.UseMnemonic = false;
    }

    private static TableLayoutPanel Split(float left)
    {
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, left));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - left));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return split;
    }

    private static TableLayoutPanel Card(TableLayoutPanel parent, int column, Padding margin)
    {
        var card = new WorkspaceCard { Dock = DockStyle.Fill, Margin = margin, Padding = new Padding(15) };
        var grid = Grid();
        grid.BackColor = Surface;
        card.Controls.Add(grid);
        parent.Controls.Add(card, column, 0);
        return grid;
    }

    private static void SetRows(TableLayoutPanel panel, params int[] heights)
    {
        panel.RowCount = heights.Length;
        foreach (int height in heights)
            panel.RowStyles.Add(height < 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
    }

    private static Control ButtonRow(params Button[] buttons)
    {
        var row = Row();
        row.BackColor = Surface;
        row.Controls.AddRange(buttons);
        return row;
    }

    private static Control Pair(Button first, Button second)
    {
        var row = Split(50);
        row.BackColor = Surface;
        first.Dock = second.Dock = DockStyle.Fill;
        first.Margin = new Padding(0, 4, 4, 4);
        second.Margin = new Padding(4, 4, 0, 4);
        row.Controls.Add(first, 0, 0);
        row.Controls.Add(second, 1, 0);
        return row;
    }
}
