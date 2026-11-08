using System.Diagnostics;
using System.IO;
using System.Text;
namespace FormatFactory;

public record ToolRow(string Key, string Name, string Status, bool CanInstall)
{
    public string Action => CanInstall ? "Install" : "Available";
}

public static class Runner
{
    public static async Task<int> Run(string exe, IEnumerable<string> args, Action<string> output, CancellationToken token, string? extraPath = null)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        if (extraPath != null) info.Environment["PATH"] = extraPath + Path.PathSeparator + info.Environment["PATH"];
        using var process = new Process { StartInfo = info };
        process.Start();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        async Task Pump(StreamReader reader)
        {
            var buffer = new char[1024]; var line = new StringBuilder(); int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) > 0)
                for (int i = 0; i < count; i++)
                    if (buffer[i] is '\r' or '\n') { if (line.Length > 0) { output(line.ToString()); line.Clear(); } }
                    else line.Append(buffer[i]);
            if (line.Length > 0) output(line.ToString());
        }
        await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError), process.WaitForExitAsync());
        token.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    public static async Task<string?> Find(string name, string[] checkArgs, CancellationToken token)
    {
        var candidates = new List<string> { name };
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = new[] { Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) };
        foreach (var dir in string.Join(';', paths).Split(';', StringSplitOptions.RemoveEmptyEntries)) candidates.Add(Path.Combine(Environment.ExpandEnvironmentVariables(dir.Trim('"')), name + ".exe"));
        if (name == "python")
        {
            var root = Path.Combine(local, "Programs", "Python");
            if (Directory.Exists(root)) candidates.AddRange(Directory.GetDirectories(root).Select(d => Path.Combine(d, "python.exe")));
        }
        if (name == "ffmpeg")
        {
            var root = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(root)) foreach (var dir in Directory.GetDirectories(root, "Gyan.FFmpeg*")) candidates.AddRange(Directory.GetFiles(dir, "ffmpeg.exe", SearchOption.AllDirectories));
        }
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                if (await Run(candidate, checkArgs, _ => { }, timeout.Token) == 0) return candidate;
            }
            catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); }
            catch (System.ComponentModel.Win32Exception) { }
            catch (IOException) { }
        }
        return null;
    }
}
