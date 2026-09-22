using System;
using System.Diagnostics;
using System.IO;

internal static class DiscordProcess
{
    internal static bool Matches(string path, string localAppData)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(localAppData)) return false;
        try
        {
            var file = new FileInfo(Path.GetFullPath(path));
            var version = file.Directory;
            if (version == null || version.Parent == null ||
                !version.Name.StartsWith("app-", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (string channel in new[] { "Discord", "DiscordPTB", "DiscordCanary" })
                if (string.Equals(file.Name, channel + ".exe", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(version.Parent.FullName, Path.GetFullPath(Path.Combine(localAppData, channel)), StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (IOException) { }
        return false;
    }
    internal static bool TryIdentify(uint pid, long eventTimestamp, out string name)
    {
        name = null;
        if (pid > int.MaxValue) return false;
        try
        {
            using (var process = Process.GetProcessById((int)pid))
            {
                // Yeniden kullanılan PID'yi eski olayla eşleştirme.
                double age = (Stopwatch.GetTimestamp() - eventTimestamp) / (double)Stopwatch.Frequency;
                if (eventTimestamp > 0 && process.StartTime.ToUniversalTime() > DateTime.UtcNow.AddSeconds(-age)) return false;
                if (!Matches(process.MainModule.FileName, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))) return false;
                name = process.ProcessName;
                return true;
            }
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
