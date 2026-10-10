using UnityEngine;
using UnityEngine.UI;

namespace Spotlight.Presentation
{
    public class RunEndPanelReferences : MonoBehaviour
    {
        public RectTransform Root;
        public Text TitleText, SummaryText, HintText;
        public Button RetryButton, ReturnMenuButton, ContinueEndlessButton;

        private void Awake()
        {
            if (Root == null)
            {
                Root = transform as RectTransform;
            }
        }

        public bool Validate(out string error)
        {
            if (Root == null) { error = "Root 缺失"; return false; }
            if (TitleText == null || SummaryText == null || HintText == null) { error = "Text 引用缺失"; return false; }
            if (RetryButton == null || ReturnMenuButton == null || ContinueEndlessButton == null) { error = "Button 引用缺失"; return false; }

            if (Root != transform as RectTransform) { error = "Root 必须是脚本所在根节点"; return false; }
            if (!TitleText.transform.IsChildOf(Root) || !SummaryText.transform.IsChildOf(Root) || !HintText.transform.IsChildOf(Root) ||
                !RetryButton.transform.IsChildOf(Root) || !ReturnMenuButton.transform.IsChildOf(Root) || !ContinueEndlessButton.transform.IsChildOf(Root))
            { error = "所有引用必须在 Root 子层级"; return false; }

            if (RetryButton == ReturnMenuButton || RetryButton == ContinueEndlessButton || ReturnMenuButton == ContinueEndlessButton)
            { error = "按钮不能重复"; return false; }

            if (TitleText.font == null || SummaryText.font == null || HintText.font == null) { error = "字体缺失"; return false; }

            if (RetryButton.onClick.GetPersistentEventCount() > 0 ||
                ReturnMenuButton.onClick.GetPersistentEventCount() > 0 ||
                ContinueEndlessButton.onClick.GetPersistentEventCount() > 0)
            { error = "按钮不能有持久 OnClick 绑定"; return false; }

            error = string.Empty;
            return true;
        }
    }
}