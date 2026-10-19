using Spotlight.Contracts;
using System.Collections.Generic;
using UnityEditor.Sprites;
using UnityEngine;

namespace Spotlight.Presentation
{
    public class ProjectileVisual2D : MonoBehaviour
    {
        public Transform VisualRoot;
        public SpriteRenderer ElementOverlay;
        [SerializeField] private ParticleSystem sharedPS; //引用粒子特效
        public AnimationCurve ZCurve_Earth; //曲线
        public AnimationCurve Curve_EarthSize;
        public AnimationCurve Curve_WoodSize;
        public AnimationCurve YCurve_Wind;
        // ===== 渐变缓存（初始化一次，后面直接用）=====
        private Gradient gradFire;
        private Gradient gradFire_over;

        private Gradient gradWater;
        private Gradient gradWater_over;

        private Gradient gradEarth;
        private Gradient gradEarth_over;

        private Gradient gradWood;
        private Gradient gradWood_over;

        private Gradient gradWind;
        private Gradient gradWind_over;

        private Gradient gradThunder;
        private Gradient gradThunder_over;

        private Gradient gradSteam;
        private Gradient gradSteam_over;
        private Gradient gradWindWood_over;
        private Gradient gradThunderEarth;
        private Gradient gradThunderEarth_over;

        [SerializeField] private Material trailMat;
        private int lastAppliedMask = -1; //脏位
        private const int WindWaveSamples = 24;
        private const float WindWaveAmplitude = 1f;
        private const float WindPhaseSpeed = 6.2831853f;
        private const float WindTrailOmega = 4f * Mathf.PI;
        private const float WindTrailLifetime = 0.5f;

        private readonly Keyframe[] windWaveKeys = new Keyframe[WindWaveSamples + 1];
        private AnimationCurve windWaveCurve;
        private float windWavePhase;
        private bool windWaveActive;

        private const int ElementWater = 1 << 0;
        private const int ElementFire = 1 << 1;
        private const int ElementEarth = 1 << 2;
        private const int ElementWood = 1 << 3;
        private const int ElementWind = 1 << 4;
        private const int ElementThunder = 1 << 5;

        private const int MaskSteam = ElementWater | ElementFire;
        private const int MaskWindWood = ElementWood | ElementWind;
        private const int MaskThunderEarth = ElementEarth | ElementThunder;

        private enum ColorTone
        {
            Water,
            Fire,
            Steam,
            Earth,
            Wood,
            Wind,
            Thunder
        }

        private enum TrailShape
        {
            Fire,
            Water,
            Earth,
            Wood,
            Wind,
            WindWood,
            Thunder
        }

        private enum ParticleForm
        {
            Default,
            Earth,
            Thunder,
            ThunderEarth
        }

        private void Awake()
        {
            // 火 - 黄→红→黑
            gradFire = new Gradient();
            gradFire.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
                }
            );

            gradFire_over = new Gradient();
            gradFire_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(1f, 0.67f, 0f), 0.27f),
                new GradientColorKey(new Color(1f, 0.15f, 0f), 0.6f),
                new GradientColorKey(new Color(0f, 0f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.33f),
                new GradientAlphaKey(0f, 1f)
                }
            );

            // 水 - 蓝白
            gradWater = new Gradient();
            gradWater.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(0f, 1f, 1f), 0f),
                new GradientColorKey(new Color(0f, 0.22f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(0.4f, 0f),
                new GradientAlphaKey(0.4f, 1f)
                }
            );

            gradWater_over = new Gradient();
            gradWater_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.4f),
                new GradientAlphaKey(0f, 1f)
                }
            );

            // 土 - 土黄褐
            gradEarth = new Gradient();
            gradEarth.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(0.31f, 0.17f, 0f), 0f),
                new GradientColorKey(new Color(0.61f, 0.4f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
                }
            );

            // 木 - 绿
            gradWood = new Gradient();
            gradWood.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(0.57f, 0.97f, 0.45f), 0f),
                new GradientColorKey(new Color(0.36f, 0.63f, 0.3f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
                }
            );

            // 风 - 青绿深浅
            gradWind = new Gradient();
            gradWind.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
                }
            );

            gradWind_over = new Gradient();
            gradWind_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(0f, 0.8f, 0.48f), 0f),
                new GradientColorKey(new Color(0f, 0f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.56f),
                new GradientAlphaKey(0f, 1f)
                }
            );

            // 雷 - 紫白
            gradThunder = new Gradient();
            gradThunder.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(0.53f, 0f, 1f), 0f),
                new GradientColorKey(new Color(0f, 0f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
                }
            );

            gradThunder_over = new Gradient();
            gradThunder_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.67f),
                new GradientAlphaKey(0f, 1f)
                }
            );

            gradSteam = new Gradient();
            gradSteam.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(0.8f, 0.9f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(0.75f, 0f),
                new GradientAlphaKey(0.75f, 1f)
                }
            );

            gradSteam_over = new Gradient();
            gradSteam_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 1f, 1f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.2f),
                new GradientAlphaKey(0f, 1f)
                }
            );

            gradWindWood_over = new Gradient();
            gradWindWood_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(0.57f, 0.97f, 0.45f), 0f),
                new GradientColorKey(new Color(0.2f, 1f, 0.8f), 0.55f),
                new GradientColorKey(new Color(0f, 0f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.6f),
                new GradientAlphaKey(0f, 1f)
                }
            );

            gradThunderEarth = new Gradient();
            gradThunderEarth.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 0.45f),
                new GradientColorKey(new Color(0.63f, 0.2f, 1f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
                }
            );

            gradThunderEarth_over = new Gradient();
            gradThunderEarth_over.SetKeys(
                new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.85f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 1f, 1f), 0.4f),
                new GradientColorKey(new Color(0.63f, 0.2f, 1f), 0.75f),
                new GradientColorKey(new Color(0f, 0f, 0f), 1f)
                },
                new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.6f),
                new GradientAlphaKey(0f, 1f)
                }
            );
        }

        private void Update()
        {
            if (!windWaveActive || sharedPS == null) return;

            windWavePhase = Mathf.Repeat(
                windWavePhase + WindPhaseSpeed * Time.deltaTime,
                Mathf.PI * 2f);
            RefreshWindWave();
        }

        private void RefreshWindWave()
        {
            if (windWaveCurve == null)
                windWaveCurve = new AnimationCurve();

            const float omega = WindTrailOmega - WindPhaseSpeed * WindTrailLifetime;
            for (int i = 0; i < windWaveKeys.Length; i++)
            {
                float t = i / (float)WindWaveSamples;
                float angle = omega * t + windWavePhase;
                float value = Mathf.Sin(angle) * WindWaveAmplitude;
                float tangent = omega * Mathf.Cos(angle) * WindWaveAmplitude;
                windWaveKeys[i] = new Keyframe(t, value, tangent, tangent);
            }

            windWaveCurve.keys = windWaveKeys;

            var vel = sharedPS.velocityOverLifetime;
            vel.y = new ParticleSystem.MinMaxCurve(WindWaveAmplitude, windWaveCurve);
        }

        private void ResetSpecialModules()
        {
            if (sharedPS == null) return;

            var vel = sharedPS.velocityOverLifetime;
            vel.orbitalX = 0f;
            vel.orbitalY = 0f;
            vel.orbitalZ = 0f;
            vel.radial = 0f;

            var noise = sharedPS.noise;
            noise.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = false;
        }

        private static readonly Color[] ElementColors =
        {
            new Color32(64, 156, 255, 255),  // 0 水
            new Color32(255, 83, 64, 255),   // 1 火
            new Color32(190, 146, 62, 255),  // 2 土
            new Color32(62, 190, 104, 255),  // 3 木
            new Color32(89, 224, 184, 255),  // 4 风
            new Color32(177, 100, 255, 255)  // 5 雷
        };

        public void SetDirection(WorldPoint direction)
        {
            if (VisualRoot == null) return;

            float lengthSq = direction.X * direction.X + direction.Y * direction.Y;
            if (lengthSq <= 0.000001f ||
                float.IsNaN(lengthSq) ||
                float.IsInfinity(lengthSq))
            {
                return;
            }

            float angle = Mathf.Atan2(direction.Y, direction.X) * Mathf.Rad2Deg;
            VisualRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        public void SetElements(int appliedElementMask)
        {
            if (lastAppliedMask == appliedElementMask) return;  // ← 没变就跳过
            lastAppliedMask = appliedElementMask;

            if (ElementOverlay == null) return;

            int index = FirstKnownElement(appliedElementMask);
            ElementOverlay.enabled = index >= 0;
            if (index >= 0)
                ElementOverlay.color = appliedElementMask == 3 ? Color.white : ElementColors[index];

            // ===== 新增：粒子特效扩展点 =====
            UpdateParticleEffects(appliedElementMask);
        }

        public void ResetVisual()
        {
            lastAppliedMask = -1;

            if (VisualRoot != null)
                VisualRoot.localRotation = Quaternion.Euler(0f, 0f, 0f);

            if (ElementOverlay != null)
            {
                ElementOverlay.enabled = false;
                ElementOverlay.color = Color.white;
            }

            // ===== 新增：重置时关掉粒子 =====
            DisableAllParticleEffects();
        }

        /// <summary>
        /// 原始方法，返回首个元素（不变）
        /// </summary>
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

        // ===== 新增：以下三个方法供你自由发挥 =====

        /// <summary>
        /// 解析mask，返回所有存在的元素索引列表
        /// 0=水 1=火 2=土 3=木 4=风 5=雷
        /// </summary>
        public static int[] GetActiveElementIndices(int mask)
        {
            List<int> result = new List<int>();
            if ((mask & 1) != 0) result.Add(0);
            if ((mask & 2) != 0) result.Add(1);
            if ((mask & 4) != 0) result.Add(2);
            if ((mask & 8) != 0) result.Add(3);
            if ((mask & 16) != 0) result.Add(4);
            if ((mask & 32) != 0) result.Add(5);
            return result.ToArray();
        }

        /// <summary>
        /// 扩展点：根据当前mask更新粒子特效
        /// 在这里写你自己的逻辑，原始脚本不干涉
        /// </summary>
        protected virtual void UpdateParticleEffects(int appliedElementMask)
        {
            int[] activeElements = GetActiveElementIndices(appliedElementMask);

            if (activeElements.Length == 0)
            {
                DisableAllParticleEffects();
                return;
            }

            if (sharedPS != null)
            {
                sharedPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            windWaveActive = false;
            ResetSpecialModules();

            if (appliedElementMask == MaskSteam)
            {
                SetSteam();
                if (sharedPS != null) sharedPS.Play(true);
                return;
            }

            if (appliedElementMask == MaskWindWood)
            {
                SetWindWood();
                if (sharedPS != null) sharedPS.Play(true);
                return;
            }

            if (appliedElementMask == MaskThunderEarth)
            {
                SetThunderEarth();
                if (sharedPS != null) sharedPS.Play(true);
                return;
            }

            if (activeElements.Length == 1)
            {
                switch (activeElements[0])
                {
                    case 0: SetWater(); break;
                    case 1: SetFire(); break;
                    case 2: SetEarth(); break;
                    case 3: SetWood(); break;
                    case 4: SetWind(); break;
                    case 5: SetThunder(); break;
                }
                if (sharedPS != null)
                {
                    sharedPS.Play(true);
                }

                return;
            }

            ApplyCompositeEffect(appliedElementMask);
            if (sharedPS != null)
            {
                sharedPS.Play(true);
            }

            // 混合元素：在这里写你的三维决策逻辑
            // 例：
            // bool hasFire = System.Array.Exists(activeElements, x => x == 1);
            // bool hasWater = System.Array.Exists(activeElements, x => x == 0);
            // if (hasFire && hasWater) { /* 蒸汽 */ }
            // else { /* 按你的规则组合 */ }
        }

        /// <summary>
        /// 关闭所有粒子特效（你在里面填你自己的关闭逻辑）
        /// </summary>
        protected virtual void DisableAllParticleEffects()
        {
            windWaveActive = false;
            ResetSpecialModules();
            if (sharedPS != null)
            {
                sharedPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private static ColorTone ResolveColorTone(int mask)
        {
            bool hasWater = (mask & ElementWater) != 0;
            bool hasFire = (mask & ElementFire) != 0;
            bool hasEarth = (mask & ElementEarth) != 0;
            bool hasWood = (mask & ElementWood) != 0;
            bool hasWind = (mask & ElementWind) != 0;

            if (hasWater && hasFire) return ColorTone.Steam;
            if (hasFire) return ColorTone.Fire;
            if (hasWater) return ColorTone.Water;
            if (hasEarth) return ColorTone.Earth;
            if (hasWood) return ColorTone.Wood;
            if (hasWind) return ColorTone.Wind;
            return ColorTone.Thunder;
        }

        private static TrailShape ResolveTrailShape(int mask)
        {
            bool hasWater = (mask & ElementWater) != 0;
            bool hasEarth = (mask & ElementEarth) != 0;
            bool hasWood = (mask & ElementWood) != 0;
            bool hasWind = (mask & ElementWind) != 0;
            bool hasThunder = (mask & ElementThunder) != 0;

            if (hasWind && hasWood) return TrailShape.WindWood;
            if (hasWind) return TrailShape.Wind;
            if (hasWood) return TrailShape.Wood;
            if (hasThunder) return TrailShape.Thunder;
            if (hasWater) return TrailShape.Water;
            if (hasEarth) return TrailShape.Earth;
            return TrailShape.Fire;
        }

        private static ParticleForm ResolveParticleForm(int mask)
        {
            bool hasEarth = (mask & ElementEarth) != 0;
            bool hasThunder = (mask & ElementThunder) != 0;

            if (hasThunder && hasEarth) return ParticleForm.ThunderEarth;
            if (hasThunder) return ParticleForm.Thunder;
            if (hasEarth) return ParticleForm.Earth;
            return ParticleForm.Default;
        }

        private void ApplyCompositeEffect(int mask)
        {
            ApplyParticleForm(ResolveParticleForm(mask));
            ApplyTrailShape(ResolveTrailShape(mask));
            ApplyMaskTuning(mask);
            ApplyCompositeColor(mask);
        }

        private void ApplyCompositeColor(int mask)
        {
            int count = CountElements(mask);

            if (count <= 1 || (count == 2 && (mask & MaskSteam) == MaskSteam))
            {
                ApplyColorTone(ResolveColorTone(mask));
                return;
            }

            ApplyLayeredColor(mask);
        }

        private static int CountElements(int mask)
        {
            int count = 0;
            if ((mask & ElementWater) != 0) count++;
            if ((mask & ElementFire) != 0) count++;
            if ((mask & ElementEarth) != 0) count++;
            if ((mask & ElementWood) != 0) count++;
            if ((mask & ElementWind) != 0) count++;
            if ((mask & ElementThunder) != 0) count++;
            return count;
        }

        private struct ColorLayer
        {
            public Gradient gradient;
            public float minT;
            public float maxT;

            public ColorLayer(Gradient gradient, float minT, float maxT)
            {
                this.gradient = gradient;
                this.minT = minT;
                this.maxT = maxT;
            }
        }

        private List<ColorLayer> BuildNearToFarLayers(int mask)
        {
            var layers = new List<ColorLayer>(5);
            bool hasWater = (mask & ElementWater) != 0;
            bool hasFire = (mask & ElementFire) != 0;

            if (hasWater && hasFire)
            {
                layers.Add(new ColorLayer(gradSteam, 0f, 1f));
            }
            else
            {
                if (hasFire) layers.Add(new ColorLayer(gradFire_over, 0f, 0.65f));
                if (hasWater) layers.Add(new ColorLayer(gradWater, 0f, 1f));
            }

            if ((mask & ElementEarth) != 0) layers.Add(new ColorLayer(gradEarth, 0f, 1f));
            if ((mask & ElementWood) != 0) layers.Add(new ColorLayer(gradWood, 0f, 1f));
            if ((mask & ElementWind) != 0) layers.Add(new ColorLayer(gradWind_over, 0f, 0.55f));
            if ((mask & ElementThunder) != 0) layers.Add(new ColorLayer(gradThunder, 0f, 0.6f));

            return layers;
        }

        private void ApplyLayeredColor(int mask)
        {
            bool hasTrail = sharedPS.trails.enabled;
            var layers = BuildNearToFarLayers(mask);

            // Trail: t=0 is the far end and t=1 is near the projectile.
            if (hasTrail)
            {
                layers.Reverse();
            }

            const int sampleCount = 8;
            int layerCount = layers.Count;
            var colorKeys = new GradientColorKey[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)(sampleCount - 1);
                int layerIndex = Mathf.Min(Mathf.FloorToInt(t * layerCount), layerCount - 1);
                float localT = t * layerCount - layerIndex;
                ColorLayer layer = layers[layerIndex];

                Color color = layer.gradient.Evaluate(
                    Mathf.Lerp(layer.minT, layer.maxT, localT));
                color.a = 1f;
                colorKeys[i] = new GradientColorKey(color, t);
            }

            var gradient = new Gradient();
            gradient.mode = GradientMode.Blend;
            gradient.SetKeys(colorKeys, BuildLayeredAlphaKeys(hasTrail));

            var start = new ParticleSystem.MinMaxGradient(Color.white);
            start.mode = ParticleSystemGradientMode.Color;
            var main = sharedPS.main;
            main.startColor = start;

            var colorOverLifetime = sharedPS.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static GradientAlphaKey[] BuildLayeredAlphaKeys(bool hasTrail)
        {
            if (hasTrail)
            {
                return new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(1f, 1f)
                };
            }

            return new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.85f),
                new GradientAlphaKey(0f, 1f)
            };
        }

        private void ApplyMaskTuning(int mask)
        {
            if ((mask >= 37 && mask <= 39) || (mask >= 60 && mask <= 63))
            {
                var main = sharedPS.main;
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);

                var si = sharedPS.sizeOverLifetime;
                si.enabled = true;
                si.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.2f),
                    new Keyframe(0.5f, 0.4f),
                    new Keyframe(1f, 0.2f)));
            }

            if (mask >= 40 && mask <= 47)
            {
                var main = sharedPS.main;
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);

                var tra = sharedPS.trails;
                tra.enabled = true;
                tra.mode = ParticleSystemTrailMode.Ribbon;
                tra.ratio = 1f;
                tra.lifetime = 1f;
                tra.minVertexDistance = 0.1f;
                tra.worldSpace = false;
                tra.sizeAffectsWidth = true;
                tra.inheritParticleColor = true;
                tra.widthOverTrail = 1f;

                var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
                ren.renderMode = ParticleSystemRenderMode.Billboard;
                ren.pivot = new Vector3(0f, 0f, 0f);
                ren.trailMaterial = trailMat;
            }

            if (mask >= 48 && mask <= 51)
            {
                var tra = sharedPS.trails;
                tra.widthOverTrail = 9f;
            }

            if (mask >= 25 && mask <= 31)
            {
                var main = sharedPS.main;
                main.startSize = mask >= 28
                    ? new ParticleSystem.MinMaxCurve(0.06f, 0.12f)
                    : new ParticleSystem.MinMaxCurve(0.08f);

                var si = sharedPS.sizeOverLifetime;
                si.enabled = true;
                si.size = new ParticleSystem.MinMaxCurve(1f,
                    AnimationCurve.Linear(0f, 0.3f, 1f, 1f));
            }

            if (mask == 5 || mask == 7)
            {
                var sha = sharedPS.shape;
                sha.position = new Vector3(0f, 0f, 0.1f);
            }
        }

        private void ApplyColorTone(ColorTone tone)
        {
            switch (tone)
            {
                case ColorTone.Water:
                    ApplyColor(gradWater, gradWater_over, true);
                    break;
                case ColorTone.Fire:
                    ApplyColor(gradFire, gradFire_over, true);
                    break;
                case ColorTone.Steam:
                    ApplyColor(gradSteam, gradSteam_over, true);
                    break;
                case ColorTone.Earth:
                    ApplyColor(gradEarth, null, false);
                    break;
                case ColorTone.Wood:
                    ApplyColor(gradWood, null, false);
                    break;
                case ColorTone.Wind:
                    ApplyColor(gradWind, gradWind_over, true);
                    break;
                case ColorTone.Thunder:
                    ApplyColor(gradThunder, gradThunder_over, true);
                    break;
            }
        }

        private void ApplyColor(Gradient startGradient, Gradient lifetimeGradient, bool useLifetimeGradient)
        {
            ParticleSystem.MinMaxGradient start = new ParticleSystem.MinMaxGradient(startGradient);
            start.mode = ParticleSystemGradientMode.RandomColor;

            var main = sharedPS.main;
            main.startColor = start;

            var col = sharedPS.colorOverLifetime;
            col.enabled = useLifetimeGradient;
            if (useLifetimeGradient && lifetimeGradient != null)
            {
                col.color = new ParticleSystem.MinMaxGradient(lifetimeGradient);
            }
        }

        private void ApplyTrailShape(TrailShape shape)
        {
            switch (shape)
            {
                case TrailShape.Fire:
                    ApplyTrailFire();
                    break;
                case TrailShape.Water:
                    ApplyTrailWater();
                    break;
                case TrailShape.Earth:
                    ApplyTrailEarth();
                    break;
                case TrailShape.Wood:
                    ApplyTrailWood();
                    break;
                case TrailShape.Wind:
                    ApplyTrailWind();
                    break;
                case TrailShape.WindWood:
                    ApplyTrailWindWood();
                    break;
                case TrailShape.Thunder:
                    ApplyTrailThunder();
                    break;
            }
        }

        private void ApplyTrailFire()
        {
            windWaveActive = false;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = null;
        }

        private void ApplyTrailWater()
        {
            windWaveActive = false;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Stretch;
            ren.lengthScale = 3f;
            ren.pivot = new Vector3(0f, 0.5f, 0f);
            ren.trailMaterial = null;
        }

        private void ApplyTrailEarth()
        {
            windWaveActive = false;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, ZCurve_Earth);
            vel.space = ParticleSystemSimulationSpace.Local;

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = null;
        }

        private void ApplyTrailWood()
        {
            windWaveActive = false;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 0f;
            emi.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10, 10, 1, 0.01f) });

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = null;
        }

        private void ApplyTrailWind()
        {
            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.space = ParticleSystemSimulationSpace.Local;

            windWavePhase = Mathf.PI * 0.5f;
            windWaveActive = true;
            RefreshWindWave();

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.ratio = 1f;
            tra.lifetime = 1f;
            tra.minVertexDistance = 0.1f;
            tra.worldSpace = false;
            tra.sizeAffectsWidth = true;
            tra.inheritParticleColor = true;
            tra.widthOverTrail = 1f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }

        private void ApplyTrailWindWood()
        {
            windWaveActive = false;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.orbitalZ = 3.5f;
            vel.radial = 0.2f;
            vel.space = ParticleSystemSimulationSpace.Local;

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.ratio = 1f;
            tra.lifetime = 1f;
            tra.minVertexDistance = 0.05f;
            tra.worldSpace = false;
            tra.sizeAffectsWidth = true;
            tra.inheritParticleColor = true;
            tra.widthOverTrail = 3f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }

        private void ApplyTrailThunder()
        {
            windWaveActive = false;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.ratio = 1f;
            tra.lifetime = 0.14f;
            tra.minVertexDistance = 0.02f;
            tra.worldSpace = false;
            tra.sizeAffectsWidth = true;
            tra.inheritParticleColor = true;
            tra.widthOverTrail = 4f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }

        private void ApplyParticleForm(ParticleForm form)
        {
            switch (form)
            {
                case ParticleForm.Default:
                    ApplyParticleDefault();
                    break;
                case ParticleForm.Earth:
                    ApplyParticleEarth();
                    break;
                case ParticleForm.Thunder:
                    ApplyParticleThunder();
                    break;
                case ParticleForm.ThunderEarth:
                    ApplyParticleThunderEarth();
                    break;
            }
        }

        private void ApplyParticleDefault()
        {
            var main = sharedPS.main;
            main.startLifetime = 0.5f;
            main.startSpeed = 1f;
            main.startSize = 0.1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 20f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            ApplyParticleShape(ParticleSystemShapeType.Cone, 0.01f, 15f);

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var noise = sharedPS.noise;
            noise.enabled = false;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void ApplyParticleEarth()
        {
            var main = sharedPS.main;
            main.startLifetime = 0.8f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 5f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            ApplyParticleShape(ParticleSystemShapeType.Cone, 0.05f, 15f);

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var noise = sharedPS.noise;
            noise.enabled = false;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, Curve_EarthSize);

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void ApplyParticleThunder()
        {
            var main = sharedPS.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = 1f;
            main.startSize = 0.01f;
            main.startRotation = 0f;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 30f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            ApplyParticleShape(ParticleSystemShapeType.Cone, 0.0001f, 20f);

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var noise = sharedPS.noise;
            noise.enabled = false;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void ApplyParticleThunderEarth()
        {
            var main = sharedPS.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 20f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            ApplyParticleShape(ParticleSystemShapeType.Cone, 0.0001f, 30f);

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var noise = sharedPS.noise;
            noise.enabled = false;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.7f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(1f, 0.2f)));

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void ApplyParticleShape(ParticleSystemShapeType shapeType, float radius, float angle)
        {
            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = shapeType;
            sha.radius = radius;
            sha.radiusThickness = 1f;
            sha.angle = angle;
            sha.arc = 360f;
            sha.position = Vector3.zero;
            sha.rotation = Vector3.zero;
            sha.scale = Vector3.one;
            sha.randomDirectionAmount = 0f;
            sha.sphericalDirectionAmount = 0f;
            sha.randomPositionAmount = 0f;
            sha.alignToDirection = false;
        }

        private void SetWater()
        {
            var main = sharedPS.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = 1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startRotation = 0;
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradWater);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 40f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.angle = 15f;
            sha.radius = 0.0001f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradWater_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Stretch;
            ren.lengthScale = 3f;
            ren.pivot = new Vector3(0f, 0.5f, 0f);
        }

        private void SetFire()
        {
            var main = sharedPS.main;
            main.startLifetime = 0.5f;
            main.startSpeed = 1f;
            main.startSize = 0.1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradFire);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 20f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.angle = 15f;
            sha.radius = 0.01f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradFire_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void SetEarth()
        {
            var main = sharedPS.main;
            main.startLifetime = 0.8f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradEarth);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 5f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.angle = 15f;
            sha.radius = 0.05f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, ZCurve_Earth);

            var col = sharedPS.colorOverLifetime;
            col.enabled = false;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, Curve_EarthSize);

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void SetWood()
        {
            var main = sharedPS.main;
            main.startLifetime = 1f;
            main.startSpeed = 0.5f;
            main.startSize = 0.1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradWood);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 0f;
            emi.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10, 10, 1, 0.01f) });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.angle = 15f;
            sha.radius = 0.01f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var col = sharedPS.colorOverLifetime;
            col.enabled = false;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, Curve_WoodSize);

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
        }

        private void SetWind()
        {
            var main = sharedPS.main;
            main.startLifetime = WindTrailLifetime;
            main.startSpeed = 1f;
            main.startSize = 0.01f;
            main.startRotation = 0f;
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradWind);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 20f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Mesh;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));

            windWavePhase = Mathf.PI * 0.5f;
            windWaveActive = true;
            RefreshWindWave();

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradWind_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.ratio = 1f;
            tra.lifetime = 1f;
            tra.minVertexDistance = 0.1f;
            tra.worldSpace = false;
            tra.sizeAffectsWidth = true;
            tra.widthOverTrail = 9f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }

        private void SetSteam()
        {
            var main = sharedPS.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradSteam);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 20f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.radius = 0.03f;
            sha.radiusThickness = 1f;
            sha.angle = 30f;
            sha.arc = 360f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var noise = sharedPS.noise;
            noise.enabled = false;
            noise.strength = 0.15f;
            noise.frequency = 1.5f;
            noise.scrollSpeed = 0.5f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradSteam_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.7f),
                new Keyframe(0.5f, 1.2f),
                new Keyframe(1f, 1.5f)));

            var tra = sharedPS.trails;
            tra.enabled = false;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = null;
        }

        private void SetWindWood()
        {
            var main = sharedPS.main;
            main.startLifetime = 0.8f;
            main.startSpeed = 0.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.05f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradWood);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 15f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.radius = 0.02f;
            sha.radiusThickness = 1f;
            sha.angle = 20f;
            sha.arc = 360f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.orbitalZ = 3.5f;
            vel.radial = 0.2f;
            vel.space = ParticleSystemSimulationSpace.Local;

            var noise = sharedPS.noise;
            noise.enabled = false;

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradWindWood_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0.6f)));

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.ratio = 1f;
            tra.lifetime = 1f;
            tra.minVertexDistance = 0.05f;
            tra.worldSpace = false;
            tra.sizeAffectsWidth = true;
            tra.inheritParticleColor = true;
            tra.widthOverTrail = 1f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }

        private void SetThunderEarth()
        {
            var main = sharedPS.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.3f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradThunderEarth);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 20f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.radius = 0.0001f;
            sha.radiusThickness = 1f;
            sha.angle = 30f;
            sha.arc = 360f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = true;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));
            vel.orbitalZ = 1.5f;
            vel.radial = 0.3f;
            vel.space = ParticleSystemSimulationSpace.Local;

            var noise = sharedPS.noise;
            noise.enabled = false;
            noise.strength = 0.8f;
            noise.frequency = 5f;
            noise.scrollSpeed = 1.5f;
            noise.damping = true;
            noise.octaveCount = 2;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradThunderEarth_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = true;
            si.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.7f),
                new Keyframe(0.5f, 0.2f),
                new Keyframe(1f, 0.2f)));

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.ratio = 1f;
            tra.lifetime = 0.8f;
            tra.minVertexDistance = 0.08f;
            tra.worldSpace = false;
            tra.sizeAffectsWidth = true;
            tra.inheritParticleColor = true;
            tra.widthOverTrail = 1f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }

        private void SetThunder()
        {
            var main = sharedPS.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = 1f;
            main.startSize = 0.01f;
            main.startRotation = 0;
            ParticleSystem.MinMaxGradient mg = new ParticleSystem.MinMaxGradient(gradThunder);
            mg.mode = ParticleSystemGradientMode.RandomColor;
            main.startColor = mg;

            var emi = sharedPS.emission;
            emi.enabled = true;
            emi.rateOverTime = 30f;
            emi.SetBursts(new ParticleSystem.Burst[] { });

            var sha = sharedPS.shape;
            sha.enabled = true;
            sha.shapeType = ParticleSystemShapeType.Cone;
            sha.angle = 20f;
            sha.radius = 0.0001f;

            var vel = sharedPS.velocityOverLifetime;
            vel.enabled = false;

            var col = sharedPS.colorOverLifetime;
            col.enabled = true;
            ParticleSystem.MinMaxGradient mg_over = new ParticleSystem.MinMaxGradient(gradThunder_over);
            col.color = mg_over;

            var si = sharedPS.sizeOverLifetime;
            si.enabled = false;

            var tra = sharedPS.trails;
            tra.enabled = true;
            tra.mode = ParticleSystemTrailMode.Ribbon;
            tra.widthOverTrail = 4f;

            var ren = sharedPS.GetComponent<ParticleSystemRenderer>();
            ren.renderMode = ParticleSystemRenderMode.Billboard;
            ren.pivot = new Vector3(0f, 0f, 0f);
            ren.trailMaterial = trailMat;
        }
    }
}
