// 3단계: 상호작용 범위, 플레이어 이동·범위 진입, 고정 스텝·낮밤 (원본 player.js / main.js)
using System;
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class PlayerTests
    {
        ApcConfig cfg;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 99);
            Player.Init();
            Sim.input = default;
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            UnityEngine.Object.DestroyImmediate(cfg);
        }

        [Test]
        public void Zones_StartMergesSamePoints()
        {
            var zl = Zones.List();
            // 하차장(넣기·꺼내기 합침) + 해금 보관함 3 + 포장대 1 + 도크 1 + 쓰레기통 (트럭은 아직 없음)
            Assert.AreEqual(7, zl.Count);
            var st = zl.First(z => z.kind == "staging");
            CollectionAssert.AreEquivalent(new[] { "in", "out" }, st.parts[0].roles);
            Assert.AreEqual("staging:in|staging:out", st.key);
            var b0 = zl.First(z => z.kind == "bin");
            Assert.AreEqual("bin0:in|bin0:out|bin0:inspect", b0.key);
            Assert.AreEqual(3, b0.parts[0].roles.Count);
            Assert.IsFalse(zl.Any(z => z.kind == "truck"));

            G.inTrucks[0].state = TruckState.Present;
            var tz = Zones.List().First(z => z.kind == "truck");
            Assert.AreEqual(6.8, tz.x, 1e-9);
            Assert.AreEqual(8.5, tz.z, 1e-9);
            Assert.AreSame(G.inTrucks[0], tz.refObj);
        }

        [Test]
        public void Zones_NearestWithinRadius()
        {
            Assert.IsNull(Zones.Nearest(LAYOUT.playerStart));
            var z = Zones.Nearest(new P(9.5 + 1.5, 17.5));
            Assert.AreEqual("staging", z.kind);
            Assert.IsNull(Zones.Nearest(new P(9.5 + 1.61, 17.5)));
        }

        [Test]
        public void Move_InputNormalizedAndFacing()
        {
            var p = G.player;
            Player.Update(0.05, new P(1, 0));
            Assert.AreEqual(12 + 7 * 0.05, p.x, 1e-9);
            Assert.AreEqual(Math.PI / 2, p.facing, 1e-9);
            Assert.IsTrue(p.moving);
            Player.Update(0.05, new P(1, 1));
            Assert.AreEqual(12 + 0.35 + 0.35 / Math.Sqrt(2), p.x, 1e-9);
            Assert.AreEqual(21 + 0.35 / Math.Sqrt(2), p.z, 1e-9);
            Player.Update(0.05, default);
            Assert.IsFalse(p.moving);
        }

        [Test]
        public void Move_TargetArrivesAndInputCancels()
        {
            var p = G.player;
            p.target = new P(13, 21);
            for (int i = 0; i < 2; i++) Player.Update(0.05, default);
            Assert.AreEqual(12.7, p.x, 1e-9);
            Player.Update(0.05, default);   // 남은 0.3 < 0.35 → 정확히 도착
            Assert.AreEqual(13, p.x, 1e-9);
            Player.Update(0.05, default);
            Assert.IsNull(p.target);
            p.target = new P(20, 21);
            Player.Update(0.05, new P(0, -1));
            Assert.IsNull(p.target);
            Assert.AreEqual(13, p.x, 1e-9);
        }

        [Test]
        public void Move_ClampedToBounds()
        {
            var p = G.player;
            p.x = 0.7; p.z = 31.3;
            Player.Update(0.05, new P(-1, 1));
            Assert.AreEqual(0.6, p.x, 1e-9);
            Assert.AreEqual(31.4, p.z, 1e-9);
        }

        [Test]
        public void Zone_EntryModeDelayAndLeave()
        {
            var p = G.player;
            p.x = 9.5; p.z = 17.5;
            Player.Update(0.05, default);
            Assert.AreEqual("staging:in|staging:out", p.zoneKey);
            Assert.AreEqual("pick", p.zoneMode);
            Assert.AreEqual(0.15, p.timer, 1e-9);   // 진입 0.2초 대기 시작 후 이번 스텝만큼 줄어듦

            // 다른 범위로 → 검수 점유 해제
            G.bins[0].inspector = Owner.Player;
            p.hand.Add(new HandEntry { kind = HandKind.Raw, item = 0 });
            p.x = 16; p.z = 6.8;
            Player.Update(0.05, default);
            Assert.AreEqual("bin0:in|bin0:out|bin0:inspect", p.zoneKey);
            Assert.IsNull(p.zoneMode);
            Assert.AreEqual(Owner.None, G.bins[0].inspector);

            p.x = 9.5; p.z = 17.5;
            Player.Update(0.05, default);
            Assert.AreEqual("drop", p.zoneMode);   // 들고 들어오면 내려놓기

            p.x = 12; p.z = 21;
            Player.Update(0.05, default);
            Assert.IsNull(p.zoneKey);
            Assert.IsNull(p.zone);
        }

        [Test]
        public void Sim_FixedStepsSpeedAndPause()
        {
            G.speed = 2;
            Sim.Frame(0.1);
            Assert.AreEqual(0.2, G.time, 1e-9);
            G.paused = true;
            Sim.Frame(0.1);
            Assert.AreEqual(0.2, G.time, 1e-9);
            G.paused = false; G.userPaused = true;
            Sim.Frame(0.1);
            Assert.AreEqual(0.2, G.time, 1e-9);
        }

        [Test]
        public void Sim_PhaseSwitchesAndCountsDays()
        {
            var toasts = new List<string>();
            Toast += (i, t) => toasts.Add(t);
            G.phaseTime = 599.98;
            Sim.Update(0.05);
            Assert.AreEqual("night", G.phase);
            Assert.AreEqual(1, G.day);
            Assert.AreEqual(0.03, G.phaseTime, 1e-9);
            G.phaseTime = 600;
            Sim.Update(0.05);
            Assert.AreEqual("day", G.phase);
            Assert.AreEqual(2, G.day);
            CollectionAssert.AreEqual(new[] { "밤이 되었습니다", "2일차 아침이 밝았습니다" }, toasts);
        }
    }
}
