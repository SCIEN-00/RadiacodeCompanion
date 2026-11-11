using System.Diagnostics;

namespace Radiacode.App;

/// <summary>Best-effort desktop notifications: Windows App SDK toasts on Windows, freedesktop notifications on Linux.</summary>
internal static class Notifier
{
#if WINDOWS10_0_17763_0_OR_GREATER
    private static bool _registered;
#endif

    /// <summary>Last failure, for diagnostics (null when the last notification was handed over fine).</summary>
    public static string? LastError { get; private set; }

#if WINDOWS10_0_17763_0_OR_GREATER
    private static bool IsPackaged()
    {
        try { return Windows.ApplicationModel.Package.Current is not null; }
        catch { return false; }
    }
#endif

    public static void Show(string title, string body, bool urgent)
    {
        try
        {
#if WINDOWS10_0_17763_0_OR_GREATER
            var manager = Microsoft.Windows.AppNotifications.AppNotificationManager.Default;
            if (!_registered)
            {
                // Unpackaged app: Windows only shows toasts for an AUMID that has a display name (and icon),
                // so register with both instead of the parameterless Register().
                // The docs require a NotificationInvoked handler before Register(); clicking a toast just shows the app.
                manager.NotificationInvoked += (_, _) => App.MainWindow?.DispatcherQueue.TryEnqueue(() => App.MainWindow?.Activate());
                if (IsPackaged())
                {
                    // MSIX (Store) build: identity, name and icon come from the package manifest.
                    manager.Register();
                }
                else
                {
                    var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "icon_foreground.scale-200.png");
                    manager.Register("Radiacode Companion", new Uri(icon));
                }
                _registered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { manager.Unregister(); } catch { } };
            }
            var builder = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                .AddText(title)
                .AddText(body);
            if (urgent) builder.SetScenario(Microsoft.Windows.AppNotifications.Builder.AppNotificationScenario.Urgent);
            manager.Show(builder.BuildNotification());
            LastError = null;
#else
            if (OperatingSystem.IsLinux()) LastError = ShowLinux(title, body, urgent) ? null : "notify-send / gdbus not available";
#endif
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            Debug.WriteLine($"Notification failed: {LastError}");
        }
    }

    private static bool ShowLinux(string title, string body, bool urgent)
    {
        // notify-send (libnotify) if present, otherwise talk to the notification daemon via gdbus.
        if (TryRun("notify-send", ["-a", "Radiacode Companion", "-u", urgent ? "critical" : "normal", title, body])) return true;
        return TryRun("gdbus", ["call", "--session", "--dest", "org.freedesktop.Notifications",
            "--object-path", "/org/freedesktop/Notifications", "--method", "org.freedesktop.Notifications.Notify",
            "Radiacode Companion", "0", "", title, body, "[]", $"{{'urgency': <byte {(urgent ? 2 : 1)}>}}", "10000"]);
    }

    private static bool TryRun(string file, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(3000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
