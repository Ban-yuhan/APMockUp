// 지게차: 탑승/하차, 앞바퀴 축 제자리 회전, 알바 막기
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class ForkliftTests
    {
        ApcConfig cfg;
        Forklift fl;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 7);
            Player.Init();
            var c = new P((LAYOUT.bounds.minX + LAYOUT.bounds.maxX) / 2, (LAYOUT.bounds.minZ + LAYOUT.bounds.maxZ) / 2);
            G.forklifts.Add(fl = new Forklift
            {
                x = c.x, z = c.z, heading = 0, length = 2.4, width = 1.2, frontAxle = 0.7, halfTrack = 0.5,
                maxSpeed = 5, reverseSpeed = 3, accel = 4, brake = 10, turnRate = 90, pivotRate = 60,
            });
        }

        [Test]
        public void Bundle_DockToPalletThenTruck()
        {
            var d = G.docks.First(x => x.unlocked);
            var tp = CONFIG.types[0].id;
            for (int i = 0; i < 9; i++)
                d.waiting.Add(new Box
                {
                    id = 100 + i, dest = d.idx, reward = 10, primary = tp, types = { tp },
                    cells = { new Vector2Int(0, 0), new Vector2Int(1, 0) }, items = { [0] = 1 },
                });
            // 포크 끝을 도크 앞에 대고 멈춘다
            var pt = S.DockIn(d);
            fl.heading = 0; fl.x = pt.x; fl.z = pt.z - fl.length / 2; fl.ridden = true;
            d.truck = null;
            // 한 개씩 바로 옮긴다: 첫 스텝에 1개, 그 뒤 집기 1회 시간(0.25초 = 5스텝)마다 1개
            Run(default, 1);
            Assert.AreEqual(1, fl.loose.Count);
            Assert.AreEqual(8, d.waiting.Count);
            Assert.AreEqual(0, fl.pallet.Count);
            // 8개째에 묶음으로 바뀌고, 9개째는 다시 낱개
            Run(default, 60);
            Assert.AreEqual(0, d.waiting.Count);
            Assert.AreEqual(1, fl.pallet.Count);
            Assert.AreEqual(1, fl.loose.Count);
            var b = fl.pallet[0];
            Assert.AreEqual(8, b.Count);
            Assert.That(b.cells.Count, Is.InRange(4, 5));
            Assert.AreEqual(96, b.reward);
            Assert.AreEqual(8, b.items[0]);
            Assert.AreEqual(8, LoadLogic.Ranks(new List<Box> { b })[tp].count);
            Assert.IsNull(Forklifts.LoadableDock());   // 트럭 없음

            // 트럭이 오면 탄 채로 미니게임: 파렛트 묶음이 목록에 나오고, 실으면 파렛트에서 빠지고, 빼면 파렛트로 돌아간다
            d.truck = LoadLogic.CreateTruck();
            d.truck.state = TruckState.Present;
            // 묶음 모양이 어느 트럭 짐칸에든 들어가게 (짐칸 밖·잠긴 칸에는 놓이지 않는다)
            d.truck.locked.Clear();
            d.truck.w = d.truck.h = 6;
            Assert.IsNull(Forklifts.LoadableDock());   // 포크만 발판 위
            fl.z = pt.z;                                // 몸통을 발판 위로
            Assert.AreSame(d, Forklifts.LoadableDock());
            Assert.IsTrue(Loading.Open(d, fl));
            Assert.Contains(b, Loading.Waiting());
            var one = fl.loose[0];
            Assert.Contains(one, Loading.Waiting());   // 지게차 낱개도 나온다
            Loading.Place(b, b.cells.ToList(), null);
            Loading.Place(one, one.cells.ToList(), null);
            Assert.AreEqual(0, fl.pallet.Count);
            Assert.AreEqual(0, fl.loose.Count);
            Loading.ResetAll();
            Assert.Contains(b, fl.pallet);
            Assert.IsFalse(d.waiting.Contains(b));
            Assert.Contains(one, d.waiting);           // 뺀 낱개는 도크 대기로
            Loading.Close();
        }

        [Test]
        public void Bundle_PalletFullStopsMoving()
        {
            var d = G.docks.First(x => x.unlocked);
            var tp = CONFIG.types[0].id;
            for (int i = 0; i < CONFIG.loading.palletCap; i++)
                fl.pallet.Add(new Box { dest = d.idx, parts = new List<Box>(), primary = tp });
            d.waiting.Add(new Box { id = 1, dest = d.idx, primary = tp, types = { tp }, cells = { new Vector2Int(0, 0) } });
            var pt = S.DockIn(d);
            fl.heading = 0; fl.x = pt.x; fl.z = pt.z - fl.length / 2; fl.ridden = true;
            Run(default, 20);
            Assert.AreEqual(1, d.waiting.Count);
            Assert.AreEqual(0, fl.loose.Count);
            StringAssert.Contains("가득", fl.action);
        }

        [Test]
        public void Summon_InFrontOfPlayerAndBack()
        {
            var s = fl.Clone();
            s.summon = true; s.shown = false;
            G.forklifts.Add(s);
            var p = G.player;
            p.x = fl.x - 5; p.z = fl.z; p.facing = -System.Math.PI / 2;   // -x 쪽을 본다
            Assert.IsTrue(Forklifts.ToggleSummon());
            Assert.IsTrue(s.shown);
            Assert.AreEqual(p.facing, s.heading, 1e-9);
            Assert.Less(s.x, p.x);
            Assert.AreEqual(p.z, s.z, 1e-9);
            Assert.AreSame(s, Forklifts.Boardable());
            Assert.IsTrue(Forklifts.ToggleRide());
            // 타고 있어도 R이면 내리고 사라진다
            Assert.IsTrue(Forklifts.ToggleSummon());
            Assert.IsFalse(s.shown);
            Assert.IsFalse(Forklifts.Riding);
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            Object.DestroyImmediate(cfg);
        }

        static void Run(P input, int steps)
        {
            for (int i = 0; i < steps; i++) Forklifts.Update(Sim.STEP, input);
        }

        [Test]
        public void BoardAndLeave()
        {
            var f = fl;
            G.player.x = f.x + 1.2; G.player.z = f.z;
            Assert.IsTrue(Forklifts.CanBoard());
            Assert.IsTrue(Forklifts.ToggleRide());
            Assert.IsTrue(Forklifts.Riding);
            double z0 = f.z;
            Run(new P(0, 1), 20);
            Assert.Greater(f.z, z0);
            Assert.AreEqual(f.x, G.player.x, 1e-9);
            double fx = f.x, fz = f.z;
            Assert.IsTrue(Forklifts.ToggleRide());
            Assert.IsFalse(Forklifts.Riding);
            Assert.AreEqual(fx, f.x, 1e-9);
            Assert.AreEqual(fz, f.z, 1e-9);
            Assert.Greater(U.Dist(G.player.Pos, new P(f.x, f.z)), f.width / 2);
        }

        [TestCase(1, TestName = "Pivot_ClockwiseAroundRightFrontWheel")]
        [TestCase(-1, TestName = "Pivot_CounterClockwiseAroundLeftFrontWheel")]
        public void Pivot(int side)
        {
            var f = fl;
            f.ridden = true;
            var w0 = Forklifts.Wheel(f, side);
            Run(new P(side, 0), 10);
            var w1 = Forklifts.Wheel(f, side);
            Assert.AreEqual(w0.x, w1.x, 1e-9);
            Assert.AreEqual(w0.z, w1.z, 1e-9);
            // 시계 방향 = heading 감소
            Assert.AreEqual(-side * 60 * Mathf.Deg2Rad * 0.5, f.heading, 1e-6);
        }

        // 대각선이라도 좌우가 앞뒤보다 크면 제자리 회전
        [Test]
        public void Pivot_WhenSideDominates()
        {
            fl.ridden = true;
            var w0 = Forklifts.Wheel(fl, 1);
            Run(new P(1, 0.5), 10);
            var w1 = Forklifts.Wheel(fl, 1);
            Assert.AreEqual(0, fl.speed);
            Assert.AreEqual(w0.x, w1.x, 1e-9);
            Assert.AreEqual(w0.z, w1.z, 1e-9);
            Assert.Less(fl.heading, 0);
        }

        // 출발 직후(거의 0 속도)에도 최고 속도의 40% 비율로는 돈다
        [Test]
        public void Turn_AtLowSpeedStillTurns()
        {
            fl.ridden = true;
            Run(new P(0.3, 1), 1);
            Assert.AreEqual(-0.3 * 90 * Mathf.Deg2Rad * 0.4 * Sim.STEP, fl.heading, 1e-9);
        }

        [Test]
        public void AlbaGoesAroundForklift()
        {
            var f = fl;
            var from = new P(f.x, f.z - 3);
            var to = new P(f.x, f.z + 3);
            var p = from;
            for (int i = 0; i < 400 && U.Dist(p, to) > 0.2; i++)
            {
                double dx = to.x - p.x, dz = to.z - p.z, d = U.Hypot(dx, dz);
                p = Forklifts.Avoid(p, dx / d, dz / d, 0.15);
                Assert.IsFalse(System.Math.Abs(p.x - f.x) < f.width / 2 && System.Math.Abs(p.z - f.z) < f.length / 2, $"차체 안: {p}");
            }
            Assert.Less(U.Dist(p, to), 0.2);
        }
    }
}
