// APCenter 목업 - 설정값
// 최종목업기획.txt 에서 [임의]로 표기한 수치는 전부 여기서 바꾼다.

const CONFIG = {
  startMoney: 500,

  // 낮/밤
  phaseSeconds: 600,      // 낮/밤 전환 주기 (실시간 초, 기본 10분)
  dayStartHour: 6,        // 낮 시작 시각 (시계 표기용)

  player: { speed: 7, capacity: 4 },
  interactRadius: 1.6,    // 상호작용 범위 (바닥 원으로 표시)
  interactDelay: 0.2,     // 범위에 들어간 뒤 작업 시작까지 (스쳐 지나갈 때 발동 방지)

  timing: {
    handle: 0.25,         // 물건 1개 집기/내려놓기
    inspect: 1.0,         // 검수 1개
    pack: 1.0,            // 포장 1회
    loadBox: 1.2,         // 알바 상차 박스 1개
  },

  debug: { moneyStep: 1000 },   // 디버그 패널 돈 치트 단위

  inbound: { returnDelay: 8, retryDelay: 2, minItems: 12, maxItems: 20 },

  // 물류 시설 업그레이드: values[레벨] = 효과, costs[레벨] = 그 레벨로 올리는 비용
  facility: {
    parking: { name: '하차 주차공간', emoji: '🅿️', unit: '대', desc: '동시에 들어올 수 있는 입고 트럭', values: [1, 2, 3], costs: [0, 400, 900] },
    staging: { name: '하차장 크기', emoji: '📥', unit: '칸', desc: '하차장(임시 보관소) 최대 보관량', values: [30, 45, 60, 80], costs: [0, 200, 400, 700] },
    bin:     { name: '보관함 최대 보관량', emoji: '🗄️', unit: '개', desc: '모든 보관함의 품목당 최대 보관량', values: [20, 30, 40, 55, 70], costs: [0, 300, 600, 1000, 1500] },
  },
  breakChance: { inspect: 0.05, carry: 0.05 },

  orders: {
    interval: 25,         // 주문 생성 주기 (초)
    firstDelay: 2,
    maxUnits: 5,          // 주문 1건 품목 총개수 최대 (= 박스 최대 칸 수)
    maxKinds: 3,
    baseReward: 10,
    priceMult: 2,         // 보상 = 기본 + 품목가격 x 수량 x 배수
  },

  event: { name: '명절 물류 폭주!', chance: 0.35, rushOrders: 10, intervalMult: 0.5 },

  types: {
    food:  { name: '식품',   emoji: '🍱', color: 0xe67e22 },
    daily: { name: '생필품', emoji: '🧼', color: 0x3498db },
    cloth: { name: '의류',   emoji: '👚', color: 0x9b59b6 },
    elec:  { name: '가전',   emoji: '🔌', color: 0x607d8b },
    book:  { name: '도서',   emoji: '📚', color: 0x27ae60 },
  },

  // 보관함 1개 = 품목 1종. cost 0 = 시작부터 해금
  items: [
    { id: 'ramen',   name: '라면',   emoji: '🍜', type: 'food',  price: 8,  cost: 0 },
    { id: 'tissue',  name: '휴지',   emoji: '🧻', type: 'daily', price: 6,  cost: 0 },
    { id: 'water',   name: '생수',   emoji: '💧', type: 'food',  price: 5,  cost: 0 },
    { id: 'shampoo', name: '샴푸',   emoji: '🧴', type: 'daily', price: 10, cost: 200 },
    { id: 'tshirt',  name: '티셔츠', emoji: '👕', type: 'cloth', price: 14, cost: 300 },
    { id: 'socks',   name: '양말',   emoji: '🧦', type: 'cloth', price: 7,  cost: 300 },
    { id: 'novel',   name: '소설책', emoji: '📕', type: 'book',  price: 12, cost: 400 },
    { id: 'earbuds', name: '이어폰', emoji: '🎧', type: 'elec',  price: 25, cost: 500 },
  ],

  // 출고 도크 1개 = 목적지 1곳
  destinations: [
    { id: 'seoul',   name: '서울', color: 0xc0392b, cost: 0 },
    { id: 'incheon', name: '인천', color: 0x2980b9, cost: 400 },
    { id: 'daegu',   name: '대구', color: 0x16a085, cost: 700 },
    { id: 'busan',   name: '부산', color: 0x8e44ad, cost: 1000 },
  ],

  packTableCosts: [0, 300, 600],

  // costs[i] = Lv(i+1)로 올리는 비용. 4번째 값이 있으면 Lv4 = 자동화
  stages: [
    { id: 'unload',   name: '하차',     emoji: '🚚', costs: [0, 150, 300] },
    { id: 'sort',     name: '분류',     emoji: '🗂️', costs: [0, 150, 300, 800] },
    { id: 'inspect',  name: '검수',     emoji: '🔍', costs: [0, 150, 300, 800] },
    { id: 'transfer', name: '이송',     emoji: '🛒', costs: [0, 200, 400, 1000] },
    { id: 'pack',     name: '포장',     emoji: '🎁', costs: [0, 200, 400, 1000] },
    { id: 'outbound', name: '출고분류', emoji: '📤', costs: [0, 200, 400, 1000] },
    { id: 'load',     name: '상차',     emoji: '🚛', costs: [0, 250, 500, 1200] },
  ],

  care: {
    tierNames: ['낡은', '평범한', '고급', '최고급'],
    rest: { costs: [0, 300, 700, 1500], recover: [4, 7, 11, 16], until: [20, 25, 30, 35], minRest: 3 },
    purifier: { costs: [300, 300, 700, 1500], chance: [0.1, 0.2, 0.3, 0.45] },
  },

  alba: {
    max: 12,
    candidates: 3,
    rerollCost: 30,
    statMin: 1, statMax: 5,
    wageBase: 2, wagePerStat: 0.5, wageJitter: 2,   // 시급(1시간) = 기본 + 스탯합 x 배수 + 0~편차
    hoursPerPhase: 12,                              // 낮/밤이 바뀔 때 12시간분 지급
    unpaidMtMult: 2,
    fleeStep: 0.1,
  },

  mt: {
    min: -100, max: 100, start: 50, perWork: 2,
    // 작업 능률 (이동속도, 작업속도 배율)
    effect: [
      { min: 50, mult: 1.15 },
      { min: 0, mult: 1.0 },
      { min: -50, mult: 0.85 },
      { min: -Infinity, mult: 0.7 },
    ],
    // 작업 후 휴식 확률 (MT가 0~20 사이에 머물도록 조정)
    restChance: [
      { min: 20, p: 0.005 },
      { min: 0, p: 0.03 },
      { min: -50, p: 0.5 },
      { min: -Infinity, p: 0.6 },
    ],
  },

  snacks: [
    { id: 'candy', emoji: '🍬', name: '사탕', mt: 10 },
    { id: 'choco', emoji: '🍫', name: '초콜릿', mt: 20 },
    { id: 'lunch', emoji: '🍱', name: '도시락', mt: 40 },
  ],

  automation: {
    sortInterval: 0.6,
    inspectTime: 1.0,
    transferInterval: 0.5,
    packTime: 1.5,
    outboundInterval: 0.8,
    loadInterval: 1.0,
    loadTarget: 0.9,     // 전체 칸 중 채우는 비율 (적재율 180%)
    flySpeed: 7,
  },

  // 상차 미니게임 (차곡차곡 이식)
  loading: {
    truckReturnDelay: 10,
    // locked: [최소, 최대] 잠긴 칸 개수. 트럭이 올 때마다 개수와 위치를 랜덤으로 뽑는다
    truckTypes: [
      { id: 'small', name: '소형', w: 5, h: 4, locked: [1, 3], weight: 4 },
      { id: 'mid',   name: '중형', w: 5, h: 5, locked: [2, 4], weight: 4 },
      { id: 'large', name: '대형', w: 6, h: 5, locked: [2, 5], weight: 2 },
    ],
    // 칸 수별 모양 (주문 품목 총개수 = 칸 수)
    shapes: {
      1: [{ name: '■', cells: [[0, 0]] }],
      2: [{ name: '-', cells: [[0, 0], [1, 0]] }],
      3: [
        { name: '-', cells: [[0, 0], [1, 0], [2, 0]] },
        { name: 'ㄱ', cells: [[0, 0], [1, 0], [1, 1]] },
        { name: 'ㄴ', cells: [[0, 0], [0, 1], [1, 1]] },
      ],
      4: [
        { name: '-', cells: [[0, 0], [1, 0], [2, 0], [3, 0]] },
        { name: 'ㄱ', cells: [[0, 0], [1, 0], [2, 0], [2, 1]] },
        { name: 'ㄴ', cells: [[0, 0], [0, 1], [1, 1], [2, 1]] },
        { name: 'ㅗ', cells: [[1, 0], [0, 1], [1, 1], [2, 1]] },
        { name: '■', cells: [[0, 0], [1, 0], [0, 1], [1, 1]] },
      ],
      5: [
        { name: '+', cells: [[1, 0], [0, 1], [1, 1], [2, 1], [1, 2]] },
        { name: '큰ㄱ', cells: [[0, 0], [1, 0], [2, 0], [2, 1], [2, 2]] },
        { name: '큰ㄴ', cells: [[0, 0], [0, 1], [0, 2], [1, 2], [2, 2]] },
      ],
    },
    rankNames: ['E', 'D', 'C', 'B', 'A', 'S'],
    rankThresholds: [2, 4, 6, 8, 10],
    rankBonusPerStep: 0.02,
    bonusCellPercent: 5,
    fullRate: 200,         // 모든 칸을 다 채웠을 때의 적재율
    maxRate: 200,          // 우선 적재 칸 보너스를 더해도 이 값을 넘지 않음
    // 100% 미만: 0.5 + 0.5 x (적재율 / 100)
    fillMult: [
      { min: 200, mult: 1.5 },
      { min: 150, mult: 1.2 },
      { min: 100, mult: 1.0 },
    ],
    overPerPoint: 0.01,    // 200% 초과(특수 목표 보너스) 1%p당 배율 +0.01
    upgrades: {
      compress: { name: '압축 적재', emoji: '🗜️', max: 5, costs: [150, 250, 400, 600, 900] },
      bonus:    { name: '우선 적재 칸', emoji: '⭐', max: 5, costs: [150, 250, 400, 600, 900] },
    },
    // 특수 목표: 트럭마다 1~5개 (수동 상차만)
    // 달성한 목표의 보너스(%p)를 적재율에 더한다. 이 보너스는 maxRate(200%) 상한을 넘을 수 있다
    objectives: {
      count: [1, 5],
      rank: { ranks: [1, 2, 3], bonus: [0, 5, 10, 15], bigTruckCells: 25 },  // D/C/B, B는 큰 트럭만
      fill: [{ rate: 100, bonus: 5 }, { rate: 150, bonus: 10 }],   // 칸 채우기 적재율 기준 (목표 보너스 제외)
      boxes: { min: 3, perCells: 4, bonusPer: 2 },         // 박스 N개 이상 (최대 N = 칸 수 / 4), 개당 +2%p
      item: { bonus: 5 },                                  // 특정 품목이 든 박스 싣기
      kinds: [{ n: 2, bonus: 5 }, { n: 3, bonus: 10 }],    // 서로 다른 타입 N종 이상
    },
    albaTarget: { min: 0.6, max: 0.9 },   // 고정 적재율: 전체 칸 중 채우는 비율 (0.6 = 적재율 120%)
    stuckDepartDelay: 10,
  },
};

// 맵 배치는 js/layout.js (LayoutLib.makeDefault, 배치 시뮬레이터에서 편집)
