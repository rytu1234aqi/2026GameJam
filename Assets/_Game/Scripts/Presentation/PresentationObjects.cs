using UnityEngine;
namespace Spotlight.Presentation
{
    public static class PresentationObjects
    {
        public static void Release(Object value)
        {
            if(value==null)return;
            if(Application.isPlaying)Object.Destroy(value);else Object.DestroyImmediate(value);
        }
    }
}
