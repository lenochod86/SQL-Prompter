using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace SqlPrompter.Prototype
{
    /// <summary>
    /// Log do %LOCALAPPDATA%\SqlPrompter\prototype.log. Zápis na disk běží na vlastním
    /// vlákně, takže volání z vlákna UI jen vloží řádek do fronty.
    /// </summary>
    internal static class Log
    {
        private static readonly BlockingCollection<string> Queue = new BlockingCollection<string>(10000);

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SqlPrompter",
            "prototype.log");

        static Log()
        {
            var writer = new Thread(WriteLoop) { IsBackground = true, Name = "SqlPrompter.Prototype.Log" };
            writer.Start();
        }

        public static void Write(string area, string message)
        {
            var line = string.Format(
                "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] [T{2}{3}] {4}",
                DateTime.Now,
                area,
                Thread.CurrentThread.ManagedThreadId,
                IsUiThread() ? " UI" : string.Empty,
                message);
            Queue.TryAdd(line);
        }

        public static void Error(string area, string message, Exception ex)
        {
            Write(area, message + ": " + ex);
        }

        private static bool IsUiThread()
        {
            try
            {
                return Microsoft.VisualStudio.Shell.ThreadHelper.CheckAccess();
            }
            catch
            {
                return false;
            }
        }

        private static void WriteLoop()
        {
            foreach (var line in Queue.GetConsumingEnumerable())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Prototyp: chybu logu nemá kam nahlásit.
                }
            }
        }
    }
}
