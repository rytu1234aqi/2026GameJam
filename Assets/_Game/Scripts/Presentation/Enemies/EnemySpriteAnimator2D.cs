using System;
using UnityEngine;

namespace Spotlight.Presentation
{
    [DisallowMultipleComponent]
    public sealed class EnemySpriteAnimator2D : MonoBehaviour
    {
        public SpriteRenderer BodyRenderer;
        public Sprite[] IdleFrames;
        public Sprite[] MoveFrames;
        public Sprite[] AttackFrames;
        public Sprite[] DeathFrames;
        public float[] IdleScales;
        public float[] MoveScales;
        public float[] AttackScales;
        public float[] DeathScales;
        public float FramesPerSecond = 6f;
        public float AttackSeconds = .3f;
        public float DeathSeconds = .5f;

        public bool IsDeathPlaying { get { return deathPlaying; } }

        private SpriteRenderer cachedRenderer;
        private Sprite staticSprite;
        private Vector3 baseBodyScale;
        private bool hasTime;
        private double lastTime;
        private double locomotionStart;
        private double actionStart;
        private bool wasMoving;
        private bool attackPlaying;
        private bool deathPlaying;

        private void OnEnable() { ResetVisual(); Show(IdleFrames, IdleScales, 0); }
        private void OnDisable() { ResetVisual(); }

        public void ResetVisual()
        {
            hasTime = false;
            lastTime = locomotionStart = actionStart = 0d;
            wasMoving = attackPlaying = deathPlaying = false;
            Show(IdleFrames, IdleScales, 0);
            RestoreBodyScale();
        }

        public void ApplyVisualTime(double gameTime, bool moving)
        {
            CacheRenderer();
            if (!IsFinite(gameTime))
            {
                if (BodyRenderer != null && BodyRenderer.sprite == null) Show(IdleFrames, IdleScales, 0);
                return;
            }

            // 时间由游戏时钟提供；暂停时重复调用不会累计帧数。
            if (!hasTime)
            {
                hasTime = true;
                locomotionStart = gameTime;
            }
            else if (gameTime < lastTime)
            {
                // 时钟重置也重置表现，避免沿用上一局的动作。
                ResetVisual();
                hasTime = true;
                locomotionStart = gameTime;
            }
            if (moving != wasMoving) locomotionStart = gameTime;
            wasMoving = moving;
            lastTime = gameTime;

            if (deathPlaying)
            {
                Show(DeathFrames, DeathScales, ActionFrame(DeathFrames, gameTime, DeathSeconds));
                return;
            }
            if (attackPlaying)
            {
                if (gameTime - actionStart < SafeDuration(AttackSeconds))
                {
                    Show(AttackFrames, AttackScales, ActionFrame(AttackFrames, gameTime, AttackSeconds));
                    return;
                }
                attackPlaying = false;
            }

            Sprite[] frames = moving ? MoveFrames : IdleFrames;
            int count = frames == null ? 0 : frames.Length;
            double fps = IsFinite(FramesPerSecond) && FramesPerSecond > 0f ? FramesPerSecond : 0d;
            double elapsed = Math.Max(0d, gameTime - locomotionStart);
            double position = elapsed * fps;
            int index = count > 0 && IsFinite(position) ? (int)(Math.Floor(position) % count) : 0;
            Show(frames, moving ? MoveScales : IdleScales, index);
        }

        public void PlayAttack(double gameTime)
        {
            if (deathPlaying || !IsFinite(gameTime)) return;
            ApplyVisualTime(gameTime, wasMoving);
            actionStart = gameTime;
            attackPlaying = true;
            Show(AttackFrames, AttackScales, 0);
        }

        public void PlayDeath(double gameTime)
        {
            if (deathPlaying || !IsFinite(gameTime)) return;
            ApplyVisualTime(gameTime, wasMoving);
            actionStart = gameTime;
            attackPlaying = false;
            deathPlaying = true;
            Show(DeathFrames, DeathScales, 0);
        }

        private int ActionFrame(Sprite[] frames, double gameTime, float duration)
        {
            int count = frames == null ? 0 : frames.Length;
            if (count <= 1) return 0;
            double seconds = SafeDuration(duration);
            double elapsed = Math.Max(0d, gameTime - actionStart);
            if (seconds <= 0d || elapsed >= seconds) return count - 1;
            return Math.Min(count - 1, (int)(elapsed / seconds * count));
        }

        private static double SafeDuration(float seconds)
        {
            return IsFinite(seconds) && seconds > 0f ? seconds : 0d;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private void CacheRenderer()
        {
            if (BodyRenderer == cachedRenderer) return;
            RestoreBodyScale();
            cachedRenderer = BodyRenderer;
            staticSprite = BodyRenderer == null ? null : BodyRenderer.sprite;
            baseBodyScale = BodyRenderer == null ? Vector3.one : BodyRenderer.transform.localScale;
        }

        private void RestoreBodyScale()
        {
            if (cachedRenderer != null && cachedRenderer.transform != transform)
                cachedRenderer.transform.localScale = baseBodyScale;
        }

        private void Show(Sprite[] frames, float[] scales, int index)
        {
            CacheRenderer();
            if (BodyRenderer == null) return;
            int selected = FrameOrFirstIndex(frames, index);
            if (selected < 0)
            {
                frames = IdleFrames;
                scales = IdleScales;
                selected = FrameOrFirstIndex(frames, 0);
            }
            if (selected < 0)
            {
                frames = MoveFrames;
                scales = MoveScales;
                selected = FrameOrFirstIndex(frames, 0);
            }
            Sprite sprite = selected < 0 ? staticSprite : frames[selected];
            float scale = selected >= 0 && scales != null && selected < scales.Length ? scales[selected] : 1f;
            if (!IsFinite(scale) || scale <= 0f) scale = 1f;
            // 每次从初始比例计算，避免暂停重复调用或对象复用时累计缩放。
            if (BodyRenderer.transform != transform)
                BodyRenderer.transform.localScale = baseBodyScale * scale;
            // 不修改颜色、朝向或对象生命周期，交给现有表现组件处理。
            if (sprite != null) BodyRenderer.sprite = sprite;
        }

        private static int FrameOrFirstIndex(Sprite[] frames, int index)
        {
            if (frames == null || frames.Length == 0) return -1;
            if (index >= 0 && index < frames.Length && frames[index] != null) return index;
            for (int i = 0; i < frames.Length; i++)
                if (frames[i] != null) return i;
            return -1;
        }
    }
}
