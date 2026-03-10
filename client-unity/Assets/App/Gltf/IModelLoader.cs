using System;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Loader contract for runtime model presentation backends.
    /// </summary>
    public interface IModelLoader
    {
        bool CanLoad(string modelFilePath);
        void LoadModel(string modelFilePath, Transform parent, Action<string> onError);
    }
}
