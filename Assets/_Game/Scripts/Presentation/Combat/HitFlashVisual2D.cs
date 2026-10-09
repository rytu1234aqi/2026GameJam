using UnityEngine;

namespace Spotlight.Presentation
{
    /// <summary>
    /// 受击闪烁组件：由主程在真实伤害事件中调用，不自行读取时间或订阅事件。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class HitFlashVisual2D : MonoBehaviour
    {
        [Header("引用")]
        public SpriteRenderer BodyRenderer;

        [Header("参数")]
        public Color FlashColor = Color.white;
        public float DurationSeconds = 0.12f;

        // 内部状态
        private Color _originalColor;
        private double _flashEndGameTime;

        #region 生命周期

        private void Awake()
        {
            // 缓存原始颜色
            if (BodyRenderer == null)
            {
                BodyRenderer = GetComponent<SpriteRenderer>();
            }
            _originalColor = BodyRenderer != null ? BodyRenderer.color : Color.white;
        }

        private void OnEnable()
        {
            ResetVisual();
        }

        private void OnDisable()
        {
            ResetVisual();
        }

        #endregion

        #region 公开接口（按文档固定签名）

        /// <summary>
        /// 触发闪烁。连续受击刷新结束时间。
        /// </summary>
        public void PlayHit(double gameTime)
        {
            if (BodyRenderer == null) return;

            // 非法时间忽略
            if (gameTime <= 0) return;

            // Duration <= 0 立即恢复
            if (DurationSeconds <= 0f)
            {
                ResetVisual();
                return;
            }

            _flashEndGameTime = gameTime + DurationSeconds;
            BodyRenderer.color = FlashColor;
        }

        /// <summary>
        /// 每帧由外部调用，传入当前游戏时间。到期恢复原始颜色。
        /// </summary>
        public void ApplyVisualTime(double gameTime)
        {
            if (BodyRenderer == null) return;

            // 非法时间忽略
            if (gameTime <= 0) return;

            // 还没到闪烁结束时间，保持闪色
            if (gameTime < _flashEndGameTime) return;

            // 到期恢复
            ResetVisual();
        }

        /// <summary>
        /// 重置显示，恢复原始颜色，清除结束时间。
        /// </summary>
        public void ResetVisual()
        {
            if (BodyRenderer == null) return;

            _flashEndGameTime = 0.0;
            BodyRenderer.color = _originalColor;
        }

        #endregion
    }
}