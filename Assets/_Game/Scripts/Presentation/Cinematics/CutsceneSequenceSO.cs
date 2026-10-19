using System;
using UnityEngine;

namespace Spotlight.Presentation.Cinematics
{
    [Serializable]
    public sealed class CutsceneSlide
    {
        public Sprite Illustration;
        public string Title;
        [TextArea(3, 12)] public string Body;
        public float FadeSeconds = .65f, CharactersPerSecond = 26, HoldSeconds = 2, MotionSeconds = 8;
        public float ZoomStart = 1, ZoomEnd = 1.06f;
        public Vector2 PanStart = new Vector2(-.015f, 0), PanEnd = new Vector2(.015f, 0);
        public bool AutoAdvance;
    }

    [CreateAssetMenu(menuName = "聚光灯/图文过场")]
    public sealed class CutsceneSequenceSO : ScriptableObject
    {
        public string Id = "story.opening";
        [Tooltip("允许跳过整段剧情；默认关闭。")] public bool AllowSkip;
        public CutsceneSlide[] Slides = new CutsceneSlide[0];
        public bool Validate(out string error)
        {
            if (Slides == null || Slides.Length == 0) { error = "剧情没有页面。"; return false; }
            for (int i = 0; i < Slides.Length; i++)
            {
                CutsceneSlide s = Slides[i];
                if (s == null || !Valid(s.FadeSeconds, 0) || !Valid(s.CharactersPerSecond, .01f) ||
                    !Valid(s.HoldSeconds, 0) || !Valid(s.MotionSeconds, .01f) ||
                    !Valid(s.ZoomStart, .01f) || !Valid(s.ZoomEnd, .01f) ||
                    !Valid(s.PanStart.x, float.MinValue) || !Valid(s.PanStart.y, float.MinValue) ||
                    !Valid(s.PanEnd.x, float.MinValue) || !Valid(s.PanEnd.y, float.MinValue))
                { error = "剧情第 " + (i + 1) + " 页的时间或镜头参数无效。"; return false; }
            }
            error = null; return true;
        }
        private static bool Valid(float value, float minimum)
        { return !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum; }
    }
}
