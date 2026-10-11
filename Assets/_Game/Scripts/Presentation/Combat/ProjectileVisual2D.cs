using UnityEngine;
using Spotlight.Contracts;

namespace Spotlight.Presentation
{
    public class ProjectileVisual2D : MonoBehaviour
    {
        public Transform VisualRoot;
        public SpriteRenderer ElementOverlay;

        [Header("Element Effects (顺序: 水/火/土/木/风/雷)")]
        public GameObject[] elementEffectObjects;

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
        /// 暂不执行颜色覆盖。
        /// </summary>
        public void SetElements(int appliedElementMask)
        {
            CacheBaseVisual();

            int index = FirstKnownElement(appliedElementMask);

            // 特效开关：开启首个，关闭其他
            SetEffectByIndex(index);

            // ---- 暂不覆盖颜色 ----
            // if (ElementOverlay != null)
            // {
            //     ElementOverlay.enabled = index >= 0;
            //     if (index >= 0)
            //         ElementOverlay.color = ElementColors[index];
            // }
        }

        /// <summary>
        /// 按索引激活对应特效GameObject，停用其余
        /// </summary>
        private void SetEffectByIndex(int index)
        {
            if (elementEffectObjects == null || elementEffectObjects.Length == 0)
                return;

            for (int i = 0; i < elementEffectObjects.Length; i++)
            {
                if (elementEffectObjects[i] == null) continue;

                bool shouldBeActive = (i == index);

                if (shouldBeActive && !elementEffectObjects[i].activeSelf)
                {
                    elementEffectObjects[i].SetActive(true);

                    // 修复报错：将 StopAll 改为 StopEmittingAndClear
                    ParticleSystem ps = elementEffectObjects[i].GetComponent<ParticleSystem>();
                    if (ps != null)
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.Play(true);
                    }
                }
                else if (!shouldBeActive && elementEffectObjects[i].activeSelf)
                {
                    elementEffectObjects[i].SetActive(false);
                }
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

            // 关闭所有特效
            SetEffectByIndex(-1);
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