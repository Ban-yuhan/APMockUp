// 바닥 원/고리 (원본 makeRing: 채운 원 + 테두리 고리). 클릭 이동 표시에도 쓴다 (채움 없음)
using UnityEngine;

namespace APC
{
    [ExecuteAlways]
    public class ZoneRing : MonoBehaviour
    {
        public float outer = 1.6f, inner = 1.48f;
        public bool fill = true;
        public Color color = Color.white;
        public float fillAlpha = 0.16f, lineAlpha = 0.7f;
        public MeshFilter fillMesh, lineMesh;

        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void OnEnable() => Build(outer, inner);

        public void Build(float outerR, float innerR)
        {
            outer = outerR; inner = innerR;
            if (fillMesh) { fillMesh.sharedMesh = Disc(outer, 40); fillMesh.gameObject.SetActive(fill); }
            if (lineMesh) lineMesh.sharedMesh = Annulus(inner, outer, 48);
            Apply();
        }

        public void SetColor(Color c) { color = c; Apply(); }

        public void SetAlpha(float fillA, float lineA)
        {
            if (fillA == fillAlpha && lineA == lineAlpha) return;
            fillAlpha = fillA; lineAlpha = lineA;
            Apply();
        }

        void Apply()
        {
            mpb ??= new MaterialPropertyBlock();
            Set(fillMesh, fillAlpha);
            Set(lineMesh, lineAlpha);
        }

        void Set(MeshFilter mf, float a)
        {
            if (!mf) return;
            var c = color; c.a = a;
            mpb.SetColor(BaseColor, c);
            mf.GetComponent<MeshRenderer>().SetPropertyBlock(mpb);
        }

        // 위에서 볼 때 시계 방향 = 앞면
        static Mesh Disc(float r, int seg)
        {
            var v = new Vector3[seg + 1];
            var t = new int[seg * 3];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1;
            }
            return new Mesh { name = "Disc", vertices = v, triangles = t };
        }

        static Mesh Annulus(float r0, float r1, int seg)
        {
            var v = new Vector3[seg * 2];
            var t = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * r0, 0, s * r0);
                v[i * 2 + 1] = new Vector3(c * r1, 0, s * r1);
                int j = (i + 1) % seg;
                t[i * 6] = i * 2; t[i * 6 + 1] = j * 2; t[i * 6 + 2] = i * 2 + 1;
                t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = j * 2; t[i * 6 + 5] = j * 2 + 1;
            }
            return new Mesh { name = "Annulus", vertices = v, triangles = t };
        }
    }
}
