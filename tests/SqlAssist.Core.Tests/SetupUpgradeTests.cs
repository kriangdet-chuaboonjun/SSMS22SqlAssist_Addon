using System;
using System.IO;
using SqlAssist.Setup;
using Xunit;

namespace SqlAssist.Core.Tests
{
    public class SetupUpgradeTests
    {
        private const string OldVersion = "1.6.0";
        private const string NewVersion = "1.6.1";
        private const string NewPackage = "new.vsix";
        private const string RecoveryPackage = "recovery.vsix";

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedInstallRestoresPreviousVersion(bool throws)
        {
            var installer = new Installer { Version = OldVersion, InstallError = true, Throws = throws };

            string error = Replace(installer, RecoveryPackage, out bool keepBackup);

            Assert.Equal(OldVersion, installer.Version);
            Assert.Contains("กู้คืน", error);
            Assert.False(keepBackup);
        }

        [Fact]
        public void FailedRecoveryReportsAndRetainsTheRecoveryPackage()
        {
            var installer = new Installer { Version = OldVersion, InstallError = true, RecoveryError = true };

            string error = Replace(installer, RecoveryPackage, out bool keepBackup);

            Assert.Null(installer.Version);
            Assert.Contains(RecoveryPackage, error);
            Assert.True(keepBackup);
        }

        [Fact]
        public void UpgradeWithoutRecoveryPackageLeavesPreviousVersionInstalled()
        {
            var installer = new Installer { Version = OldVersion };

            string error = Replace(installer, null, out _);

            Assert.Equal(OldVersion, installer.Version);
            Assert.NotNull(error);
            Assert.Equal(0, installer.UninstallCount);
        }

        [Fact]
        public void UninstallFailureStopsTheUpgrade()
        {
            var installer = new Installer { Version = OldVersion, UninstallError = true };

            string error = Replace(installer, RecoveryPackage, out _);

            Assert.Equal(OldVersion, installer.Version);
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData(OldVersion, 1)]
        public void SuccessfulInstallUsesUninstallOnlyWhenReplacing(string installedVersion, int uninstallCount)
        {
            var installer = new Installer { Version = installedVersion };

            Assert.Null(Replace(installer, installedVersion == null ? null : RecoveryPackage, out bool keepBackup));
            Assert.Equal(NewVersion, installer.Version);
            Assert.Equal(uninstallCount, installer.UninstallCount);
            Assert.False(keepBackup);
        }

        private static string Replace(Installer installer, string backup, out bool keepBackup) =>
            VsixUpgrade.Replace(NewPackage, installer.Version, backup, installer.Uninstall, installer.Install,
                () => installer.Version, out keepBackup);

        private sealed class Installer
        {
            public string Version;
            public bool UninstallError;
            public bool InstallError;
            public bool RecoveryError;
            public bool Throws;
            public int UninstallCount;

            public int Uninstall()
            {
                UninstallCount++;
                if (UninstallError) return 1;
                Version = null;
                return 0;
            }

            public int Install(string package)
            {
                switch (package)
                {
                    case NewPackage:
                        if (Throws) throw new IOException("Installer could not start");
                        if (InstallError) return 1;
                        Version = NewVersion;
                        return 0;
                    case RecoveryPackage:
                        if (RecoveryError) return 1;
                        Version = OldVersion;
                        return 0;
                    default:
                        throw new ArgumentException(package);
                }
            }
        }
    }
}
