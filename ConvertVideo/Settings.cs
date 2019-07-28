using System;
using System.Collections.Generic;
using System.Text;
using Tesseract;

namespace ConvertVideo
{
    public class Settings
    {
        public string FfmpegFullPath;
        public string FfmpegEncoder;
        public string StartImageFile;
        public int AddFramesAfterStartImage;
        public string StopImageFile;
        public int AddFramesAfterStopImage;
        public string SourceFolder;
        public string OutputFolder;
        public string FilenameFilter;
        public bool SkipFileIfOutputExists;
        public int DefaultVideoLengthInSeconds;
        public int FramesPerSecond;
        public int ThumbnailTakenInFramesAfterStart;
        public List<Rectangle> TextLocations;
        public string TextLanguage;
    }

    public class Rectangle
    {
        public int X, Y, Width, Height;
    }
}
