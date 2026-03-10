using System;
using UnityEngine;

namespace Guidance.Runtime
{
    /// <summary>
    /// Safe fallback loader that spawns a primitive when glTF runtime loading is unavailable.
    /// </summary>
    public sealed class PrimitiveFallbackModelLoader : IModelLoader
    {
        public bool CanLoad(string modelFilePath)
        {
            return true;
        }

        public void LoadModel(string modelFilePath, Transform parent, Action<string> onError)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "PreviewModel";
            visual.transform.SetParent(parent, false);
            visual.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
        }
    }
}
