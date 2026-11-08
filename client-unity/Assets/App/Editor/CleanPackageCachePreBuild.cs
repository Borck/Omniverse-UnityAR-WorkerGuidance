using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Guidance.Editor
{
    /// <summary>
    /// Deletes IDE-generated .lscache files from Library/PackageCache before each build.
    /// These files are created by Visual Studio's language server inside immutable UPM
    /// package folders. Unity cannot assign them a .meta file and aborts the build.
    /// callbackOrder -100 ensures this runs before Vuforia's BuildObserver (order 0),
    /// which triggers AssetDatabase.Refresh() and would otherwise surface the file.
    /// </summary>
    public sealed class CleanPackageCachePreBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            var packageCacheRoot = Path.GetFullPath("Library/PackageCache");
            if (!Directory.Exists(packageCacheRoot))
                return;

            var deleted = 0;
            foreach (var file in Directory.GetFiles(packageCacheRoot, "*.lscache", SearchOption.AllDirectories))
            {
                File.Delete(file);
                deleted++;
            }

            if (deleted > 0)
                Debug.Log($"[CleanPackageCachePreBuild] Removed {deleted} stale .lscache file(s) from PackageCache.");
        }
    }
}
