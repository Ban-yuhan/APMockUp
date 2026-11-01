// 뷰 공용 색 (원본 world.js의 COLORS)과 색 바꾸기 헬퍼
using UnityEngine;

namespace APC
{
    public static class Palette
    {
        public const int GROUND = 0xc9ced6, CARDBOARD = 0xc8a675, BOX = 0xa47148, BROKEN = 0xd63031,
            LOCKED = 0x9aa0a8, WOOD = 0xb98b5e, BELT = 0x37474f, METAL = 0x78909c, SKIN = 0xf1c27d;

        public static Color TypeColor(int item) => Game.CONFIG.Type(Game.CONFIG.items[item].type).color;

        // 손에 든 물건 색 (원본 entryColor)
        public static Color EntryColor(HandEntry e) =>
            e.kind == HandKind.Broken ? U.Hex(BROKEN) : e.kind == HandKind.Box ? U.Hex(BOX) : TypeColor(e.item);

        static MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // 재질은 그대로 두고 색만 바꾼다 (공유 재질 에셋을 건드리지 않음)
        public static void Tint(Renderer r, Color c)
        {
            mpb ??= new MaterialPropertyBlock();
            mpb.SetColor(BaseColor, c);
            r.SetPropertyBlock(mpb);
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    }
}
