using System;
using System.Collections;
using UnityEngine;

#if VUFORIA_ENGINE
using Vuforia;
#endif

namespace Guidance.Runtime
{
    /// <summary>
    /// Loads a Vuforia Model Target database that was downloaded at runtime from the server.
    /// The database files (<c>.dat</c> / <c>.xml</c>) are never bundled in the app; they are
    /// fetched from the guidance server and stored in <c>Application.persistentDataPath</c>.
    ///
    /// Uses a coroutine so that Vuforia API calls happen on the Unity main thread
    /// without dropping frames.
    /// </summary>
    public static class VuforiaModelTargetLoader
    {
#if VUFORIA_ENGINE
        /// <summary>
        /// Coroutine that activates a Vuforia Model Target dataset from a downloaded .dat file path.
        /// </summary>
        /// <param name="datFilePath">
        /// Absolute path to the <c>.dat</c> file. The paired <c>.xml</c> must exist alongside it.
        /// </param>
        /// <param name="onLoaded">
        /// Called with the activated <see cref="ObserverBehaviour"/> on success.
        /// </param>
        /// <param name="onError">Called with an error description on failure.</param>
        public static IEnumerator LoadModelTargetDatabaseAsync(
            string datFilePath,
            Action<ObserverBehaviour> onLoaded,
            Action<string> onError)
        {
            if (string.IsNullOrEmpty(datFilePath))
            {
                onError?.Invoke("VuforiaModelTargetLoader: datFilePath is null or empty");
                yield break;
            }

            ObjectTracker objectTracker = null;
            DataSet dataSet = null;

            // Retrieve the ObjectTracker on the main thread
            objectTracker = TrackerManager.Instance.GetTracker<ObjectTracker>();
            if (objectTracker == null)
            {
                onError?.Invoke("VuforiaModelTargetLoader: ObjectTracker is not available");
                yield break;
            }

            objectTracker.Stop();

            // CreateDataSet and ActivateDataSet must run on the main thread
            dataSet = objectTracker.CreateDataSet();
            if (dataSet == null)
            {
                onError?.Invoke("VuforiaModelTargetLoader: Failed to create DataSet");
                objectTracker.Start();
                yield break;
            }

            bool loaded = dataSet.Load(datFilePath, VuforiaUnity.StorageType.STORAGE_ABSOLUTE);
            if (!loaded)
            {
                onError?.Invoke($"VuforiaModelTargetLoader: DataSet.Load failed for {datFilePath}");
                objectTracker.Start();
                yield break;
            }

            bool activated = objectTracker.ActivateDataSet(dataSet);
            if (!activated)
            {
                onError?.Invoke("VuforiaModelTargetLoader: ActivateDataSet failed");
                objectTracker.Start();
                yield break;
            }

            objectTracker.Start();

            // Allow Vuforia one frame to register the observers
            yield return null;

            ObserverBehaviour observer = null;
            foreach (var trackable in dataSet.GetTrackables<TrackableBehaviour>())
            {
                observer = trackable as ObserverBehaviour;
                if (observer != null)
                {
                    break;
                }
            }

            if (observer == null)
            {
                onError?.Invoke("VuforiaModelTargetLoader: No ObserverBehaviour found after activation");
                yield break;
            }

            Debug.Log($"[VuforiaModelTargetLoader] Loaded runtime target from {datFilePath}");
            onLoaded?.Invoke(observer);
        }

#else

        /// <summary>
        /// No-op fallback used when Vuforia Engine is not present in the project.
        /// Logs a warning and immediately calls <paramref name="onLoaded"/> with null.
        /// </summary>
        public static IEnumerator LoadModelTargetDatabaseAsync(
            string datFilePath,
            Action<UnityEngine.Object> onLoaded,
            Action<string> onError)
        {
            Debug.LogWarning("[VuforiaModelTargetLoader] Vuforia Engine is not available — target loading skipped.");
            onLoaded?.Invoke(null);
            yield break;
        }

#endif
    }
}
