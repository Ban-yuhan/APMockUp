// 게임 상태 (원본 js/state.js의 G, createState). 유일한 가변 게임 상태
using System;
using System.Collections.Generic;
using UnityEngine;
using static APC.Game;

namespace APC
{
    // 점유자: 원본의 null / 'player' / 'auto' / 알바 id
    public static class Owner
    {
        public const int None = 0, Player = -1, Auto = -2;
    }

    public enum TruckState { Away, Arriving, Present, Leaving }
    public enum HandKind { Raw, Pick, Box, Broken }

    // 손에 든 물건. 한 손에는 같은 kind만
    public class HandEntry
    {
        public HandKind kind;
        public int item;
        public bool reserved;   // raw: 분류 알바가 보관함 공간을 예약함
        public Box box;
    }

    public class Bin
    {
        public int idx, item;
        public BinSlot slot;           // 배치의 보관함 (없으면 null → 해금 불가)
        public bool unlocked;
        public int uncheckedCount, checkedCount, broken;
        public int incomingRaw;        // 분류 중인(손/컨베이어) 물품 → 보관함 공간 예약
        public int reservedPick;       // 이송 알바가 집으러 가는 중인 검수완료 재고
        public int inFlightPick;       // 이송 컨베이어 위의 물품
        public int inspector = Owner.None;
        public double armProgress;
    }

    public class InTruck
    {
        public int idx;
        public Slot slot;
        public TruckState state;
        public List<int> items = new List<int>();
        public double timer, off;      // off = 주차 위치에서 떨어진 거리 (0 = 주차됨)
    }

    public class Order
    {
        public int id, dest, units, reward;
        public SortedDictionary<int, int> items = new SortedDictionary<int, int>();   // 품목 idx → 수량 (원본 객체 키 순서 = 숫자 오름차순)
        public int table = -1;
    }

    public class Box
    {
        public int id, order, dest, reward, units;
        public string shape;
        public List<Vector2Int> cells = new List<Vector2Int>();
        public List<string> types = new List<string>();
        public string primary;
        public SortedDictionary<int, int> items = new SortedDictionary<int, int>();
        public int claimedBy = Owner.None;
    }

    public class Table
    {
        public int idx;
        public Slot slot;
        public bool unlocked;
        public Order order;
        public Dictionary<int, int> delivered = new Dictionary<int, int>();
        public int packer = Owner.None;
        public double packProgress, machineProgress;
        public List<Box> boxes = new List<Box>();
    }

    public class Objective
    {
        public string kind;   // rank | fill | boxes | item | kinds
        public string type;
        public int rank, n, item, bonus;
        public double rate;
    }

    public class Placement { public Box box; public List<Vector2Int> cells; }

    // 출고 트럭 (원본 LoadLogic.createTruck)
    public class OutTruck
    {
        public TruckTypeDef def;
        public int w, h;
        public HashSet<Vector2Int> locked = new HashSet<Vector2Int>(), bonus = new HashSet<Vector2Int>();
        public List<Objective> objectives = new List<Objective>();
        public List<Placement> placements = new List<Placement>();
        public string mode;   // null | manual | auto
        public int autoCells;
        public List<Box> autoBoxes = new List<Box>();
        public double autoTarget;
        public TruckState state = TruckState.Arriving;
        public double off;
    }

    public class Dock
    {
        public int idx;
        public DockSlot slot;
        public bool unlocked;
        public List<Box> waiting = new List<Box>();
        public OutTruck truck;
        public double truckTimer, autoTimer, stuckTimer;
        public int loader = Owner.None;
    }

    // 컨베이어 위 물품
    public class Flyer
    {
        public HandKind kind;
        public int item;    // raw/pick/broken: 품목 idx
        public Box box;     // box
        public List<P> path;
        public int seg;
        public double x, z;
        public Action onArrive;
    }

    public class Actor
    {
        public double x, z, facing;
        public bool moving;
        public List<HandEntry> hand = new List<HandEntry>();
        public P Pos => new P(x, z);
    }

    public class PlayerState : Actor
    {
        public P? target;
        public double timer, progress;
        public string zoneKey, zoneMode, busy, action;
        public Zone zone;
    }

    public class Candidate { public string name; public int str, agi, intel, wage; }

    public class Alba : Actor
    {
        public string name;
        public int str, agi, intel, wage, id;
        public double mt;
        public double skLift, skCarry, skPack, skInspect;
        public string stage;
        public SortedDictionary<int, int> pickRes = new SortedDictionary<int, int>();
        public bool wantRest, resting, unpaid;
        public double restT, flee, progress, fx;
        public string status = "대기";
        public List<Step> plan = new List<Step>();
        public P? idleSpot;   // 할 일 없을 때 잡아 둔 대기 자리 (다른 알바와 겹치지 않게)
    }

    public class GameState
    {
        public double time, phaseTime;
        public string phase = "day";
        public int day = 1;
        public int speed = 1;
        public bool userPaused, paused;   // paused: 미니게임 패널이 열려 있으면 true
        public int money;
        public int earned, shipped, brokenCount, trucks;   // 원본 stats

        public List<Bin> bins = new List<Bin>();
        public List<int> staging = new List<int>();   // 하차장에 쌓인 품목 idx
        public List<InTruck> inTrucks = new List<InTruck>();
        public Dictionary<string, int> fac = new Dictionary<string, int> { ["parking"] = 0, ["staging"] = 0, ["bin"] = 0 };
        public List<Table> tables = new List<Table>();
        public List<Dock> docks = new List<Dock>();
        public List<Order> orders = new List<Order>();
        public double orderTimer;
        public int seqOrder = 1, seqBox = 1, seqAlba = 1;
        public List<Alba> albas = new List<Alba>();
        public List<Candidate> candidates = new List<Candidate>();
        public Dictionary<string, int> stages = new Dictionary<string, int>();
        public int careRest = 0, carePurifier = -1;
        public int loadCompress, loadBonus;
        public double autoSort, autoTransfer, autoOutbound;
        public List<Flyer> flyers = new List<Flyer>();
        public string eventName;   // null = 이벤트 없음
        public PlayerState player;

        public static GameState Create()
        {
            var g = new GameState { money = CONFIG.startMoney, orderTimer = CONFIG.orders.firstDelay };
            for (int i = 0; i < CONFIG.items.Count; i++)
            {
                var slot = LAYOUT.bins.Find(b => b.item == CONFIG.items[i].id);
                g.bins.Add(new Bin { idx = i, item = i, slot = slot, unlocked = slot != null && slot.cost == 0 });
            }
            for (int i = 0; i < LAYOUT.inTrucks.Count; i++)
                g.inTrucks.Add(new InTruck { idx = i, slot = LAYOUT.inTrucks[i], state = TruckState.Away, timer = 0.5 + i, off = S.TRUCK_AWAY });
            for (int i = 0; i < LAYOUT.tables.Count; i++)
                g.tables.Add(new Table { idx = i, slot = LAYOUT.tables[i], unlocked = LAYOUT.tables[i].cost == 0 });
            for (int i = 0; i < CONFIG.destinations.Count; i++)
            {
                var slot = LAYOUT.docks.Find(d => d.dest == CONFIG.destinations[i].id);
                g.docks.Add(new Dock { idx = i, slot = slot, unlocked = slot != null && slot.cost == 0, truckTimer = 0.5 });
            }
            foreach (var s in CONFIG.stages) g.stages[s.id] = 1;
            return g;
        }
    }
}
