// 노치·홈 바를 피해 자식 UI(HUD·패널·디버그)를 Screen.safeArea 안에 둔다. 화면 크기·방향이 바뀌면 다시 맞춘다
using UnityEngine;

namespace APC
{
    public class SafeArea : MonoBehaviour
    {
        Rect last;
        Vector2Int size;

        void Update()
        {
            var sa = Screen.safeArea;
            var sz = new Vector2Int(Screen.width, Screen.height);
            if (sa == last && sz == size) return;
            last = sa;
            size = sz;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(sa.xMin / sz.x, sa.yMin / sz.y);
            rt.anchorMax = new Vector2(sa.xMax / sz.x, sa.yMax / sz.y);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
