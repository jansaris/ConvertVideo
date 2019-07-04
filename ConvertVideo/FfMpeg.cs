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

        public FfMpeg(Settings settings)
        {
            _settings = settings;
        }

        //Extract single frame
        //ffmpeg.exe -ss 01:23:45 -i input -vframes 1 -q:v 2 output.jpg

        //Find frame by image
        //ffmpeg.exe -i "input.mp4" -loop 1 -i "image.png" -an -filter_complex "blend=difference:shortest=1,blackframe=98:32" -f null -
        public async Task<(int frame, int keyframe)?> FindFrameByImage(string movie, string image)
        {
            Logger.Info($"Find keyframe with image {image}");
            var arguments = $"-i \"{movie}\" -loop 1 -i \"{image}\" -an -filter_complex \"blend=difference:shortest=1,blackframe=98:32\" -f null -";
            return await HandleWithCancellationToken(async token =>
            {
                (int frame, int keyframe)? frame = null;
                await RunFfmpeg(arguments, line =>
                {
                    var result = AnalyzeForKeyFrame(line, token);
                    if (result != null) frame = result;
                }, token.Token);
                return frame;
            });
        }

        private (int frame, int keyframe)? AnalyzeForKeyFrame(string line, CancellationTokenSource token)
        {
            if (token.IsCancellationRequested) return null;
            var frame = AnalyzeForKeyFrame(line);
            if (frame == null) return null;

            Logger.Info($"Found keyframe with for image at {frame.Value.frame} with keyframe {frame.Value.keyframe}");
            token.Cancel();
            return frame;
        }

        private (int frame, int keyframe)? AnalyzeForKeyFrame(string console)
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
            if (startIndex >= 0 && endIndex > startIndex)
            {
                var frameString = console.Substring(startIndex, endIndex - startIndex);
                if (int.TryParse(frameString, out int keyframe)) return keyframe;
            }
            return null;
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

        private async Task<T> HandleWithCancellationToken<T>(Func<CancellationTokenSource, Task<T>> job)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            try
            {
                lock (_cancellationTokenSources) _cancellationTokenSources.Add(source);
                return await job(source);
            }
            finally
            {
                lock (_cancellationTokenSources) _cancellationTokenSources.Remove(source);
            }
        }

        private async Task RunFfmpeg(string arguments, Action<string> handleOutput)
        {
            await HandleWithCancellationToken<object>(async token =>
            {
                await RunFfmpeg(arguments, handleOutput, token.Token);
                return null;
            });
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