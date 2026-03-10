using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;

namespace Guidance.Runtime
{
    // Utility probe for M8 long-run memory checks in editor/device builds.
    public sealed class ModelMemoryProbe : MonoBehaviour
    {
        [SerializeField] private bool autoRunOnStart = false;
        [SerializeField] private int iterations = 50;
        [SerializeField] private float delaySeconds = 0.05f;
        [SerializeField] private long maxAllowedDeltaBytes = 8 * 1024 * 1024;

        private readonly ModelPresenter _presenter = new ModelPresenter();
        public string LastReportPath { get; private set; }

        [System.Serializable]
        private sealed class MemoryProbeReport
        {
            public int iterations;
            public float delaySeconds;
            public long baselineAllocatedBytes;
            public long finalAllocatedBytes;
            public long deltaAllocatedBytes;
            public long maxAllowedDeltaBytes;
            public bool withinThreshold;
            public string generatedAtUtc;
        }

        private void Start()
        {
            if (autoRunOnStart)
            {
                StartCoroutine(RunProbe());
            }
        }

        public IEnumerator RunProbe()
        {
            var baseMemory = Profiler.GetTotalAllocatedMemoryLong();
            Debug.Log($"[ModelMemoryProbe] Baseline allocated bytes: {baseMemory}");

            var probeModelPath = Path.Combine(Application.temporaryCachePath, "probe-model.glb");
            if (!File.Exists(probeModelPath))
            {
                File.WriteAllBytes(probeModelPath, new byte[] { 0x67, 0x6C, 0x54, 0x46 });
            }

            for (var i = 0; i < iterations; i++)
            {
                var activation = new StepActivationDto("job-probe", i.ToString(), "PART_X", "Probe Part");
                _presenter.PresentModel(probeModelPath, activation);
                _presenter.ClearActiveModel();
                yield return new WaitForSeconds(delaySeconds);
            }

            Resources.UnloadUnusedAssets();
            System.GC.Collect();

            var finalMemory = Profiler.GetTotalAllocatedMemoryLong();
            var delta = finalMemory - baseMemory;
            Debug.Log($"[ModelMemoryProbe] Final allocated bytes: {finalMemory}");
            Debug.Log($"[ModelMemoryProbe] Delta allocated bytes: {delta}");

            WriteReport(baseMemory, finalMemory, delta);
        }

        private void WriteReport(long baselineBytes, long finalBytes, long deltaBytes)
        {
            var report = new MemoryProbeReport
            {
                iterations = iterations,
                delaySeconds = delaySeconds,
                baselineAllocatedBytes = baselineBytes,
                finalAllocatedBytes = finalBytes,
                deltaAllocatedBytes = deltaBytes,
                maxAllowedDeltaBytes = maxAllowedDeltaBytes,
                withinThreshold = deltaBytes <= maxAllowedDeltaBytes,
                generatedAtUtc = System.DateTime.UtcNow.ToString("o"),
            };

            var reportDirectory = Path.Combine(Application.persistentDataPath, "model-memory-probe");
            Directory.CreateDirectory(reportDirectory);

            var fileName = $"report-{System.DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            LastReportPath = Path.Combine(reportDirectory, fileName);

            File.WriteAllText(LastReportPath, JsonUtility.ToJson(report, true));

            if (report.withinThreshold)
            {
                Debug.Log($"[ModelMemoryProbe] Report written: {LastReportPath}");
                return;
            }

            Debug.LogWarning(
                $"[ModelMemoryProbe] Delta exceeded threshold ({report.deltaAllocatedBytes} > {report.maxAllowedDeltaBytes}). Report: {LastReportPath}"
            );
        }
    }
}
