using System;
using System.IO;
using UnityEngine;

namespace Crony
{
    /// <summary>
    /// Keeps a small file of errors and exceptions in the game's data folder, so a player who hits a problem can
    /// send it to the developer (Settings > Display > Problem log). Nothing is uploaded.
    /// </summary>
    public static class ErrorLog
    {
        const long MaxBytes = 256 * 1024;
        static readonly object Gate = new object();
        static string path;

        public static string Folder => Application.persistentDataPath;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            // persistentDataPath may only be read on the main thread, and messages can arrive from worker threads.
            path = Path.Combine(Application.persistentDataPath, "crony-errors.log");
            Application.logMessageReceivedThreaded -= Record;
            Application.logMessageReceivedThreaded += Record;
        }

        static void Record(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            try
            {
                lock (Gate)
                {
                    // Start over instead of growing forever; the newest errors are the useful ones.
                    if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                    File.AppendAllText(path, DateTime.UtcNow.ToString("u") + "  Crony " + Application.version + "  " + type + "\n" + message + "\n" + stackTrace + "\n");
                }
            }
            catch (Exception)
            {
                // A log that can't be written must never become a second error.
            }
        }
    }
}
