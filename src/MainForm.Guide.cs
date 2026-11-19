using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private void ShowGuide()
    {
        int page = -1;
        using (var guide = new Form { Text = "Reze Zapret · Быстрый гид", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(760, 560), MinimumSize = new Size(700, 550), BackColor = Background,
            ForeColor = TextColor, Icon = appIcon, KeyPreview = true, ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi, MinimizeBox = false, MaximizeBox = false })
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24), BackColor = Background };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(Heading("Как пользоваться Reze Zapret", 18, TextColor), 0, 0);
            root.Controls.Add(Heading("Откройте нужный раздел из карточки. Этот гид всегда доступен по F1.", 9, Muted), 0, 1);
            var steps = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Background, Margin = Padding.Empty };
            steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            steps.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            steps.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Action<int> navigate = delegate(int target) { page = target; guide.DialogResult = DialogResult.OK; };
            AddGuideCard(steps, 0, 0, "Первый запуск", "Через «Выбрать папку» укажите распакованную сборку Flowseal и выберите профиль. Запущенной службой можно управлять через «Меню Flowseal».", "Открыть профили", delegate { navigate(0); });
            AddGuideCard(steps, 1, 0, "Проверка подключения", "Нажмите «Проверить текущее». Для сравнения с базой нужны права администратора, остановленная служба и завершённый сеанс.", "Открыть тесты ALT", delegate { navigate(3); });
            AddGuideCard(steps, 0, 1, "Свои домены", "Выберите «Включения» или «Исключения». «Импорт» покажет адреса до записи, а «Отменить» вернёт состояние до последнего изменения.", "Открыть домены", delegate { navigate(1); });
            AddGuideCard(steps, 1, 1, "Обновление и возврат", "Нажмите «Проверить GitHub». Новая и предыдущие папки доступны в списке локальных сборок. Службу можно переключить через «Меню Flowseal».", "Открыть обновления", delegate { navigate(4); });
            root.Controls.Add(steps, 0, 2);
            var close = Action("Понятно", delegate { guide.Close(); }, true);
            close.Dock = DockStyle.Right;
            close.MinimumSize = new Size(110, 32);
            root.Controls.Add(close, 0, 3);
            guide.Controls.Add(root);
            guide.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F1) guide.Close(); };
            if (guide.ShowDialog(this) == DialogResult.OK && page >= 0) SelectView(page);
        }
    }

    private void AddGuideCard(TableLayoutPanel host, int column, int row, string title, string text, string action, Action handler)
    {
        var card = new WorkspaceCard { Dock = DockStyle.Fill, Margin = new Padding(column == 0 ? 0 : 7, row == 0 ? 0 : 7, column == 1 ? 0 : 7, row == 1 ? 0 : 7), Padding = new Padding(14) };
        var grid = Grid();
        grid.BackColor = Surface;
        SetRows(grid, 28, -1, 36);
        grid.Controls.Add(Heading(title, 11, AccentLight), 0, 0);
        grid.Controls.Add(Heading(text, 9, Muted), 0, 1);
        var button = Action(action, handler, true);
        button.Dock = DockStyle.Left;
        button.MinimumSize = new Size(142, 30);
        grid.Controls.Add(button, 0, 2);
        card.Controls.Add(grid);
        host.Controls.Add(card, column, row);
    }
}
