using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public async Task<(int frame, int keyframe)> FindFrameByImage(string movie, string image)
        {
            Logger.Info($"Find keyframe with image {image}");
            var arguments = $"-i \"{movie}\" -loop 1 -i \"{image}\" -an -filter_complex \"blend=difference:shortest=1,blackframe=98:32\" -f null -";
            return await HandleWithCancellationToken(async token =>
            {
                (int frame, int keyframe) frame = default;
                var task = RunFfmpeg(arguments, line =>
                {
                    if (token.IsCancellationRequested) return;
                    frame = AnalyseForKeyFrame(line);
                    if (!frame.Equals(default))
                    {
                        Logger.Info($"Found keyframe with image {image} at {frame.Item1} with keyframe {frame.Item2}");
                        task.Cancel();
                    }
                }, token);
                await task;
                return frame;
            });
        }

        private (int frame, int keyframe) AnalyseForKeyFrame(string line)
        {
            throw new NotImplementedException();
        }

        public async Task Convert(string input, string output, string start, string end)
        {
            //Convert video
            //ffmpeg.exe -ss 00:01:23 -i "input.mp4" -t 00:02:42 -c:v h264_nvenc "output.mp4"
            var arguments = $"-ss {start} -i \"{input}\" -t {end} -c:v {_settings.FfmpegEncoder} \"{output}\"";
            await RunFfmpeg(arguments, Logger.Debug);
        }

        private async Task<T> HandleWithCancellationToken<T>(Func<CancellationToken, Task<T>> job)
        {
            var source = new CancellationTokenSource();
            try
            {
                lock(_cancellationTokenSources) _cancellationTokenSources.Add(source);
                return await job(source.Token);
            }
            finally
            {
                lock (_cancellationTokenSources) _cancellationTokenSources.Remove(source);
            }
        }



        public void Cancel()
        {
            lock (_cancellationTokenSources)
            {
                _cancellationTokenSources.ForEach(c => c.Cancel());
            }
        }

        private async Task RunFfmpeg(string arguments, Action<string> handleOutput)
        {
            await HandleWithCancellationToken<object>(async token =>
            {
                await RunFfmpeg(arguments, handleOutput, token);
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
                await Task.Delay(100, token);
            }

            if (!process.HasExited) process.Kill();
        }
    }
}