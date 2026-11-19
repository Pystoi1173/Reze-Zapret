using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class MainForm
{
    private async void CheckEnteredDomain()
    {
        string value = addresses.Lines.FirstOrDefault(line => !String.IsNullOrWhiteSpace(line));
        if (String.IsNullOrWhiteSpace(value))
        {
            domainsStatus.Text = "Введите домен для проверки.";
            return;
        }
        string host = AltLab.NormalizeHost(value);
        checkDomain.Enabled = false;
        domainsStatus.Text = "Проверка " + host + "…";
        try
        {
            AltLabSample sample = await AltLab.MeasureAsync(host, CancellationToken.None);
            domainsStatus.Text = host + " · " + sample.Describe();
            Log("Проверен домен " + host + " · " + sample.Describe() + ".");
        }
        finally
        {
            checkDomain.Enabled = true;
        }
    }

    private void ShowDomainImport()
    {
        if (!ProfileFiles.IsBundle(preferences.Folder)) return;
        using (var dialog = new OpenFileDialog
        {
            Filter = "Домены и CSV|*.txt;*.csv|Текстовые файлы|*.txt|CSV-файлы|*.csv|Все файлы|*.*",
            Multiselect = false,
            Title = "Импорт доменов"
        })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                string source = File.ReadAllText(dialog.FileName, new UTF8Encoding(false, true));
                DomainImportSource type = Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? DomainImportSource.Csv : DomainImportSource.Text;
                ShowDomainImportPreview(source, type, Path.GetFileName(dialog.FileName));
            }
        }
    }

    private void ShowDomainClipboardImport()
    {
        if (!ProfileFiles.IsBundle(preferences.Folder) || !Clipboard.ContainsText()) return;
        ShowDomainImportPreview(Clipboard.GetText(), DomainImportSource.Clipboard, "Буфер обмена");
    }

    private void ShowDomainImportPreview(string source, DomainImportSource sourceType, string title)
    {
        DomainImportPreview preview = DomainHistory.Preview(preferences.Folder, excludeDomains, source, sourceType);
        using (var dialog = new Form
        {
            Text = "Предпросмотр импорта · " + title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(760, 530),
            MinimumSize = new Size(620, 400),
            BackColor = Background,
            ForeColor = TextColor,
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.Sizable,
            Icon = appIcon
        })
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14), BackColor = Background };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            var summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Text = "Будет добавлено: " + preview.ValidCount + " · дубликаты: " + preview.DuplicateCount +
                    " · конфликты: " + preview.ConflictCount + " · ошибки: " + preview.InvalidCount };
            summary.Font = new Font("Segoe UI Semibold", 10);
            root.Controls.Add(summary, 0, 0);
            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                HideSelection = false, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
            rows.Columns.Add("Строка", 58);
            rows.Columns.Add("Значение", 290);
            rows.Columns.Add("Результат", 110);
            rows.Columns.Add("Подробности", 250);
            foreach (DomainImportRow row in preview.Rows)
            {
                string result = row.Status == DomainImportStatus.Valid ? "Добавить" :
                    row.Status == DomainImportStatus.Invalid ? "Ошибка" :
                    row.Status == DomainImportStatus.Duplicate ? "Дубликат" : "Конфликт";
                var item = new ListViewItem(row.Line.ToString());
                item.SubItems.Add(row.Value);
                item.SubItems.Add(result);
                item.SubItems.Add(row.Message);
                item.ForeColor = row.Status == DomainImportStatus.Valid ? Positive :
                    row.Status == DomainImportStatus.Invalid ? Accent : Muted;
                rows.Items.Add(item);
            }
            root.Controls.Add(rows, 0, 1);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var cancel = new Button { Text = "Закрыть", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(100, 30) };
            var apply = new Button { Text = "Импортировать " + preview.ValidCount, AutoSize = true, MinimumSize = new Size(150, 30),
                BackColor = Accent, ForeColor = Color.White, Enabled = preview.ValidCount > 0 };
            apply.Click += delegate
            {
                DomainChangeResult result = DomainHistory.Apply(preferences.Folder, excludeDomains, preview);
                if (!result.Changed)
                {
                    MessageBox.Show(dialog, "Изменений нет. Перечитайте список и повторите импорт.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (session != null) restartNeeded = true;
                LoadEntries();
                Log("Импортировано доменов: " + result.Count + ".");
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };
            footer.Controls.Add(cancel);
            footer.Controls.Add(apply);
            root.Controls.Add(footer, 0, 2);
            dialog.AcceptButton = apply;
            dialog.CancelButton = cancel;
            dialog.Controls.Add(root);
            dialog.ShowDialog(this);
        }
    }

    private void ShowDomainHistory()
    {
        if (!ProfileFiles.IsBundle(preferences.Folder)) return;
        DomainHistoryRecord[] records = DomainHistory.History(preferences.Folder);
        using (var dialog = new Form
        {
            Text = "История списков доменов",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(620, 390),
            MinimumSize = new Size(500, 300),
            BackColor = Background,
            ForeColor = TextColor,
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false,
            Icon = appIcon
        })
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14), BackColor = Background };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Изменения сохраняются для выбранной папки Flowseal.",
                ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
            list.Columns.Add("Время", 145);
            list.Columns.Add("Действие", 300);
            list.Columns.Add("Записей", 80);
            foreach (DomainHistoryRecord record in records.Reverse())
            {
                var item = new ListViewItem(record.Utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(record.Description);
                item.SubItems.Add(record.Count.ToString());
                list.Items.Add(item);
            }
            root.Controls.Add(list, 0, 1);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var close = new Button { Text = "Закрыть", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(100, 30) };
            var undo = new Button { Text = "Отменить последнее", AutoSize = true, MinimumSize = new Size(150, 30), Enabled = records.Length > 0 };
            undo.Click += delegate
            {
                if (!DomainHistory.UndoLatest(preferences.Folder))
                {
                    MessageBox.Show(dialog, "Последнее изменение уже устарело или отсутствует.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (session != null) restartNeeded = true;
                LoadEntries();
                Log("Отменено последнее изменение списков доменов.");
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };
            footer.Controls.Add(close);
            footer.Controls.Add(undo);
            root.Controls.Add(footer, 0, 2);
            dialog.CancelButton = close;
            dialog.Controls.Add(root);
            dialog.ShowDialog(this);
        }
    }

    private void UndoLatestDomains()
    {
        if (!ProfileFiles.IsBundle(preferences.Folder)) return;
        if (!DomainHistory.UndoLatest(preferences.Folder))
        {
            MessageBox.Show(this, "Последнее изменение уже устарело или отсутствует.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (session != null) restartNeeded = true;
        LoadEntries();
        Log("Отменено последнее изменение списков доменов.");
    }

    internal DomainListsSnapshot SnapshotPortableDomainLists()
    {
        return DomainHistory.Snapshot(preferences.Folder);
    }

    internal bool RestorePortableDomainLists(DomainListsSnapshot snapshot)
    {
        bool changed = DomainHistory.Restore(preferences.Folder, snapshot);
        if (changed)
        {
            if (session != null) restartNeeded = true;
            LoadEntries();
            Log("Восстановлены два списка доменов из снимка.");
        }
        return changed;
    }
}
