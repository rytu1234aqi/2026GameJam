using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Spotlight.Presentation
{
    public class MainMenuPanelReferences : MonoBehaviour
    {
        public RectTransform Root;
        public Text TitleText, HintText;
        public Button StoryButton, EndlessButton, ContinueButton;

        public bool Validate(out string error)
        {
            // 1. 检查字段
            if (Root == null) { error = "Root is missing."; return false; }
            if (TitleText == null || HintText == null) { error = "TitleText or HintText is missing."; return false; }
            if (StoryButton == null || EndlessButton == null || ContinueButton == null) { error = "One or more buttons are missing."; return false; }

            // 2. 检查层级（Root 必须是脚本所在根 RectTransform，其他在 Root 子层级）
            if (Root != transform as RectTransform) { error = "Root must be the script's own RectTransform."; return false; }
            if (!TitleText.transform.IsChildOf(Root) || !HintText.transform.IsChildOf(Root) ||
                !StoryButton.transform.IsChildOf(Root) || !EndlessButton.transform.IsChildOf(Root) || !ContinueButton.transform.IsChildOf(Root))
            {
                error = "UI references must be children of Root.";
                return false;
            }

            // 3. 按钮互不重复
            if (StoryButton == EndlessButton || StoryButton == ContinueButton || EndlessButton == ContinueButton)
            {
                error = "Buttons must not be duplicated.";
                return false;
            }

            // 4. 字体有效
            if (TitleText.font == null || HintText.font == null)
            {
                error = "Text font is missing.";
                return false;
            }

            // 5. 检查无持久 OnClick 绑定
            if (StoryButton.onClick.GetPersistentEventCount() > 0 ||
                EndlessButton.onClick.GetPersistentEventCount() > 0 ||
                ContinueButton.onClick.GetPersistentEventCount() > 0)
            {
                error = "Buttons must not have persistent OnClick events. Let the main programmer bind them.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}