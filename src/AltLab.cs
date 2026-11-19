using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class AltLabSettings
{
    public string SelectedSet { get; set; }
    public bool Detailed { get; set; }
    public string[] MySites { get; set; }

    public AltLabSettings()
    {
        SelectedSet = AltLab.All;
        MySites = new string[0];
    }
}

internal sealed class AltLabRun
{
    public DateTime DateUtc { get; set; }
    public string Version { get; set; }
    public string BundleFolder { get; set; }
    public string Set { get; set; }
    public int Attempts { get; set; }
    public string[] Sites { get; set; }
    public List<AltLabRow> Rows { get; set; }

    public AltLabRun() { Rows = new List<AltLabRow>(); }

    public override string ToString()
    {
        return DateUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") + " · Flowseal " + Version + " · " + AltLab.SetTitle(Set);
    }
}

internal sealed class AltLabRow
{
    public string Key { get; set; }
    public string Title { get; set; }
    public string Profile { get; set; }
    public string Note { get; set; }
    public Dictionary<string, List<AltLabSample>> Sites { get; set; }

    public AltLabRow() { Sites = new Dictionary<string, List<AltLabSample>>(StringComparer.OrdinalIgnoreCase); }

    public IList<AltLabSample> Samples(string site)
    {
        List<AltLabSample> samples;
        return Sites.TryGetValue(site, out samples) ? samples : null;
    }
}

internal sealed class AltLabSample
{
    public string Kind { get; set; }
    public int HttpCode { get; set; }
    public long Milliseconds { get; set; }

    public bool IsSuccess() { return HttpCode >= 200 && HttpCode < 300; }

    public string Describe()
    {
        return (HttpCode == 0 ? Kind : "HTTP " + HttpCode) + " · " + Milliseconds + " мс";
    }
}

internal sealed class AltLabStats
{
    public readonly int Total, Success;
    public readonly long? Median;

    public AltLabStats(IList<AltLabSample> samples)
    {
        if (samples == null) return;
        Total = samples.Count;
        long[] times = samples.Where(sample => sample.IsSuccess()).Select(sample => sample.Milliseconds).OrderBy(ms => ms).ToArray();
        Success = times.Length;
        if (Success > 0) Median = (times[(Success - 1) / 2] + times[Success / 2]) / 2;
    }

    public string CountText { get { return Total == 0 ? "—" : Success + "/" + Total; } }
    public string TimeText { get { return Median.HasValue ? Median.Value + " мс" : "—"; } }

    public static int Compare(AltLabStats left, AltLabStats right)
    {
        // Missing measurements sort below every measured result in the default (descending) order.
        int result = (left.Total > 0).CompareTo(right.Total > 0);
        if (result != 0) return result;
        result = (left.Success * right.Total).CompareTo(right.Success * left.Total);
        if (result != 0) return result;
        return (right.Median ?? Int64.MaxValue).CompareTo(left.Median ?? Int64.MaxValue);
    }
}

internal static class AltLab
{
    internal const int MaxCustomSites = 3;
    internal const int MaxProfiles = 5;
    internal const int MaxHistory = 20;
    internal const int DetailedAttempts = 3;
    internal const int TimeoutMs = 6000;
    internal const string All = "all", Video = "video", Voice = "voice", MySites = "my-sites";
    internal const string Discord = "discord.com", YouTube = "www.youtube.com";
    internal const string Legend = "Успех = HTTP 2xx. Время = медиана успешных ответов, от DNS до заголовков. " +
        "Прямой HTTPS GET / (TCP 443, TLS 1.2), до 6 с. Без прокси и переходов. Проверяется HTTPS, воспроизведение и звонки не проверяются.";
    internal static readonly string[] Sets = { All, Video, Voice, MySites };
    private const int MaxStateBytes = 1024 * 1024;
    private const int MaxHeaders = 16 * 1024;

    internal static string SetTitle(string set)
    {
        switch (set)
        {
            case Video: return "Видео";
            case Voice: return "Голос";
            case MySites: return "Мои сайты";
            default: return "Все сайты";
        }
    }

    internal static string SiteTitle(string host)
    {
        return host == Discord ? "Discord" : host == YouTube ? "YouTube" : host;
    }

    internal static AltLabSettings NormalizeSettings(AltLabSettings settings)
    {
        if (settings == null || !Sets.Contains(settings.SelectedSet)) throw new FormatException("Неизвестный набор сайтов ALT.");
        var sites = new List<string>();
        foreach (string value in settings.MySites ?? new string[0])
        {
            if (String.IsNullOrWhiteSpace(value)) continue;
            string host = NormalizeHost(value);
            if (host != Discord && host != YouTube && !sites.Contains(host)) sites.Add(host);
            if (sites.Count > MaxCustomSites) throw new FormatException("Можно сохранить до " + MaxCustomSites + " дополнительных сайтов.");
        }
        return new AltLabSettings { SelectedSet = settings.SelectedSet, Detailed = settings.Detailed, MySites = sites.ToArray() };
    }

    internal static string[] SitesFor(AltLabSettings settings)
    {
        switch (settings.SelectedSet)
        {
            case Video: return new[] { YouTube };
            case Voice: return new[] { Discord };
            case MySites: return settings.MySites.ToArray();
            default: return new[] { Discord, YouTube }.Concat(settings.MySites).ToArray();
        }
    }

    internal static string NormalizeHost(string value)
    {
        string input = (value ?? "").Trim();
        if (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) input = input.Substring(8);
        if (input.EndsWith("/", StringComparison.Ordinal)) input = input.Substring(0, input.Length - 1);
        if (input.EndsWith(".", StringComparison.Ordinal)) input = input.Substring(0, input.Length - 1);
        try
        {
            string host = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(input).ToLowerInvariant();
            IPAddress address;
            if (host.Length <= 253 && host.IndexOf('.') > 0 && !IPAddress.TryParse(host, out address) &&
                host.Split('.').All(label => label.Length > 0 && label.Length <= 63 &&
                    label[0] != '-' && label[label.Length - 1] != '-' && label.All(ch =>
                        ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' || ch == '-')) &&
                !new[] { ".localhost", ".local", ".internal", ".lan", ".home", ".arpa", ".onion", ".invalid", ".test" }
                    .Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal)))
                return host;
        }
        catch (ArgumentException) { }
        throw new FormatException("Укажите публичный домен или HTTPS-адрес главной страницы без порта, параметров и пути: " + input);
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        byte[] b = address.GetAddressBytes();
        if (b.Length == 4)
        {
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 &&
                !(b[0] == 100 && b[1] >= 64 && b[1] <= 127) && !(b[0] == 169 && b[1] == 254) &&
                !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) &&
                !(b[0] == 192 && (b[1] == 168 || b[1] == 0 && (b[2] == 0 || b[2] == 2) || b[1] == 88 && b[2] == 99)) &&
                !(b[0] == 198 && (b[1] == 18 || b[1] == 19 || b[1] == 51 && b[2] == 100)) &&
                !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        }
        return b.Length == 16 && address.ScopeId == 0 && (b[0] & 0xe0) == 0x20 &&
            !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8)) &&
            !(b[0] == 0x20 && b[1] == 0x02) && !(b[0] == 0x3f && b[1] == 0xff && b[2] < 0x10);
    }

    internal static async Task<AltLabSample> MeasureAsync(string host, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var clock = Stopwatch.StartNew();
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            Task timeout = Task.Delay(TimeoutMs, deadline.Token);
            Task<AltLabSample> request = ReadHeadersAsync(host, clock, deadline.Token);
            bool completed = await Task.WhenAny(request, timeout).ConfigureAwait(false) == request;
            deadline.Cancel();
            if (!completed) ObserveAbortedRequest(request);
            cancellation.ThrowIfCancellationRequested();
            return completed ? await request.ConfigureAwait(false) : Sample("Тайм-аут", clock);
        }
    }

    private static void ObserveAbortedRequest(Task request)
    {
        request.ContinueWith(failed => { failed.Exception.Handle(error => true); }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static async Task<AltLabSample> ReadHeadersAsync(string host, Stopwatch clock, CancellationToken cancellation)
    {
        string phase = "DNS";
        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host + ".").ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (addresses.Length == 0) return Sample("DNS", clock);
            if (addresses.Any(address => !IsPublicAddress(address))) return Sample("Непубличный DNS-адрес", clock);

            phase = "TCP";
            IPAddress primary = addresses[0];
            IPAddress fallback = addresses.FirstOrDefault(address => address.AddressFamily != primary.AddressFamily);
            // Connect to an address from the checked DNS answer. TLS still verifies the original host name.
            using (var client = await ConnectAsync(primary, fallback, cancellation).ConfigureAwait(false))
            using (cancellation.Register(client.Close))
            using (var stream = new SslStream(client.GetStream()))
            {
                phase = "TLS";
                await stream.AuthenticateAsClientAsync(host, null, SslProtocols.Tls12, false).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                phase = "HTTP";
                byte[] request = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: " + host +
                    "\r\nConnection: close\r\nCache-Control: no-cache, no-store\r\n\r\n");
                await stream.WriteAsync(request, 0, request.Length, cancellation).ConfigureAwait(false);
                int status = await ReadStatusAsync(stream, cancellation).ConfigureAwait(false);
                return status == 0 ? Sample("HTTP без корректных заголовков", clock) : Sample("HTTP", clock, status);
            }
        }
        catch (Exception error)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!(error is SocketException || error is IOException || error is AuthenticationException)) throw;
            return Sample(phase, clock);
        }
    }

    private static async Task<TcpClient> ConnectAsync(IPAddress address, IPAddress fallback, CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var client = new TcpClient(address.AddressFamily);
            try
            {
                using (cancellation.Register(client.Close))
                    await client.ConnectAsync(address, 443).ConfigureAwait(false);
                return client;
            }
            catch (SocketException)
            {
                client.Close();
                cancellation.ThrowIfCancellationRequested();
                if (fallback == null) throw;
                address = fallback;
                fallback = null;
            }
            catch { client.Close(); throw; }
        }
    }

    internal static async Task<int> ReadStatusAsync(Stream input, CancellationToken cancellation)
    {
        byte[] buffer = new byte[1024];
        var line = new StringBuilder(80);
        int headerBytes = 0, ending = 0, status = 0;
        while (headerBytes < MaxHeaders)
        {
            int read = await input.ReadAsync(buffer, 0, Math.Min(buffer.Length, MaxHeaders - headerBytes), cancellation).ConfigureAwait(false);
            if (read == 0) break;
            headerBytes += read;
            for (int i = 0; i < read; i++)
            {
                char ch = (char)buffer[i];
                if (status == 0)
                {
                    if (ch == '\n')
                    {
                        string[] parts = line.ToString().TrimEnd('\r').Split(new[] { ' ' }, 3);
                        if (ending != 1 || parts.Length < 2 || (parts[0] != "HTTP/1.1" && parts[0] != "HTTP/1.0") ||
                            parts[1].Length != 3 || !Int32.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out status) ||
                            status < 100 || status > 599) return 0;
                        line.Clear();
                    }
                    else if (line.Length < 512) line.Append(ch);
                    else return 0;
                }
                if (ch == '\r') ending = ending == 2 ? 3 : 1;
                else if (ch == '\n' && ending == 1) ending = 2;
                else if (ch == '\n' && ending == 3)
                {
                    if (status >= 200 || status == 101) return status;
                    status = ending = 0;
                }
                else ending = 0;
            }
        }
        return 0;
    }

    private static AltLabSample Sample(string kind, Stopwatch clock, int code = 0)
    {
        return new AltLabSample { Kind = kind, HttpCode = code, Milliseconds = clock.ElapsedMilliseconds };
    }

    internal static string StatePath(string name)
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (String.IsNullOrEmpty(root)) throw new IOException("Папка LocalAppData недоступна.");
        return Path.Combine(root, "Reze-Zapret", name);
    }

    internal static T Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        using (var file = File.OpenRead(path))
        {
            if (file.Length > MaxStateBytes) throw new IOException("Файл ALT слишком большой.");
            var data = new byte[(int)file.Length];
            int offset = 0;
            while (offset < data.Length)
            {
                int count = file.Read(data, offset, data.Length - offset);
                if (count == 0) throw new IOException("Файл ALT изменился во время чтения.");
                offset += count;
            }
            try { return Serializer().Deserialize<T>(Encoding.UTF8.GetString(data).TrimStart('\uFEFF')); }
            catch (ArgumentException error) { throw new IOException("Файл ALT повреждён.", error); }
            catch (InvalidOperationException error) { throw new IOException("Файл ALT повреждён.", error); }
        }
    }

    internal static void Save<T>(string path, T state)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = Serializer().Serialize(state);
            if (Encoding.UTF8.GetByteCount(json) > MaxStateBytes) throw new IOException("Файл ALT слишком большой.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static JavaScriptSerializer Serializer()
    {
        return new JavaScriptSerializer { MaxJsonLength = MaxStateBytes, RecursionLimit = 16 };
    }

    internal static bool ValidRun(AltLabRun run)
    {
        if (run == null || run.DateUtc == DateTime.MinValue || String.IsNullOrEmpty(run.Version) || !Sets.Contains(run.Set) ||
            run.Attempts != 1 && run.Attempts != DetailedAttempts || run.Sites == null || run.Sites.Length == 0 ||
            run.Sites.Length > MaxCustomSites + 2 || run.Rows == null || run.Rows.Count == 0 || run.Rows.Count > MaxProfiles + 1)
            return false;
        try
        {
            if (run.Sites.Any(site => site != NormalizeHost(site)) || run.Sites.Distinct().Count() != run.Sites.Length) return false;
        }
        catch (FormatException) { return false; }
        return run.Rows.All(row => row != null && !String.IsNullOrEmpty(row.Key) && !String.IsNullOrEmpty(row.Title) && row.Sites != null &&
            row.Sites.All(pair => run.Sites.Contains(pair.Key) && pair.Value != null && pair.Value.Count <= run.Attempts &&
            pair.Value.All(sample => sample != null && !String.IsNullOrEmpty(sample.Kind) && sample.Milliseconds >= 0 &&
                sample.Milliseconds <= Int32.MaxValue && (sample.HttpCode == 0 || sample.HttpCode >= 100 && sample.HttpCode <= 599)))) &&
            run.Rows.Select(row => row.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == run.Rows.Count;
    }

    internal static string Csv(AltLabRun before, AltLabRun after)
    {
        var csv = new StringBuilder("run,date_utc,flowseal,test_set,attempts_planned,profile,host,attempt,outcome,http_status,elapsed_ms,success,total,median_success_ms,note\r\n");
        if (before != null) AppendCsv(csv, "before", before);
        AppendCsv(csv, before == null ? "run" : "after", after);
        return csv.ToString();
    }

    private static void AppendCsv(StringBuilder csv, string side, AltLabRun run)
    {
        foreach (AltLabRow row in run.Rows)
        foreach (string site in run.Sites)
        {
            IList<AltLabSample> samples = row.Samples(site);
            var stats = new AltLabStats(samples);
            for (int index = 0; index < Math.Max(1, stats.Total); index++)
            {
                AltLabSample sample = stats.Total > 0 ? samples[index] : null;
                string[] fields = { side, run.DateUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture), run.Version,
                    run.Set, Number(run.Attempts), row.Title, site, sample == null ? "" : Number(index + 1),
                    sample == null ? "" : sample.Kind, sample == null || sample.HttpCode == 0 ? "" : Number(sample.HttpCode),
                    sample == null ? "" : Number(sample.Milliseconds), Number(stats.Success), Number(stats.Total),
                    stats.Median.HasValue ? Number(stats.Median.Value) : "", row.Note };
                csv.Append(String.Join(",", fields.Select(CsvField))).Append("\r\n");
            }
        }
    }

    private static string Number(long number) { return number.ToString(CultureInfo.InvariantCulture); }

    private static string CsvField(string value)
    {
        value = value ?? "";
        string trimmed = value.TrimStart();
        if (value.StartsWith("\t", StringComparison.Ordinal) || value.StartsWith("\r", StringComparison.Ordinal) ||
            value.StartsWith("\n", StringComparison.Ordinal) || trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0)
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
