using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

internal static class Check
{
    private const string TreeProfile = "general (Tree test).bat";
    private const string PidFile = "tree-pids.txt";
    private const string StopFile = "stop-tree.txt";
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

    private static int Main(string[] args)
    {
        try
        {
            if (args[0] == "--worker")
            {
                RunWorker();
                return 0;
            }
            if (args[0] == "--leaf")
            {
                WaitForFile(StopFile, 30000);
                return 0;
            }

            string bundle = Path.Combine(args[0], "Synthetic Flowseal bundle");
            CreateBundle(bundle);
            CheckProfileFiles(bundle);
            Console.WriteLine("PASS ProfileFiles (spaced path, UTF-8 IDN, deduplication, preservation, backup, final removal)");
            CheckSession(bundle);
            Console.WriteLine("PASS Session (job membership and process-tree stop)");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void CreateBundle(string bundle)
    {
        Directory.CreateDirectory(Path.Combine(bundle, "lists"));
        Directory.CreateDirectory(Path.Combine(bundle, "bin"));
        File.WriteAllBytes(Path.Combine(bundle, "bin", "winws.exe"), new byte[0]);
        File.WriteAllText(Path.Combine(bundle, "service.bat"), "");
        File.WriteAllText(Path.Combine(bundle, "general.bat"), "");
        File.WriteAllText(Path.Combine(bundle, TreeProfile), "@echo off\r\nstart \"Synthetic profile\" /min \".\\SessionCheck.exe\" --worker\r\n");
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(bundle, "SessionCheck.exe"));
    }

    private static void CheckProfileFiles(string bundle)
    {
        Require(ProfileFiles.IsBundle(bundle), "Synthetic bundle was not recognized.");
        Require(ProfileFiles.Profiles(bundle).SequenceEqual(new[] { "general.bat", TreeProfile }), "Profile discovery or ordering failed.");
        string list = ProfileFiles.ListPath(bundle, false);
        string backup = list + ".reze.bak";
        string[] original = { "# комментарий", "", "  Keep.Example  ", "KEEP.EXAMPLE" };
        File.WriteAllLines(list, original, Utf8);
        byte[] originalBytes = File.ReadAllBytes(list);
        Require(ProfileFiles.Entries(list).SequenceEqual(new[] { "keep.example" }), "Existing case variants were not deduplicated.");

        string idn = ProfileFiles.Normalize("https://Пример.РФ/страница");
        Require(idn == "xn--e1afmkfd.xn--p1ai", "UTF-8 IDN conversion failed.");
        string[] duplicates = { idn, idn.ToUpperInvariant() };
        Require(ProfileFiles.Update(list, duplicates, false) == 1, "Duplicate input produced multiple additions.");
        Require(ProfileFiles.Update(list, duplicates, false) == 0, "Re-adding the domain was not a no-op.");
        Require(File.ReadAllLines(list, Utf8).SequenceEqual(original.Concat(new[] { idn })), "Add changed unrelated list lines.");
        Require(File.ReadAllBytes(backup).SequenceEqual(originalBytes), "Backup differs from the original file.");

        Require(ProfileFiles.Update(list, new[] { "keep.example" }, true) == 1, "Removal did not count one domain.");
        Require(ProfileFiles.Entries(list).SequenceEqual(new[] { idn }), "Removal did not remove all case variants.");
        byte[] beforeFinalRemoval = File.ReadAllBytes(list);
        Require(ProfileFiles.Update(list, new[] { idn }, true) == 1, "Final domain removal failed.");
        Require(ProfileFiles.Entries(list).Length == 0, "An entry remained after final removal.");
        Require(File.ReadAllLines(list, Utf8).SequenceEqual(new[] { original[0], "", "domain.example.abc" }), "Final removal lost unrelated lines or did not leave the placeholder.");
        Require(File.ReadAllBytes(backup).SequenceEqual(beforeFinalRemoval), "Backup was not refreshed before final removal.");
        byte[] beforeRestore = File.ReadAllBytes(list);
        ProfileFiles.RestoreBackup(list);
        Require(File.ReadAllBytes(list).SequenceEqual(beforeFinalRemoval), "Restore did not recover the previous list.");
        Require(File.ReadAllBytes(backup).SequenceEqual(beforeRestore), "Restore did not keep the superseded list.");
        string exclude = ProfileFiles.ListPath(bundle, true);
        Require(ProfileFiles.Update(exclude, new[] { "separate.example" }, false) == 1, "Exclude list was not created.");
        Require(ProfileFiles.Entries(exclude).SequenceEqual(new[] { "separate.example" }), "Exclude list did not retain its entry.");
        Require(ProfileFiles.Entries(list).SequenceEqual(new[] { idn }), "Editing the exclude list changed the include list.");
    }

    private static void CheckSession(string bundle)
    {
        try
        {
            using (Session session = Session.Start(bundle, TreeProfile))
            {
                string pids = Path.Combine(bundle, PidFile);
                Require(WaitForFile(pids, 8000), "Synthetic process tree did not start in the bundle folder.");
                int[] ids = File.ReadAllLines(pids).Select(line => Int32.Parse(line, CultureInfo.InvariantCulture)).ToArray();
                using (Process worker = Process.GetProcessById(ids[0]))
                using (Process leaf = Process.GetProcessById(ids[1]))
                using (Process driver = Process.GetCurrentProcess())
                {
                    Require(session.IsActive, "The running job reported no active processes.");
                    Require(session.Contains(worker), "The worker was outside the session job.");
                    Require(session.Contains(leaf), "The worker's child was outside the session job.");
                    Require(!session.Contains(driver), "The check runner was inside the session job.");
                    session.Dispose();
                    Require(worker.WaitForExit(5000), "The worker survived session disposal.");
                    Require(leaf.WaitForExit(5000), "The worker's child survived session disposal.");
                }
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(bundle, StopFile), "");
        }
    }

    private static void RunWorker()
    {
        var info = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--leaf") { UseShellExecute = false };
        using (Process worker = Process.GetCurrentProcess())
        using (Process leaf = Process.Start(info))
        {
            File.WriteAllLines(PidFile + ".tmp", new[] { worker.Id.ToString(CultureInfo.InvariantCulture), leaf.Id.ToString(CultureInfo.InvariantCulture) });
            File.Move(PidFile + ".tmp", PidFile);
            leaf.WaitForExit(31000);
        }
    }

    private static bool WaitForFile(string path, int milliseconds)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!File.Exists(path) && elapsed.ElapsedMilliseconds < milliseconds)
            Thread.Sleep(25);
        return File.Exists(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
