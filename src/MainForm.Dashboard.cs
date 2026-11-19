using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private Label dashboardService, dashboardProfile, dashboardVersion, dashboardTest, dashboardFolder, dashboardStrategy;

    private Control CreateDashboard()
    {
        var body = Grid();
        SetRows(body, 38, 100, 12, 146, -1);
        var section = Heading("Состояние системы", 14, TextColor);
        section.Margin = new Padding(0, 6, 0, 0);
        body.Controls.Add(section, 0, 0);

        dashboardService = DashboardValue();
        dashboardProfile = DashboardValue();
        dashboardVersion = DashboardValue();
        dashboardTest = DashboardValue();
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        for (int index = 0; index < 4; index++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        AddDashboardMetric(cards, 0, "Служба", "tests", dashboardService);
        AddDashboardMetric(cards, 1, "Профиль", "profiles", dashboardProfile);
        AddDashboardMetric(cards, 2, "Flowseal", "updates", dashboardVersion);
        AddDashboardMetric(cards, 3, "Последний тест", "domains", dashboardTest);
        body.Controls.Add(cards, 0, 1);

        var lower = Split(51);
        var info = Card(lower, 0, new Padding(0, 0, 6, 0));
        SetRows(info, 23, 33, -1);
        info.Controls.Add(DashboardCaption("Выбранная папка", "folder"), 0, 0);
        dashboardFolder = DashboardValue();
        info.Controls.Add(dashboardFolder, 0, 1);
        dashboardStrategy = Heading("", 9.5f, Muted);
        info.Controls.Add(dashboardStrategy, 0, 2);

        var actions = Card(lower, 1, new Padding(6, 0, 0, 0));
        SetRows(actions, 23, 41, 41, -1);
        actions.Controls.Add(DashboardCaption("Быстрые действия", "play"), 0, 0);
        var profilesAction = Action("Открыть профили", delegate { SelectView(0); }, true);
        profilesAction.Glyph = "profiles";
        var testAction = Action("Проверить подключение", delegate { SelectView(3); RunCurrentAltFromDashboard(); });
        testAction.Glyph = "tests";
        profilesAction.Dock = testAction.Dock = DockStyle.Fill;
        profilesAction.Margin = testAction.Margin = new Padding(0, 4, 0, 3);
        actions.Controls.Add(profilesAction, 0, 1);
        actions.Controls.Add(testAction, 0, 2);
        body.Controls.Add(lower, 0, 3);
        RefreshDashboard();
        return body;
    }

    private static void AddDashboardMetric(TableLayoutPanel host, int column, string caption, string glyph, Label value)
    {
        var grid = Card(host, column, new Padding(column == 0 ? 0 : 4, 0, column == 3 ? 0 : 4, 0));
        SetRows(grid, 24, -1);
        grid.Controls.Add(DashboardCaption(caption, glyph), 0, 0);
        grid.Controls.Add(value, 0, 1);
    }

    private static Control DashboardCaption(string caption, string glyph)
    {
        var title = Heading(caption, 9, Muted);
        title.Padding = new Padding(23, 0, 0, 0);
        title.Paint += delegate(object sender, PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96f;
            WorkspaceStyle.Glyph(e.Graphics, glyph, 0, Math.Max(0, (title.Height - (int)(16 * scale)) / 2), WorkspaceStyle.Highlight, scale);
        };
        return title;
    }

    private void RunCurrentAltFromDashboard()
    {
        if (AltTestsRunning) return;
        RunAltTests(false);
    }

    private static Label DashboardValue()
    {
        return Heading("", 12.5f, TextColor);
    }

    private void RefreshDashboard()
    {
        if (dashboardService == null) return;
        dashboardService.Text = serviceRunning ? "Работает" : "Остановлена";
        dashboardService.ForeColor = serviceRunning ? Positive : Muted;
        string current = profiles.SelectedItem as string ?? preferences.Profile;
        dashboardProfile.Text = String.IsNullOrEmpty(current) ? "Не выбран" : ProfileTitle(current);
        dashboardVersion.Text = selectedFlowsealVersion == null ? "—" : selectedFlowsealVersion.ToString();
        dashboardTest.Text = String.IsNullOrEmpty(AltLastSummary) ? "Ещё не было" : AltLastSummary;
        string folder = preferences.Folder;
        dashboardFolder.Text = String.IsNullOrEmpty(folder) ? "Flowseal не выбран" : Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (String.IsNullOrEmpty(dashboardFolder.Text)) dashboardFolder.Text = folder;
        dashboardStrategy.Text = serviceRunning && !String.IsNullOrWhiteSpace(serviceStrategy) ? "Стратегия службы: " + serviceStrategy :
            FlowsealServiceVersion != null ? "Версия службы: " + FlowsealServiceVersion : "Папку можно изменить в разделе «Профили».";
        SetDashboardHint(dashboardFolder, folder);
        SetDashboardHint(dashboardTest, AltLastSummary);
        SetDashboardHint(dashboardProfile, dashboardProfile.Text);
    }

    private void SetDashboardHint(Control control, string text)
    {
        if (workspaceHints.GetToolTip(control) != (text ?? String.Empty)) workspaceHints.SetToolTip(control, text);
    }
}
