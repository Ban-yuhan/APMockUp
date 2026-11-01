// 설정값 (원본 js/config.js). 기본값은 원본 CONFIG와 같다. 에셋: Assets/Data/ApcConfig.asset
// emoji 필드는 표시용 문자가 아니라 아이콘 키다 (IconSet에서 스프라이트로 바꾼다)
using System;
using System.Collections.Generic;
using UnityEngine;

namespace APC
{
    [CreateAssetMenu(menuName = "APC/Config", fileName = "ApcConfig")]
    public class ApcConfig : ScriptableObject
    {
        public int startMoney = 500;

        [Header("낮/밤")]
        public double phaseSeconds = 600;      // 낮/밤 전환 주기 (실시간 초)
        public int dayStartHour = 6;            // 낮 시작 시각 (시계 표기용)

        public PlayerCfg player = new PlayerCfg();
        public double interactRadius = 1.6;     // 상호작용 범위
        public double interactDelay = 0.2;      // 범위에 들어간 뒤 작업 시작까지

        public TimingCfg timing = new TimingCfg();
        public DebugCfg debug = new DebugCfg();
        public InboundCfg inbound = new InboundCfg();

        // 물류 시설 업그레이드: values[레벨] = 효과, costs[레벨] = 그 레벨로 올리는 비용
        public FacilityCfg facility = new FacilityCfg();
        public BreakChanceCfg breakChance = new BreakChanceCfg();
        public OrdersCfg orders = new OrdersCfg();
        [Tooltip("원본 CONFIG.event")]
        public EventCfg evt = new EventCfg();

        public List<TypeDef> types = new List<TypeDef>
        {
            new TypeDef { id = "food",  name = "식품",   emoji = "🍱", color = U.Hex(0xe67e22) },
            new TypeDef { id = "daily", name = "생필품", emoji = "🧼", color = U.Hex(0x3498db) },
            new TypeDef { id = "cloth", name = "의류",   emoji = "👚", color = U.Hex(0x9b59b6) },
            new TypeDef { id = "elec",  name = "가전",   emoji = "🔌", color = U.Hex(0x607d8b) },
            new TypeDef { id = "book",  name = "도서",   emoji = "📚", color = U.Hex(0x27ae60) },
        };

        // 보관함 1개 = 품목 1종. cost는 기본 배치의 보관함 해금 비용으로 쓴다 (0 = 시작부터 해금)
        public List<ItemDef> items = new List<ItemDef>
        {
            new ItemDef { id = "ramen",   name = "라면",   emoji = "🍜", type = "food",  price = 8,  cost = 0 },
            new ItemDef { id = "tissue",  name = "휴지",   emoji = "🧻", type = "daily", price = 6,  cost = 0 },
            new ItemDef { id = "water",   name = "생수",   emoji = "💧", type = "food",  price = 5,  cost = 0 },
            new ItemDef { id = "shampoo", name = "샴푸",   emoji = "🧴", type = "daily", price = 10, cost = 200 },
            new ItemDef { id = "tshirt",  name = "티셔츠", emoji = "👕", type = "cloth", price = 14, cost = 300 },
            new ItemDef { id = "socks",   name = "양말",   emoji = "🧦", type = "cloth", price = 7,  cost = 300 },
            new ItemDef { id = "novel",   name = "소설책", emoji = "📕", type = "book",  price = 12, cost = 400 },
            new ItemDef { id = "earbuds", name = "이어폰", emoji = "🎧", type = "elec",  price = 25, cost = 500 },
        };

        // 출고 도크 1개 = 목적지 1곳
        public List<DestDef> destinations = new List<DestDef>
        {
            new DestDef { id = "seoul",   name = "서울", color = U.Hex(0xc0392b), cost = 0 },
            new DestDef { id = "incheon", name = "인천", color = U.Hex(0x2980b9), cost = 400 },
            new DestDef { id = "daegu",   name = "대구", color = U.Hex(0x16a085), cost = 700 },
            new DestDef { id = "busan",   name = "부산", color = U.Hex(0x8e44ad), cost = 1000 },
        };

        public int[] packTableCosts = { 0, 300, 600 };

        // costs[i] = Lv(i+1)로 올리는 비용. 4번째 값이 있으면 Lv4 = 자동화
        public List<StageDef> stages = new List<StageDef>
        {
            new StageDef { id = "unload",   name = "하차",     emoji = "🚚", costs = new[] { 0, 150, 300 } },
            new StageDef { id = "sort",     name = "분류",     emoji = "🗂️", costs = new[] { 0, 150, 300, 800 } },
            new StageDef { id = "inspect",  name = "검수",     emoji = "🔍", costs = new[] { 0, 150, 300, 800 } },
            new StageDef { id = "transfer", name = "이송",     emoji = "🛒", costs = new[] { 0, 200, 400, 1000 } },
            new StageDef { id = "pack",     name = "포장",     emoji = "🎁", costs = new[] { 0, 200, 400, 1000 } },
            new StageDef { id = "outbound", name = "출고분류", emoji = "📤", costs = new[] { 0, 200, 400, 1000 } },
            new StageDef { id = "load",     name = "상차",     emoji = "🚛", costs = new[] { 0, 250, 500, 1200 } },
        };

        public CareCfg care = new CareCfg();
        public AlbaCfg alba = new AlbaCfg();
        public MtCfg mt = new MtCfg();

        public List<SnackDef> snacks = new List<SnackDef>
        {
            new SnackDef { id = "candy", emoji = "🍬", name = "사탕", mt = 10 },
            new SnackDef { id = "choco", emoji = "🍫", name = "초콜릿", mt = 20 },
            new SnackDef { id = "lunch", emoji = "🍱", name = "도시락", mt = 40 },
        };

        public AutomationCfg automation = new AutomationCfg();
        public LoadingCfg loading = new LoadingCfg();

        public TypeDef Type(string id) => types.Find(t => t.id == id);
        public int DestIndex(string id) => destinations.FindIndex(d => d.id == id);
    }

    [Serializable] public class PlayerCfg { public double speed = 7; public int capacity = 4; }

    [Serializable]
    public class TimingCfg
    {
        public double handle = 0.25;   // 물건 1개 집기/내려놓기
        public double inspect = 1.0;   // 검수 1개
        public double pack = 1.0;      // 포장 1회
        public double loadBox = 1.2;   // 알바 상차 박스 1개
    }

    [Serializable] public class DebugCfg { public int moneyStep = 1000; }

    [Serializable]
    public class InboundCfg { public double returnDelay = 8, retryDelay = 2; public int minItems = 12, maxItems = 20; }

    [Serializable]
    public class FacilityLevel
    {
        public string name, emoji, unit, desc;
        public int[] values;
        public int[] costs;
    }

    [Serializable]
    public class FacilityCfg
    {
        public static readonly string[] Keys = { "parking", "staging", "bin" };
        public FacilityLevel parking = new FacilityLevel { name = "하차 주차공간", emoji = "🅿️", unit = "대", desc = "동시에 들어올 수 있는 입고 트럭", values = new[] { 1, 2, 3 }, costs = new[] { 0, 400, 900 } };
        public FacilityLevel staging = new FacilityLevel { name = "하차장 크기", emoji = "📥", unit = "칸", desc = "하차장(임시 보관소) 최대 보관량", values = new[] { 30, 45, 60, 80 }, costs = new[] { 0, 200, 400, 700 } };
        public FacilityLevel bin = new FacilityLevel { name = "보관함 최대 보관량", emoji = "🗄️", unit = "개", desc = "모든 보관함의 품목당 최대 보관량", values = new[] { 20, 30, 40, 55, 70 }, costs = new[] { 0, 300, 600, 1000, 1500 } };
        public FacilityLevel Get(string key) => key == "parking" ? parking : key == "staging" ? staging : key == "bin" ? bin : null;
    }

    [Serializable] public class BreakChanceCfg { public double inspect = 0.05, carry = 0.05; }

    [Serializable]
    public class OrdersCfg
    {
        public double interval = 25;   // 주문 생성 주기 (초)
        public double firstDelay = 2;
        public int maxUnits = 5;       // 주문 1건 품목 총개수 최대 (= 박스 최대 칸 수)
        public int maxKinds = 3;
        public int baseReward = 10;
        public int priceMult = 2;      // 보상 = 기본 + 품목가격 x 수량 x 배수
    }

    [Serializable]
    public class EventCfg { public string name = "명절 물류 폭주!"; public double chance = 0.35; public int rushOrders = 10; public double intervalMult = 0.5; }

    [Serializable] public class TypeDef { public string id, name, emoji; public Color color; }
    [Serializable] public class ItemDef { public string id, name, emoji, type; public int price, cost; }
    [Serializable] public class DestDef { public string id, name; public Color color; public int cost; }
    [Serializable] public class StageDef { public string id, name, emoji; public int[] costs; }
    [Serializable] public class SnackDef { public string id, emoji, name; public double mt; }

    [Serializable]
    public class CareCfg
    {
        public string[] tierNames = { "낡은", "평범한", "고급", "최고급" };
        public RestCfg rest = new RestCfg();
        public PurifierCfg purifier = new PurifierCfg();
    }

    [Serializable]
    public class RestCfg
    {
        public int[] costs = { 0, 300, 700, 1500 };
        public double[] recover = { 4, 7, 11, 16 };
        public double[] until = { 20, 25, 30, 35 };
        public double minRest = 3;
    }

    [Serializable]
    public class PurifierCfg { public int[] costs = { 300, 300, 700, 1500 }; public double[] chance = { 0.1, 0.2, 0.3, 0.45 }; }

    [Serializable]
    public class AlbaCfg
    {
        public int max = 12, candidates = 3, rerollCost = 30;
        public int statMin = 1, statMax = 5;
        public double wageBase = 2, wagePerStat = 0.5;   // 시급(1시간) = 기본 + 스탯합 x 배수 + 0~편차
        public int wageJitter = 2;
        public int hoursPerPhase = 12;                   // 낮/밤이 바뀔 때 12시간분 지급
        public double unpaidMtMult = 2, fleeStep = 0.1;
    }

    [Serializable] public class MinMult { public double min, mult; }
    [Serializable] public class MinP { public double min, p; }

    [Serializable]
    public class MtCfg
    {
        public double min = -100, max = 100, start = 50, perWork = 2;
        // 작업 능률 (이동속도, 작업속도 배율). 위에서부터 mt >= min인 첫 항목
        public List<MinMult> effect = new List<MinMult>
        {
            new MinMult { min = 50, mult = 1.15 }, new MinMult { min = 0, mult = 1.0 },
            new MinMult { min = -50, mult = 0.85 }, new MinMult { min = double.NegativeInfinity, mult = 0.7 },
        };
        // 작업 후 휴식 확률
        public List<MinP> restChance = new List<MinP>
        {
            new MinP { min = 20, p = 0.005 }, new MinP { min = 0, p = 0.03 },
            new MinP { min = -50, p = 0.5 }, new MinP { min = double.NegativeInfinity, p = 0.6 },
        };
    }

    [Serializable]
    public class AutomationCfg
    {
        public double sortInterval = 0.6, inspectTime = 1.0, transferInterval = 0.5, packTime = 1.5;
        public double outboundInterval = 0.8, loadInterval = 1.0;
        public double loadTarget = 0.9;   // 전체 칸 중 채우는 비율 (적재율 180%)
        public double flySpeed = 7;
    }

    [Serializable] public class TruckTypeDef { public string id, name; public int w, h; public int[] locked; public double weight; }
    [Serializable] public class ShapeDef { public string name; public Vector2Int[] cells; }
    [Serializable] public class ShapeGroup { public int units; public List<ShapeDef> list; }
    [Serializable] public class RateBonus { public double rate; public int bonus; }
    [Serializable] public class KindsBonus { public int n, bonus; }

    [Serializable]
    public class LoadUpgradeDef { public string name, emoji; public int max; public int[] costs; }

    [Serializable]
    public class LoadUpgrades
    {
        public LoadUpgradeDef compress = new LoadUpgradeDef { name = "압축 적재", emoji = "🗜️", max = 5, costs = new[] { 150, 250, 400, 600, 900 } };
        public LoadUpgradeDef bonus = new LoadUpgradeDef { name = "우선 적재 칸", emoji = "⭐", max = 5, costs = new[] { 150, 250, 400, 600, 900 } };
    }

    [Serializable] public class RankObjCfg { public int[] ranks = { 1, 2, 3 }; public int[] bonus = { 0, 5, 10, 15 }; public int bigTruckCells = 25; }
    [Serializable] public class BoxesObjCfg { public int min = 3, perCells = 4, bonusPer = 2; }
    [Serializable] public class ItemObjCfg { public int bonus = 5; }

    // 특수 목표: 트럭마다 1~5개 (수동 상차만)
    [Serializable]
    public class ObjectivesCfg
    {
        public int[] count = { 1, 5 };
        public RankObjCfg rank = new RankObjCfg();   // D/C/B, B는 큰 트럭만
        public List<RateBonus> fill = new List<RateBonus> { new RateBonus { rate = 100, bonus = 5 }, new RateBonus { rate = 150, bonus = 10 } };
        public BoxesObjCfg boxes = new BoxesObjCfg();
        public ItemObjCfg item = new ItemObjCfg();
        public List<KindsBonus> kinds = new List<KindsBonus> { new KindsBonus { n = 2, bonus = 5 }, new KindsBonus { n = 3, bonus = 10 } };
    }

    [Serializable] public class AlbaTargetCfg { public double min = 0.6, max = 0.9; }

    // 상차 미니게임
    [Serializable]
    public class LoadingCfg
    {
        public double truckReturnDelay = 10;
        public List<TruckTypeDef> truckTypes = new List<TruckTypeDef>
        {
            new TruckTypeDef { id = "small", name = "소형", w = 5, h = 4, locked = new[] { 1, 3 }, weight = 4 },
            new TruckTypeDef { id = "mid",   name = "중형", w = 5, h = 5, locked = new[] { 2, 4 }, weight = 4 },
            new TruckTypeDef { id = "large", name = "대형", w = 6, h = 5, locked = new[] { 2, 5 }, weight = 2 },
        };
        // 칸 수별 모양 (주문 품목 총개수 = 칸 수)
        public List<ShapeGroup> shapes = new List<ShapeGroup>
        {
            G(1, S("■", 0, 0)),
            G(2, S("-", 0, 0, 1, 0)),
            G(3, S("-", 0, 0, 1, 0, 2, 0), S("ㄱ", 0, 0, 1, 0, 1, 1), S("ㄴ", 0, 0, 0, 1, 1, 1)),
            G(4, S("-", 0, 0, 1, 0, 2, 0, 3, 0), S("ㄱ", 0, 0, 1, 0, 2, 0, 2, 1), S("ㄴ", 0, 0, 0, 1, 1, 1, 2, 1),
                 S("ㅗ", 1, 0, 0, 1, 1, 1, 2, 1), S("■", 0, 0, 1, 0, 0, 1, 1, 1)),
            G(5, S("+", 1, 0, 0, 1, 1, 1, 2, 1, 1, 2), S("큰ㄱ", 0, 0, 1, 0, 2, 0, 2, 1, 2, 2), S("큰ㄴ", 0, 0, 0, 1, 0, 2, 1, 2, 2, 2)),
        };
        public string[] rankNames = { "E", "D", "C", "B", "A", "S" };
        public int[] rankThresholds = { 2, 4, 6, 8, 10 };
        public double rankBonusPerStep = 0.02;
        public double bonusCellPercent = 5;
        public double fullRate = 200;   // 모든 칸을 다 채웠을 때의 적재율
        public double maxRate = 200;    // 우선 적재 칸 보너스를 더해도 이 값을 넘지 않음
        // 100% 미만: 0.5 + 0.5 x (적재율 / 100)
        public List<MinMult> fillMult = new List<MinMult>
        {
            new MinMult { min = 200, mult = 1.5 }, new MinMult { min = 150, mult = 1.2 }, new MinMult { min = 100, mult = 1.0 },
        };
        public double overPerPoint = 0.01;   // 200% 초과(특수 목표 보너스) 1%p당 배율 +0.01
        public LoadUpgrades upgrades = new LoadUpgrades();
        public ObjectivesCfg objectives = new ObjectivesCfg();
        public AlbaTargetCfg albaTarget = new AlbaTargetCfg();   // 고정 적재율: 전체 칸 중 채우는 비율
        public double stuckDepartDelay = 10;

        public List<ShapeDef> Shapes(int units) => shapes.Find(g => g.units == units).list;

        static ShapeGroup G(int units, params ShapeDef[] list) => new ShapeGroup { units = units, list = new List<ShapeDef>(list) };
        static ShapeDef S(string name, params int[] xy)
        {
            var cells = new Vector2Int[xy.Length / 2];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Vector2Int(xy[i * 2], xy[i * 2 + 1]);
            return new ShapeDef { name = name, cells = cells };
        }
    }
}
