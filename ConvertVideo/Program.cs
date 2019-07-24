using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tesseract;

namespace ConvertVideo
{
    class Program
    {
        private const string TimeFormat = @"hh\:mm\:ss";

        private FileInfo _input;
        private DirectoryInfo _inputFolder;
        private DirectoryInfo _outputFolder;
        private Settings _settings;
        private FfMpeg _ffmpeg;

        private readonly CancellationTokenSource _cancel;
        private readonly CancellationToken _token;
        private string _outputFileName;

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
                if (FilterOnFileName(file.Name)) continue;
                GenerateOutputName();
                var output = GetOutputFileAs("mp4");
                if (!ValidOutput(output)) continue;
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

                var thumbnail = await CreateThumbnail(startFrames.Value);
                var title = ExtractTitle(thumbnail);
                await ConvertVideo(startFrames.Value.keyframe, endFrame.Value.frame, output.FullName);
                CreateNfo(title, new FileInfo(thumbnail).Name);
                Logger.Info($"Finished conversion of {_input.FullName}");
            }
        }

        private void GenerateOutputName()
        {
            Logger.Info($"Prepare output filename for: {_input.Name}");
            var inputNameWithoutExtension = _input.Name.Replace(_input.Extension, "");
            Logger.Debug($"Removed the extension: {inputNameWithoutExtension}");
            var regex = new Regex(@"(-\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            if (regex.Match(inputNameWithoutExtension).Success)
            {
                //Input name ends with a number eg. -2 or -13
                //Strip it
                inputNameWithoutExtension = inputNameWithoutExtension.Substring(0, inputNameWithoutExtension.LastIndexOf('-'));
                Logger.Debug($"Removed last number from input: {inputNameWithoutExtension}");
            }

            var episodeNumber = "S01E01";
            foreach (var output in _outputFolder.EnumerateFiles().OrderByDescending(f => f.Name))
            {
                if (!output.Name.Contains("S01E")) continue;

                Logger.Debug($"Found the last episode in the output directory: {output.Name}");
                var index = output.Name.IndexOf("S01E", StringComparison.Ordinal) + 4;
                var newEpisodeNumber = Convert.ToInt32(output.Name.Substring(index, 2)) + 1;
                episodeNumber = $"S01E{newEpisodeNumber:D2}";
                break;
            }
            Logger.Debug($"Episode number chosen as: {episodeNumber}");
            _outputFileName = $"{inputNameWithoutExtension}.{episodeNumber}";
            Logger.Info($"Output filename generated: {_outputFileName}");
        }

        private FileInfo GetOutputFileAs(string extension, string addition = "")
        {
            return new FileInfo($"{_outputFolder.FullName}\\{_outputFileName}{addition}.{extension}");
        }

        private void CreateNfo(string title, string thumbnail)
        {
            var nfo = GetOutputFileAs("nfo");
            File.WriteAllText(nfo.FullName, $"<?xml version=\"1.0\" encoding=\"utf-8\"?><episodedetails><title>{title}</title><thumb>{thumbnail}</thumb></episodedetails>");
        }

        public string ExtractTitle(string thumbnail)
        {
            var pix = Pix.LoadFromFile(thumbnail);
            var engine = new TesseractEngine(".", _settings.TextLanguage, EngineMode.Default);
            //x400 y530 w600 h70
            foreach (var rect in _settings.TextLocations)
            {
                using (var page = engine.Process(pix, new Rect(rect.X, rect.Y, rect.Width, rect.Height), PageSegMode.SingleBlock))
                {
                    var text = page.GetText();
                    var confidence = page.GetMeanConfidence();
                    Logger.Debug($"Extracted title ({confidence}): {text}");
                    if (confidence > 0.7)
                    {
                        Logger.Info($"Found title ({confidence}): {text}");
                        return text;
                    }
                }
            }

            Logger.Warn($"Failed to extract title from: {thumbnail}");
            var thumbFile = new FileInfo(thumbnail);
            return thumbFile.Name.Replace(thumbFile.Extension, "");
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
                Logger.Info($"Output file '{output.FullName}' already exists, skip this one");
                return false;
            }
            Logger.Info($"Output file '{output.FullName}' already exists, delete it first");
            output.Delete();

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

        private async Task<string> CreateThumbnail((int frame, int keyframe) startFrames)
        {
            if (_token.IsCancellationRequested) return null;
            var frameNumber = startFrames.frame + _settings.ThumbnailTakenInFramesAfterStart;
            var start = TimeSpan.FromSeconds((double)frameNumber / 25).ToString(TimeFormat);
            var thumbnail = GetOutputFileAs("jpg", "-thumb");
            await _ffmpeg.ExtractImage(_input.FullName, thumbnail.FullName, start);
            return thumbnail.FullName;
        }

        private async Task ConvertVideo(int startFrame, int endFrame, string output)
        {
            if (_token.IsCancellationRequested) return;
            var start = TimeSpan.FromSeconds((double)startFrame / 25).ToString(TimeFormat);
            var end = TimeSpan.FromSeconds((double)(endFrame - startFrame) / 25).ToString(TimeFormat);
            await _ffmpeg.Convert(_input.FullName, output, start, end);
        }
    }
}
