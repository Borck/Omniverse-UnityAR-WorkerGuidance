using System;
using UnityEngine;

namespace Guidance.Runtime
{
    // Safe fallback loader used when no runtime glTF loader is installed.
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
