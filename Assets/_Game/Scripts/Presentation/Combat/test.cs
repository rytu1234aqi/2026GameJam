using Spotlight.Contracts;
using UnityEngine;

namespace Spotlight.Presentation
{
    /// <summary>
    /// 测试用：运行时按数字键切换元素 mask，验证粒子特效
    /// 挂到和 ProjectileVisual2D 同一个 GameObject 上
    /// </summary>
    public class ProjectileVisualTester : MonoBehaviour
    {
        [Header("引用（自动获取）")]
        private ProjectileVisual2D visual;

        [Header("当前 Mask（运行时可观察）")]
        [SerializeField] private int currentMask = 0;

        [Header("测试用速度方向（可选）")]
        public bool autoSetDirection = true;
        public Vector2 testDirection = Vector2.right;

        private void Awake()
        {
            visual = GetComponent<ProjectileVisual2D>();
            if (visual == null)
            {
                Debug.LogError("[Tester] 找不到 ProjectileVisual2D！");
            }
        }

        private void Start()
        {
            // 启动时默认无元素
            currentMask = 0;
            visual?.ResetVisual();
        }

        private void Update()
        {
            // ── 单元素测试：1~6 ──
            if (Input.GetKeyDown(KeyCode.Alpha1)) ApplyMask(1);      // 水
            if (Input.GetKeyDown(KeyCode.Alpha2)) ApplyMask(2);      // 火
            if (Input.GetKeyDown(KeyCode.Alpha3)) ApplyMask(4);      // 土
            if (Input.GetKeyDown(KeyCode.Alpha4)) ApplyMask(8);      // 木
            if (Input.GetKeyDown(KeyCode.Alpha5)) ApplyMask(16);     // 风
            if (Input.GetKeyDown(KeyCode.Alpha6)) ApplyMask(32);     // 雷

            // ── 混合元素测试 ──
            if (Input.GetKeyDown(KeyCode.Q)) ApplyMask(1 | 2);       // 水+火 = 蒸汽
            if (Input.GetKeyDown(KeyCode.W)) ApplyMask(4 | 32);      // 土+雷 = 带电
            if (Input.GetKeyDown(KeyCode.E)) ApplyMask(2 | 8);       // 火+木
            if (Input.GetKeyDown(KeyCode.R)) ApplyMask(1 | 16);      // 水+风
            if (Input.GetKeyDown(KeyCode.T)) ApplyMask(2 | 4 | 8);   // 火+土+木 三元素

            // ── 清空：C ──
            if (Input.GetKeyDown(KeyCode.C))
            {
                currentMask = 0;
                visual?.ResetVisual();
                Debug.Log("[Tester] 已清空所有元素");
            }

            // ── 重新应用当前 mask：空格 ──
            if (Input.GetKeyDown(KeyCode.Space))
            {
               ApplyMask(currentMask);
            }
        }

        private void ApplyMask(int mask)
        {
            currentMask = mask;
            visual?.SetElements(mask);

            if (autoSetDirection && visual != null)
            {
                // 用 WorldPoint 结构体构造方向
                visual.SetDirection(new WorldPoint(testDirection.x, testDirection.y));
            }

            string elementNames = MaskToString(mask);
            Debug.Log($"[Tester] 应用 mask = {mask} ({elementNames})");
        }

        private string MaskToString(int mask)
        {
            if (mask == 0) return "无元素";

            string[] names = { "水", "火", "土", "木", "风", "雷" };
            int[] bits = { 1, 2, 4, 8, 16, 32 };
            string result = "";

            for (int i = 0; i < 6; i++)
            {
                if ((mask & bits[i]) != 0)
                {
                    result += names[i] + " ";
                }
            }
            return result.Trim();
        }
    }
}