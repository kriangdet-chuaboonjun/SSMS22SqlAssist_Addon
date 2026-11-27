using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;

namespace SqlAssist.Setup
{
    /// <summary>Everything the installer does, without any UI: shared by the window and by /quiet command-line runs.</summary>
    internal static class SetupEngine
    {
        public const string VsixId = "SqlAssist.9cc4aed8-f2ad-4120-ba6f-222b08aa04a0";
        private const string DefaultSsmsIde = @"C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE";

        /// <summary>From Directory.Build.props — the same number the embedded .vsix carries.</summary>
        public static readonly string SetupVersion = typeof(SetupEngine).Assembly.GetName().Version.ToString(3);

        public static int RunningSsmsCount() => Process.GetProcessesByName("Ssms").Length;

        public static string FindSsmsIde()
        {
            // vswhere lists SSMS 22 too, since it installs through the Visual Studio Installer.
            string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft Visual Studio\Installer\vswhere.exe");
            if (File.Exists(vswhere))
            {
                try
                {
                    var psi = new ProcessStartInfo(vswhere, "-all -prerelease -products * -property installationPath")
                    {
                        UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                    };
                    using (var p = Process.Start(psi))
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string ide = Path.Combine(line.Trim(), @"Common7\IDE");
                            if (line.IndexOf("Management Studio 22", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                File.Exists(Path.Combine(ide, "Ssms.exe")))
                                return ide;
                        }
                    }
                }
                catch
                {
                    // Fall back to the default location.
                }
            }
            return File.Exists(Path.Combine(DefaultSsmsIde, "Ssms.exe")) ? DefaultSsmsIde : null;
        }

        /// <summary>Reads the version from the installed copy's extension.vsixmanifest: per-user installs live under
        /// %LOCALAPPDATA%\Microsoft\SSMS\22.0_*\Extensions, machine-wide ones under the SSMS folder.</summary>
        public static string FindInstalledVersion(string ide)
        {
            string manifest = FindInstalledManifest(ide);
            if (manifest == null) return null;
            try
            {
                return (string)ReadIdentity(XDocument.Load(manifest))?.Attribute("Version");
            }
            catch
            {
                return null;
            }
        }

        private static XElement ReadIdentity(XDocument manifest) =>
            manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");

        private static string FindInstalledManifest(string ide)
        {
            var roots = new List<string> { Path.Combine(ide, "Extensions") };
            string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS");
            if (Directory.Exists(local))
                roots.AddRange(Directory.GetDirectories(local, "22.0_*").Select(d => Path.Combine(d, "Extensions")));

            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var manifest in SafeFiles(root))
                {
                    try
                    {
                        var identity = ReadIdentity(XDocument.Load(manifest));
                        if ((string)identity?.Attribute("Id") == VsixId) return manifest;
                    }
                    catch
                    {
                        // Unreadable manifest of some other extension — skip it.
                    }
                }
            }
            return null;
        }

        /// <summary>-1 installed is older, 0 same, 1 installed is newer; null when either is unknown.</summary>
        public static int? CompareInstalled(string installed)
        {
            if (installed == null || !Version.TryParse(installed, out var i) || !Version.TryParse(SetupVersion, out var s)) return null;
            return i.CompareTo(s) < 0 ? -1 : i.CompareTo(s) > 0 ? 1 : 0;
        }

        public static bool DbForgeInstalled(string ide) => Directory.Exists(Path.Combine(ide, @"Extensions\Devart"));

        /// <summary>One step of an install or uninstall, for the window to show while it runs.</summary>
        public struct Step
        {
            public Step(int number, int total, string text) { Number = number; Total = total; Text = text; }
            public int Number { get; }
            public int Total { get; }
            public string Text { get; }
        }

        /// <summary>Installs (or updates over) the embedded .vsix. Null on success, otherwise the error text.</summary>
        public static string Install(string ide, IProgress<Step> progress = null) =>
            RunExclusive(() => InstallCore(ide, progress));

        private static string InstallCore(string ide, IProgress<Step> progress)
        {
            const int total = 4;
            string vsix = Path.Combine(Path.GetTempPath(), "SqlAssist-" + Guid.NewGuid().ToString("N") + ".vsix");
            string backup = null;
            bool keepBackup = false;
            try
            {
                progress?.Report(new Step(1, total, "เตรียมไฟล์ add-on"));
                using (var res = typeof(SetupEngine).Assembly.GetManifestResourceStream("SqlAssist.vsix"))
                    CopyPackage(res, vsix, SetupVersion);

                string installedManifest = FindInstalledManifest(ide);
                string installedVersion = null;
                if (installedManifest != null)
                {
                    installedVersion = (string)ReadIdentity(XDocument.Load(installedManifest))?.Attribute("Version");
                    if (!Version.TryParse(installedVersion, out _))
                        return "อ่านเวอร์ชัน SqlAssist ที่ติดตั้งอยู่ไม่ได้ จึงหยุดการอัปเดต";

                    backup = Path.Combine(Path.GetDirectoryName(InstalledSetupPath), "recovery",
                        "SqlAssist-" + installedVersion + "-" + Guid.NewGuid().ToString("N") + ".vsix");
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(backup));
                        // InstalledSetupPath is the cached installer for the currently installed version.
                        // Load only its metadata/resources; never execute the previous setup.
                        var previousSetup = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(InstalledSetupPath));
                        using (var res = previousSetup.GetManifestResourceStream("SqlAssist.vsix"))
                            CopyPackage(res, backup, installedVersion);
                        SetupLog.Info("SqlAssist " + installedVersion + " recovery package: " + backup);
                    }
                    catch (Exception ex)
                    {
                        SetupLog.Error("Preparing VSIX recovery: " + ex.Message);
                        return "เตรียมไฟล์กู้คืน SqlAssist " + installedVersion + " ไม่สำเร็จ จึงหยุดการอัปเดต: " + ex.Message;
                    }
                }

                string error = VsixUpgrade.Replace(vsix, installedVersion, backup,
                    () =>
                    {
                        progress?.Report(new Step(2, total, "ถอน SqlAssist ที่ติดตั้งอยู่"));
                        return RunInstaller(ide, "/quiet /uninstall:" + VsixId);
                    },
                    package =>
                    {
                        string action = package == backup ? "กู้คืน SqlAssist " + installedVersion : "ติดตั้ง SqlAssist " + SetupVersion;
                        progress?.Report(new Step(3, total, action + " ลงใน SSMS"));
                        return RunInstaller(ide, "/quiet \"" + package + "\"");
                    },
                    () => FindInstalledVersion(ide), out keepBackup);

                progress?.Report(new Step(4, total, "ล้าง cache ของ SSMS"));
                try { ClearComponentCache(); }
                catch (Exception ex) { SetupLog.Error("Clearing component cache: " + ex.Message); }
                if (error != null)
                {
                    SetupLog.Error("SqlAssist install: " + error);
                    return error;
                }
                RegisterUninstaller(ide);
                return null;
            }
            finally
            {
                TryDelete(() => File.Delete(vsix));
                if (backup != null && !keepBackup) TryDelete(() => File.Delete(backup));
            }
        }

        private static void CopyPackage(Stream resource, string path, string version)
        {
            if (resource == null) throw new InvalidDataException("ไม่พบ VSIX ในไฟล์ติดตั้ง");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite))
            {
                resource.CopyTo(file);
                file.Position = 0;
                using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    var manifest = archive.GetEntry("extension.vsixmanifest");
                    if (manifest == null) throw new InvalidDataException("ไม่พบ manifest ใน VSIX");
                    using (var stream = manifest.Open())
                    {
                        var identity = ReadIdentity(XDocument.Load(stream));
                        if ((string)identity?.Attribute("Id") != VsixId || (string)identity.Attribute("Version") != version)
                            throw new InvalidDataException("รหัสหรือเวอร์ชันของ VSIX ไม่ตรงกับ SqlAssist " + version);
                    }
                }
            }
        }

        private static string RunExclusive(Func<string> operation)
        {
            using (var mutex = new Mutex(false, @"Local\SqlAssist.Setup.Installation"))
            {
                try
                {
                    if (!mutex.WaitOne(0)) return "SqlAssist Setup กำลังทำงานในหน้าต่างอื่น";
                }
                catch (AbandonedMutexException)
                {
                    // The previous setup exited before releasing the lock; this process now owns it.
                }

                try
                {
                    if (RunningSsmsCount() > 0) return "ปิด SSMS ทุกหน้าต่างก่อนติดตั้งหรือถอน SqlAssist";
                    return operation();
                }
                catch (Exception ex)
                {
                    SetupLog.Error("SqlAssist Setup: " + ex.Message);
                    return ex.Message;
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        public static string Uninstall(string ide, IProgress<Step> progress = null) =>
            RunExclusive(() => UninstallCore(ide, progress));

        private static string UninstallCore(string ide, IProgress<Step> progress)
        {
            const int total = 2;
            progress?.Report(new Step(1, total, "ถอน SqlAssist ออกจาก SSMS"));
            int code = RunInstaller(ide, "/quiet /uninstall:" + VsixId);
            if (code != 0) return VsixUpgrade.InstallerError(code);
            progress?.Report(new Step(2, total, "ล้าง cache ของ SSMS"));
            ClearComponentCache();
            UnregisterUninstaller();
            return null;
        }

        // ---- Programs and Features ----------------------------------------------------------------------------
        // The extension itself is installed by VSIXInstaller, which adds nothing to Programs and Features. So after an
        // install the setup exe copies itself to %LOCALAPPDATA%\SqlAssist (the downloaded copy may be deleted) and
        // writes a per-user Uninstall key pointing at that copy with /remove, which opens the window on "uninstall".

        private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SqlAssist";

        public static string InstalledSetupPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlAssist", "SqlAssistSetup.exe");

        private static string RunningExe => typeof(SetupEngine).Assembly.Location;

        public static void RegisterUninstaller(string ide)
        {
            try
            {
                string target = InstalledSetupPath;
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (!string.Equals(Path.GetFullPath(RunningExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    File.Copy(RunningExe, target, true);

                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    key.SetValue("DisplayName", "SqlAssist for SSMS 22");
                    key.SetValue("DisplayVersion", SetupVersion);
                    key.SetValue("Publisher", "SqlAssist");
                    key.SetValue("DisplayIcon", target + ",0");
                    key.SetValue("InstallLocation", Path.GetDirectoryName(target));
                    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    key.SetValue("UninstallString", $"\"{target}\" /remove");
                    key.SetValue("QuietUninstallString", $"\"{target}\" /uninstall");
                    key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    long kb = (ExtensionSize(ide) + new FileInfo(target).Length) / 1024;
                    key.SetValue("EstimatedSize", (int)Math.Min(kb, int.MaxValue), Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch
            {
                // The extension is installed either way; only the Programs and Features entry is missing.
            }
        }

        /// <summary>Removes the Programs and Features entry and the copied setup exe. When that copy is the one running
        /// (uninstall started from Programs and Features), a short-lived cmd deletes it once this process has exited.</summary>
        public static void UnregisterUninstaller()
        {
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { /* already gone */ }

            string target = InstalledSetupPath;
            if (!File.Exists(target)) return;
            if (!string.Equals(Path.GetFullPath(RunningExe), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(() => File.Delete(target));
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 4 > nul & del /f /q \"{target}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch
            {
                // Left behind: a 270 KB file in %LOCALAPPDATA%\SqlAssist.
            }
        }

        public static bool UninstallerRegistered
        {
            get
            {
                try { using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKey)) return key != null; }
                catch { return false; }
            }
        }

        private static long ExtensionSize(string ide)
        {
            try
            {
                string dir = Path.GetDirectoryName(FindInstalledManifest(ide));
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }
            catch
            {
                return 0;
            }
        }

        // SSMS's own VSIXInstaller: it targets the SSMS instance, where Visual Studio's would not.
        private static int RunInstaller(string ide, string arguments)
        {
            var psi = new ProcessStartInfo(Path.Combine(ide, "VSIXInstaller.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // SSMS caches its MEF parts; without clearing, a new or removed extension may not be noticed.
        // It also caches every extension's .imagemanifest in ImageLibrary.cache, so a changed toolbar icon
        // would keep showing the old image until that file is rebuilt.
        private static void ClearComponentCache()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\SSMS");
            if (!Directory.Exists(root)) return;
            foreach (var dir in Directory.GetDirectories(root, "22.0_*"))
            {
                string images = Path.Combine(dir, @"ImageLibrary\ImageLibrary.cache");
                if (File.Exists(images)) TryDelete(() => File.Delete(images));

                string cache = Path.Combine(dir, "ComponentModelCache");
                if (!Directory.Exists(cache)) continue;
                foreach (var f in Directory.GetFiles(cache)) TryDelete(() => File.Delete(f));
                foreach (var d in Directory.GetDirectories(cache)) TryDelete(() => Directory.Delete(d, true));
            }
        }

        private static IEnumerable<string> SafeFiles(string root)
        {
            try { return Directory.GetFiles(root, "extension.vsixmanifest", SearchOption.AllDirectories); }
            catch { return Enumerable.Empty<string>(); }
        }

        private static void TryDelete(Action delete)
        {
            try { delete(); } catch { /* locked or already gone */ }
        }
    }
}
