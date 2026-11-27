using System;

namespace SqlAssist.Setup
{
    internal static class VsixUpgrade
    {
        /// <summary>Replaces an installed VSIX only with a verified backup available. Returns the failure even if rolled back.</summary>
        internal static string Replace(string package, string installedVersion, string backup,
            Func<int> uninstall, Func<string, int> install, Func<string> getInstalledVersion, out bool keepBackup)
        {
            keepBackup = false;
            if (installedVersion == null) return Run(() => install(package));
            if (backup == null) return "หาไฟล์กู้คืนที่ตรงกับ SqlAssist " + installedVersion + " ไม่ได้ จึงหยุดการอัปเดต";

            string error = Run(uninstall);
            bool removed = error == null;
            if (removed)
            {
                error = Run(() => install(package));
                if (error == null) return null;
            }

            string recoveryError = Run(() =>
            {
                string currentVersion = getInstalledVersion();
                if (!removed && currentVersion == installedVersion) return null;

                // The failed installer can leave a partially installed extension.
                if (currentVersion != null)
                {
                    string cleanupError = Run(uninstall);
                    if (cleanupError != null) return cleanupError;
                }

                string installError = Run(() => install(backup));
                if (installError != null) return installError;
                return getInstalledVersion() == installedVersion
                    ? null : "ตรวจสอบเวอร์ชันที่กู้คืนไม่ได้";
            });

            if (recoveryError == null)
                return error + "\nตรวจพบ SqlAssist เวอร์ชันเดิม (" + installedVersion + ") หลังขั้นตอนกู้คืน";

            keepBackup = true;
            return error + "\nกู้คืน SqlAssist " + installedVersion + " ไม่สำเร็จ (" + recoveryError
                + ")\nไฟล์สำหรับติดตั้งเวอร์ชันเดิม: " + backup;
        }

        private static string Run(Func<int> action) => Run(() =>
        {
            int code = action();
            return code == 0 ? null : InstallerError(code);
        });

        private static string Run(Func<string> action)
        {
            try { return action(); }
            catch (Exception ex) { return ex.Message; }
        }

        internal static string InstallerError(int code) =>
            $"VSIXInstaller exited with code {code}.\nดูรายละเอียดได้ที่ %TEMP%\\dd_VSIXInstaller_*.log";
    }
}
