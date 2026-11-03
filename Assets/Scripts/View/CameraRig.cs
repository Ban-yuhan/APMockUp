// 직교 카메라: 원본 오프셋 (8, 48, 36)을 z 반전한 (8, 48, -36) 각도로 본다.
// 맵 전체가 HUD 여백을 피해 들어가게 크기를 맞추고 (원본 World.resize), 휠·핀치 줌·플레이어 추종 (원본 updateCamera)
using UnityEngine;

namespace APC
{
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public class CameraRig : MonoBehaviour
    {
        public MapRoot map;
        public Vector3 offset = new Vector3(8, 48, -36);
        [Tooltip("HUD에 가리지 않게 비워 두는 화면 여백 (px): 왼쪽, 위, 오른쪽, 아래")]
        public Vector4 hudMargin = new Vector4(10, 100, 86, 46);
        [Tooltip("목표 줌 (1 = 맵 전체, 1보다 크면 플레이어를 따라간다)")]
        [Range(1, 2.6f)] public float zoom = 1;
        [Tooltip("기본으로 얼마나 가까이 볼지 (1 = 맵 전체). 카메라 거리는 여기서 조절한다.\n" +
                 "Camera의 크기·위치는 매 프레임 이 컴포넌트가 다시 계산하므로 직접 바꿔도 덮어써진다")]
        [Range(1, 3)] public float closeness = 1.6f;

        Camera cam;
        float camZoom = 1;
        Vector3 camTarget;
        bool following;

        // 원본 World.center
        public Vector3 Center => map ? CoordUtil.ToWorld(map.mapSize.x / 2 - 1, map.mapSize.y / 2) : Vector3.zero;

        void OnEnable() => cam = GetComponent<Camera>();

        // 원본 zoomBy: 아래로 굴리면(deltaY > 0) 축소
        public void ZoomBy(float deltaY) => ZoomScale(deltaY > 0 ? 0.88f : 1.14f);

        // 두 손가락 핀치: 벌린 비율만큼 확대 (1보다 크면 확대)
        public void ZoomScale(float f) => zoom = Mathf.Clamp(zoom * f, 1, 2.6f);

        void LateUpdate()
        {
            if (!map) return;
            var p = Application.isPlaying ? Game.G?.player : null;
            if (p == null) { Fit(Center, zoom * closeness); return; }
            if (!following) { following = true; camTarget = Center; camZoom = 1; }
            float a = Mathf.Min(1, GameRoot.FrameDt * 8);
            camZoom += (zoom * closeness - camZoom) * a;
            float k = Mathf.Clamp01((camZoom - 1) / 0.6f);
            Vector3 c = Center, pw = CoordUtil.ToWorld(p.x, p.z);
            camTarget.x += (c.x + (pw.x - c.x) * k - camTarget.x) * a;
            camTarget.z += (c.z + (pw.z - c.z) * k - camTarget.z) * a;
            Fit(camTarget, camZoom);
        }

        // target: 카메라가 볼 지점. 맵 전체 기준 크기는 맵 중심을 볼 때로 계산한다
        public void Fit(Vector3 target, float z)
        {
            cam.orthographic = true;
            var rot = Quaternion.LookRotation(-offset);
            var inv = Matrix4x4.TRS(Center + offset, rot, Vector3.one).inverse;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var x in new[] { -1f, map.mapSize.x })
                foreach (var zz in new[] { -0.5f, map.mapSize.y + 0.5f })
                    foreach (var y in new[] { 0f, 2.5f })
                    {
                        var v = inv.MultiplyPoint3x4(CoordUtil.ToWorld(x, zz, y));
                        minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                        minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
                    }
            float W = cam.pixelWidth, H = cam.pixelHeight;
            float aw = Mathf.Max(200, W - hudMargin.x - hudMargin.z), ah = Mathf.Max(200, H - hudMargin.y - hudMargin.w);
            float s = Mathf.Min(aw / (maxX - minX), ah / (maxY - minY));
            float cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            // 원본의 비대칭 frustum(left/right/top/bottom)을 카메라 평면 이동으로 같게 만든다
            float left = cx - (hudMargin.x + aw / 2) / s, top = cy + (hudMargin.y + ah / 2) / s;
            float vx = left + W / (2 * s), vy = top - H / (2 * s);
            cam.orthographicSize = H / (2 * s) / z;
            transform.rotation = rot;
            // 원본 camera.zoom은 frustum 중심을 그대로 두고 크기만 줄인다
            transform.position = target + offset + rot * new Vector3(vx, vy, 0);
        }
    }
}
