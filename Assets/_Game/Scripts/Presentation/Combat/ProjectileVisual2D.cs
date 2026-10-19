using UnityEngine;
using Spotlight.Contracts;

namespace Spotlight.Presentation
{
    public class ProjectileVisual2D : MonoBehaviour
    {
        public Transform VisualRoot;
        public SpriteRenderer ElementOverlay;

        [Header("Element Effects (顺序: 水/火/土/木/风/雷)")]
        public GameObject[] elementEffectObjects = new GameObject[6];

        private GameObject[] cachedEffects;
        private ParticleSystem[][] cachedParticles;
        private int selectedEffect = -1;
        private bool hasVisualTime;
        private double lastVisualTime;

        private Quaternion baseRotation;
        private Color baseColor;
        private bool cachedRotation;
        private bool cachedColor;

        private void Awake() { CacheBaseVisual(); }
        private void OnEnable() { ResetVisual(); }
        private void OnDisable() { ResetVisual(); }

        private void CacheBaseVisual()
        {
            if (!cachedRotation && VisualRoot != null)
            {
                baseRotation = VisualRoot.localRotation;
                cachedRotation = true;
            }
            if (!cachedColor && ElementOverlay != null)
            {
                baseColor = ElementOverlay.color;
                cachedColor = true;
            }
        }

        private static readonly Color[] ElementColors =
        {
            new Color32(64, 156, 255, 255),  // Water
            new Color32(255, 83, 64, 255),   // Fire
            new Color32(190, 146, 62, 255),  // Earth
            new Color32(62, 190, 104, 255),  // Wood
            new Color32(89, 224, 184, 255),  // Wind
            new Color32(177, 100, 255, 255)  // Thunder
        };

        public void SetDirection(WorldPoint direction)
        {
            CacheBaseVisual();
            if (VisualRoot == null) return;

            float lengthSq =
                direction.X * direction.X +
                direction.Y * direction.Y;

            if (lengthSq <= 0.000001f ||
                float.IsNaN(lengthSq) ||
                float.IsInfinity(lengthSq))
                return;

            float angle =
                Mathf.Atan2(direction.Y, direction.X) *
                Mathf.Rad2Deg;

            VisualRoot.localRotation =
                Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// 根据mask显示首个对应特效，关闭其他特效。
        /// </summary>
        public void SetElements(int appliedElementMask)
        {
            CacheBaseVisual();

            int index = FirstKnownElement(appliedElementMask);

            SetEffectByIndex(index);
            if (ElementOverlay != null)
            {
                ElementOverlay.enabled = index >= 0;
                if (index >= 0) ElementOverlay.color = ElementColors[index];
            }
        }

        /// <summary>
        /// 按索引激活对应特效GameObject，停用其余
        /// </summary>
        private void SetEffectByIndex(int index)
        {
            CacheEffects();
            if (selectedEffect == index) return;
            StopEffects();
            selectedEffect = index;
            hasVisualTime = false;
            if (index < 0 || index >= cachedEffects.Length || cachedEffects[index] == null) return;
            cachedEffects[index].SetActive(true);
            foreach (ParticleSystem particle in cachedParticles[index])
            {
                // Starting at zero skips prefab prewarm and leaves all advancement to the game clock.
                particle.Simulate(0f, false, true, false);
                particle.Pause(false);
            }
        }

        private void CacheEffects()
        {
            int length = elementEffectObjects == null ? 0 : elementEffectObjects.Length;
            bool changed = cachedEffects == null || cachedEffects.Length != length;
            if (!changed)
                for (int i = 0; i < length; i++)
                    if (cachedEffects[i] != elementEffectObjects[i]) { changed = true; break; }
            if (!changed) return;
            StopEffects();
            cachedEffects = new GameObject[length];
            cachedParticles = new ParticleSystem[length][];
            for (int i = 0; i < length; i++)
            {
                cachedEffects[i] = elementEffectObjects[i];
                cachedParticles[i] = cachedEffects[i] == null
                    ? new ParticleSystem[0] : cachedEffects[i].GetComponentsInChildren<ParticleSystem>(true);
                foreach (ParticleSystem particle in cachedParticles[i])
                {
                    // Even runtime-provided effects must start empty and never advance automatically.
                    particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particle.main;
                    main.prewarm = false;
                    main.playOnAwake = false;
                }
            }
            StopEffects();
            selectedEffect = -1;
            hasVisualTime = false;
        }

        private void StopEffects()
        {
            if (cachedEffects == null) return;
            for (int i = 0; i < cachedEffects.Length; i++)
            {
                foreach (ParticleSystem particle in cachedParticles[i])
                {
                    if (particle != null) particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                if (cachedEffects[i] != null) cachedEffects[i].SetActive(false);
            }
        }

        public void ApplyVisualTime(double gameTime)
        {
            if (double.IsNaN(gameTime) || double.IsInfinity(gameTime) || gameTime < 0) return;
            if (!hasVisualTime) { lastVisualTime = gameTime; hasVisualTime = true; return; }
            double delta = gameTime - lastVisualTime;
            if (delta <= 0 || delta > float.MaxValue) return;
            lastVisualTime = gameTime;
            if (cachedEffects == null || selectedEffect < 0 || selectedEffect >= cachedEffects.Length) return;
            foreach (ParticleSystem particle in cachedParticles[selectedEffect])
            {
                if (particle == null) continue;
                particle.Simulate((float)delta, false, false, false);
                particle.Pause(false);
            }
        }

        public void ResetVisual()
        {
            CacheBaseVisual();
            if (VisualRoot != null)
                VisualRoot.localRotation = baseRotation;

            if (ElementOverlay != null)
            {
                ElementOverlay.enabled = false;
                ElementOverlay.color = baseColor;
            }

            CacheEffects();
            StopEffects();
            selectedEffect = -1;
            hasVisualTime = false;
            lastVisualTime = 0;
        }

        private static int FirstKnownElement(int mask)
        {
            if ((mask & 1) != 0) return 0;
            if ((mask & 2) != 0) return 1;
            if ((mask & 4) != 0) return 2;
            if ((mask & 8) != 0) return 3;
            if ((mask & 16) != 0) return 4;
            if ((mask & 32) != 0) return 5;
            return -1;
        }
    }
}
