using System;
using System.Collections;
using System.IO;
using UnityEngine;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace Guidance.Runtime
{
    public static class VuforiaModelTargetLoader
    {
#if VUFORIA_ENGINE
        /// <summary>
        /// Loads a Model Target database following the official Vuforia runtime scripting pattern.
        /// The .xml/.dat pair must be in StreamingAssets/Vuforia/ (bundled in the build).
        /// databaseFilePath is only used to derive the database name — the actual files read
        /// are always the bundled ones, which Vuforia handles correctly on Android too.
        /// </summary>
        public static IEnumerator LoadModelTargetDatabaseAsync(
            string databaseFilePath,
            string targetNameFallback,
            Action<ObserverBehaviour> onLoaded,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(databaseFilePath))
            {
                onError?.Invoke("[VuforiaModelTargetLoader] databaseFilePath is null or empty");
                yield break;
            }

            // Derive the absolute path to the downloaded XML file.
            // Vuforia CreateModelTarget accepts absolute paths for runtime-downloaded databases.
            string dbName = Path.GetFileNameWithoutExtension(databaseFilePath);
            string absoluteXmlPath = Path.ChangeExtension(databaseFilePath, ".xml");

            // Validate that both required files exist in the download cache.
            string absoluteDatPath = Path.ChangeExtension(databaseFilePath, ".dat");
            if (!File.Exists(absoluteXmlPath) || !File.Exists(absoluteDatPath))
            {
                onError?.Invoke(
                    $"[VuforiaModelTargetLoader] Database files missing in cache — " +
                    $"xml={File.Exists(absoluteXmlPath)} dat={File.Exists(absoluteDatPath)} " +
                    $"(xml: {absoluteXmlPath})");
                yield break;
            }

            string exactTargetName = ExtractTargetNameFromXml(absoluteXmlPath, targetNameFallback ?? dbName);

            bool done = false;
            string capturedError = null;
            ObserverBehaviour capturedResult = null;

            void TryCreateTarget()
            {
                try
                {
                    // Absolute path to the runtime-downloaded XML — Vuforia supports absolute paths.
                    var modelTarget = VuforiaBehaviour.Instance.ObserverFactory.CreateModelTarget(
                        absoluteXmlPath, exactTargetName);

                    if (modelTarget != null)
                    {
                        modelTarget.gameObject.AddComponent<RuntimeTargetVisibilityManager>();
                        modelTarget.OnTargetStatusChanged += (b, s) =>
                            Debug.Log($"[VuforiaModelTargetLoader] Target status: {s.Status}");
                        Debug.Log($"[VuforiaModelTargetLoader] Target created: {modelTarget.TargetName}");
                        capturedResult = modelTarget;
                    }
                    else
                    {
                        capturedError = $"[VuforiaModelTargetLoader] CreateModelTarget returned null for '{exactTargetName}' (path: {absoluteXmlPath}).";
                    }
                }
                catch (Exception ex)
                {
                    capturedError = $"[VuforiaModelTargetLoader] Exception creating target: {ex.Message}";
                }
                done = true;
            }

            if (VuforiaApplication.Instance.IsInitialized)
            {
                // Vuforia is already running — create the target immediately (official pattern)
                TryCreateTarget();
            }
            else
            {
                // Vuforia not yet started — subscribe to OnVuforiaStarted (official pattern)
                VuforiaApplication.Instance.OnVuforiaStarted += TryCreateTarget;

                float waited = 0f;
                while (!done && waited < 10f)
                {
                    yield return null;
                    waited += Time.deltaTime;
                }

                VuforiaApplication.Instance.OnVuforiaStarted -= TryCreateTarget;

                if (!done)
                {
                    onError?.Invoke("[VuforiaModelTargetLoader] Timed out waiting for Vuforia to start (10 s).");
                    yield break;
                }
            }

            if (capturedError != null)
                onError?.Invoke(capturedError);
            else
                onLoaded?.Invoke(capturedResult);
        }

        private static string ExtractTargetNameFromXml(string xmlFilePath, string fallbackName)
        {
            try
            {
                if (File.Exists(xmlFilePath))
                {
                    string content = File.ReadAllText(xmlFilePath);
                    const string searchStr = "<ModelTarget name=\"";
                    int start = content.IndexOf(searchStr, StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        start += searchStr.Length;
                        int end = content.IndexOf('"', start);
                        if (end > start) return content.Substring(start, end - start);
                    }
                }
            }
            catch { }
            return fallbackName;
        }
#endif
    }

#if VUFORIA_ENGINE
    public class RuntimeTargetVisibilityManager : MonoBehaviour
    {
        private ObserverBehaviour _observer;

        void Start()
        {
            _observer = GetComponent<ObserverBehaviour>();
            if (_observer)
            {
                _observer.OnTargetStatusChanged += OnStatusChanged;
                UpdateVisibility(_observer.TargetStatus.Status);
            }
        }

        void OnDestroy()
        {
            if (_observer) _observer.OnTargetStatusChanged -= OnStatusChanged;
        }

        private void OnStatusChanged(ObserverBehaviour behaviour, TargetStatus targetStatus)
        {
            UpdateVisibility(targetStatus.Status);
        }

        private void UpdateVisibility(Status status)
        {
            bool isTracked = status == Status.TRACKED || status == Status.EXTENDED_TRACKED;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = isTracked;
        }
    }
#endif
}
