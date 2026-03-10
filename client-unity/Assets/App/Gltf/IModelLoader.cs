using System;
using UnityEngine;

namespace Guidance.Runtime
{
    public interface IModelLoader
    {
        bool CanLoad(string modelFilePath);
        void LoadModel(string modelFilePath, Transform parent, Action<string> onError);
    }
}
