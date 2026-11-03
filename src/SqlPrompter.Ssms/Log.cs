using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace SqlPrompter.Ssms
{
    /// <summary>
    /// Local diagnostic log in %LOCALAPPDATA%\SqlPrompter\sqlprompter.log; nothing is sent anywhere.
    /// Writing runs on its own thread; a call from the UI thread only queues the line.
    /// Passwords and script contents never go into the log.
    /// </summary>
    internal static class Log
    {
        private const long MaxSize = 5 * 1024 * 1024;
        private static readonly BlockingCollection<string> Queue = new BlockingCollection<string>(10000);

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SqlPrompter",
            "sqlprompter.log");

        static Log()
        {
            var writer = new Thread(WriteLoop) { IsBackground = true, Name = "SqlPrompter.Log" };
            writer.Start();
        }

        public static void Write(string message)
        {
            Queue.TryAdd(string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [T{1}] {2}", DateTime.Now, Thread.CurrentThread.ManagedThreadId, message));
        }

        public static void Error(string message, Exception ex)
        {
            Write(message + ": " + ex);
        }

        private static void WriteLoop()
        {
            foreach (var line in Queue.GetConsumingEnumerable())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxSize)
                    {
                        File.Copy(FilePath, FilePath + ".1", overwrite: true);
                        File.Delete(FilePath);
                    }

                    File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // There is nowhere to report a logging failure.
                }
            }
        }
    }
}
