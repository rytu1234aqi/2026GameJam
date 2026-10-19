using UnityEngine;
namespace Spotlight.Presentation.Cinematics
{
    public sealed class OpeningStoryPreview : MonoBehaviour
    {
        public CutsceneSequenceSO Sequence;
        public CutscenePanelReferences Panel;
        private void Start()
        {
            CutscenePlayer player = Panel.gameObject.AddComponent<CutscenePlayer>();
            player.Panel = Panel; player.Begin(Sequence, null);
        }
    }
}
