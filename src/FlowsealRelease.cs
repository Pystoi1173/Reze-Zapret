using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using Microsoft.Win32;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class FlowsealRelease
{
    private const string Repository = "https://github.com/Flowseal/zapret-discord-youtube";
    private const string Latest = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
    private const string BundlePrefix = "zapret-discord-youtube-";
    private const string PreparedMarker = ".reze-flowseal-release";
    private const int MaxMetadata = 512 * 1024;
    private const int MaxZip = 32 * 1024 * 1024;
    private const int MaxFile = 32 * 1024 * 1024;
    private const int MaxExpanded = 128 * 1024 * 1024;
    private const int MaxEntries = 1024;
    private const int MaxPersonalFile = 8 * 1024 * 1024;
    private const string VersionPattern = @"(?:0|[1-9][0-9]{0,8})(?:\.(?:0|[1-9][0-9]{0,8})){2}";
    private static readonly Regex StableTag = new Regex(@"\A" + VersionPattern + @"\z", RegexOptions.CultureInvariant);
    private static readonly Regex LocalVersion = new Regex("\\A[ \\t]*set[ \\t]+\"LOCAL_VERSION=(" + VersionPattern + ")\"[ \\t]*\\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256 = new Regex(@"\A[0-9a-f]{64}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DeviceName = new Regex(@"\A(?:CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³]|CONIN\$|CONOUT\$)\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly string[] PersonalNames = {
        "list-general-user.txt", "list-exclude-user.txt",
        "list-general-user.txt.reze.bak", "list-exclude-user.txt.reze.bak"
    };
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    public readonly Version Version;
    public readonly string Tag;
    public readonly string Digest;
    public readonly string Notes;
    public readonly DateTime PublishedUtc;
    private readonly Uri download;
    private readonly long size;

    private FlowsealRelease(string tag, string digest, long size, string url, string notes, DateTime publishedUtc)
    {
        Tag = tag;
        Version = new Version(tag);
        Digest = digest.ToLowerInvariant();
        Notes = notes ?? String.Empty;
        PublishedUtc = publishedUtc;
        this.size = size;
        download = new Uri(url);
    }

    internal sealed class Prepared
    {
        public readonly string Folder;
        public readonly Version Version;
        public readonly string Digest;
        internal readonly string Source;
        internal readonly string[] ListHashes;

        internal Prepared(string folder, Version version, string digest, string source, string[] listHashes)
        {
            Folder = folder;
            Version = version;
            Digest = digest;
            Source = source;
            ListHashes = listHashes;
        }
    }

    internal sealed class ServiceInfo
    {
        public readonly bool Installed;
        public readonly string ImagePath;
        public readonly string BundleFolder;
        public readonly Version Version;

        internal ServiceInfo(bool installed, string imagePath, string bundleFolder, Version version)
        {
            Installed = installed;
            ImagePath = imagePath;
            BundleFolder = bundleFolder;
            Version = version;
        }
    }

    public static ServiceInfo ReadServiceInfo()
    {
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\zapret", false))
            {
                if (key == null) return new ServiceInfo(false, null, null, null);
                return ReadServiceImage(key.GetValue("ImagePath") as string);
            }
        }
        catch (Exception error)
        {
            if (!IsLocalReadError(error)) throw;
            return new ServiceInfo(true, null, null, null);
        }
    }

    internal static ServiceInfo ReadServiceImage(string imagePath)
    {
        if (String.IsNullOrWhiteSpace(imagePath)) return new ServiceInfo(true, null, null, null);
        string executable = imagePath.Trim();
        if (executable[0] == '"')
        {
            int end = executable.IndexOf('"', 1);
            executable = end > 1 ? executable.Substring(1, end - 1) : String.Empty;
        }
        else
        {
            int separator = executable.IndexOfAny(new[] { ' ', '\t' });
            if (separator >= 0) executable = executable.Substring(0, separator);
        }
        if (executable.StartsWith(@"\??\", StringComparison.Ordinal) || executable.StartsWith(@"\\?\", StringComparison.Ordinal))
            executable = executable.Substring(4);

        // Only inspect a direct local Flowseal binary. The service command line is never evaluated.
        if (!Regex.IsMatch(executable, @"\A[A-Za-z]:[\\/]") || executable.IndexOf(':', 2) >= 0)
            return new ServiceInfo(true, executable, null, null);
        try
        {
            string full = Path.GetFullPath(executable);
            string bin = Path.GetDirectoryName(full);
            if (!SamePath(Path.GetFileName(full), "winws.exe") || !SamePath(Path.GetFileName(bin), "bin"))
                return new ServiceInfo(true, executable, null, null);
            string folder = Path.GetDirectoryName(bin);
            CheckDirectoryChain(bin);
            Version version = IsPlain(full, false) && IsPlain(Path.Combine(folder, "service.bat"), false) ? InstalledVersion(folder) : null;
            return new ServiceInfo(true, executable, folder, version);
        }
        catch (Exception error)
        {
            if (!IsLocalReadError(error)) throw;
            return new ServiceInfo(true, executable, null, null);
        }
    }

    public static string StagingRoot()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (String.IsNullOrEmpty(local)) throw new IOException("Папка LocalAppData недоступна.");
        return Path.Combine(local, "Reze-Zapret", "Flowseal");
    }

    public static Version InstalledVersion(string root)
    {
        if (String.IsNullOrEmpty(root)) return null;
        string script = Path.Combine(root, "service.bat");
        if (!File.Exists(script)) return null;
        using (var input = new FileStream(script, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var bytes = new byte[4096];
            int count = input.Read(bytes, 0, bytes.Length);
            string head = Encoding.UTF8.GetString(bytes, 0, count).TrimStart('\uFEFF');
            foreach (string line in head.Split('\n'))
            {
                Match match = LocalVersion.Match(line.TrimEnd('\r'));
                if (match.Success) return new Version(match.Groups[1].Value);
            }
        }
        return null;
    }

    public static async Task<FlowsealRelease> GetLatestAsync(CancellationToken token)
    {
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var client = CreateClient())
        using (var request = new HttpRequestMessage(HttpMethod.Get, Latest))
        using (var buffer = new MemoryStream())
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            try
            {
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    CheckResponse(response, MaxMetadata, null);
                    await ReadResponseAsync(response, buffer, MaxMetadata, deadline.Token).ConfigureAwait(false);
                }
                return ParseLatest(Utf8.GetString(buffer.ToArray()));
            }
            catch (Exception ex)
            {
                ThrowIfInterrupted(token, deadline.Token, ex);
                throw;
            }
        }
    }

    internal static FlowsealRelease ParseLatest(string json)
    {
        var serializer = new JavaScriptSerializer { MaxJsonLength = MaxMetadata, RecursionLimit = 24 };
        try
        {
            var release = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (release == null || !Object.Equals(Value(release, "draft"), false) ||
                !Object.Equals(Value(release, "prerelease"), false)) throw InvalidRelease();
            string tag = Value(release, "tag_name") as string;
            object[] assets = Value(release, "assets") as object[];
            string notes = Value(release, "body") as string ?? String.Empty;
            DateTime published = ParsePublished(Value(release, "published_at") as string);
            if (tag == null || !StableTag.IsMatch(tag) || assets == null) throw InvalidRelease();
            string assetName = BundlePrefix + tag + ".zip";
            string url = Repository + "/releases/download/" + tag + "/" + assetName;
            FlowsealRelease result = null;
            foreach (object asset in assets)
            {
                var data = asset as Dictionary<string, object>;
                if (data == null || !String.Equals(Value(data, "name") as string, assetName, StringComparison.Ordinal)) continue;
                string digest = Value(data, "digest") as string;
                object number = Value(data, "size");
                long assetSize = number is int ? (int)number : number is long ? (long)number : -1;
                if (result != null || assetSize <= 0 || assetSize > MaxZip || digest == null ||
                    !digest.StartsWith("sha256:", StringComparison.Ordinal) || !Sha256.IsMatch(digest.Substring(7)) ||
                    !String.Equals(Value(data, "state") as string, "uploaded", StringComparison.Ordinal) ||
                    !String.Equals(Value(data, "content_type") as string, "application/zip", StringComparison.Ordinal) ||
                    !String.Equals(Value(data, "browser_download_url") as string, url, StringComparison.Ordinal)) throw InvalidRelease();
                result = new FlowsealRelease(tag, digest.Substring(7), assetSize, url, notes, published);
            }
            if (result == null) throw InvalidRelease();
            return result;
        }
        catch (ArgumentException) { throw InvalidRelease(); }
        catch (InvalidOperationException) { throw InvalidRelease(); }
    }

    private static object Value(Dictionary<string, object> data, string name)
    {
        object value;
        return data.TryGetValue(name, out value) ? value : null;
    }

    private static DateTime ParsePublished(string value)
    {
        DateTime result;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result)
            ? result : DateTime.MinValue;
    }

    private static InvalidDataException InvalidRelease()
    {
        return new InvalidDataException("GitHub не вернул ожидаемые данные стабильного релиза Flowseal и SHA-256 его ZIP.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, SslProtocols = SslProtocols.Tls12 });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Reze-Zapret/1.0");
        return client;
    }

    private static void CheckResponse(HttpResponseMessage response, long maximum, long? exact)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            throw new IOException("GitHub ответил HTTP " + (int)response.StatusCode + ".");
        long? length = response.Content.Headers.ContentLength;
        if (response.Content.Headers.ContentEncoding.Count != 0 || length > maximum ||
            (length.HasValue && exact.HasValue && length.Value != exact.Value))
            throw new InvalidDataException("Получен неожиданный размер или формат ответа GitHub.");
    }

    private static async Task<long> ReadResponseAsync(HttpResponseMessage response, Stream output, long limit, CancellationToken token)
    {
        using (token.Register(response.Dispose))
        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        {
            var buffer = new byte[64 * 1024];
            long total = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int count = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                if (count == 0) return total;
                total += count;
                if (total > limit) throw new InvalidDataException("Ответ GitHub превышает допустимый размер.");
                output.Write(buffer, 0, count);
            }
        }
    }

    private static void ThrowIfInterrupted(CancellationToken external, CancellationToken deadline, Exception cause)
    {
        external.ThrowIfCancellationRequested();
        if (deadline.IsCancellationRequested || cause is OperationCanceledException)
            throw new IOException("Истекло время ожидания ответа GitHub.", cause);
    }

    private async Task DownloadAsync(Stream output, CancellationToken token)
    {
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var client = CreateClient())
        {
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                using (var response = await client.GetAsync(download, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    int status = (int)response.StatusCode;
                    Uri redirect = response.Headers.Location;
                    if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                    {
                        if (redirect == null || !redirect.IsAbsoluteUri || redirect.Scheme != Uri.UriSchemeHttps ||
                            redirect.Host != "release-assets.githubusercontent.com" || !redirect.IsDefaultPort ||
                            redirect.UserInfo.Length != 0 || redirect.Fragment.Length != 0 ||
                            !redirect.AbsolutePath.StartsWith("/github-production-release-asset/869741127/", StringComparison.Ordinal))
                            throw new InvalidDataException("GitHub вернул неожиданный адрес загрузки ZIP.");
                        using (var asset = await client.GetAsync(redirect, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                            await ReadZipResponseAsync(asset, output, deadline.Token).ConfigureAwait(false);
                    }
                    else await ReadZipResponseAsync(response, output, deadline.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ThrowIfInterrupted(token, deadline.Token, ex);
                throw;
            }
        }
    }

    private async Task ReadZipResponseAsync(HttpResponseMessage response, Stream output, CancellationToken token)
    {
        CheckResponse(response, MaxZip, size);
        if (await ReadResponseAsync(response, output, size, token).ConfigureAwait(false) != size)
            throw new InvalidDataException("Размер ZIP не совпадает с данными GitHub.");
    }

    public async Task<Prepared> StageAsync(string stagingRoot, string sourceFolder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string root = FullDirectory(stagingRoot);
        string source = FullDirectory(sourceFolder);
        if (source.Length != 0)
        {
            CheckDirectoryChain(source);
            if (SamePath(root, source) || IsUnder(root, source))
                throw new IOException("Хранилище обновлений находится внутри выбранной сборки.");
        }
        CheckDirectoryChain(root);
        Directory.CreateDirectory(root);
        CheckDirectoryChain(root);
        string temporary = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
        string extracted = Path.Combine(temporary, "bundle");
        Directory.CreateDirectory(temporary);
        try
        {
            token.ThrowIfCancellationRequested();
            using (var zip = new FileStream(Path.Combine(temporary, "release.zip"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                await DownloadAsync(zip, token).ConfigureAwait(false);
                zip.Position = 0;
                if (!String.Equals(HashStream(zip, null, MaxZip, token), Digest, StringComparison.Ordinal))
                    throw new InvalidDataException("SHA-256 загруженного ZIP не совпадает с данными GitHub.");
                zip.Position = 0;
                Directory.CreateDirectory(extracted);
                Extract(zip, extracted, BundlePrefix + Tag, token);
            }
            if (!ProfileFiles.IsBundle(extracted) || ProfileFiles.Profiles(extracted).Length == 0 || InstalledVersion(extracted) != Version)
                throw new InvalidDataException("В ZIP отсутствует ожидаемая сборка Flowseal " + Tag + ".");
            var hashes = PersonalFiles(source, extracted, token);
            string marker = Path.Combine(extracted, PreparedMarker);
            using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var text = new StreamWriter(stream, Utf8))
            {
                text.WriteLine(Tag);
                text.WriteLine(Digest);
                text.WriteLine(source);
                foreach (string hash in hashes) text.WriteLine(hash);
            }
            string bundleName = BundlePrefix + Tag;
            string destination = Path.Combine(root, bundleName);
            if (Directory.Exists(destination) || File.Exists(destination))
                destination = Path.Combine(root, bundleName + "-" + Guid.NewGuid().ToString("N"));
            token.ThrowIfCancellationRequested();
            Directory.Move(extracted, destination);
            return new Prepared(destination, Version, Digest, source, hashes);
        }
        finally
        {
            try { Directory.Delete(temporary, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static void Extract(Stream zip, string destination, string wrapper, CancellationToken token)
    {
        string root = FullDirectory(destination);
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, true))
        {
            if (archive.Entries.Count == 0 || archive.Entries.Count > MaxEntries)
                throw new InvalidDataException("Недопустимое число записей в ZIP.");
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string name = entry.FullName;
                bool directory = name.EndsWith("/", StringComparison.Ordinal);
                int kind = (entry.ExternalAttributes >> 16) & 0xF000;
                if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 ||
                    (kind != 0 && kind != (directory ? 0x4000 : 0x8000)) ||
                    (!directory && (entry.ExternalAttributes & (int)FileAttributes.Directory) != 0)) throw InvalidEntry();
                if (String.Equals(name, wrapper + "/", StringComparison.Ordinal))
                {
                    if (entry.Length != 0 || !entries.Add(name)) throw InvalidEntry();
                    continue;
                }
                if (name.StartsWith(wrapper + "/", StringComparison.Ordinal)) name = name.Substring(wrapper.Length + 1);
                // Official releases can be flat or have a single wrapper. Never combine both layouts.
                string layout = entry.FullName.StartsWith(wrapper + "/", StringComparison.Ordinal) ? "wrapped" : "flat";
                string previousLayout;
                if (directories.TryGetValue(String.Empty, out previousLayout) && previousLayout != layout) throw InvalidEntry();
                directories[String.Empty] = layout;
                string[] parts = (directory ? name.Substring(0, name.Length - 1) : name).Split('/');
                if (parts.Length > 12 || parts.Any(part => !IsSafeSegment(part)) ||
                    String.Equals(parts[0], PreparedMarker, StringComparison.OrdinalIgnoreCase)) throw InvalidEntry();
                string relative = String.Join(Path.DirectorySeparatorChar.ToString(), parts);
                if (!entries.Add(relative)) throw InvalidEntry();
                string target = Path.GetFullPath(Path.Combine(root, relative));
                if (!IsUnder(target, root) || target.Length >= 248) throw InvalidEntry();

                string folder = root;
                for (int index = 0; index < parts.Length - (directory ? 0 : 1); index++)
                {
                    folder = Path.Combine(folder, parts[index]);
                    string existing;
                    if (directories.TryGetValue(folder, out existing) && !String.Equals(existing, folder, StringComparison.Ordinal)) throw InvalidEntry();
                    directories[folder] = folder;
                }
                if (directory && entry.Length != 0 || !directory && directories.ContainsKey(target)) throw InvalidEntry();
                Directory.CreateDirectory(folder);
                if (directory) continue;
                if (entry.Length < 0 || entry.Length > MaxFile || entry.Length > MaxExpanded - expanded) throw InvalidEntry();
                using (Stream input = entry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (CopyBounded(input, output, entry.Length, null, token) != entry.Length) throw InvalidEntry();
                }
                expanded += entry.Length;
            }
            string finalLayout;
            if (entries.Contains(wrapper + "/") && directories.TryGetValue(String.Empty, out finalLayout) && finalLayout != "wrapped") throw InvalidEntry();
        }
    }

    private static bool IsSafeSegment(string name)
    {
        return name.Length > 0 && name.Length <= 120 && name[0] != ' ' && name[name.Length - 1] != '.' &&
            name[name.Length - 1] != ' ' && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOf('~') < 0 &&
            !DeviceName.IsMatch(name.Split('.')[0].TrimEnd(' '));
    }

    private static InvalidDataException InvalidEntry()
    {
        return new InvalidDataException("ZIP содержит недопустимые, повторяющиеся или слишком большие записи.");
    }

    public static Prepared FindPrepared(string stagingRoot, string sourceFolder, Version installed, CancellationToken token)
    {
        string source = FullDirectory(sourceFolder);
        return ReadStaged(stagingRoot, token).FirstOrDefault(candidate => SamePath(candidate.Source, source) &&
            !SamePath(candidate.Folder, source) && (installed == null || candidate.Version > installed));
    }

    public static Prepared[] ReadStaged(string stagingRoot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string root = FullDirectory(stagingRoot);
        if (String.IsNullOrEmpty(root)) return new Prepared[0];
        CheckDirectoryChain(root);
        if (!Directory.Exists(root)) return new Prepared[0];
        var results = new List<KeyValuePair<Prepared, DateTime>>();
        foreach (string folder in Directory.EnumerateDirectories(root, BundlePrefix + "*"))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                Prepared candidate = ReadPrepared(folder, token);
                if (candidate != null) results.Add(new KeyValuePair<Prepared, DateTime>(candidate,
                    File.GetLastWriteTimeUtc(Path.Combine(candidate.Folder, PreparedMarker))));
            }
            catch (Exception ex)
            {
                if (!IsLocalReadError(ex)) throw;
            }
        }
        return results.OrderByDescending(candidate => candidate.Key.Version)
            .ThenByDescending(candidate => candidate.Value)
            .ThenBy(candidate => candidate.Key.Folder, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Key).ToArray();
    }

    private static bool HasBundleFiles(string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return IsPlain(Path.Combine(folder, "service.bat"), false) && IsPlain(Path.Combine(folder, "bin"), true) &&
            IsPlain(Path.Combine(folder, "bin", "winws.exe"), false) && IsPlain(Path.Combine(folder, "lists"), true) &&
            ProfileFiles.Profiles(folder).Any(name => IsPlain(Path.Combine(folder, name), false));
    }

    internal static bool IsLocalReadError(Exception error)
    {
        return error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
            error is NotSupportedException || error is System.Security.SecurityException;
    }

    internal static bool TryReadLocalBundle(string folder, CancellationToken token, out Version version)
    {
        version = null;
        if (String.IsNullOrEmpty(folder)) return false;
        CheckDirectoryChain(folder);
        if (!HasBundleFiles(folder, token)) return false;
        version = InstalledVersion(folder);
        return true;
    }

    internal static void ValidateExistingSelection(string folder, Version expected, Prepared prepared, CancellationToken token)
    {
        Version version;
        if (!TryReadLocalBundle(folder, token, out version) || version != expected)
            throw new IOException("Папка сборки изменилась или недоступна. Обновите список папок.");
        if (prepared == null) return;
        Prepared current = ReadPrepared(folder, token);
        if (current == null || current.Digest != prepared.Digest || !SamePath(current.Source, prepared.Source) ||
            !current.ListHashes.SequenceEqual(prepared.ListHashes))
            throw new IOException("Метка скачанной сборки изменилась. Обновите список папок.");
    }

    private static Prepared ReadPrepared(string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsPlain(folder, true)) return null;
        string marker = Path.Combine(folder, PreparedMarker);
        if (!IsPlain(marker, false)) return null;
        string[] lines;
        using (var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new StreamReader(stream, Utf8))
        {
            if (stream.Length > 4096) return null;
            lines = reader.ReadToEnd().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        }
        if (lines.Length != 4 + PersonalNames.Length || lines[lines.Length - 1].Length != 0 ||
            !StableTag.IsMatch(lines[0]) || !Sha256.IsMatch(lines[1])) return null;
        string source = FullDirectory(lines[2]);
        if (!SamePath(lines[2], source) || SamePath(folder, source)) return null;
        string name = Path.GetFileName(folder);
        string expected = BundlePrefix + lines[0];
        Guid suffix;
        if (!String.Equals(name, expected, StringComparison.Ordinal) &&
            (!name.StartsWith(expected + "-", StringComparison.Ordinal) || !Guid.TryParseExact(name.Substring(expected.Length + 1), "N", out suffix))) return null;
        string[] hashes = lines.Skip(3).Take(PersonalNames.Length).ToArray();
        if (hashes.Any(hash => hash != "-" && !Sha256.IsMatch(hash)) || !HasBundleFiles(folder, token)) return null;
        var version = new Version(lines[0]);
        return InstalledVersion(folder) == version ? new Prepared(folder, version, lines[1].ToLowerInvariant(), source, hashes) : null;
    }

    public static void ValidateSelection(Prepared prepared, string sourceFolder, CancellationToken token)
    {
        if (prepared == null || String.IsNullOrEmpty(prepared.Folder))
            throw new IOException("Подготовленная папка не выбрана.");
        string source = FullDirectory(sourceFolder);
        CheckDirectoryChain(Path.GetDirectoryName(prepared.Folder));
        Prepared current = ReadPrepared(prepared.Folder, token);
        if (current == null || current.Version != prepared.Version || current.Digest != prepared.Digest ||
            !SamePath(current.Source, source) || !SamePath(prepared.Source, source) || !current.ListHashes.SequenceEqual(prepared.ListHashes))
            throw new IOException("Подготовленная папка недоступна. Проверьте обновления ещё раз.");
        if (!PersonalFiles(source, null, token).SequenceEqual(prepared.ListHashes))
            throw new IOException("Ваши списки изменились после загрузки. Скачайте релиз заново, чтобы перенести изменения.");
        var stagedHashes = PersonalFiles(prepared.Folder, null, token);
        for (int index = 0; index < stagedHashes.Length; index++)
        {
            if (prepared.ListHashes[index] != "-" && stagedHashes[index] != prepared.ListHashes[index])
                throw new IOException("Копия пользовательских списков в подготовленной папке изменилась. Скачайте релиз заново.");
        }
    }

    private static string[] PersonalFiles(string source, string destination, CancellationToken token)
    {
        var hashes = Enumerable.Repeat("-", PersonalNames.Length).ToArray();
        if (source.Length == 0) return hashes;
        string lists = Path.Combine(source, "lists");
        if (!IsPlain(source, true) || !IsPlain(lists, true)) throw new IOException("Папка исходных пользовательских списков недоступна.");
        for (int index = 0; index < PersonalNames.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            string name = PersonalNames[index];
            string path = Path.Combine(lists, name);
            if (!IsPlain(path, false)) continue;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var output = destination == null ? null : new FileStream(Path.Combine(destination, "lists", name), FileMode.Create, FileAccess.Write, FileShare.None))
                hashes[index] = HashStream(input, output, MaxPersonalFile, token);
        }
        return hashes;
    }

    private static string HashStream(Stream input, Stream output, long limit, CancellationToken token)
    {
        using (var sha = SHA256.Create())
        {
            CopyBounded(input, output, limit, sha, token);
            sha.TransformFinalBlock(new byte[0], 0, 0);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private static long CopyBounded(Stream input, Stream output, long limit, HashAlgorithm hash, CancellationToken token)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int count = input.Read(buffer, 0, buffer.Length);
            if (count == 0) return total;
            total += count;
            if (total > limit) throw new InvalidDataException("Размер файла превышает допустимый предел.");
            if (hash != null) hash.TransformBlock(buffer, 0, count, null, 0);
            if (output != null) output.Write(buffer, 0, count);
        }
    }

    private static string FullDirectory(string folder)
    {
        if (String.IsNullOrEmpty(folder)) return String.Empty;
        string full = Path.GetFullPath(folder);
        return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool SamePath(string first, string second) { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }

    private static bool IsUnder(string path, string root)
    {
        return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckDirectoryChain(string folder)
    {
        for (var current = new DirectoryInfo(folder); current != null; current = current.Parent) IsPlain(current.FullName, true);
    }

    private static bool IsPlain(string path, bool directory)
    {
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != directory)
            throw new IOException("Неподдерживаемый тип файла или ссылка: " + path);
        return true;
    }
}
