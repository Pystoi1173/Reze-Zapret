using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private sealed class WorkspaceCommand
    {
        public string Title;
        public string Keywords;
        public Action Run;
        public override string ToString() { return Title; }
    }

    private Control CreateCompactHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        compactTitle = Heading("", 21, TextColor);
        header.Controls.Add(compactTitle, 0, 0);
        var commands = Action("Команды", ShowCommandPalette);
        commands.Glyph = "search";
        commands.KeyHint = "Ctrl+K";
        commands.Dock = DockStyle.Fill;
        commands.Margin = new Padding(0, 7, 0, 7);
        header.Controls.Add(commands, 1, 0);
        return header;
    }

    private void RefreshWorkspaceHeader()
    {
        if (workspaceBanner == null || compactHeader == null) return;
        float dpi = workspaceContent.Padding.Top / 18f;
        bool overview = activeView == 5;
        int height = workspaceContent.ClientSize.Height - workspaceContent.Padding.Vertical;
        float header = 54 * dpi;
        float footer = 35 * dpi;
        float room = height - header - footer - (overview ? 294 : 426) * dpi;
        float desired = (overview ? 220 : 150) * dpi * preferences.UiScale / 100f;
        float bannerHeight = Math.Max(0, Math.Min(room, desired));
        if ((!overview && preferences.CompactHeader) || bannerHeight < (overview ? 156 : 106) * dpi) bannerHeight = 0;
        workspaceContent.SuspendLayout();
        workspaceContent.RowStyles[0].Height = header;
        workspaceContent.RowStyles[1].Height = bannerHeight;
        workspaceContent.RowStyles[3].Height = footer;
        workspaceBanner.Visible = bannerHeight > 0;
        compactTitle.Text = ViewTitles[activeView];
        workspaceContent.ResumeLayout(true);
    }

    private void ApplyWorkspacePreferences()
    {
        Color oldAccent = Accent;
        Color oldAccentLight = AccentLight;
        WorkspaceStyle.SetAccent(preferences.AccentName);
        RefreshAccentColors(this, oldAccent, oldAccentLight);
        RefreshWorkspaceHeader();
        ApplyDensity(this);
        RefreshScrollBars(this);
        RefreshControls();
        RefreshDashboard();
        Invalidate(true);
    }

    private static void RefreshAccentColors(Control parent, Color oldAccent, Color oldAccentLight)
    {
        foreach (Control control in parent.Controls)
        {
            if (control.ForeColor == oldAccent) control.ForeColor = Accent;
            else if (control.ForeColor == oldAccentLight) control.ForeColor = AccentLight;
            var link = control as LinkLabel;
            if (link != null)
            {
                if (link.LinkColor == oldAccentLight) link.LinkColor = AccentLight;
                if (link.ActiveLinkColor == oldAccentLight) link.ActiveLinkColor = AccentLight;
            }
            var table = control as DataGridView;
            if (table != null)
            {
                table.DefaultCellStyle.SelectionForeColor = AccentLight;
                table.ColumnHeadersDefaultCellStyle.SelectionForeColor = AccentLight;
            }
            var button = control as WorkspaceButton;
            if (button != null) button.Invalidate();
            RefreshAccentColors(control, oldAccent, oldAccentLight);
        }
    }

    private static void RefreshListScrollBar(ListBox list)
    {
        var workspaceList = list as WorkspaceListBox;
        if (workspaceList != null) workspaceList.RefreshScrollBar();
    }

    private static void RefreshScrollBars(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            var list = control as WorkspaceListBox;
            if (list != null) list.RefreshScrollBar();
            RefreshScrollBars(control);
        }
    }

    private void ApplyDensity(Control parent)
    {
        int itemHeight = preferences.Density == "compact" ? 27 : preferences.Density == "spacious" ? 36 : 31;
        foreach (Control control in parent.Controls)
        {
            var list = control as ListBox;
            if (list != null && list.DrawMode == DrawMode.OwnerDrawFixed)
            {
                list.ItemHeight = (int)Math.Round(itemHeight * DeviceDpi / 96f);
                var workspace = list as WorkspaceListBox;
                if (workspace != null) workspace.RefreshScrollBar();
            }
            ApplyDensity(control);
        }
    }

    private void ShowCommandPalette()
    {
        var commands = new List<WorkspaceCommand>();
        for (int index = 0; index < ViewTitles.Length; index++)
        {
            int page = index;
            commands.Add(new WorkspaceCommand { Title = "Открыть «" + ViewTitles[page] + "»", Run = delegate { SelectView(page); } });
        }
        commands.Add(new WorkspaceCommand { Title = "Проверить текущее подключение", Keywords = "ALT Discord YouTube", Run = delegate { SelectView(3); RunCurrentAltFromDashboard(); } });
        commands.Add(new WorkspaceCommand { Title = "Экспортировать список доменов", Run = delegate { SelectView(1); ExportDomains(); } });
        commands.Add(new WorkspaceCommand { Title = "Открыть встроенный гид", Keywords = "справка помощь F1", Run = ShowGuide });
        commands.Add(new WorkspaceCommand { Title = "Посмотреть иллюстрацию", Keywords = "баннер арт", Run = ViewArtwork });
        foreach (string profile in availableProfiles)
        {
            string file = profile;
            commands.Add(new WorkspaceCommand { Title = "Профиль · " + ProfileTitle(file), Keywords = file, Run = delegate { PickProfile(file); } });
        }

        using (var palette = new Form { Text = "Reze Zapret · Команды", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(550, 440), MinimumSize = new Size(430, 300), BackColor = Background, ForeColor = TextColor,
            Icon = appIcon, ShowInTaskbar = false, KeyPreview = true, Padding = new Padding(18) })
        {
            var body = Grid();
            SetRows(body, 39, 42, -1, 34);
            var query = new WorkspaceTextBox();
            var results = new WorkspaceListBox();
            ConfigureList(results, "Команды");
            results.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index >= 0 && e.Index < results.Items.Count) DrawListRow(results, e, results.Items[e.Index].ToString(), "arrow");
            };
            body.Controls.Add(Heading("Быстрые команды", 17, TextColor), 0, 0);
            body.Controls.Add(SearchRow(query, "Найти команду или профиль", null), 0, 1);
            body.Controls.Add(results, 0, 2);
            body.Controls.Add(Heading("↑ / ↓  Выбор      Enter  Открыть      Esc  Закрыть", 9, Muted), 0, 3);
            palette.Controls.Add(body);
            WorkspaceCommand selected = null;
            Action choose = delegate
            {
                selected = results.SelectedItem as WorkspaceCommand;
                if (selected != null) palette.DialogResult = DialogResult.OK;
            };
            Action filter = delegate
            {
                string[] terms = query.Text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                results.BeginUpdate();
                try
                {
                    results.Items.Clear();
                    foreach (var command in commands.Where(item => terms.All(term =>
                        (item.Title + " " + item.Keywords).IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0)))
                        results.Items.Add(command);
                    if (results.Items.Count > 0) results.SelectedIndex = 0;
                }
                finally { results.EndUpdate(); }
                RefreshListScrollBar(results);
            };
            query.TextChanged += delegate { filter(); };
            results.DoubleClick += delegate { choose(); };
            palette.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) palette.Close();
                else if (e.KeyCode == Keys.Enter) choose();
                else if (query.Focused && results.Items.Count > 0 && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
                    results.SelectedIndex = Math.Max(0, Math.Min(results.Items.Count - 1, results.SelectedIndex + (e.KeyCode == Keys.Up ? -1 : 1)));
                else return;
                e.SuppressKeyPress = true;
            };
            palette.Shown += delegate { query.Focus(); };
            filter();
            if (palette.ShowDialog(this) == DialogResult.OK && selected != null) Guard(selected.Run);
        }
    }

    private void PickProfile(string name)
    {
        SelectView(0);
        if (!profiles.Enabled || !availableProfiles.Contains(name)) return;
        favoritesOnly.Selected = false;
        profileSearch.Clear();
        preferences.Profile = name;
        FilterProfiles();
        profiles.Focus();
        SavePreferences();
    }
}
