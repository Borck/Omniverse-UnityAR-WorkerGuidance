using System.Collections.Generic;
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace Guidance.Editor
{
    /// <summary>
    /// Defensive cleanup pass that removes duplicate keys from any
    /// gradle.properties file Unity stages for the Android build.
    ///
    /// Vuforia's post-processor parses gradle.properties via LINQ
    /// ToDictionary(), which throws on duplicate keys. The historical cause
    /// of duplicates in this project was the com.unity.purchasing package
    /// (now removed) appending its own android.useAndroidX / android.enableJetifier
    /// section even though Unity 6 already injects those keys.
    ///
    /// This fixer remains as insurance against any future package that
    /// behaves the same way. It runs at the most negative callbackOrder
    /// Unity allows so that Vuforia (callbackOrder = 0) always reads a
    /// clean file regardless of what other tooling has done.
    /// </summary>
    public sealed class GradlePropertiesDuplicateFixer : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MinValue + 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var seen  = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var files = new List<string>();

            // Walk up the directory tree to find the Gradle project root, then
            // recursively pick up every gradle.properties under it.
            var root = FindGradleRoot(path);
            if (root != null)
            {
                CollectGradleProperties(root, files, seen, recursive: true);
            }
            else
            {
                CollectGradleProperties(path, files, seen, recursive: false);
            }

            foreach (var file in files)
            {
                DeduplicateFile(file);
            }
        }

        private static void CollectGradleProperties(string dir, List<string> output,
                                                    HashSet<string> seen, bool recursive)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.GetFiles(dir, "gradle.properties", option))
                {
                    var full = Path.GetFullPath(f);
                    if (seen.Add(full)) output.Add(full);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GradlePropertiesDuplicateFixer] Could not scan {dir}: {ex.Message}");
            }
        }

        private static void DeduplicateFile(string filePath)
        {
            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[GradlePropertiesDuplicateFixer] Could not read {filePath}: {ex.Message}");
                return;
            }

            var seenKeys   = new HashSet<string>();
            var cleanLines = new List<string>(lines.Length);
            bool changed   = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                {
                    cleanLines.Add(line);
                    continue;
                }

                var eq  = trimmed.IndexOf('=');
                var key = eq >= 0 ? trimmed.Substring(0, eq).Trim() : trimmed;

                if (seenKeys.Add(key))
                {
                    cleanLines.Add(line);
                }
                else
                {
                    changed = true;
                    Debug.LogWarning($"[GradlePropertiesDuplicateFixer] Removed duplicate '{key}' from {filePath}");
                }
            }

            if (changed)
            {
                try { File.WriteAllLines(filePath, cleanLines); }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[GradlePropertiesDuplicateFixer] Could not write {filePath}: {ex.Message}");
                }
            }
        }

        private static string FindGradleRoot(string startPath)
        {
            var dir = new DirectoryInfo(startPath);
            for (int i = 0; i < 6 && dir != null; i++)
            {
                var hasSettings   = File.Exists(Path.Combine(dir.FullName, "settings.gradle"))
                                 || File.Exists(Path.Combine(dir.FullName, "settings.gradle.kts"));
                var hasProperties = File.Exists(Path.Combine(dir.FullName, "gradle.properties"));
                if (hasSettings || hasProperties) return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
