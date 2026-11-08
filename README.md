# FormatFactory Video Studio

Windows WPF application using .NET 10. Open `FormatFactory.csproj` in Visual Studio with the .NET desktop workload, or run `dotnet run --project FormatFactory.csproj`.

## Usage

1. Click **Check tools**. Install missing FFmpeg, Python, then OpenAI Whisper using their buttons. FFmpeg and Python use Windows Package Manager (WinGet / App Installer); Whisper uses the detected Python interpreter and pip. Installations require internet access and may require administrator approval. Restart and check again if a tool was installed in a custom location; its executable must be on PATH.
2. Choose a video and output directory, model, and language. Auto detect supports all Whisper languages; the language selector offers common explicit choices.
3. Start transcription. The app extracts the first audio track as a mono 16 kHz MP3, loads Whisper, and displays the finished transcript. Each job gets its own output folder with MP3, TXT, SRT, VTT, TSV, and JSON files. A Python worker script is retained for troubleshooting.
4. View **Live log** for current output. Cancel stops the child process tree; partial files and already installed tools remain.

Any container and audio codec supported by your installed FFmpeg can be selected using **All files**. Files without an audio stream and protected or damaged files cannot be transcribed.

Progress is based on FFmpeg media timestamps and Whisper processed audio frames. The overall bar allocates 20% to extraction and 80% to transcription. Time remaining is an approximate estimate for the active stage based on measured throughput; model download/loading has indeterminate duration. Whisper can take time before emitting its first progress update. CPU FP32 inference is used for compatibility. The first run downloads model weights to the standard Whisper cache. Audio processing stays local after package/model downloads.

Python 3.11 is the installer default. Existing Python environments must support PyTorch and OpenAI Whisper. The app checks importability of both libraries. This initial project implements the tool setup and transcription workflow; other FormatFactory conversion/editing operations are outside this version.

References: [FFmpeg](https://ffmpeg.org/ffmpeg.html), [OpenAI Whisper](https://github.com/openai/whisper).
