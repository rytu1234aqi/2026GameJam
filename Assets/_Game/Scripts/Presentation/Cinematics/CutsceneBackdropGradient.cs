using UnityEngine;
using UnityEngine.UI;
namespace Spotlight.Presentation.Cinematics
{
    public sealed class CutsceneBackdropGradient : BaseMeshEffect
    {
        public override void ModifyMesh(VertexHelper vertices)
        {
            if(!IsActive())return;
            Rect rect=graphic.rectTransform.rect;UIVertex vertex=new UIVertex();
            for(int i=0;i<vertices.currentVertCount;i++)
            {
                vertices.PopulateUIVertex(ref vertex,i);
                float t=Mathf.InverseLerp(rect.yMin,rect.yMax,vertex.position.y);
                vertex.color=Color.Lerp(new Color(.015f,.025f,.055f),new Color(.075f,.11f,.18f),t);
                vertices.SetUIVertex(vertex,i);
            }
        }
    }
}
