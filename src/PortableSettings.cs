using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class PortableSettingsDocument
{
    public int Version { get; set; }
    public PortablePreferences Preferences { get; set; }
    public string SelectedProfile { get; set; }
    public string[] Favorites { get; set; }
    public Dictionary<string, string> ProfileNotes { get; set; }
    public Dictionary<string, string[]> ProfileTags { get; set; }
    public AltLabSettings Alt { get; set; }
    public PortableDomainLists Domains { get; set; }
}

internal sealed class PortablePreferences
{
    public bool AutoUpdates { get; set; }
    public bool CompactHeader { get; set; }
    public int UiScale { get; set; }
    public string AccentName { get; set; }
    public bool TrayEnabled { get; set; }
    public bool MinimizeToTray { get; set; }
    public bool TestNotifications { get; set; }
    public bool UpdateNotifications { get; set; }
    public string Density { get; set; }
    public string LastWorkingProfile { get; set; }
    public string LastWorkingFolder { get; set; }
    public string[] FlowsealFolders { get; set; }
}

internal sealed class PortableDomainLists
{
    public string[] Include { get; set; }
    public string[] Exclude { get; set; }
}

internal static class PortableSettings
{
    private const int CurrentVersion = 1;
    private const int MaxBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public static PortableSettingsDocument Capture(Preferences source, AltLabSettings alt, PortableDomainLists domains)
    {
        return new PortableSettingsDocument
        {
            Version = CurrentVersion,
            Preferences = new PortablePreferences
            {
                AutoUpdates = source.AutoUpdates,
                CompactHeader = source.CompactHeader,
                UiScale = source.UiScale,
                AccentName = source.AccentName,
                TrayEnabled = source.TrayEnabled,
                MinimizeToTray = source.MinimizeToTray,
                TestNotifications = source.TestNotifications,
                UpdateNotifications = source.UpdateNotifications,
                Density = Preferences.NormalizeDensity(source.Density),
                LastWorkingProfile = source.LastWorkingProfile,
                LastWorkingFolder = source.LastWorkingFolder,
                FlowsealFolders = source.FlowsealFolders.OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToArray()
            },
            SelectedProfile = ProfileFiles.IsProfile(source.Profile) ? source.Profile : null,
            Favorites = source.Favorites.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            ProfileNotes = new Dictionary<string, string>(source.ProfileNotes, StringComparer.OrdinalIgnoreCase),
            ProfileTags = source.ProfileTags.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            Alt = alt,
            Domains = domains
        };
    }

    public static void ApplyPreferences(Preferences target, PortableSettingsDocument document)
    {
        PortablePreferences values = document.Preferences;
        target.AutoUpdates = values.AutoUpdates;
        target.CompactHeader = values.CompactHeader;
        target.UiScale = values.UiScale;
        target.AccentName = values.AccentName;
        target.TrayEnabled = values.TrayEnabled;
        target.MinimizeToTray = values.MinimizeToTray;
        target.TestNotifications = values.TestNotifications;
        target.UpdateNotifications = values.UpdateNotifications;
        target.Density = Preferences.NormalizeDensity(values.Density);
        target.LastWorkingProfile = values.LastWorkingProfile;
        target.LastWorkingFolder = values.LastWorkingFolder;
        target.FlowsealFolders.Clear();
        if (values.FlowsealFolders != null) target.FlowsealFolders.UnionWith(values.FlowsealFolders.Where(folder => !String.IsNullOrWhiteSpace(folder)));
        target.Favorites.Clear();
        target.Favorites.UnionWith(document.Favorites);
        target.ProfileNotes.Clear();
        foreach (var pair in document.ProfileNotes) target.ProfileNotes.Add(pair.Key, pair.Value);
        target.ProfileTags.Clear();
        if (document.ProfileTags != null)
            foreach (var pair in document.ProfileTags) target.ProfileTags[pair.Key] = pair.Value ?? new string[0];
    }

    public static void Write(string path, PortableSettingsDocument document)
    {
        Validate(document);
        byte[] content = Utf8.GetBytes(Serializer().Serialize(document));
        if (content.Length > MaxBytes) throw new IOException("Файл настроек превышает 2 МБ.");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static PortableSettingsDocument Read(string path)
    {
        using (var file = File.OpenRead(path))
        using (var reader = new StreamReader(file, Utf8, false))
        {
            if (file.Length > MaxBytes) throw new IOException("Файл настроек превышает 2 МБ.");
            return Parse(reader.ReadToEnd().TrimStart('\uFEFF'));
        }
    }

    internal static PortableSettingsDocument Parse(string json)
    {
        if (String.IsNullOrWhiteSpace(json)) throw new FormatException("Файл настроек пуст.");
        PortableSettingsDocument document;
        try
        {
            var serializer = Serializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("Version") || !(root["Version"] is int) || (int)root["Version"] != CurrentVersion)
                throw new FormatException("Эта версия файла настроек Reze Zapret не поддерживается.");
            ValidatePreferenceFields(root);
            document = serializer.ConvertToType<PortableSettingsDocument>(root);
        }
        catch (ArgumentException error) { throw new FormatException("Повреждён файл настроек Reze Zapret.", error); }
        catch (InvalidOperationException error) { throw new FormatException("Повреждён файл настроек Reze Zapret.", error); }
        Validate(document);
        return document;
    }

    private static void ValidatePreferenceFields(Dictionary<string, object> root)
    {
        object value;
        var fields = root.TryGetValue("Preferences", out value) ? value as Dictionary<string, object> : null;
        string[] switches = { "AutoUpdates", "CompactHeader", "TrayEnabled", "MinimizeToTray", "TestNotifications", "UpdateNotifications" };
        if (fields == null || switches.Any(key => !IsFieldType<bool>(fields, key)) ||
            !IsFieldType<int>(fields, "UiScale") || !IsFieldType<string>(fields, "AccentName"))
            throw new FormatException("В файле отсутствуют обязательные параметры Reze Zapret.");
    }

    private static bool IsFieldType<T>(Dictionary<string, object> fields, string key)
    {
        object value;
        return fields.TryGetValue(key, out value) && value is T;
    }

    private static JavaScriptSerializer Serializer()
    {
        return new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 12 };
    }

    private static void Validate(PortableSettingsDocument document)
    {
        if (document == null || document.Version != CurrentVersion || document.Preferences == null ||
            document.Favorites == null || document.ProfileNotes == null || document.Alt == null)
            throw new FormatException("В файле отсутствуют обязательные разделы настроек Reze Zapret.");
        PortablePreferences values = document.Preferences;
        values.Density = Preferences.NormalizeDensity(values.Density);
        if (values.UiScale != Preferences.ClampUiScale(values.UiScale) || values.AccentName != Preferences.NormalizeAccent(values.AccentName))
            throw new FormatException("В файле указан неизвестный акцент или недопустимый масштаб.");
        if ((document.SelectedProfile != null && !ProfileFiles.IsProfile(document.SelectedProfile)) ||
            document.Favorites.Any(name => !ProfileFiles.IsProfile(name)) ||
            document.ProfileNotes.Any(pair => !ProfileFiles.IsProfile(pair.Key) || pair.Value == null) ||
            document.ProfileNotes.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != document.ProfileNotes.Count ||
            document.ProfileTags != null && document.ProfileTags.Any(pair => !ProfileFiles.IsProfile(pair.Key) || pair.Value == null ||
                pair.Value.Any(tag => String.IsNullOrWhiteSpace(tag) || Preferences.NormalizeTags(tag).Length != 1)))
            throw new FormatException("В файле есть недопустимые или повторяющиеся записи профилей.");
        document.Favorites = document.Favorites.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        document.Alt = AltLab.NormalizeSettings(document.Alt);
        if (document.Domains != null)
        {
            document.Domains.Include = NormalizeDomains(document.Domains.Include);
            document.Domains.Exclude = NormalizeDomains(document.Domains.Exclude);
        }
    }

    private static string[] NormalizeDomains(string[] domains)
    {
        if (domains == null) throw new FormatException("В файле отсутствует список доменов.");
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string domain in domains)
        {
            if (String.IsNullOrWhiteSpace(domain) || domain.Any(ch => Char.IsWhiteSpace(ch) || "/\\:@?#".IndexOf(ch) >= 0))
                throw new FormatException("Список переноса должен содержать только домены.");
            result.Add(ProfileFiles.Normalize(domain));
        }
        return result.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static PortableDomainLists CaptureDomains(DomainListsSnapshot snapshot)
    {
        return new PortableDomainLists
        {
            Include = ReadDomains(snapshot.Inclusions).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            Exclude = ReadDomains(snapshot.Exclusions).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public static DomainListsSnapshot MergeDomains(DomainListsSnapshot current, PortableDomainLists incoming,
        out int included, out int excluded)
    {
        byte[] inclusions = AppendDomains(current.Inclusions, incoming.Include, out included);
        byte[] exclusions = AppendDomains(current.Exclusions, incoming.Exclude, out excluded);
        return new DomainListsSnapshot(inclusions, exclusions);
    }

    private static byte[] AppendDomains(byte[] original, string[] incoming, out int count)
    {
        count = 0;
        if (incoming.Length == 0) return original;
        var existing = ReadDomains(original);
        string[] additions = incoming.Where(existing.Add).ToArray();
        count = additions.Length;
        if (count == 0) return original;
        string existingText = original == null ? String.Empty : Utf8.GetString(original);
        string prefix = existingText.Length > 0 && !existingText.EndsWith("\n", StringComparison.Ordinal) ? Environment.NewLine : "";
        byte[] suffix = Utf8.GetBytes(prefix + String.Join(Environment.NewLine, additions) + Environment.NewLine);
        return original == null ? suffix : original.Concat(suffix).ToArray();
    }

    private static HashSet<string> ReadDomains(byte[] bytes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (bytes == null) return result;
        foreach (string raw in Utf8.GetString(bytes).TrimStart('\uFEFF').Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            try { result.Add(ProfileFiles.Normalize(line)); }
            catch (FormatException) { }
        }
        return result;
    }
}

