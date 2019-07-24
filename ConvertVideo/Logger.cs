using System;
using System.IO;

namespace ConvertVideo
{
    public static class Logger
    {
        public static void Debug(string message)
        {
            Console($"DEBUG:  {message}");
        }

        public static void Info(string message)
        {
            Log($"INFO :  {message}");
        }

        public static void Warn(string message)
        {
            Log($"WARN : {message}");
        }

        public static void Error(string message)
        {
            Log($"ERROR: {message}");
        }

        private static void Console(string message)
        {
            System.Console.WriteLine(message);
        }

        private static void Log(string message)
        {
            System.Console.WriteLine(message);
            File.AppendAllText("ConvertVideo.log", $"{DateTime.Now:s} - {message}{Environment.NewLine}");
        }
    }
}