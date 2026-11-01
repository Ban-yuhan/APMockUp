// 2단계: 좌표 변환과 씬 배치 → 배치 데이터 왕복 (씬에서 읽은 LAYOUT이 원본 makeDefault와 같아야 함)
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace APC.Tests
{
    public class MapTests
    {
        [Test]
        public void Coord_FlipsZ()
        {
            Assert.AreEqual(new Vector3(3, 1, -8.5f), CoordUtil.ToWorld(new P(3, 8.5), 1));
            var p = CoordUtil.ToLayout(new Vector3(16, 0, -4.6f));
            Assert.AreEqual(16, p.x, 1e-9);
            Assert.AreEqual(4.6, p.z, 1e-9);
        }

        [TestCase(0), TestCase(90), TestCase(180), TestCase(270)]
        public void Coord_YawRoundTrip(double rot)
        {
            Assert.AreEqual(rot, CoordUtil.RotFromFaceYaw(CoordUtil.FaceYaw(rot)), 1e-6);
            Assert.AreEqual(rot, CoordUtil.RotFromTruckYaw(CoordUtil.TruckYaw(rot)), 1e-6);
        }

        [Test]
        public void Coord_FacingPointsAlongLayoutDirection()
        {
            // 시설 rot 90 (+z, 화면 아래) → Unity 앞면은 -z
            var front = Quaternion.Euler(0, CoordUtil.FaceYaw(90), 0) * Vector3.forward;
            Assert.AreEqual(-1, front.z, 1e-5);
            // 트럭 rot 180 (뒷문 -x) → 로컬 -x가 Unity -x
            var back = Quaternion.Euler(0, CoordUtil.TruckYaw(180), 0) * Vector3.left;
            Assert.AreEqual(-1, back.x, 1e-5);
            // 캐릭터 이동 (vx, vz) = (0, 1) (화면 아래) → Unity -z
            var fwd = Quaternion.Euler(0, CoordUtil.FacingYaw(0, 1), 0) * Vector3.forward;
            Assert.AreEqual(-1, fwd.z, 1e-5);
        }

        [Test]
        public void Scene_BuildsSameLayoutAsDefault()
        {
            var cfg = ScriptableObject.CreateInstance<ApcConfig>();
            var src = DefaultLayout.Make(cfg);
            var root = MapBuilder.Build(src, cfg, assets: false);
            try
            {
                var l = root.GetComponent<MapRoot>().Build();
                Assert.AreEqual(3, l.inTrucks.Count);
                Assert.AreEqual(8, l.bins.Count);
                Assert.AreEqual(3, l.tables.Count);
                Assert.AreEqual(4, l.docks.Count);
                Assert.AreEqual(7, l.idle.Count);
                for (int i = 0; i < 3; i++) SameSlot(src.inTrucks[i], l.inTrucks[i], "inTruck" + i);
                SameSlot(src.staging, l.staging, "staging");
                for (int i = 0; i < 8; i++)
                {
                    SameSlot(src.bins[i], l.bins[i], "bin" + i);
                    Assert.AreEqual(src.bins[i].item, l.bins[i].item);
                    Assert.AreEqual(src.bins[i].cost, l.bins[i].cost);
                }
                for (int i = 0; i < 3; i++) { SameSlot(src.tables[i], l.tables[i], "table" + i); Assert.AreEqual(src.tables[i].cost, l.tables[i].cost); }
                for (int i = 0; i < 4; i++)
                {
                    SameSlot(src.docks[i], l.docks[i], "dock" + i);
                    SameSlot(src.docks[i].truck, l.docks[i].truck, "dockTruck" + i);
                    Assert.AreEqual(src.docks[i].dest, l.docks[i].dest);
                }
                SameSlot(src.trash, l.trash, "trash");
                Same(src.purifier, l.purifier, "purifier");
                Same(src.entrance, l.entrance, "entrance");
                Same(src.playerStart, l.playerStart, "playerStart");
                foreach (var kv in src.idle) Same(kv.Value, l.idle[kv.Key], "idle " + kv.Key);
                Assert.AreEqual(src.rest.x, l.rest.x, 1e-4); Assert.AreEqual(src.rest.z, l.rest.z, 1e-4);
                Assert.AreEqual(src.rest.w, l.rest.w, 1e-4); Assert.AreEqual(src.waiting.d, l.waiting.d, 1e-4);
                Assert.AreEqual(src.ix2, l.ix2); Assert.AreEqual(src.mapW, l.mapW);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cfg);
            }
        }

        static void SameSlot(Slot a, Slot b, string tag)
        {
            Same(a.Pos, b.Pos, tag);
            Assert.AreEqual(a.rot, b.rot, 1e-4, tag + " rot");
            foreach (var role in new[] { "in", "out", "inspect", "beltIn", "beltOut" }) Same(a.Pt(role), b.Pt(role), tag + " " + role);
        }

        static void Same(P a, P b, string tag)
        {
            Assert.AreEqual(a.x, b.x, 1e-4, tag + " x");
            Assert.AreEqual(a.z, b.z, 1e-4, tag + " z");
        }
    }
}
