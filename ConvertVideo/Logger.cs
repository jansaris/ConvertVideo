using System;
using System.IO;

namespace ConvertVideo
{
    public static class Logger
    {
        public static void Info(string message)
        {
            Log($"INFO:  {message}");
        }

        public static void Error(string message)
        {
            Log($"ERROR: {message}");
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
            File.AppendAllText("ConvertVideo.log", $"{DateTime.Now:s} - {message}{Environment.NewLine}");
        }
    }
}