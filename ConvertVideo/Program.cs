﻿using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConvertVideo
{
    class Program
    {
        private const string TimeFormat = @"hh\:mm\:ss";

        private FileInfo _input;
        private FileInfo _output;
        private bool _keepRunning;
        private DirectoryInfo _inputFolder;
        private DirectoryInfo _outputFolder;
        private Settings _settings;

        static void Main(string[] args)
        {
            try
            {
                Logger.Info("Welcome to movie converter!");
                var program = new Program();
                program.Initialize(args);
                program.Run();
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
        }

        private void Run()
        {
            Logger.Info($"Convert all video's in {_inputFolder.FullName}");
            foreach (var file in _inputFolder.GetFiles())
            {
                _input = file;
                _output = new FileInfo($"{_outputFolder.FullName}\\{_input.Name}.mp4");
                if (FilterOnFileName(file.Name)) continue;
                if (!ValidOutput(_output)) continue;

                var firstKeyFrame = FindKeyFrame(_settings.StartImageFile);
                if (firstKeyFrame == 0)
                {
                    Logger.Info($"Failed to find a key-frame. Skip {_inputFolder.FullName}");
                    continue;
                }

                var lastKeyFrame = FindKeyFrame(_settings.StopImageFile, firstKeyFrame);
                if (lastKeyFrame == 0 || lastKeyFrame <= firstKeyFrame)
                {
                    Logger.Info($"Failed to find the end. Use the end time {_settings.DefaultVideoLengthInSeconds}s from the settings");
                    lastKeyFrame = firstKeyFrame + _settings.DefaultVideoLengthInSeconds * _settings.FramesPerSecond;
                }

                ConvertVideo(firstKeyFrame, lastKeyFrame);
            }
        }

        private bool ValidOutput(FileInfo output)
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

        private void ConvertVideo(int startFrame, int endFrame)
        {
            var start = TimeSpan.FromSeconds(startFrame / 25).ToString(TimeFormat);
            var end = TimeSpan.FromSeconds((endFrame - startFrame) / 25).ToString(TimeFormat);
            var arguments = $"-ss {start} -i \"{_input.FullName}\" -t {end} -c:v h264_nvenc \"{_output.FullName}\"";
            RunFfmpeg(arguments, Logger.Debug);
        }

        private int FindKeyFrame(string image, int startFrame = 0)
        {
            var keyframe = startFrame;
            if (string.IsNullOrWhiteSpace(image)) return keyframe;

            var start = "";

            Logger.Info($"Find keyframe with image {image}");
            var arguments = $"{start}-i \"{_input.FullName}\" -loop 1 -i \"{image}\" -an -filter_complex \"blend=difference:shortest=1,blackframe=98:32\" -f null -";
            RunFfmpeg(arguments, line =>
            {
                if (!_keepRunning) return;
                var frame = AnalyseForKeyFrame(line);
                if (frame.HasValue)
                {
                    _keepRunning = false;
                    keyframe = frame.Value;
                    Logger.Info($"found keyframe with image {image} at {keyframe}");
                }
            });
            return keyframe;
        }

        private void RunFfmpeg(string arguments, Action<string> handleOutput)
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
            _keepRunning = true;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (_keepRunning && !process.HasExited)
            {
                Task.Delay(100).Wait();
            }

            if (!process.HasExited) process.Kill();
        }

        private int? AnalyseForKeyFrame(string console)
        {
            if (string.IsNullOrWhiteSpace(console)) return null;
            Logger.Debug(console);
            if (!console.StartsWith("[Parsed_blackframe_")) return null;

            Logger.Info("Found the image");
            var index = console.LastIndexOf("last_keyframe:", StringComparison.Ordinal) + "last_keyframe:".Length;
            var keyframe = console.Substring(index, console.Length - index);
            Logger.Info("Found a keyframe at: " + keyframe);
            return int.Parse(keyframe);
        }
    }
}
