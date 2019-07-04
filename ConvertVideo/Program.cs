using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConvertVideo
{
    class Program
    {
        private const string TimeFormat = @"hh\:mm\:ss";

        private FileInfo _input;
        private FileInfo _output;
        private DirectoryInfo _inputFolder;
        private DirectoryInfo _outputFolder;
        private Settings _settings;
        private FfMpeg _ffmpeg;

        private readonly CancellationTokenSource _cancel;
        private readonly CancellationToken _token;

        private Program()
        {
            _cancel = new CancellationTokenSource();
            _token = _cancel.Token;
        }

        static void Main(string[] args)
        {
            try
            {
                Logger.Info("Welcome to movie converter!");
                var program = new Program();
                program.Initialize(args);
                program.Run().Wait();
            }
            catch (Exception ex)
            {
                Logger.Error($"Oh oh: {ex.Message}");
                Logger.Error(ex.StackTrace);
            }

            Logger.Info("Bye!");
        }

        private void Initialize(string[] args)
        {
            if (args.Length != 1)
            {
                throw new ArgumentException("ConvertVideo.exe [settings.json]");
            }

            Logger.Info($"Load settings file '{args[0]}'");
            _settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(args[0]));
            _inputFolder = new DirectoryInfo(_settings.SourceFolder);
            if(!_inputFolder.Exists) throw new Exception($"Input folder: {_inputFolder.FullName} doesn't exist");
            _outputFolder = new DirectoryInfo(_settings.OutputFolder);
            if (!_outputFolder.Exists)
            {
               throw new Exception($"Cannot write to {_outputFolder.FullName}");
            }
            Logger.Info("Initialized settings");
            _ffmpeg = new FfMpeg(_settings);
        }

        private async Task Run()
        {
            var keyboard = ListenForKeyboardInput();
            var conversion = ConvertAll();
            await Task.WhenAny(keyboard, conversion);
            await conversion;
        }

        private async Task ConvertAll()
        {
            Logger.Info($"Convert all video's in {_inputFolder.FullName}");
            foreach (var file in _inputFolder.GetFiles())
            {
                _input = file;
                var inputNameWithoutExtension = _input.Name.Replace(_input.Extension, "");
                _output = new FileInfo($"{_outputFolder.FullName}\\{inputNameWithoutExtension}.mp4");
                if (FilterOnFileName(file.Name)) continue;
                if (!ValidOutput(_output)) continue;
                Logger.Info($"Start conversion of {_input.FullName}");

                var startFrames = await FindKeyFrame(_settings.StartImageFile);
                if (!startFrames.HasValue)
                {
                    Logger.Info($"Failed to find a key-frame. Skip {_input.FullName}");
                    continue;
                }

                var endFrame = await FindKeyFrame(_settings.StopImageFile);
                if (!endFrame.HasValue)
                {
                    Logger.Info($"No end frame. Use the end time {_settings.DefaultVideoLengthInSeconds}s from the settings");
                    endFrame = (startFrames.Value.frame + _settings.DefaultVideoLengthInSeconds * _settings.FramesPerSecond, 0);
                }

                await CreateThumbnail(startFrames.Value);
                //await ConvertVideo(startFrames.Value.keyframe, endFrame.Value.frame);
                Logger.Info($"Finished conversion of {_input.FullName}");
            }
        }

        private async Task ListenForKeyboardInput()
        {
            await Task.Run(() =>
            {
                while (!_cancel.IsCancellationRequested)
                {
                    var key = Console.ReadKey();
                    Logger.Info($"Analyze keyboard input: {key}");
                    if (key.KeyChar != 'q') continue;

                    Logger.Info("Cancellation requested.....");
                    _cancel.Cancel();
                    Logger.Info("Cancel Ffmpeg");
                    _ffmpeg.Cancel();
                    Logger.Info("Cancelled Ffmpeg");
                }
            }, _token);
        }

        private bool ValidOutput(FileSystemInfo output)
        {
            if (!output.Exists) return true;

            if (_settings.SkipFileIfOutputExists)
            {
                Logger.Info($"Output file '{_output.FullName}' already exists, skip this one");
                return false;
            }
            Logger.Info($"Output file '{_output.FullName}' already exists, delete it first");
            _output.Delete();

            return true;
        }

        private bool FilterOnFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(_settings.FilenameFilter) || 
                fileName.Contains(_settings.FilenameFilter)) return false;
            Logger.Info($"Skip {fileName} because filename doesn't match {_settings.FilenameFilter}");
            return true;
        }

        private async Task<(int frame, int keyframe)?> FindKeyFrame(string image)
        {
            if (_token.IsCancellationRequested) return null;
            if (string.IsNullOrWhiteSpace(image)) return null;
            return await _ffmpeg.FindFrameByImage(_input.FullName, image);
        }

        private async Task CreateThumbnail((int frame, int keyframe) startFrames)
        {
            if (_token.IsCancellationRequested) return;
            var frameNumber = startFrames.frame + _settings.ThumbnailTakenInFramesAfterStart;
            var start = TimeSpan.FromSeconds((double)frameNumber / 25).ToString(TimeFormat);
            await _ffmpeg.ExtractImage(_input.FullName, $"{_output.FullName.Replace(_output.Extension,"")}.jpg", start);
        }

        private async Task ConvertVideo(int startFrame, int endFrame)
        {
            if (_token.IsCancellationRequested) return;
            var start = TimeSpan.FromSeconds((double)startFrame / 25).ToString(TimeFormat);
            var end = TimeSpan.FromSeconds((double)(endFrame - startFrame) / 25).ToString(TimeFormat);
            await _ffmpeg.Convert(_input.FullName, _output.FullName, start, end);
        }
    }
}
