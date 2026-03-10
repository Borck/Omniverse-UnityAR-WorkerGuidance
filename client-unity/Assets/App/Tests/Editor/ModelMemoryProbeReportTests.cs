using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Guidance.Runtime.Tests.Editor
{
    public sealed class ModelMemoryProbeReportTests
    {
        [System.Serializable]
        private sealed class ProbeReportDto
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

        [Test]
        public void WriteReport_CreatesValidJson_WithExpectedFields()
        {
            var host = new GameObject("MemoryProbeTestHost");
            try
            {
                var probe = host.AddComponent<ModelMemoryProbe>();
                SetPrivateField(probe, "iterations", 5);
                SetPrivateField(probe, "delaySeconds", 0.01f);
                SetPrivateField(probe, "maxAllowedDeltaBytes", 64L);

                InvokeWriteReport(probe, baselineBytes: 100L, finalBytes: 120L, deltaBytes: 20L);

                var reportPath = probe.LastReportPath;
                Assert.IsFalse(string.IsNullOrEmpty(reportPath), "Report path must be set");
                Assert.IsTrue(File.Exists(reportPath), $"Report file missing: {reportPath}");

                var json = File.ReadAllText(reportPath);
                var report = JsonUtility.FromJson<ProbeReportDto>(json);

                Assert.NotNull(report, "Report JSON should deserialize");
                Assert.AreEqual(5, report.iterations);
                Assert.AreEqual(0.01f, report.delaySeconds);
                Assert.AreEqual(100L, report.baselineAllocatedBytes);
                Assert.AreEqual(120L, report.finalAllocatedBytes);
                Assert.AreEqual(20L, report.deltaAllocatedBytes);
                Assert.AreEqual(64L, report.maxAllowedDeltaBytes);
                Assert.IsTrue(report.withinThreshold);
                Assert.IsFalse(string.IsNullOrEmpty(report.generatedAtUtc));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void WriteReport_SetsWithinThresholdFalse_WhenDeltaExceedsLimit()
        {
            var host = new GameObject("MemoryProbeThresholdHost");
            try
            {
                var probe = host.AddComponent<ModelMemoryProbe>();
                SetPrivateField(probe, "maxAllowedDeltaBytes", 10L);

                InvokeWriteReport(probe, baselineBytes: 100L, finalBytes: 150L, deltaBytes: 50L);

                var json = File.ReadAllText(probe.LastReportPath);
                var report = JsonUtility.FromJson<ProbeReportDto>(json);
                Assert.NotNull(report);
                Assert.IsFalse(report.withinThreshold);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static void InvokeWriteReport(ModelMemoryProbe probe, long baselineBytes, long finalBytes, long deltaBytes)
        {
            var method = typeof(ModelMemoryProbe).GetMethod("WriteReport", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method, "WriteReport method must exist");
            method.Invoke(probe, new object[] { baselineBytes, finalBytes, deltaBytes });
        }

        private static void SetPrivateField(ModelMemoryProbe probe, string fieldName, object value)
        {
            var field = typeof(ModelMemoryProbe).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field, $"Missing private field: {fieldName}");
            field.SetValue(probe, value);
        }
    }
}
