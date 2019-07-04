using System;
using System.Collections.Generic;
using System.Text;

namespace ConvertVideo
{
    public class Settings
    {
        public string FfmpegFullPath;
        public string FfmpegEncoder;
        public string StartImageFile;
        public string StopImageFile;
        public string SourceFolder;
        public string OutputFolder;
        public string FilenameFilter;
        public bool SkipFileIfOutputExists;
        public int DefaultVideoLengthInSeconds;
        public int FramesPerSecond;
        public int ThumbnailTakenInFramesAfterStart;
    }
}
