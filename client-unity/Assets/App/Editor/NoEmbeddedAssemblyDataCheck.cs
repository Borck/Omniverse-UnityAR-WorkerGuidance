using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Guidance.Editor
{
    /// <summary>
    /// Pre-build validation step that aborts the build if any assembly-specific
    /// data files (GLB models, Vuforia target databases, manifest JSON, animation clips
    /// not shipped as part of the engine) are found inside the Unity project's
    /// Assets or StreamingAssets folders.
    ///
    /// The Unity AR client must ship with ZERO embedded assembly data. All models,
    /// targets, and assembly information are fetched from the server at runtime.
    /// </summary>
    public sealed class NoEmbeddedAssemblyDataCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        // File extensions that must not be present in the project (outside of Editor-only paths).
        private static readonly string[] ForbiddenExtensions =
        {
            ".glb",
            ".gltf",
            ".dat",   // Vuforia model target database
            ".xml",   // Vuforia dataset descriptor (paired with .dat)
        };

        // File name patterns that indicate embedded manifest / step data.
        private static readonly string[] ForbiddenNamePatterns =
        {
            ".manifest.json",
            ".step.json",
        };

        // Paths that are explicitly excluded from the check (Editor-only test fixtures).
        private static readonly string[] AllowedSubPaths =
        {
            Path.Combine("Assets", "App", "Tests"),
        };

        public void OnPreprocessBuild(BuildReport report)
        {
            var assetsRoot = Path.GetFullPath("Assets");
            var streamingRoot = Path.GetFullPath(Path.Combine("Assets", "StreamingAssets"));

            var violations = Directory
                .GetFiles(assetsRoot, "*", SearchOption.AllDirectories)
                .Where(f => !IsInAllowedPath(f))
                .Where(f => IsViolation(f))
                .ToList();

            if (violations.Count == 0)
            {
                return;
            }

            var list = string.Join("\n  ", violations.Select(f => f.Replace(assetsRoot, "Assets")));
            var message =
                $"[NoEmbeddedAssemblyDataCheck] Build aborted: {violations.Count} forbidden assembly data " +
                $"file(s) found in the project bundle.\n\n" +
                $"The Unity AR client must not embed any assembly-specific content.\n" +
                $"All models, targets, and manifests must be fetched from the server at runtime.\n\n" +
                $"Remove the following files:\n  {list}";

            Debug.LogError(message);
            throw new BuildFailedException(message);
        }

        private static bool IsInAllowedPath(string filePath)
        {
            var normalised = filePath.Replace('\\', '/');
            return AllowedSubPaths.Any(allowed =>
                normalised.Contains(allowed.Replace('\\', '/')));
        }

        private static bool IsViolation(string filePath)
        {
            var lowerName = Path.GetFileName(filePath).ToLowerInvariant();
            var lowerFull = filePath.ToLowerInvariant();

            foreach (var ext in ForbiddenExtensions)
            {
                if (lowerName.EndsWith(ext))
                {
                    return true;
                }
            }

            foreach (var pattern in ForbiddenNamePatterns)
            {
                if (lowerFull.Contains(pattern.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
