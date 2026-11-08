using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace FormatFactory;
public partial class MainWindow : Window
{
    private string? ffmpeg, python;
    private bool whisper, busy;
    private CancellationTokenSource? operation;
    public MainWindow() { InitializeComponent(); OutputPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FormatFactory"); }
    private async void OnLoaded(object sender, RoutedEventArgs e) => await Check();
    private void Append(string line) => Dispatcher.Invoke(() => { Log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}"); if (Log.Text.Length > 200000) Log.Text = Log.Text[^150000..]; Log.ScrollToEnd(); });
    private void SetBusy(bool value)
    {
        busy = value;
        CheckButton.IsEnabled = BrowseButton.IsEnabled = OutputButton.IsEnabled = StartButton.IsEnabled = ToolsList.IsEnabled = Model.IsEnabled = LanguageChoice.IsEnabled = !value;
        CancelButton.IsEnabled = value;
    }
    private async Task Check()
    {
        if (busy) return;
        SetBusy(true); operation = new(); Stage.Text = "Checking tools…";
        try { await Detect(operation.Token); Stage.Text = "Tool check complete"; }
        catch (OperationCanceledException) { Stage.Text = "Check cancelled"; }
        catch (Exception ex) { Append(ex.Message); Stage.Text = "Tool check failed"; }
        finally { SetBusy(false); operation.Dispose(); operation = null; }
    }
    private async Task Detect(CancellationToken token)
    {
        ffmpeg = await Runner.Find("ffmpeg", ["-version"], token);
        python = await Runner.Find("python", ["-c", "import sys; assert sys.version_info >= (3,9)"], token);
        whisper = python != null && await Runner.Run(python, ["-c", "import whisper, torch; assert callable(whisper.load_model)"], Append, token) == 0;
        ToolsList.ItemsSource = new[] {
            new ToolRow("ffmpeg", "FFmpeg", ffmpeg ?? "Missing — needed to read video and extract MP3", ffmpeg == null),
            new ToolRow("python", "Python 3.9+", python ?? "Missing — Python 3.11 is recommended", python == null),
            new ToolRow("whisper", "OpenAI Whisper", whisper ? "Ready — local speech recognition" : "Missing — install Python first", !whisper) };
    }
    private async void CheckTools(object sender, RoutedEventArgs e) => await Check();
    private async void InstallTool(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        string key = (string)((Button)sender).Tag;
        if (key == "whisper" && python == null) { MessageBox.Show("Install Python first, then install Whisper."); return; }
        SetBusy(true); operation = new(); Progress.IsIndeterminate = true; Stage.Text = "Installing " + key + "…";
        try
        {
            int code;
            if (key == "whisper") code = await Runner.Run(python!, ["-m", "pip", "install", "--user", "--upgrade", "openai-whisper"], Append, operation.Token);
            else code = await Runner.Run("winget", ["install", "--id", key == "ffmpeg" ? "Gyan.FFmpeg" : "Python.Python.3.11", "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], Append, operation.Token);
            if (code != 0) throw new InvalidOperationException($"Installer exited with code {code}. See the log. WinGet (App Installer) must be installed; system installation may require administrator approval.");
            await Detect(operation.Token); Stage.Text = "Installation finished; tools checked";
        }
        catch (OperationCanceledException) { Stage.Text = "Installation cancelled"; Append("Installation cancelled; already installed components may remain."); }
        catch (Exception ex) { Stage.Text = "Installation failed"; Append(ex.Message); MessageBox.Show(ex.Message, "Installation"); }
        finally { Progress.IsIndeterminate = false; SetBusy(false); operation.Dispose(); operation = null; }
    }
    private void Browse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.flv;*.m4v;*.mpeg;*.mpg;*.ts;*.mts;*.m2ts;*.3gp|All files|*.*" };
        if (dialog.ShowDialog() == true) VideoPath.Text = dialog.FileName;
    }
    private void ChooseOutput(object sender, RoutedEventArgs e) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog() == true) OutputPath.Text = dialog.FolderName; }
    private void Cancel(object sender, RoutedEventArgs e) { operation?.Cancel(); CancelButton.IsEnabled = false; }
    private void OnClosing(object? sender, CancelEventArgs e) { if (busy) { e.Cancel = true; MessageBox.Show("Cancel the current operation and wait for it to stop before closing."); } }
    private async void Start(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!File.Exists(VideoPath.Text)) { MessageBox.Show("Choose an existing video file."); return; }
        if (ffmpeg == null || python == null || !whisper) { MessageBox.Show("Check and install the required tools first."); return; }
        SetBusy(true); operation = new(); Transcript.Clear(); Progress.Value = 0; Progress.IsIndeterminate = true; Estimate.Text = "Estimating…";
        string folder = Path.Combine(OutputPath.Text, Path.GetFileNameWithoutExtension(VideoPath.Text) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            Directory.CreateDirectory(folder);
            string audio = Path.Combine(folder, "audio.mp3");
            Stage.Text = "Extracting MP3 audio…"; var watch = Stopwatch.StartNew(); double duration = 0;
            var durationPattern = new Regex(@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
            int code = await Runner.Run(ffmpeg, ["-nostdin", "-n", "-i", VideoPath.Text, "-map", "0:a:0", "-vn", "-ac", "1", "-ar", "16000", "-c:a", "libmp3lame", "-b:a", "64k", "-progress", "pipe:1", "-nostats", audio], line =>
            {
                Append(line); var match = durationPattern.Match(line);
                if (match.Success) duration = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 3600 + double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (line.StartsWith("out_time_us=") && double.TryParse(line[12..], CultureInfo.InvariantCulture, out var us) && duration > 0) Report(us / 1000000 / duration, watch, "Extracting MP3", 0, 20);
            }, operation.Token);
            if (code != 0) throw new InvalidOperationException("FFmpeg could not extract audio. The file may be unsupported, damaged, or contain no audio. See the live log.");
            Stage.Text = "Loading Whisper model (may download on first use)…"; Progress.IsIndeterminate = true; Estimate.Text = "Download / model loading — time unknown";
            string script = Path.Combine(folder, "transcribe_worker.py"); await File.WriteAllTextAsync(script, Worker, operation.Token);
            var args = new List<string> { "-u", script, audio, ((ComboBoxItem)Model.SelectedItem).Content.ToString()!, folder, ((ComboBoxItem)LanguageChoice.SelectedItem).Tag?.ToString() ?? "" };
            watch = new Stopwatch();
            code = await Runner.Run(python, args, line =>
            {
                if (line.StartsWith("FF_EVENT "))
                {
                    using var json = JsonDocument.Parse(line[9..]); var root = json.RootElement;
                    if (root.GetProperty("type").GetString() == "start") { watch.Restart(); Dispatcher.Invoke(() => { Stage.Text = "Transcribing…"; Estimate.Text = "Estimating transcription time…"; }); }
                    else Report(root.GetProperty("fraction").GetDouble(), watch, "Transcribing", 20, 80);
                }
                else Append(line);
            }, operation.Token, Path.GetDirectoryName(ffmpeg));
            if (code != 0) throw new InvalidOperationException($"Whisper exited with code {code}. See the live log for details.");
            Transcript.Text = await File.ReadAllTextAsync(Path.Combine(folder, "audio.txt"), operation.Token);
            Progress.IsIndeterminate = false; Progress.Value = 100; Stage.Text = "Transcription complete"; Estimate.Text = "Complete"; Append("Output saved to " + folder);
        }
        catch (OperationCanceledException) { Stage.Text = "Cancelled"; Estimate.Text = ""; Append("Cancelled. Partial output retained in " + folder); }
        catch (Exception ex) { Stage.Text = "Failed — see live log"; Estimate.Text = ""; Append(ex.Message); MessageBox.Show(ex.Message, "Transcription failed"); }
        finally { Progress.IsIndeterminate = false; SetBusy(false); operation.Dispose(); operation = null; }
    }
    private void Report(double fraction, Stopwatch watch, string stage, double offset, double weight) => Dispatcher.Invoke(() =>
    {
        fraction = Math.Clamp(fraction, 0, 1); Progress.IsIndeterminate = false; Progress.Value = offset + weight * fraction;
        Stage.Text = $"{stage} · {fraction:P0}";
        Estimate.Text = fraction > .005 ? $"About {TimeSpan.FromSeconds(Math.Min(31536000, watch.Elapsed.TotalSeconds / fraction * (1 - fraction))):hh\\:mm\\:ss} left in this stage" : "Estimating…";
    });
    private const string Worker = """
import sys, json, importlib
import whisper
from whisper.utils import get_writer

def emit(kind, **values):
    print('FF_EVENT ' + json.dumps(dict(type=kind, **values)), flush=True)

module = importlib.import_module('whisper.transcribe')
original = module.tqdm.tqdm
class Progress(original):
    def update(self, n=1):
        result = super().update(n)
        emit('progress', fraction=min(1.0, self.n / self.total) if self.total else 0)
        return result
module.tqdm.tqdm = Progress
audio, model_name, output, language = sys.argv[1:]
print('Loading model: ' + model_name, flush=True)
model = whisper.load_model(model_name)
emit('start')
result = model.transcribe(audio, verbose=False, fp16=False, language=language or None)
get_writer('all', output)(result, audio, options={'max_line_width': None, 'max_line_count': None, 'highlight_words': False})
print('Transcript and subtitles saved.', flush=True)
""";
}
