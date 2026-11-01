// 낮/밤 (원본 World.syncLighting): 밤에는 창고 바깥(배경, 바깥 땅)만 어두워지고 내부 조명은 그대로
using UnityEngine;

namespace APC
{
    public class DayNightView : MonoBehaviour
    {
        [SerializeField] Camera cam;
        [SerializeField] Renderer outsideGround;

        float k = -1;
        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void LateUpdate()
        {
            var G = Game.G;
            if (G == null) return;
            float night = G.phase == "night" ? 1 : 0;
            k = k < 0 ? night : k + (night - k) * Mathf.Min(1, GameRoot.ViewDt * 1.5f);
            cam.backgroundColor = new Color(0.875f - 0.77f * k, 0.9f - 0.75f * k, 0.925f - 0.68f * k);
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor(BaseColor, new Color(0.706f - 0.6f * k, 0.737f - 0.6f * k, 0.776f - 0.5f * k));
            outsideGround.SetPropertyBlock(mpb);
        }
    }
}
