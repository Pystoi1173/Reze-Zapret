using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal enum DomainImportSource
{
    Text,
    Csv,
    Clipboard
}

internal enum DomainImportStatus
{
    Valid,
    Invalid,
    Duplicate,
    Conflict
}

internal sealed class DomainListsSnapshot
{
    public readonly byte[] Inclusions;
    public readonly byte[] Exclusions;

    public DomainListsSnapshot(byte[] inclusions, byte[] exclusions)
    {
        Inclusions = EmptyAsNull(inclusions);
        Exclusions = EmptyAsNull(exclusions);
    }

    public bool Equivalent(DomainListsSnapshot other)
    {
        return other != null && Same(Inclusions, other.Inclusions) && Same(Exclusions, other.Exclusions);
    }

    private static byte[] EmptyAsNull(byte[] value)
    {
        return value == null || value.Length == 0 ? null : (byte[])value.Clone();
    }

    private static bool Same(byte[] first, byte[] second)
    {
        return first == null ? second == null : second != null && first.SequenceEqual(second);
    }
}

internal sealed class DomainImportRow
{
    public readonly int Line;
    public readonly string Value;
    public readonly string Normalized;
    public readonly DomainImportStatus Status;
    public readonly string Message;

    internal DomainImportRow(int line, string value, string normalized, DomainImportStatus status, string message)
    {
        Line = line;
        Value = value;
        Normalized = normalized;
        Status = status;
        Message = message;
    }
}

internal sealed class DomainImportPreview
{
    internal readonly string Folder;
    internal readonly bool Exclude;
    internal readonly DomainListsSnapshot Current;
    public readonly DomainImportSource SourceType;
    public readonly DomainImportRow[] Rows;
    private readonly string[] validEntries;
    public string[] ValidEntries { get { return (string[])validEntries.Clone(); } }

    internal DomainImportPreview(string folder, bool exclude, DomainImportSource sourceType,
        DomainImportRow[] rows, string[] validEntries, DomainListsSnapshot current)
    {
        Folder = folder;
        Exclude = exclude;
        SourceType = sourceType;
        Rows = rows;
        this.validEntries = validEntries;
        Current = current;
    }

    public int ValidCount { get { return validEntries.Length; } }
    public int InvalidCount { get { return Rows.Count(row => row.Status == DomainImportStatus.Invalid); } }
    public int DuplicateCount { get { return Rows.Count(row => row.Status == DomainImportStatus.Duplicate); } }
    public int ConflictCount { get { return Rows.Count(row => row.Status == DomainImportStatus.Conflict); } }
}

internal sealed class DomainChangeResult
{
    public readonly bool Changed;
    public readonly int Count;
    public readonly DomainListsSnapshot Before;
    public readonly DomainListsSnapshot After;

    internal DomainChangeResult(bool changed, int count, DomainListsSnapshot before, DomainListsSnapshot after)
    {
        Changed = changed;
        Count = count;
        Before = before;
        After = after;
    }
}

internal sealed class DomainHistoryRecord
{
    public readonly DateTime Utc;
    public readonly string Description;
    public readonly int Count;

    internal DomainHistoryRecord(DateTime utc, string description, int count)
    {
        Utc = utc;
        Description = description;
        Count = count;
    }
}

internal static class DomainHistory
{
    private const string HistoryName = ".reze-domain-history.bin";
    private const string HistoryBackupName = ".reze-domain-history.bin.bak";
    private const string Magic = "REZE-DOMAIN-HISTORY-1";
    private const int MaximumRecords = 40;
    private static readonly object SyncRoot = new object();
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static DomainListsSnapshot Snapshot(string folder)
    {
        if (!ProfileFiles.IsBundle(folder)) throw new DirectoryNotFoundException("Выберите распакованную папку Flowseal.");
        return ReadSnapshot(folder);
    }

    public static DomainImportPreview Preview(string folder, bool exclude, string source, DomainImportSource sourceType)
    {
        if (!ProfileFiles.IsBundle(folder)) throw new DirectoryNotFoundException("Выберите распакованную папку Flowseal.");
        if (source == null) source = String.Empty;
        string fullFolder = Path.GetFullPath(folder);
        DomainListsSnapshot current = ReadSnapshot(fullFolder);
        string targetPath = ProfileFiles.ListPath(fullFolder, exclude);
        string oppositePath = ProfileFiles.ListPath(fullFolder, !exclude);
        string[] currentEntries = ProfileFiles.Entries(targetPath);
        string[] oppositeEntries = ProfileFiles.Entries(oppositePath);
        var currentSet = new HashSet<string>(currentEntries, StringComparer.OrdinalIgnoreCase);
        var oppositeSet = new HashSet<string>(oppositeEntries, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<DomainImportRow>();
        var valid = new List<string>();
        IEnumerable<ImportToken> tokens = sourceType == DomainImportSource.Csv ? ParseCsv(source) : ParseText(source);

        foreach (ImportToken token in tokens)
        {
            string value = token.Value == null ? String.Empty : token.Value.Trim();
            if (value.Length == 0 || (sourceType != DomainImportSource.Csv && value.StartsWith("#", StringComparison.Ordinal))) continue;
            try
            {
                string normalized = ProfileFiles.Normalize(value);
                if (oppositeSet.Contains(normalized))
                {
                    rows.Add(new DomainImportRow(token.Line, value, normalized, DomainImportStatus.Conflict,
                        "Домен уже есть в другом списке."));
                }
                else if (currentSet.Contains(normalized) || !seen.Add(normalized))
                {
                    rows.Add(new DomainImportRow(token.Line, value, normalized, DomainImportStatus.Duplicate,
                        currentSet.Contains(normalized) ? "Домен уже есть в выбранном списке." : "Повтор в импортируемых данных."));
                }
                else
                {
                    valid.Add(normalized);
                    rows.Add(new DomainImportRow(token.Line, value, normalized, DomainImportStatus.Valid, "Будет добавлен."));
                }
            }
            catch (FormatException error)
            {
                rows.Add(new DomainImportRow(token.Line, value, null, DomainImportStatus.Invalid, error.Message));
            }
        }
        return new DomainImportPreview(fullFolder, exclude, sourceType, rows.ToArray(),
            valid.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), current);
    }

    public static DomainChangeResult Apply(string folder, bool exclude, DomainImportPreview preview)
    {
        if (preview == null) throw new ArgumentNullException("preview");
        if (preview.Exclude != exclude || !String.Equals(Path.GetFullPath(folder), preview.Folder, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Предпросмотр относится к другой папке или списку.");
        lock (SyncRoot)
        {
            DomainListsSnapshot before = ReadSnapshot(folder);
            if (!before.Equivalent(preview.Current))
                throw new InvalidOperationException("Список изменился после предпросмотра. Перечитайте данные и повторите импорт.");
            if (preview.ValidEntries.Length == 0)
                return new DomainChangeResult(false, 0, before, before);
            string path = ProfileFiles.ListPath(folder, exclude);
            int count = ProfileFiles.Update(path, preview.ValidEntries, false);
            DomainListsSnapshot after = ReadSnapshot(folder);
            if (count == 0 || before.Equivalent(after)) return new DomainChangeResult(false, 0, before, after);
            Record(folder, before, after, "Импорт доменов", count);
            return new DomainChangeResult(true, count, before, after);
        }
    }

    public static DomainChangeResult Update(string folder, bool exclude, IEnumerable<string> values, bool deleting)
    {
        if (values == null) throw new ArgumentNullException("values");
        string[] normalized = values.Select(ProfileFiles.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        lock (SyncRoot)
        {
            DomainListsSnapshot before = ReadSnapshot(folder);
            int count = ProfileFiles.Update(ProfileFiles.ListPath(folder, exclude), normalized, deleting);
            DomainListsSnapshot after = ReadSnapshot(folder);
            if (count == 0 || before.Equivalent(after)) return new DomainChangeResult(false, 0, before, after);
            Record(folder, before, after, deleting ? "Удаление доменов" : "Добавление доменов", count);
            return new DomainChangeResult(true, count, before, after);
        }
    }

    public static DomainChangeResult RestoreBackup(string folder, bool exclude)
    {
        lock (SyncRoot)
        {
            DomainListsSnapshot before = ReadSnapshot(folder);
            ProfileFiles.RestoreBackup(ProfileFiles.ListPath(folder, exclude));
            DomainListsSnapshot after = ReadSnapshot(folder);
            if (before.Equivalent(after)) return new DomainChangeResult(false, 0, before, after);
            Record(folder, before, after, "Восстановление копии списка", 1);
            return new DomainChangeResult(true, 1, before, after);
        }
    }

    public static bool Restore(string folder, DomainListsSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException("snapshot");
        lock (SyncRoot)
        {
            DomainListsSnapshot before = ReadSnapshot(folder);
            if (before.Equivalent(snapshot)) return false;
            WriteSnapshot(folder, snapshot);
            DomainListsSnapshot after = ReadSnapshot(folder);
            Record(folder, before, after, "Восстановление двух списков", 2);
            return true;
        }
    }

    public static bool UndoLatest(string folder)
    {
        lock (SyncRoot)
        {
            List<StoredRecord> records = Load(folder);
            if (records.Count == 0) return false;
            StoredRecord latest = records[records.Count - 1];
            DomainListsSnapshot current = ReadSnapshot(folder);
            if (!current.Equivalent(latest.After)) return false;
            WriteSnapshot(folder, latest.Before);
            records.RemoveAt(records.Count - 1);
            Save(folder, records);
            return true;
        }
    }

    public static DomainHistoryRecord[] History(string folder)
    {
        lock (SyncRoot)
        {
            return Load(folder).Select(record => new DomainHistoryRecord(record.Utc, record.Description, record.Count)).ToArray();
        }
    }

    private static DomainListsSnapshot ReadSnapshot(string folder)
    {
        return new DomainListsSnapshot(ReadRaw(ProfileFiles.ListPath(folder, false)), ReadRaw(ProfileFiles.ListPath(folder, true)));
    }

    private static byte[] ReadRaw(string path)
    {
        if (!File.Exists(path)) return null;
        byte[] value = File.ReadAllBytes(path);
        return value.Length == 0 ? null : value;
    }

    private static void WriteSnapshot(string folder, DomainListsSnapshot snapshot)
    {
        WriteRaw(ProfileFiles.ListPath(folder, false), snapshot.Inclusions);
        WriteRaw(ProfileFiles.ListPath(folder, true), snapshot.Exclusions);
    }

    private static void WriteRaw(string path, byte[] value)
    {
        bool existed = File.Exists(path);
        if (value == null)
        {
            if (!existed) return;
            string empty = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(empty, new byte[0]);
                File.Replace(empty, path, ProfileFiles.BackupPath(path));
                File.Delete(path);
            }
            finally { if (File.Exists(empty)) File.Delete(empty); }
            return;
        }
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, value);
            if (existed) File.Replace(temporary, path, ProfileFiles.BackupPath(path));
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Record(string folder, DomainListsSnapshot before, DomainListsSnapshot after, string description, int count)
    {
        if (before.Equivalent(after)) return;
        List<StoredRecord> records = Load(folder);
        records.Add(new StoredRecord(DateTime.UtcNow, description, count, before, after));
        while (records.Count > MaximumRecords) records.RemoveAt(0);
        Save(folder, records);
    }

    private static List<StoredRecord> Load(string folder)
    {
        string path = Path.Combine(folder, HistoryName);
        if (!File.Exists(path)) return new List<StoredRecord>();
        try
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Utf8))
            {
                if (reader.ReadString() != Magic) return new List<StoredRecord>();
                int count = reader.ReadInt32();
                if (count < 0 || count > MaximumRecords) return new List<StoredRecord>();
                var result = new List<StoredRecord>(count);
                for (int index = 0; index < count; index++)
                {
                    DateTime utc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
                    string description = reader.ReadString();
                    int changeCount = reader.ReadInt32();
                    DomainListsSnapshot before = ReadSnapshot(reader);
                    DomainListsSnapshot after = ReadSnapshot(reader);
                    result.Add(new StoredRecord(utc, description, changeCount, before, after));
                }
                return result;
            }
        }
        catch (Exception error)
        {
            if (error is IOException || error is UnauthorizedAccessException || error is EndOfStreamException ||
                error is InvalidDataException || error is ArgumentException) return new List<StoredRecord>();
            throw;
        }
    }

    private static void Save(string folder, List<StoredRecord> records)
    {
        string path = Path.Combine(folder, HistoryName);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var writer = new BinaryWriter(stream, Utf8))
            {
                writer.Write(Magic);
                writer.Write(records.Count);
                foreach (StoredRecord record in records)
                {
                    writer.Write(record.Utc.Ticks);
                    writer.Write(record.Description ?? String.Empty);
                    writer.Write(record.Count);
                    WriteSnapshot(writer, record.Before);
                    WriteSnapshot(writer, record.After);
                }
            }
            if (File.Exists(path)) File.Replace(temporary, path, Path.Combine(folder, HistoryBackupName));
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static DomainListsSnapshot ReadSnapshot(BinaryReader reader)
    {
        return new DomainListsSnapshot(ReadBlob(reader), ReadBlob(reader));
    }

    private static void WriteSnapshot(BinaryWriter writer, DomainListsSnapshot snapshot)
    {
        WriteBlob(writer, snapshot.Inclusions);
        WriteBlob(writer, snapshot.Exclusions);
    }

    private static byte[] ReadBlob(BinaryReader reader)
    {
        if (!reader.ReadBoolean()) return null;
        int length = reader.ReadInt32();
        if (length < 0 || length > 32 * 1024 * 1024) throw new InvalidDataException("История доменов повреждена.");
        return reader.ReadBytes(length);
    }

    private static void WriteBlob(BinaryWriter writer, byte[] value)
    {
        writer.Write(value != null);
        if (value != null)
        {
            writer.Write(value.Length);
            writer.Write(value);
        }
    }

    private static IEnumerable<ImportToken> ParseText(string source)
    {
        string[] lines = (source ?? String.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int index = 0; index < lines.Length; index++) yield return new ImportToken(index + 1, lines[index]);
    }

    private static IEnumerable<ImportToken> ParseCsv(string source)
    {
        var result = new List<ImportToken>();
        var value = new StringBuilder();
        bool quoted = false;
        bool quoteClosed = false;
        int line = 1;
        int startLine = 1;
        string input = source ?? String.Empty;
        for (int index = 0; index < input.Length; index++)
        {
            char current = input[index];
            if (quoted)
            {
                if (current == '"')
                {
                    if (index + 1 < input.Length && input[index + 1] == '"') { value.Append('"'); index++; }
                    else { quoted = false; quoteClosed = true; }
                }
                else
                {
                    value.Append(current);
                    if (current == '\n') line++;
                }
                continue;
            }
            if (current == '"' && value.Length == 0 && !quoteClosed) { quoted = true; continue; }
            if (current == ',')
            {
                result.Add(new ImportToken(startLine, value.ToString()));
                value.Length = 0;
                quoteClosed = false;
                startLine = line;
                continue;
            }
            if (current == '\r' || current == '\n')
            {
                result.Add(new ImportToken(startLine, value.ToString()));
                value.Length = 0;
                quoteClosed = false;
                if (current == '\r' && index + 1 < input.Length && input[index + 1] == '\n') index++;
                line++;
                startLine = line;
                continue;
            }
            value.Append(current);
        }
        if (value.Length > 0 || quoteClosed || input.EndsWith(",", StringComparison.Ordinal))
            result.Add(new ImportToken(startLine, value.ToString()));
        return result;
    }

    private sealed class ImportToken
    {
        internal readonly int Line;
        internal readonly string Value;

        internal ImportToken(int line, string value)
        {
            Line = line;
            Value = value;
        }
    }

    private sealed class StoredRecord
    {
        internal readonly DateTime Utc;
        internal readonly string Description;
        internal readonly int Count;
        internal readonly DomainListsSnapshot Before;
        internal readonly DomainListsSnapshot After;

        internal StoredRecord(DateTime utc, string description, int count, DomainListsSnapshot before, DomainListsSnapshot after)
        {
            Utc = utc;
            Description = description;
            Count = count;
            Before = before;
            After = after;
        }

        internal StoredRecord(DateTime utc, string description, int count, DomainListsSnapshot before)
            : this(utc, description, count, before, null) { }
    }
}
