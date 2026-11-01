// 8단계: 자동화(Lv4)와 컨베이어 벨트 경로 (원본 automation.js, layout.js). 경로 기대값은 원본을 Node로 실행해 뽑았다
using System.Collections.Generic;
using System.Linq;
using APC.EditorTools;
using NUnit.Framework;
using UnityEngine;
using static APC.Game;

namespace APC.Tests
{
    public class AutoTests
    {
        ApcConfig cfg;

        [SetUp]
        public void SetUp()
        {
            cfg = ScriptableObject.CreateInstance<ApcConfig>();
            Setup(cfg, DefaultLayout.Make(cfg), seed: 5);
            Sim.Start();
            Sim.input = default;
        }

        [TearDown]
        public void TearDown()
        {
            ClearListeners();
            Object.DestroyImmediate(cfg);
        }

        static string Fmt(List<P> r) => r == null ? "null" : string.Join(" ", r.Select(p => $"{p.x},{p.z}"));

        [Test]
        public void BeltGraph_MatchesOriginal()
        {
            var expect = new Dictionary<string, (int n, int e, int p)> { ["sort"] = (14, 13, 9), ["transfer"] = (19, 18, 11), ["outbound"] = (16, 15, 7) };
            foreach (var kv in expect)
            {
                var g = Belts.BuildGraph(LAYOUT, kv.Key);
                Assert.AreEqual(kv.Value, (g.nodes.Count, g.edges.Count, g.ports.Count), kv.Key);
            }
        }

        [Test]
        public void BeltRoutes_MatchOriginal()
        {
            Assert.AreEqual("11.5,17.5 11.5,10.4 15.65,10.4 15.65,4.6", Fmt(Belts.CachedRoute("sort", "staging:0:beltOut", "bin:0:beltIn")));
            Assert.AreEqual("11.5,17.5 11.5,10.4 15.65,10.4 20.65,10.4 25.65,10.4 30.65,10.4 30.65,17.4",
                Fmt(Belts.CachedRoute("sort", "staging:0:beltOut", "bin:7:beltIn")));
            Assert.AreEqual("26.35,4.6 26.35,11.6 31.35,11.6 37.4,11.6 37.4,11 38.7,11", Fmt(Belts.CachedRoute("transfer", "bin:2:beltOut", "table:1:beltIn")));
            Assert.AreEqual("39.3,17 41,17 41,11 41,5 41,2 43.5,2 43.5,4 43.5,11 43.5,18 43.5,25 48,25",
                Fmt(Belts.CachedRoute("outbound", "table:2:beltOut", "dock:3:beltIn")));
            Assert.IsNull(Belts.CachedRoute("outbound", "dock:0:beltIn", "table:0:beltOut"));   // 벨트는 한 방향
        }

        [Test]
        public void Flyer_FollowsPathAndArrives()
        {
            G.stages["sort"] = 4;
            G.staging.Clear();
            G.staging.Add(0);
            var b = G.bins[0];
            int un = b.uncheckedCount;
            Auto.Update(cfg.automation.sortInterval);   // 하나 출발
            Assert.AreEqual(1, G.flyers.Count);
            Assert.AreEqual(1, b.incomingRaw);
            Assert.IsEmpty(Invariants.Check());
            var path = G.flyers[0].path;
            Assert.AreEqual("11.5,17.5 11.5,10.4 15.65,10.4 15.65,4.6", Fmt(path));
            double len = 0;
            for (int i = 0; i < path.Count - 1; i++) len += U.Dist(path[i], path[i + 1]);
            // 출발한 그 호출에서도 sortInterval만큼 이미 움직였다
            for (double t = cfg.automation.sortInterval; t < len / cfg.automation.flySpeed - 0.1; t += 0.05) Auto.Update(0.05);
            Assert.AreEqual(1, G.flyers.Count, "아직 가는 중");
            for (int i = 0; i < 10; i++) Auto.Update(0.05);
            Assert.IsEmpty(G.flyers);
            Assert.AreEqual(0, b.incomingRaw);
            Assert.AreEqual(un + 1, b.uncheckedCount);
        }

        // 하차만 알바 1명, 나머지 공정은 Lv4 자동화: 사람 없이 출고까지 돌고 불변식이 매초 유지된다
        [Test]
        public void AutomatedStages_RunWithoutPeople()
        {
            foreach (var st in new[] { "sort", "inspect", "transfer", "pack", "outbound", "load" }) G.stages[st] = 4;
            var a = Albas.Hire(0);
            a.mt = 100;
            Albas.Assign(a, "unload");
            G.player.x = LAYOUT.rest.x; G.player.z = LAYOUT.rest.z;
            int maxFlyers = 0;
            var kinds = new HashSet<HandKind>();
            for (int sec = 0; sec < 300; sec++)
            {
                for (int k = 0; k < 20; k++) Sim.Update(Sim.STEP);
                maxFlyers = Mathf.Max(maxFlyers, G.flyers.Count);
                foreach (var f in G.flyers) kinds.Add(f.kind);
                var errs = Invariants.Check();
                Assert.IsEmpty(errs, $"t={sec}s " + string.Join("; ", errs));
            }
            Assert.Greater(G.trucks, 0, "자동 상차로 출고 트럭이 떠나야 한다");
            Assert.Greater(G.shipped, 0);
            Assert.Greater(maxFlyers, 0);
            CollectionAssert.IsSupersetOf(kinds, new[] { HandKind.Raw, HandKind.Pick, HandKind.Box });
        }
    }
}
