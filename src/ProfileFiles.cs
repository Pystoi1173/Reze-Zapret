using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

internal static class ProfileFiles
{
    private static readonly Regex ProfileName = new Regex(@"^general(?: \([A-Za-z0-9 _.-]+\))?\.bat\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Label = new Regex(@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
    private const string Placeholder = "domain.example.abc";

    public static bool IsProfile(string name) { return name != null && ProfileName.IsMatch(name); }

    public static bool IsBundle(string root)
    {
        return !String.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, "service.bat")) &&
               Directory.Exists(Path.Combine(root, "lists")) && File.Exists(Path.Combine(root, "bin", "winws.exe"));
    }

    public static string[] Profiles(string root)
    {
        if (!IsBundle(root)) return new string[0];
        return Directory.EnumerateFiles(root, "*.bat").Select(Path.GetFileName).Where(IsProfile)
            .OrderBy(name => name.Equals("general.bat", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string ValidateProfile(string root, string profile)
    {
        if (!IsBundle(root)) return "Папка Flowseal не содержит полной сборки.";
        if (!IsProfile(profile)) return "Имя профиля не поддерживается.";
        string path = Path.Combine(root, profile);
        if (!File.Exists(path)) return "Файл профиля не найден.";
        FileInfo file = new FileInfo(path);
        if (file.Length == 0) return "Файл профиля пуст.";
        if (file.Length > 1024 * 1024) return "Файл профиля слишком большой.";
        string text = File.ReadAllText(path, Encoding.Default);
        if (text.IndexOf("winws", StringComparison.OrdinalIgnoreCase) < 0)
            return "В профиле не найден запуск winws.exe.";
        return null;
    }

    public static string Normalize(string input)
    {
        string candidate = input.Trim();
        if (candidate.Length == 0 || candidate.Any(Char.IsWhiteSpace) || candidate.IndexOf('\\') >= 0)
            throw new FormatException("Введите домен или адрес HTTP/HTTPS целиком.");
        Uri address;
        if (!Uri.TryCreate(candidate.Contains("://") ? candidate : "https://" + candidate, UriKind.Absolute, out address) ||
            (address.Scheme != "https" && address.Scheme != "http") || address.UserInfo.Length != 0 || address.HostNameType != UriHostNameType.Dns)
            throw new FormatException("Нужен домен сайта. IP-адреса, маски и адреса с логином здесь не поддерживаются.");
        string host;
        try { host = new IdnMapping().GetAscii(address.IdnHost.TrimEnd('.')).ToLowerInvariant(); }
        catch (ArgumentException) { throw new FormatException("Проверьте написание домена."); }
        string[] labels = host.Split('.');
        if (host.Length > 253 || labels.Length < 2 || labels.Any(part => !Label.IsMatch(part)) || labels.Last().All(Char.IsDigit) || host == Placeholder)
            throw new FormatException("Проверьте написание домена. Пример: example.org.");
        return host;
    }

    public static string ListPath(string root, bool exclude)
    {
        if (!IsBundle(root)) throw new DirectoryNotFoundException("Выберите распакованную папку Flowseal.");
        return Path.Combine(root, "lists", exclude ? "list-exclude-user.txt" : "list-general-user.txt");
    }

    public static string[] Entries(string path)
    {
        return ReadLines(path).Select(Entry).Where(value => value != null && value != Placeholder)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static int Update(string path, IEnumerable<string> entries, bool remove)
    {
        bool existed = File.Exists(path);
        var lines = ReadLines(path).ToList();
        var desired = new HashSet<string>(entries, StringComparer.OrdinalIgnoreCase);
        var existing = new HashSet<string>(lines.Select(Entry).Where(value => value != null), StringComparer.OrdinalIgnoreCase);
        int changes;
        if (remove)
        {
            changes = desired.Count(existing.Contains);
            lines.RemoveAll(line => desired.Contains(Entry(line) ?? ""));
        }
        else
        {
            var added = desired.Where(value => !existing.Contains(value)).OrderBy(value => value).ToArray();
            changes = added.Length;
            lines.AddRange(added);
        }
        if (changes == 0) return 0;
        if (!lines.Any(line => { string value = line.Trim(); return value.Length != 0 && !value.StartsWith("#", StringComparison.Ordinal); }))
            lines.Add(Placeholder);
        WriteList(path, existed, temporary => File.WriteAllLines(temporary, lines, Utf8));
        return changes;
    }

    public static string BackupPath(string path) { return path + ".reze.bak"; }

    public static void RestoreBackup(string path)
    {
        string backup = BackupPath(path);
        WriteList(path, File.Exists(path), temporary => File.Copy(backup, temporary));
    }

    private static void WriteList(string path, bool existed, Action<string> stage)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            stage(temporary);
            if (existed) File.Replace(temporary, path, BackupPath(path));
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string[] ReadLines(string path)
    {
        return File.Exists(path) ? File.ReadAllLines(path, Utf8) : new string[0];
    }

    private static string Entry(string line)
    {
        line = line.Trim();
        if (line == Placeholder) return line;
        if (line.Any(c => Char.IsWhiteSpace(c) || "/:@?#".Contains(c))) return null;
        try { return Normalize(line); }
        catch (FormatException) { return null; }
    }
}
