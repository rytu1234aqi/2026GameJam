using UnityEngine;
using UnityEngine.UI;

namespace Spotlight.Presentation
{
    public class NightResultPanelReferences : MonoBehaviour
    {
        public RectTransform Root;
        public Text TitleText, SummaryText, HintText;
        public Button NextDayButton, ReturnMenuButton;

        // 自动把 Root 设为自己，解决 Unity 预制体无法拖拽自身的问题
        private void Awake()
        {
            if (Root == null)
            {
                Root = transform as RectTransform;
            }
        }

        public bool Validate(out string error)
        {
            // 1. 检查字段
            if (Root == null) { error = "Root 缺失"; return false; }
            if (TitleText == null || SummaryText == null || HintText == null) { error = "Text 引用缺失"; return false; }
            if (NextDayButton == null || ReturnMenuButton == null) { error = "Button 引用缺失"; return false; }

            // 2. 检查层级（Root 必须是脚本所在根节点）
            if (Root != transform as RectTransform) { error = "Root 必须是脚本所在根节点"; return false; }
            if (!TitleText.transform.IsChildOf(Root) || !SummaryText.transform.IsChildOf(Root) || !HintText.transform.IsChildOf(Root) ||
                !NextDayButton.transform.IsChildOf(Root) || !ReturnMenuButton.transform.IsChildOf(Root))
            { error = "所有引用必须在 Root 子层级"; return false; }

            // 3. 检查按钮互不重复
            if (NextDayButton == ReturnMenuButton) { error = "按钮不能重复"; return false; }

            // 4. 检查字体有效
            if (TitleText.font == null || SummaryText.font == null || HintText.font == null) { error = "字体缺失"; return false; }

            // 5. 检查无持久 OnClick 绑定
            if (NextDayButton.onClick.GetPersistentEventCount() > 0 ||
                ReturnMenuButton.onClick.GetPersistentEventCount() > 0)
            { error = "按钮不能有持久 OnClick 绑定"; return false; }

            error = string.Empty;
            return true;
        }
    }
}