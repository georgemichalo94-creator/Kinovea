#region License
/*
Copyright © Kinovea contributors. Swim race analysis module.
This file is part of Kinovea and is distributed under the terms of the GNU General Public License v2.
*/
#endregion
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Kinovea.ScreenManager
{
    /// <summary>
    /// (2.22.0) The files of the swim analysis written whole or not at all: the new text goes to a temporary file beside the target and
    /// then takes its place, so that a crash, a full disk or a disk unplugged in the middle of a save leaves the file of before (not half
    /// a file, which was read as "no analysis" and then written over). The files of the settings folder keep the one of before (".bak")
    /// and are read from it when they cannot be read; the analyses keep a copy a day in the settings folder.
    /// </summary>
    internal static class SwimFiles
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly object gate = new object();

        /// <summary>
        /// The text into the file, whole or not at all (throws when it cannot be written: the file of before is still there).
        /// keepBackup: the file of before stays beside it as ".bak".
        /// </summary>
        public static void WriteText(string path, string text, Encoding encoding, bool keepBackup = false)
        {
            lock (gate)
            {
                string temp = path + ".tmp";
                using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, encoding))
                {
                    writer.Write(text);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                    return;
                }

                try
                {
                    File.Replace(temp, path, keepBackup ? path + ".bak" : null, true);
                }
                catch (Exception e)
                {
                    // A volume without this replace (FAT, exFAT, some network folders), or the file held for a moment by another program:
                    // the whole new text is on the disk already, it is copied over the old one.
                    log.Debug("File.Replace not possible for " + Path.GetFileName(path) + " (" + e.Message + "): copied.");
                    if (keepBackup)
                        TryCopy(path, path + ".bak");
                    File.Copy(temp, path, true);
                    TryDelete(temp);
                }
            }
        }

        /// <summary>
        /// The object of a JSON file of the settings folder; when the file is there but cannot be read (or 'valid' rejects it), from its
        /// copy of before (".bak"). Null: no file, or neither can be read.
        /// </summary>
        public static T ReadJson<T>(string path, Func<T, bool> valid = null) where T : class
        {
            if (!File.Exists(path))
                return null;

            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate))
                        continue;

                    T value = JsonConvert.DeserializeObject<T>(File.ReadAllText(candidate, Encoding.UTF8));
                    if (value != null && (valid == null || valid(value)))
                    {
                        if (candidate != path)
                            log.Warn(Path.GetFileName(path) + " could not be read: its copy of before (.bak) was used.");
                        return value;
                    }

                    log.Warn(Path.GetFileName(candidate) + " is empty or not what was expected.");
                }
                catch (Exception e)
                {
                    log.Error("Could not read " + Path.GetFileName(candidate) + ".", e);
                }
            }

            return null;
        }

        /// <summary>
        /// A copy of the file as it is now in 'folder', as "prefix.yyyyMMdd.ext", once a day: before the first save of the day changes it.
        /// The newest 'keep' copies of the prefix stay, and the newest of all the copies up to 'maxBytes' in the folder.
        /// </summary>
        public static void BackupDaily(string source, string folder, string prefix, string extension, int keep, long maxBytes)
        {
            try
            {
                if (!File.Exists(source) || new FileInfo(source).Length == 0)
                    return;

                string today = Path.Combine(folder, prefix + "." + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + extension);
                if (File.Exists(today))
                    return;

                Directory.CreateDirectory(folder);
                File.Copy(source, today, false);

                DirectoryInfo dir = new DirectoryInfo(folder);
                foreach (FileInfo old in Dated(dir, prefix, extension).Skip(Math.Max(1, keep)))
                    TryDelete(old.FullName);

                long total = 0;
                foreach (FileInfo f in dir.GetFiles("*" + extension).OrderByDescending(x => x.LastWriteTimeUtc))
                {
                    total += f.Length;
                    if (total > maxBytes && f.FullName != today)
                        TryDelete(f.FullName);
                }
            }
            catch (Exception e)
            {
                log.Error("Could not keep a copy of " + Path.GetFileName(source) + ".", e);
            }
        }

        /// <summary>The daily copies of a prefix, the newest first.</summary>
        public static List<FileInfo> Backups(string folder, string prefix, string extension)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(folder);
                return dir.Exists ? Dated(dir, prefix, extension).ToList() : new List<FileInfo>();
            }
            catch (Exception e)
            {
                log.Error("Could not list the copies of " + prefix + ".", e);
                return new List<FileInfo>();
            }
        }

        private static IEnumerable<FileInfo> Dated(DirectoryInfo dir, string prefix, string extension)
        {
            // "prefix.yyyyMMdd.ext": the date sorts as text.
            int length = prefix.Length + 9 + extension.Length;
            return dir.GetFiles("*" + extension)
                .Where(f => f.Name.Length == length && f.Name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase) &&
                    f.Name.Substring(prefix.Length + 1, 8).All(char.IsDigit))
                .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>A file that cannot be read is copied to 'folder' as "prefix.unreadable-date.ext" before anything is written over it. The copy, or null.</summary>
        public static string KeepUnreadable(string source, string folder, string prefix, string extension)
        {
            try
            {
                if (!File.Exists(source))
                    return null;

                Directory.CreateDirectory(folder);
                string copy = Path.Combine(folder, prefix + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + extension);
                File.Copy(source, copy, true);
                return copy;
            }
            catch (Exception e)
            {
                log.Error("Could not keep the unreadable " + Path.GetFileName(source) + ".", e);
                return null;
            }
        }

        private static void TryCopy(string from, string to)
        {
            try
            {
                File.Copy(from, to, true);
            }
            catch (Exception e)
            {
                log.Debug("Could not copy " + Path.GetFileName(from) + ": " + e.Message);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                log.Debug("Could not delete " + Path.GetFileName(path) + ": " + e.Message);
            }
        }
    }
}
