// 배치 좌표(원본 Three.js, x 오른쪽 / z 아래쪽) ↔ Unity 월드 좌표 변환. 변환은 여기서만 한다
// 규칙: unity.x = x, unity.y = y, unity.z = -z
using System;
using UnityEngine;

namespace APC
{
    public static class CoordUtil
    {
        public static Vector3 ToWorld(P p, float y = 0) => new Vector3((float)p.x, y, (float)-p.z);
        public static Vector3 ToWorld(double x, double z, float y = 0) => new Vector3((float)x, y, (float)-z);

        // float 오차를 지우려고 0.0001칸 단위로 맞춘다
        public static P ToLayout(Vector3 w) => new P(R(w.x), R(-w.z));
        static double R(double v) => Math.Round(v * 10000) / 10000;

        // 시설: 로컬 +z가 앞면 (원본 World.faceYaw)
        public static float FaceYaw(double rot)
        {
            var d = LayoutData.Dir(rot);
            return (float)(Math.Atan2(d.x, -d.z) * 180 / Math.PI);
        }

        // 트럭: 로컬 -x가 뒷문 (원본 World.truckYaw)
        public static float TruckYaw(double rot)
        {
            var d = LayoutData.Dir(rot);
            return (float)(Math.Atan2(-d.z, -d.x) * 180 / Math.PI);
        }

        // 위 두 함수의 역변환 (씬 오브젝트 회전 → 배치 rot, 0~360)
        public static double RotFromFaceYaw(float yaw)
        {
            double a = yaw * Math.PI / 180;
            return Norm(Math.Atan2(-Math.Cos(a), Math.Sin(a)));
        }

        public static double RotFromTruckYaw(float yaw)
        {
            double a = yaw * Math.PI / 180;
            return Norm(Math.Atan2(-Math.Sin(a), -Math.Cos(a)));
        }

        static double Norm(double rad) => (Math.Round(rad * 180 / Math.PI * 100) / 100 % 360 + 360) % 360;

        // 이동 방향(배치 좌표) → 캐릭터 yaw. 원본 facing = atan2(vx, vz)를 z 반전에 맞춘 것
        public static float FacingYaw(double vx, double vz) => (float)(Math.Atan2(vx, -vz) * 180 / Math.PI);

        // 원본 facing(라디안, atan2(vx, vz)) → Unity yaw
        public static float FacingToYaw(double facing) => FacingYaw(Math.Sin(facing), Math.Cos(facing));

        // 위 함수의 역변환 (Unity yaw → facing)
        public static double YawToFacing(float yaw) => Math.PI - yaw * Math.PI / 180;
    }
}
