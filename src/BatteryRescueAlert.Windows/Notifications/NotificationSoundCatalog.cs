using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using BatteryRescueAlert.Core.Settings;

namespace BatteryRescueAlert.Windows.Notifications
{
    /// <summary>
    /// Manages user-owned copies of imported tones. The app never depends on the original file after import.
    /// </summary>
    public sealed class NotificationSoundCatalog
    {
        private const long MaxToneBytes = 20L * 1024L * 1024L;
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".wav", ".mp3", ".wma", ".m4a", ".aac", ".flac"
        };

        public NotificationSoundCatalog(string folderPath)
        {
            FolderPath = folderPath ?? throw new ArgumentNullException(nameof(folderPath));
            Directory.CreateDirectory(FolderPath);
        }

        public string FolderPath { get; }

        public IReadOnlyList<NotificationSoundOption> GetOptions()
        {
            Directory.CreateDirectory(FolderPath);
            var options = new List<NotificationSoundOption>
            {
                new(AppSettings.DefaultNotificationSoundId, "Windows notification (default)")
            };
            options.AddRange(GetCurrentWindowsSoundOptions());

            foreach (string path in Directory.EnumerateFiles(FolderPath)
                         .Where(IsSupportedTonePath)
                         .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase))
            {
                string fileName = Path.GetFileName(path);
                string stem = Path.GetFileNameWithoutExtension(fileName);
                string displayName = GetDisplayName(stem);
                options.Add(new NotificationSoundOption("custom:" + fileName, "Custom: " + displayName, IsCustom: true));
            }

            return options;
        }

        private static IReadOnlyList<NotificationSoundOption> GetCurrentWindowsSoundOptions()
        {
            var options = new List<NotificationSoundOption>();
            try
            {
                using RegistryKey? events = Registry.CurrentUser.OpenSubKey(
                    @"AppEvents\Schemes\Apps\.Default");
                if (events is null)
                {
                    return options;
                }

                foreach (string eventName in events.GetSubKeyNames().OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
                {
                    if (string.Equals(eventName, "Notification.Default", StringComparison.OrdinalIgnoreCase))
                    {
                        // This is already the first/default option above.
                        continue;
                    }

                    string? soundPath = GetEventPath(eventName);
                    if (string.IsNullOrWhiteSpace(soundPath) || !File.Exists(soundPath))
                    {
                        continue;
                    }

                    string displayName = GetEventDisplayName(eventName);
                    options.Add(new NotificationSoundOption("system-event:" + eventName, "Windows: " + displayName));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // The default notification sound remains available even if the registry cannot be read.
            }

            return options;
        }

        private static string? GetEventPath(string eventName)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                    @"AppEvents\Schemes\Apps\.Default\" + eventName + @"\.Current");
                string? raw = key?.GetValue(null, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                string path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                return Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? path : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return null;
            }
        }

        private static string GetEventDisplayName(string eventName)
        {
            try
            {
                using RegistryKey? label = Registry.CurrentUser.OpenSubKey(
                    @"AppEvents\EventLabels\" + eventName);
                if (label?.GetValue(null) is string name && !string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Use the event key name below.
            }

            return eventName.Replace('.', ' ');
        }

        public NotificationSoundOption Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Choose an audio file that still exists.", sourcePath);
            }

            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension))
            {
                throw new InvalidDataException("Supported tone formats are WAV, MP3, WMA, M4A, AAC and FLAC.");
            }

            var source = new FileInfo(sourcePath);
            if (source.Length <= 0)
            {
                throw new InvalidDataException("The selected audio file is empty.");
            }

            if (source.Length > MaxToneBytes)
            {
                throw new InvalidDataException("A custom tone must be 20 MB or smaller.");
            }

            Directory.CreateDirectory(FolderPath);
            string originalStem = Path.GetFileNameWithoutExtension(sourcePath);
            string safeStem = new string(originalStem
                .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                .Take(48)
                .ToArray());
            if (string.IsNullOrWhiteSpace(safeStem))
            {
                safeStem = "Custom-tone";
            }

            string fileName = $"tone-{Guid.NewGuid():N}-{safeStem}{extension}";
            string destination = Path.Combine(FolderPath, fileName);
            string temporary = destination + ".tmp";

            try
            {
                File.Copy(sourcePath, temporary, overwrite: false);
                File.Move(temporary, destination);
                File.SetAttributes(destination, FileAttributes.Normal);
            }
            catch
            {
                TryDeleteFile(temporary);
                TryDeleteFile(destination);
                throw;
            }

            return new NotificationSoundOption("custom:" + fileName, "Custom: " + GetDisplayName(Path.GetFileNameWithoutExtension(fileName)), IsCustom: true);
        }

        public string? GetCustomTonePath(string soundId)
        {
            if (!TryGetSafeCustomFileName(soundId, out string fileName))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(Path.Combine(FolderPath, fileName));
            string root = Path.GetFullPath(FolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return fullPath;
        }

        public bool TryDelete(string soundId, out string? error)
        {
            error = null;
            string? path = GetCustomTonePath(soundId);
            if (path is null)
            {
                error = "Only imported custom tones can be deleted here.";
                return false;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "The copied tone could not be deleted. Close any program using the file and try again.";
                return false;
            }
        }

        private static bool IsSupportedTonePath(string path) =>
            SupportedExtensions.Contains(Path.GetExtension(path)) && !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetSafeCustomFileName(string soundId, out string fileName)
        {
            fileName = string.Empty;
            const string prefix = "custom:";
            if (string.IsNullOrWhiteSpace(soundId) || !soundId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string candidate = soundId.Substring(prefix.Length);
            if (string.IsNullOrWhiteSpace(candidate) ||
                !string.Equals(Path.GetFileName(candidate), candidate, StringComparison.Ordinal) ||
                candidate.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !SupportedExtensions.Contains(Path.GetExtension(candidate)))
            {
                return false;
            }

            fileName = candidate;
            return true;
        }

        private static string GetDisplayName(string stem)
        {
            // Imported file names carry a unique prefix to prevent overwrites. Hide that prefix in the UI.
            if (stem.StartsWith("tone-", StringComparison.OrdinalIgnoreCase) &&
                stem.Length > 38 && stem[37] == '-')
            {
                stem = stem.Substring(38);
            }

            return stem.Replace('_', ' ');
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
