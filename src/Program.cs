using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.Length == 2 && args[0] == "--folder" ? args[1] : null));
    }
}

internal sealed class Preferences
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reze-Zapret", "settings.txt");
    private readonly string filePath;

    public string Folder { get; set; }
    public string Profile { get; set; }
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
    public readonly HashSet<string> FlowsealFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string[]> ProfileTags = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> ProfileNotes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public Preferences() : this(DefaultPath)
    {
    }

    private Preferences(string path)
    {
        filePath = path;
        CompactHeader = true;
        UiScale = 100;
        AccentName = "ice";
        TrayEnabled = true;
        MinimizeToTray = false;
        TestNotifications = true;
        UpdateNotifications = true;
        Density = "normal";
    }

    public static Preferences Load(string path = null)
    {
        var result = new Preferences(path ?? DefaultPath);
        if (File.Exists(result.filePath))
        {
            string[] lines = File.ReadAllLines(result.filePath);
            if (lines.Length > 0) result.Folder = lines[0];
            if (lines.Length > 1) result.Profile = lines[1];
            result.AutoUpdates = lines.Skip(2).Contains("auto-updates=on", StringComparer.OrdinalIgnoreCase);
            result.Favorites.UnionWith(lines.Skip(2).Where(ProfileFiles.IsProfile));
            LoadLegacyValues(result, lines.Skip(2));
        }
        return result;
    }

    private static void LoadLegacyValues(Preferences result, IEnumerable<string> values)
    {
        foreach (string value in values)
        {
            string[] pair = value.Split(new[] { '=' }, 2);
            if (pair.Length != 2) continue;
            bool boolean;
            int number;
            if (pair[0].Equals("compact-header", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(pair[1], out boolean)) result.CompactHeader = boolean;
            else if (pair[0].Equals("ui-scale", StringComparison.OrdinalIgnoreCase) && Int32.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) result.UiScale = ClampUiScale(number);
            else if (pair[0].Equals("accent", StringComparison.OrdinalIgnoreCase)) result.AccentName = NormalizeAccent(pair[1]);
            else if (pair[0].Equals("tray", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(pair[1], out boolean)) result.TrayEnabled = boolean;
            else if (pair[0].Equals("minimize-to-tray", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(pair[1], out boolean)) result.MinimizeToTray = boolean;
            else if (pair[0].Equals("test-notifications", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(pair[1], out boolean)) result.TestNotifications = boolean;
            else if (pair[0].Equals("update-notifications", StringComparison.OrdinalIgnoreCase) && Boolean.TryParse(pair[1], out boolean)) result.UpdateNotifications = boolean;
            else if (pair[0].Equals("density", StringComparison.OrdinalIgnoreCase)) result.Density = NormalizeDensity(pair[1]);
            else if (pair[0].Equals("last-working-profile", StringComparison.OrdinalIgnoreCase) && ProfileFiles.IsProfile(pair[1])) result.LastWorkingProfile = pair[1];
            else if (pair[0].Equals("last-working-folder", StringComparison.OrdinalIgnoreCase)) result.LastWorkingFolder = pair[1];
            else if (pair[0].Equals("flowseal-folder", StringComparison.OrdinalIgnoreCase) && !String.IsNullOrWhiteSpace(pair[1])) result.FlowsealFolders.Add(pair[1]);
            else if (pair[0].Equals("profile-tag", StringComparison.OrdinalIgnoreCase))
            {
                string[] tag = pair[1].Split(new[] { '|' }, 2);
                if (tag.Length == 2 && ProfileFiles.IsProfile(tag[0]))
                {
                    try { result.ProfileTags[tag[0]] = DecodeTags(tag[1]); }
                    catch (FormatException) { }
                }
            }
            else if (pair[0].Equals("profile-note", StringComparison.OrdinalIgnoreCase))
            {
                string[] note = pair[1].Split(new[] { '|' }, 2);
                if (note.Length == 2 && ProfileFiles.IsProfile(note[0]))
                {
                    try { result.ProfileNotes[note[0]] = Encoding.UTF8.GetString(Convert.FromBase64String(note[1])); }
                    catch (FormatException) { }
                }
            }
        }
    }

    internal const int MinUiScale = 80, MaxUiScale = 140;
    internal static int ClampUiScale(int value) { return Math.Max(MinUiScale, Math.Min(MaxUiScale, value)); }

    internal static string NormalizeDensity(string value)
    {
        return String.Equals(value, "compact", StringComparison.OrdinalIgnoreCase) || String.Equals(value, "spacious", StringComparison.OrdinalIgnoreCase) ? value.ToLowerInvariant() : "normal";
    }

    internal static string[] NormalizeTags(string value)
    {
        return (value ?? "").Split(new[] { ' ', '\t', ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().TrimStart('#').ToLowerInvariant())
            .Where(tag => tag.Length > 0 && tag.All(ch => Char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }

    private static string[] DecodeTags(string value)
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim().ToLowerInvariant()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }

    internal static string NormalizeAccent(string value)
    {
        if (String.Equals(value, "lavender", StringComparison.OrdinalIgnoreCase)) return "lavender";
        if (String.Equals(value, "mint", StringComparison.OrdinalIgnoreCase)) return "mint";
        return "ice";
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var values = new List<string> { Folder ?? "", Profile ?? "" };
            values.AddRange(Favorites.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            if (AutoUpdates) values.Add("auto-updates=on");
            values.Add("compact-header=" + CompactHeader.ToString(CultureInfo.InvariantCulture));
            values.Add("ui-scale=" + ClampUiScale(UiScale).ToString(CultureInfo.InvariantCulture));
            values.Add("accent=" + NormalizeAccent(AccentName));
            values.Add("tray=" + TrayEnabled.ToString(CultureInfo.InvariantCulture));
            values.Add("minimize-to-tray=" + MinimizeToTray.ToString(CultureInfo.InvariantCulture));
            values.Add("test-notifications=" + TestNotifications.ToString(CultureInfo.InvariantCulture));
            values.Add("update-notifications=" + UpdateNotifications.ToString(CultureInfo.InvariantCulture));
            values.Add("density=" + NormalizeDensity(Density));
            if (ProfileFiles.IsProfile(LastWorkingProfile)) values.Add("last-working-profile=" + LastWorkingProfile);
            if (!String.IsNullOrEmpty(LastWorkingFolder)) values.Add("last-working-folder=" + LastWorkingFolder);
            values.AddRange(FlowsealFolders.Where(folder => !String.IsNullOrWhiteSpace(folder))
                .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
                .Select(folder => "flowseal-folder=" + folder));
            values.AddRange(ProfileTags.Where(pair => ProfileFiles.IsProfile(pair.Key) && pair.Value != null)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => "profile-tag=" + pair.Key + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Join(",", pair.Value)))));
            values.AddRange(ProfileNotes.Where(pair => ProfileFiles.IsProfile(pair.Key) && pair.Value != null)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => "profile-note=" + pair.Key + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Value))));
            File.WriteAllLines(temporary, values);
            if (File.Exists(filePath)) File.Replace(temporary, filePath, null);
            else File.Move(temporary, filePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
