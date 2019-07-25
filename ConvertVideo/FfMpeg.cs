using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ConvertVideo
{
    public class FfMpeg
    {
        private readonly Settings _settings;
        private readonly List<CancellationTokenSource> _cancellationTokenSources = new List<CancellationTokenSource>();
        private (int frame, int keyframe)? _frame = null;

        public FfMpeg(Settings settings)
        {
            _settings = settings;
        }

         public async Task<(int frame, int keyframe)?> FindFrameByImage(string movie, string image)
         {
            //Find frame by image
            //ffmpeg.exe -i "input.mp4" -loop 1 -i "image.png" -an -filter_complex "blend=difference:shortest=1,blackframe=98:32" -f null -
            Logger.Info($"Find keyframe with image {image}");
            var arguments = $"-i \"{movie}\" -loop 1 -i \"{image}\" -an -filter_complex \"blend=difference:shortest=1,blackframe=95:32\" -f null -";
            _frame = null;

            await HandleWithCancellationToken(async token =>
            {
                await RunFfmpeg(arguments, line =>
                {
                    if (_frame != null) return;

                    var result = AnalyzeForKeyFrame(line);
                    if (result == null) return;

                    _frame = result;
                    token.Cancel();

                }, token.Token);
            });

            return _frame;
        }

        public async Task ExtractImage(string movie, string image, string start)
        {
            //Extract single frame
            //ffmpeg.exe -ss 01:23:45 -i input -vframes 1 -q:v 2 output.jpg
            var arguments = $"-ss {start} -i \"{movie}\" -vframes 1 -q:v 2 \"{image}\"";
            await RunFfmpeg(arguments, Logger.Debug);
        }

        public async Task Convert(string input, string output, string start, string end)
        {
            //Convert video
            //ffmpeg.exe -ss 00:01:23 -i "input.mp4" -t 00:02:42 -c:v h264_nvenc "output.mp4"
            var arguments = $"-ss {start} -i \"{input}\" -t {end} -c:v {_settings.FfmpegEncoder} \"{output}\"";
            await RunFfmpeg(arguments, Logger.Debug);
        }

        public void Cancel()
        {
            List<CancellationTokenSource> tokens;
            lock (_cancellationTokenSources) tokens = _cancellationTokenSources.ToList();
            tokens.ForEach(c => c.Cancel());
        }

        private (int frame, int keyframe)? AnalyzeForKeyFrame(string line)
        {
            var frame = FindFrameInConsoleLine(line);
            if (frame == null) return null;

            Logger.Info($"Found keyframe with for image at {frame.Value.frame} with keyframe {frame.Value.keyframe}");
            return frame;
        }

        private (int frame, int keyframe)? FindFrameInConsoleLine(string console)
        {
            Logger.Debug(console);
            //[Parsed_blackframe_1 @ 000002badb4ee880] frame:49 pblack:99 pts:25088 t:1.960000 type:P last_keyframe:0
            if (string.IsNullOrWhiteSpace(console) || !console.StartsWith("[Parsed_blackframe_")) return null;
            var frame = ExtractFrame(console, " frame:");
            var keyframe = ExtractFrame(console, "last_keyframe:");
            if (frame.HasValue && keyframe.HasValue)
            {
                return (frame.Value, keyframe.Value);
            }

            return null;
        }

        private int? ExtractFrame(string console, string frame)
        {
            var startIndex = console.LastIndexOf(frame, StringComparison.Ordinal) + frame.Length;
            var endIndex = console.IndexOf(' ', startIndex);
            if (endIndex == -1) endIndex = console.Length;

            if (startIndex < 0 || endIndex <= startIndex) return null;

            var frameString = console.Substring(startIndex, endIndex - startIndex);
            if (int.TryParse(frameString, out var keyframe)) return keyframe;
            return null;
        }

        private async Task HandleWithCancellationToken(Func<CancellationTokenSource, Task> job)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            try
            {
                lock (_cancellationTokenSources) _cancellationTokenSources.Add(source);
                await job(source);
            }
            finally
            {
                lock (_cancellationTokenSources) _cancellationTokenSources.Remove(source);
            }
        }

        private async Task RunFfmpeg(string arguments, Action<string> handleOutput)
        {
            await HandleWithCancellationToken(async token =>
                await RunFfmpeg(arguments, handleOutput, token.Token)
            );
        }

        private async Task RunFfmpeg(string arguments, Action<string> handleOutput, CancellationToken token)
        {
            Logger.Info($"Start ffmpeg with: {arguments}");
            var processInfo = new ProcessStartInfo
            {
                FileName = _settings.FfmpegFullPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var process = new Process { StartInfo = processInfo };
            process.OutputDataReceived += (sender, args) => handleOutput(args.Data);
            process.ErrorDataReceived += (sender, args) => handleOutput(args.Data);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (!token.IsCancellationRequested && !process.HasExited)
            {
                try
                {
                    await Task.Delay(25, token);
                }
                catch (TaskCanceledException)
                {
                    Logger.Info("Requested to stop Ffmpeg");
                }
            }

            if (!process.HasExited) process.Kill();
        }
    }
}