using System;
using System.Globalization;
using System.IO;
using System.Text;
using TopSpeed.Localization;
using TopSpeed.Server.Control;

namespace TopSpeed.Server.Service
{
    /// <summary>
    /// Prepares what systemd or launchd needs and says what to run, rather than doing it.
    ///
    /// Installing a system service on these platforms means writing into a directory only root
    /// owns, and there is no equivalent of the consent prompt Windows shows: the answer is to
    /// run one command with sudo. A program that tries to obtain root for itself is worse than
    /// one that hands over an exact, readable file and the two lines that install it, because
    /// whoever types those lines can see beforehand precisely what they are agreeing to.
    /// </summary>
    internal sealed class UnixServiceManager : IServiceManager
    {
        public ServiceStatus Query(string directory)
        {
            // Asking systemd or launchd would mean running them, and their answers are not
            // worth shelling out for when this platform never installs anything itself.
            return new ServiceStatus(ServiceInstallState.Unsupported, UnitNameFor(directory), false);
        }

        public ServiceActionResult Install(string directory, bool startAutomatically)
        {
            if (ServiceIdentity.IsProtectedLocation(directory, out var location))
            {
                return ServiceActionResult.Failed(LocalizationService.Format(
                    LocalizationService.Mark("This server cannot be installed as a service from {0}. The server updates itself in place, so it must run from a folder it can write to, and giving a service write access inside a protected location would let anything that can write there run as that service later. Move the server folder somewhere else, such as a folder you created yourself, and install it from there."),
                    location ?? string.Empty));
            }

            try
            {
                return OperatingSystem.IsMacOS()
                    ? WriteLaunchd(directory)
                    : WriteSystemd(directory);
            }
            catch (IOException ex)
            {
                return ServiceActionResult.Failed(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return ServiceActionResult.Failed(ex.Message);
            }
        }

        public ServiceActionResult Uninstall(string directory)
        {
            var name = UnitNameFor(directory);
            var text = OperatingSystem.IsMacOS()
                ? LocalizationService.Format(
                    LocalizationService.Mark("To remove the service, run:\n  sudo launchctl bootout system/{0}\n  sudo rm /Library/LaunchDaemons/{0}.plist"),
                    name)
                : LocalizationService.Format(
                    LocalizationService.Mark("To remove the service, run:\n  sudo systemctl disable --now {0}\n  sudo rm /etc/systemd/system/{0}\n  sudo systemctl daemon-reload"),
                    name);

            return ServiceActionResult.Ok(text);
        }

        public ServiceActionResult Start(string directory)
        {
            var name = UnitNameFor(directory);
            return ServiceActionResult.Ok(OperatingSystem.IsMacOS()
                ? LocalizationService.Format(LocalizationService.Mark("To start it, run:\n  sudo launchctl kickstart -k system/{0}"), name)
                : LocalizationService.Format(LocalizationService.Mark("To start it, run:\n  sudo systemctl start {0}"), name));
        }

        public ServiceActionResult Stop(string directory)
        {
            var name = UnitNameFor(directory);
            return ServiceActionResult.Ok(OperatingSystem.IsMacOS()
                ? LocalizationService.Format(LocalizationService.Mark("To stop it, run:\n  sudo launchctl bootout system/{0}"), name)
                : LocalizationService.Format(LocalizationService.Mark("To stop it, run:\n  sudo systemctl stop {0}"), name));
        }

        private static ServiceActionResult WriteSystemd(string directory)
        {
            var folder = ServiceIdentity.DisplayPath(directory);
            var name = UnitNameFor(directory);
            var path = Path.Combine(folder, name);

            var unit = new StringBuilder();
            unit.Append("[Unit]\n");
            unit.Append("Description=").Append(ServiceIdentity.DisplayNameFor(directory)).Append('\n');
            // Not merely "network", which is satisfied before an address exists.
            unit.Append("Wants=network-online.target\n");
            unit.Append("After=network-online.target\n\n");
            unit.Append("[Service]\n");
            unit.Append("Type=simple\n");
            unit.Append("ExecStart=").Append(ServiceIdentity.ExecutablePathFor(directory)).Append('\n');
            unit.Append("WorkingDirectory=").Append(folder).Append('\n');
            // The account that owns the folder, so the server can still write its settings, its
            // log and its own updates. Nothing new is created and no password exists.
            unit.Append("User=").Append(Environment.UserName).Append('\n');
            // The server exits on its own to apply an update and counts on being brought back.
            unit.Append("Restart=always\n");
            unit.Append("RestartSec=5\n\n");
            unit.Append("[Install]\n");
            unit.Append("WantedBy=multi-user.target\n");

            File.WriteAllText(path, unit.ToString());

            return ServiceActionResult.Ok(LocalizationService.Format(
                LocalizationService.Mark("Wrote {0}.\n\nCheck it, then install it by running:\n  sudo cp {0} /etc/systemd/system/{1}\n  sudo systemctl enable --now {1}\n\nAfter that it starts with the machine. To see how it is doing:\n  systemctl status {1}"),
                path,
                name));
        }

        private static ServiceActionResult WriteLaunchd(string directory)
        {
            var folder = ServiceIdentity.DisplayPath(directory);
            var label = UnitNameFor(directory);
            var path = Path.Combine(folder, label + ".plist");

            var plist = new StringBuilder();
            plist.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            plist.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
            plist.Append("<plist version=\"1.0\">\n<dict>\n");
            AppendKey(plist, "Label", label);
            plist.Append("  <key>ProgramArguments</key>\n  <array>\n    <string>")
                .Append(Escape(ServiceIdentity.ExecutablePathFor(directory)))
                .Append("</string>\n  </array>\n");
            AppendKey(plist, "WorkingDirectory", folder);
            AppendKey(plist, "UserName", Environment.UserName);
            plist.Append("  <key>RunAtLoad</key>\n  <true/>\n");
            plist.Append("  <key>KeepAlive</key>\n  <true/>\n");
            plist.Append("</dict>\n</plist>\n");

            File.WriteAllText(path, plist.ToString());

            return ServiceActionResult.Ok(LocalizationService.Format(
                LocalizationService.Mark("Wrote {0}.\n\nCheck it, then install it by running:\n  sudo cp {0} /Library/LaunchDaemons/{1}.plist\n  sudo launchctl bootstrap system /Library/LaunchDaemons/{1}.plist\n\nAfter that it starts with the machine."),
                path,
                label));
        }

        private static void AppendKey(StringBuilder builder, string key, string value)
        {
            builder.Append("  <key>").Append(key).Append("</key>\n  <string>").Append(Escape(value)).Append("</string>\n");
        }

        private static string Escape(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal);
        }

        /// <summary>
        /// Built from the same per folder key as everything else, so two servers on one machine
        /// produce two units that cannot collide, lowercased because these systems conventionally
        /// expect that.
        /// </summary>
        public static string UnitNameFor(string directory)
        {
            var key = ServiceIdentity.NameFor(directory).ToLowerInvariant();
            return OperatingSystem.IsMacOS()
                ? string.Format(CultureInfo.InvariantCulture, "org.{0}", key)
                : key + ".service";
        }
    }
}
