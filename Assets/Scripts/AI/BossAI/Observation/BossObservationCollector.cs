using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents.Sensors;

// Phase 3+ 보스 ML-Agent 용 관측값 중앙 수집기 (33개)
// Phase 1/2 에이전트(BossAgent, DualTargetAgent)는 자체 CollectObservations 사용
// Phase 3+ 에이전트가 CollectObservations 에서 CollectUpToPhaseN(sensor) 호출
public class BossObservationCollector : MonoBehaviour
{
    public const int Phase3Size = 28;
    public const int Phase4Size = 34;
    public const int Phase5Size = 43;
    public const int FullObsSize = 129;
    private const int BossSkillSlots = 5;
    private const int ChannelsPerSlot = 7;
    private const float TouchRange = 5f;

    [Header("플레이어 참조")]
    [SerializeField] private GameObject _p1;
    [SerializeField] private GameObject _p2;

    [Header("보스 참조")]
    [SerializeField] private BossController _bossController;
    [SerializeField] private StatManager    _bossStatManager;
    [SerializeField] private SkillExecutor  _skillExecutor;

    [Header("보스 스킬 슬롯 (5종)")]
    [SerializeField] private SkillDefinition[] _bossSkills = new SkillDefinition[BossSkillSlots];

    [Header("관측 설정")]
    [SerializeField] private float _maxDistance  = 55f;
    [SerializeField] private float _maxCooldown  = 30f;
    [SerializeField] private float _maxBurstDmg  = 120f;
    [SerializeField] private float _maxSpeed     = 16f;
    [SerializeField] private int   _maxParryCount = 5;
    [SerializeField] private int   _maxBossPhase  = 4;

    // P1/P2 컴포넌트 캐시
    private StatManager  _p1StatManager;
    private StatManager  _p2StatManager;
    private StateManager _p1StateManager;
    private StateManager _p2StateManager;
    private SkillManager _p1SkillManager;
    private SkillManager _p2SkillManager;
    private SkillExecutor _p1SkillExecutor;
    private SkillExecutor _p2SkillExecutor;
    private TrainingSkillManager _p1TrainingSkillMgr;
    private TrainingSkillManager _p2TrainingSkillMgr;

    // Phase 4 추적 버퍼
    private float _prevBossHp;
    private readonly Queue<(float time, float damage)> _damageLog = new();
    private float _recentDamageSum;

    private Vector3 _prevP1Pos;
    private Vector3 _prevP2Pos;
    private float   _p1AvgSpeed;
    private float   _p2AvgSpeed;
    private bool    _initialized;
    private int     _unlockedSlotCount = BossSkillSlots;

    private const float SpeedSmooth = 0.15f;

    // ══════════════════════════════════════════════════════════
    // 초기화
    // ══════════════════════════════════════════════════════════

    private void Awake()
    {
        if (_bossController == null) _bossController = GetComponent<BossController>();
        if (_bossStatManager == null) _bossStatManager = GetComponent<StatManager>();
        if (_skillExecutor == null) _skillExecutor = GetComponent<SkillExecutor>();
    }

    private void Start()
    {
        CachePlayerComponents();
    }

    private void CachePlayerComponents()
    {
        if (_p1 != null)
        {
            _p1StatManager      = _p1.GetComponent<StatManager>();
            _p1StateManager     = _p1.GetComponent<StateManager>();
            _p1SkillManager     = _p1.GetComponent<SkillManager>();
            _p1SkillExecutor    = _p1.GetComponent<SkillExecutor>();
            _p1TrainingSkillMgr = _p1.GetComponent<TrainingSkillManager>();
            _prevP1Pos          = _p1.transform.position;
        }
        if (_p2 != null)
        {
            _p2StatManager      = _p2.GetComponent<StatManager>();
            _p2StateManager     = _p2.GetComponent<StateManager>();
            _p2SkillManager     = _p2.GetComponent<SkillManager>();
            _p2SkillExecutor    = _p2.GetComponent<SkillExecutor>();
            _p2TrainingSkillMgr = _p2.GetComponent<TrainingSkillManager>();
            _prevP2Pos          = _p2.transform.position;
        }

        if (_bossStatManager != null)
            _prevBossHp = _bossStatManager.GetHP();

        EnsureSkillArrays();
        _initialized = true;
    }

    public void SetPlayers(GameObject p1, GameObject p2)
    {
        _p1 = p1;
        _p2 = p2;
        CachePlayerComponents();
    }

    public void SetBossSkill(int slot, SkillDefinition skill)
    {
        EnsureSkillArrays();
        if (slot >= 0 && slot < BossSkillSlots)
            _bossSkills[slot] = skill;
    }

    public void SetUnlockedSlotCount(int count)
    {
        _unlockedSlotCount = Mathf.Clamp(count, 0, BossSkillSlots);
    }

    private void EnsureSkillArrays()
    {
        if (_bossSkills == null || _bossSkills.Length < BossSkillSlots)
            System.Array.Resize(ref _bossSkills, BossSkillSlots);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureSkillArrays();
    }
#endif

    // ══════════════════════════════════════════════════════════
    // 매 프레임 추적 (Phase 4 데이터)
    // ══════════════════════════════════════════════════════════

    private void Update()
    {
        if (!_initialized) return;
        TrackBurstDamage();
        TrackMovementSpeed();
    }

    private void TrackBurstDamage()
    {
        if (_bossStatManager == null) return;

        float currentHp = _bossStatManager.GetHP();
        float delta     = _prevBossHp - currentHp;
        if (delta > 0f)
        {
            _damageLog.Enqueue((Time.time, delta));
            _recentDamageSum += delta;
        }
        _prevBossHp = currentHp;

        while (_damageLog.Count > 0 && Time.time - _damageLog.Peek().time > 1f)
            _recentDamageSum -= _damageLog.Dequeue().damage;

        if (_recentDamageSum < 0f) _recentDamageSum = 0f;
    }

    private void TrackMovementSpeed()
    {
        if (_p1 != null)
        {
            float s = Vector3.Distance(_p1.transform.position, _prevP1Pos) / Mathf.Max(Time.deltaTime, 0.001f);
            _p1AvgSpeed = Mathf.Lerp(_p1AvgSpeed, s, SpeedSmooth);
            _prevP1Pos  = _p1.transform.position;
        }
        if (_p2 != null)
        {
            float s = Vector3.Distance(_p2.transform.position, _prevP2Pos) / Mathf.Max(Time.deltaTime, 0.001f);
            _p2AvgSpeed = Mathf.Lerp(_p2AvgSpeed, s, SpeedSmooth);
            _prevP2Pos  = _p2.transform.position;
        }
    }

    // ══════════════════════════════════════════════════════════
    // 관측 수집 — 129ch 통합 (BossInferenceAgent 용)
    // ══════════════════════════════════════════════════════════

    public void CollectFull129(VectorSensor sensor)
    {
        EnsureSkillArrays();
        Vector3 bossPos = transform.position;
        Vector3 fwd     = transform.forward;

        // ── Section 1: Position (11ch, #0-10) ────────────────
        float distP1 = 0f, distP2 = 0f;
        if (_p1 != null)
        {
            Vector3 toP1 = _p1.transform.position - bossPos;
            distP1 = toP1.magnitude;
            Vector3 dirP1 = distP1 > 0.001f ? toP1.normalized : Vector3.forward;
            sensor.AddObservation(dirP1.x);                              // #0
            sensor.AddObservation(dirP1.z);                              // #1
            sensor.AddObservation(Mathf.Clamp01(distP1 / _maxDistance)); // #2
            sensor.AddObservation(fwd.x);                                // #3
            sensor.AddObservation(fwd.z);                                // #4
            sensor.AddObservation(Vector3.Dot(fwd, dirP1));              // #5
        }
        else
        {
            for (int i = 0; i < 6; i++) sensor.AddObservation(0f);
        }

        if (_p1 != null && _p2 != null)
        {
            Vector3 toP2 = _p2.transform.position - bossPos;
            distP2 = toP2.magnitude;
            Vector3 dirP2 = distP2 > 0.001f ? toP2.normalized : Vector3.forward;
            float distPP = Vector3.Distance(_p1.transform.position, _p2.transform.position);
            sensor.AddObservation(dirP2.x);                              // #6
            sensor.AddObservation(dirP2.z);                              // #7
            sensor.AddObservation(Mathf.Clamp01(distP2 / _maxDistance)); // #8
            sensor.AddObservation(Mathf.Clamp01(distPP / _maxDistance)); // #9
            sensor.AddObservation(Vector3.Dot(fwd, dirP2));              // #10
        }
        else
        {
            for (int i = 0; i < 5; i++) sensor.AddObservation(0f);
        }

        // ── Section 2: Boss state (4ch, #11-14) ─────────────
        sensor.AddObservation(_bossStatManager != null ? _bossStatManager.GetHPPercent() : 0f);  // #11
        sensor.AddObservation(_p1StatManager   != null ? _p1StatManager.GetHPPercent()   : 0f);  // #12
        sensor.AddObservation(_p2StatManager   != null ? _p2StatManager.GetHPPercent()   : 0f);  // #13
        int phase = _bossController != null ? _bossController.CurrentPhase : 0;
        sensor.AddObservation(Mathf.Clamp01((float)(phase + 1) / _maxBossPhase));                // #14

        // ── Section 3: Boss slots (35ch, #15-49) ────────────
        for (int i = 0; i < BossSkillSlots; i++)
            AddSlotObservation(sensor, i < _bossSkills.Length ? _bossSkills[i] : null, _skillExecutor);

        // ── Section 4: Touch (2ch, #50-51) ──────────────────
        sensor.AddObservation(distP1 < TouchRange ? 1f : 0f);  // #50
        sensor.AddObservation(distP2 < TouchRange ? 1f : 0f);  // #51

        // ── Section 5: P1 slots (35ch, #52-86) ─────────────
        AddPlayerSlots(sensor, _p1SkillManager, _p1SkillExecutor);

        // ── Section 5: P2 slots (35ch, #87-121) ────────────
        AddPlayerSlots(sensor, _p2SkillManager, _p2SkillExecutor);

        // ── Section 6: Extra (7ch, #122-128) ────────────────
        bool p1Casting = _p1StatManager != null && _p1StatManager.IsCasting;
        bool p2Casting = _p2StatManager != null && _p2StatManager.IsCasting;
        sensor.AddObservation(p1Casting ? 1f : 0f);                          // #122
        sensor.AddObservation(p2Casting ? 1f : 0f);                          // #123
        sensor.AddObservation(Mathf.Clamp01(_p1AvgSpeed / _maxSpeed));       // #124
        sensor.AddObservation(Mathf.Clamp01(_p2AvgSpeed / _maxSpeed));       // #125
        int p1Unlocked = _p1TrainingSkillMgr != null ? _p1TrainingSkillMgr.UnlockedCount : 0;
        int p2Unlocked = _p2TrainingSkillMgr != null ? _p2TrainingSkillMgr.UnlockedCount : 0;
        sensor.AddObservation(Mathf.Clamp01((float)p1Unlocked / BossSkillSlots)); // #126
        sensor.AddObservation(Mathf.Clamp01((float)p2Unlocked / BossSkillSlots)); // #127
        sensor.AddObservation(Mathf.Clamp01(_recentDamageSum / _maxBurstDmg));    // #128
    }

    private void AddSlotObservation(VectorSensor sensor, SkillDefinition skill, SkillExecutor executor)
    {
        if (skill == null)
        {
            for (int c = 0; c < ChannelsPerSlot; c++) sensor.AddObservation(0f);
            return;
        }

        float remCD = executor != null ? executor.GetRemainingCooldown(skill) : 0f;
        sensor.AddObservation(Mathf.Clamp01(remCD / _maxCooldown));          // +0 remCD
        sensor.AddObservation(Mathf.Clamp01(skill.Cooldown / _maxCooldown)); // +1 maxCD
        sensor.AddObservation(Mathf.Clamp01(skill.Range / _maxDistance));    // +2 range

        float coneAoe = 0f;
        if (skill.AIHint_Category == SkillCategoryFlag.Directional)
            coneAoe = skill.AIHint_ConeOrAoE / 180f;
        else if (skill.AIHint_Category == SkillCategoryFlag.AoE)
            coneAoe = skill.AIHint_ConeOrAoE / 25f;
        sensor.AddObservation(Mathf.Clamp01(coneAoe));                       // +3 coneOrAoE

        sensor.AddObservation(skill.AIHint_Category == SkillCategoryFlag.Directional ? 1f : 0f); // +4
        sensor.AddObservation(skill.AIHint_Category == SkillCategoryFlag.AoE         ? 1f : 0f); // +5
        sensor.AddObservation(skill.AIHint_Category == SkillCategoryFlag.Projectile  ? 1f : 0f); // +6
    }

    private void AddPlayerSlots(VectorSensor sensor, SkillManager skillMgr, SkillExecutor executor)
    {
        for (int i = 0; i < BossSkillSlots; i++)
        {
            SkillDefinition skill = (skillMgr != null && i < skillMgr.Slots.Count)
                ? skillMgr.Slots[i] : null;
            AddSlotObservation(sensor, skill, executor);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 관측 수집 — Phase 별 누적 (Legacy)
    // ══════════════════════════════════════════════════════════

    [System.Obsolete("CollectFull129 사용")]
    public void CollectUpToPhase3(VectorSensor sensor)
    {
        CollectPhase1(sensor);
        CollectPhase2(sensor);
        CollectPhase3(sensor);
    }

    [System.Obsolete("CollectFull129 사용")]
    public void CollectUpToPhase4(VectorSensor sensor)
    {
        CollectUpToPhase3(sensor);
        CollectPhase4(sensor);
    }

    [System.Obsolete("CollectFull129 사용")]
    public void CollectUpToPhase5(VectorSensor sensor)
    {
        CollectUpToPhase4(sensor);
        CollectPhase5(sensor);
    }

    // ── Phase 1 (#0-5): P1 방향/거리 + 보스 전방 ────────────
    private void CollectPhase1(VectorSensor sensor)
    {
        if (_p1 == null)
        {
            sensor.AddObservation(new float[6]);
            return;
        }

        Vector3 bossPos = transform.position;
        Vector3 fwd     = transform.forward;
        Vector3 toP1    = _p1.transform.position - bossPos;
        float   distP1  = toP1.magnitude;
        Vector3 dirP1   = distP1 > 0.001f ? toP1.normalized : Vector3.forward;

        sensor.AddObservation(dirP1.x);                                 // #0
        sensor.AddObservation(dirP1.z);                                 // #1
        sensor.AddObservation(Mathf.Clamp01(distP1 / _maxDistance));    // #2
        sensor.AddObservation(fwd.x);                                   // #3
        sensor.AddObservation(fwd.z);                                   // #4
        sensor.AddObservation(Vector3.Dot(fwd, dirP1));                 // #5
    }

    // ── Phase 2 (#6-10): P2 방향/거리 + P1↔P2 거리 ─────────
    private void CollectPhase2(VectorSensor sensor)
    {
        if (_p1 == null || _p2 == null)
        {
            sensor.AddObservation(new float[5]);
            return;
        }

        Vector3 bossPos = transform.position;
        Vector3 fwd     = transform.forward;
        Vector3 toP2    = _p2.transform.position - bossPos;
        float   distP2  = toP2.magnitude;
        Vector3 dirP2   = distP2 > 0.001f ? toP2.normalized : Vector3.forward;
        float   distPP  = Vector3.Distance(_p1.transform.position, _p2.transform.position);

        sensor.AddObservation(dirP2.x);                                 // #6
        sensor.AddObservation(dirP2.z);                                 // #7
        sensor.AddObservation(Mathf.Clamp01(distP2 / _maxDistance));    // #8
        sensor.AddObservation(Mathf.Clamp01(distPP / _maxDistance));    // #9
        sensor.AddObservation(Vector3.Dot(fwd, dirP2));                 // #10
    }

    // ── Phase 3 (#11-18): HP + 스킬 쿨다운 + 페이즈 + 해금 슬롯 ──
    private void CollectPhase3(VectorSensor sensor)
    {
        float bossHp = _bossStatManager != null ? _bossStatManager.GetHPPercent() : 0f;
        float p1Hp   = _p1StatManager   != null ? _p1StatManager.GetHPPercent()   : 0f;
        float p2Hp   = _p2StatManager   != null ? _p2StatManager.GetHPPercent()   : 0f;

        sensor.AddObservation(bossHp);  // #11
        sensor.AddObservation(p1Hp);    // #12
        sensor.AddObservation(p2Hp);    // #13

        for (int i = 0; i < BossSkillSlots; i++)
        {
            if (_skillExecutor != null && i < _bossSkills.Length && _bossSkills[i] != null)
            {
                float remaining = _skillExecutor.GetRemainingCooldown(_bossSkills[i]);
                float total     = _bossSkills[i].Cooldown;
                sensor.AddObservation(total > 0f ? Mathf.Clamp01(remaining / total) : 0f);  // #14-16
            }
            else
            {
                sensor.AddObservation(0f);
            }
        }

        int phase    = _bossController != null ? _bossController.CurrentPhase : 0;
        int maxPhase = _maxBossPhase > 0 ? _maxBossPhase : 1;
        sensor.AddObservation(Mathf.Clamp01((float)phase / maxPhase));  // #17

        sensor.AddObservation(
            Mathf.Clamp01((float)_unlockedSlotCount / BossSkillSlots));  // #18

        for (int i = 0; i < BossSkillSlots; i++)
        {
            float range = (i < _bossSkills.Length && _bossSkills[i] != null)
                ? _bossSkills[i].Range : 0f;
            sensor.AddObservation(Mathf.Clamp01(range / _maxDistance));  // #19-21
        }

        float maxCd = _maxCooldown > 0f ? _maxCooldown : 1f;
        int targetTypeCount = System.Enum.GetValues(typeof(TargetType)).Length;
        float ttDivisor = Mathf.Max(targetTypeCount - 1, 1);

        for (int i = 0; i < BossSkillSlots; i++)
        {
            if (i < _bossSkills.Length && _bossSkills[i] != null)
            {
                sensor.AddObservation(Mathf.Clamp01(_bossSkills[i].Cooldown / maxCd));     // #22-24
                sensor.AddObservation((float)_bossSkills[i].TargetType / ttDivisor);        // #25-27
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }
    }

    // ── Phase 4 (#28-33): 패링/버스트/이속/캐스팅 ───────────
    private void CollectPhase4(VectorSensor sensor)
    {
        // #22 — recentParryCount (패링 시스템 미구현 → 0 고정)
        sensor.AddObservation(0f);

        // #23 — recentBurstDamage
        sensor.AddObservation(Mathf.Clamp01(_recentDamageSum / _maxBurstDmg));

        // #24-25 — p1/p2 평균 이동속도
        sensor.AddObservation(Mathf.Clamp01(_p1AvgSpeed / _maxSpeed));
        sensor.AddObservation(Mathf.Clamp01(_p2AvgSpeed / _maxSpeed));

        // #26-27 — p1/p2 캐스팅 여부
        bool p1Casting = _p1StatManager != null && _p1StatManager.IsCasting;
        bool p2Casting = _p2StatManager != null && _p2StatManager.IsCasting;
        sensor.AddObservation(p1Casting ? 1f : 0f);
        sensor.AddObservation(p2Casting ? 1f : 0f);
    }

    // ── Phase 5 (#28-36): 스킬 이력/명중률/사용 비율 ────────
    private void CollectPhase5(VectorSensor sensor)
    {
        // #28-30 — lastSkill[0~2]: 최근 3회 스킬 (슬롯 인덱스 정규화)
        string[] lastIds = _skillExecutor != null
            ? _skillExecutor.GetLastNSkillIds(3)
            : new string[3];

        for (int i = 0; i < 3; i++)
        {
            int slotIdx = SkillIdToSlotIndex(lastIds[i]);
            sensor.AddObservation(slotIdx >= 0 ? (float)slotIdx / Mathf.Max(1, BossSkillSlots - 1) : 0f);
        }

        // #31-33 — skillHitRate[0~2]
        for (int i = 0; i < BossSkillSlots; i++)
        {
            float rate = 0f;
            if (_skillExecutor != null && i < _bossSkills.Length && _bossSkills[i] != null)
                rate = _skillExecutor.GetHitRate(_bossSkills[i].SkillId);
            sensor.AddObservation(rate);
        }

        // #34-36 — skillUseCount[0~2] (사용 비율)
        int totalUses = _skillExecutor != null ? _skillExecutor.TotalUseCount : 0;
        for (int i = 0; i < BossSkillSlots; i++)
        {
            float ratio = 0f;
            if (totalUses > 0 && _skillExecutor != null && i < _bossSkills.Length && _bossSkills[i] != null)
                ratio = (float)_skillExecutor.GetUseCount(_bossSkills[i].SkillId) / totalUses;
            sensor.AddObservation(ratio);
        }
    }

    // ══════════════════════════════════════════════════════════
    // 외부 조회 (PlayerBiasTracker 등에서 사용)
    // ══════════════════════════════════════════════════════════

    public float GetDistanceToP1()
    {
        if (_p1 == null) return _maxDistance;
        return Vector3.Distance(transform.position, _p1.transform.position);
    }

    public float GetDistanceToP2()
    {
        if (_p2 == null) return _maxDistance;
        return Vector3.Distance(transform.position, _p2.transform.position);
    }

    public float GetP1P2Distance()
    {
        if (_p1 == null || _p2 == null) return _maxDistance;
        return Vector3.Distance(_p1.transform.position, _p2.transform.position);
    }

    public float P1AvgSpeed => _p1AvgSpeed;
    public float P2AvgSpeed => _p2AvgSpeed;
    public float RecentBurstDamage => _recentDamageSum;

    public GameObject P1 => _p1;
    public GameObject P2 => _p2;

    // ── 내부 ─────────────────────────────────────────────────

    private int SkillIdToSlotIndex(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return -1;
        for (int i = 0; i < _bossSkills.Length; i++)
        {
            if (_bossSkills[i] != null && _bossSkills[i].SkillId == skillId)
                return i;
        }
        return -1;
    }
}
