using System;
using System.IO;
using UnityEngine;

namespace Guidance.Runtime
{
    [Serializable]
    public sealed class RuntimeDiagnosticsSnapshot
    {
        public string generatedAtUtc;
        public string unityVersion;
        public string deviceModel;
        public string operatingSystem;
        public string connectionState;
        public string stepState;
        public string activeStepId;
        public string activePartId;
        public string activeTargetId;
        public string activeTargetVersion;
        public bool trackingAcquired;
    }

    public sealed class DiagnosticsBundleExporter
    {
        public string Export(RuntimeDiagnosticsSnapshot snapshot)
        {
            var outputDirectory = Path.Combine(Application.persistentDataPath, "diagnostics");
            Directory.CreateDirectory(outputDirectory);

            var filePath = Path.Combine(
                outputDirectory,
                $"guidance-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"
            );

            File.WriteAllText(filePath, JsonUtility.ToJson(snapshot, true));
            return filePath;
        }
    }
}
