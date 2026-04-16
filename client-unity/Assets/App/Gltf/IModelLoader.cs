using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Loader contract for runtime model presentation backends.
    /// </summary>
    public interface IModelLoader
    {
        bool CanLoad(string modelFilePath);

        /// <summary>
        /// Synchronous loader kept for Editor test compatibility.
        /// Prefer <see cref="LoadModelAsync"/> in production code.
        /// </summary>
        void LoadModel(string modelFilePath, Transform parent, Action<string> onError);

        /// <summary>Asynchronously loads and instantiates the model under <paramref name="parent"/>.</summary>
        Task LoadModelAsync(string modelFilePath, Transform parent, CancellationToken ct);
    }
}
