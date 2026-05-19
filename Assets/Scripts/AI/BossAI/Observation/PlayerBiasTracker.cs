using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 플레이어 행동 편향 점수 추적기 (9종, 0.0~1.0)
// 이중 시간축: 15~20초 장기 로그 + 0.5초 단기 재계산 + 이벤트 트리거
public class PlayerBiasTracker : MonoBehaviour
{
    public const int BiasCount = 9;

    [Header("참조")]
    [SerializeField] private BossObservationCollector _collector;

    [Header("거리 기준 (m)")]
    [SerializeField] private float _meleeThreshold    = 12f;
    [SerializeField] private float _rangedThreshold   = 25f;
    [SerializeField] private float _teamCloseThreshold = 10f;
    [SerializeField] private float _teamFarThreshold   = 25f;

    [Header("변화량")]
    [SerializeField] private float _normalRise   =  0.02f;
    [SerializeField] private float _strongRise   =  0.05f;
    [SerializeField] private float _normalFall   = -0.02f;
    [SerializeField] private float _naturalDecay = -0.01f;

    [Header("시간 설정")]
    [SerializeField] private float _updateInterval = 0.5f;
    [SerializeField] private float _logWindow      = 18f;

    private readonly float[] _biases = new float[BiasCount];

    // 행동 로그 (위치 기반)
    private readonly Queue<PositionSample> _positionLog = new();

    // 이벤트 카운터 (최근 윈도우 내)
    private readonly Queue<(float time, EventType type)> _eventLog = new();

    private WaitForSeconds _waitUpdate;

    private struct PositionSample
    {
        public float Time;
        public float DistP1ToBoss;
        public float DistP2ToBoss;
        public float DistP1ToP2;
        public float P1Speed;
        public float P2Speed;
    }

    private enum EventType
    {
        P1Attack, P2Attack,
        P1SkillUse, P2SkillUse,
        P1HitTaken, P2HitTaken,
        P1Evade, P2Evade,
        // 패링/로프: 시스템 구현 시 활성화
        P1Parry, P2Parry,
        P1Rope, P2Rope
    }

    // ══════════════════════════════════════════════════════════
    // 초기화
    // ══════════════════════════════════════════════════════════

    private void Awake()
    {
        if (_collector == null)
            _collector = GetComponent<BossObservationCollector>();
        _waitUpdate = new WaitForSeconds(_updateInterval);
    }

    private void OnEnable()
    {
        StartCoroutine(LogSamplerRoutine());
        StartCoroutine(BiasUpdaterRoutine());
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    // ══════════════════════════════════════════════════════════
    // 외부 이벤트 수신 (이벤트 트리거 즉시 반영)
    // ══════════════════════════════════════════════════════════

    public void NotifyAttack(bool isP1)
    {
        _eventLog.Enqueue((Time.time, isP1 ? EventType.P1Attack : EventType.P2Attack));
        ApplyDelta((int)PlayerBiasType.AttackFocus, _normalRise);
    }

    public void NotifySkillUse(bool isP1)
    {
        _eventLog.Enqueue((Time.time, isP1 ? EventType.P1SkillUse : EventType.P2SkillUse));
        ApplyDelta((int)PlayerBiasType.SkillCentric, _normalRise);
    }

    public void NotifyHitTaken(bool isP1)
    {
        _eventLog.Enqueue((Time.time, isP1 ? EventType.P1HitTaken : EventType.P2HitTaken));
    }

    public void NotifyEvade(bool isP1)
    {
        _eventLog.Enqueue((Time.time, isP1 ? EventType.P1Evade : EventType.P2Evade));
        ApplyDelta((int)PlayerBiasType.SurvivalFirst, _normalRise);
    }

    // 패링 시스템 구현 시 활성화
    public void NotifyParry(bool isP1, bool success)
    {
        _eventLog.Enqueue((Time.time, isP1 ? EventType.P1Parry : EventType.P2Parry));
        float delta = success ? _strongRise : _normalRise;
        ApplyDelta((int)PlayerBiasType.ParryDepend, delta);
    }

    // 로프 시스템 구현 시 활성화
    public void NotifyRopeUse(bool isP1)
    {
        _eventLog.Enqueue((Time.time, isP1 ? EventType.P1Rope : EventType.P2Rope));
        ApplyDelta((int)PlayerBiasType.RopeManeuver, _normalRise);
    }

    // ══════════════════════════════════════════════════════════
    // 코루틴: 위치 샘플링 (0.5초마다)
    // ══════════════════════════════════════════════════════════

    private IEnumerator LogSamplerRoutine()
    {
        while (true)
        {
            yield return _waitUpdate;
            SamplePositions();
            PurgeOldData();
        }
    }

    private void SamplePositions()
    {
        if (_collector == null) return;

        _positionLog.Enqueue(new PositionSample
        {
            Time         = Time.time,
            DistP1ToBoss = _collector.GetDistanceToP1(),
            DistP2ToBoss = _collector.GetDistanceToP2(),
            DistP1ToP2   = _collector.GetP1P2Distance(),
            P1Speed      = _collector.P1AvgSpeed,
            P2Speed      = _collector.P2AvgSpeed
        });
    }

    private void PurgeOldData()
    {
        float cutoff = Time.time - _logWindow;
        while (_positionLog.Count > 0 && _positionLog.Peek().Time < cutoff)
            _positionLog.Dequeue();
        while (_eventLog.Count > 0 && _eventLog.Peek().time < cutoff)
            _eventLog.Dequeue();
    }

    // ══════════════════════════════════════════════════════════
    // 코루틴: 편향 재계산 (0.5초마다)
    // ══════════════════════════════════════════════════════════

    private IEnumerator BiasUpdaterRoutine()
    {
        while (true)
        {
            yield return _waitUpdate;
            RecalculateAllBiases();
        }
    }

    private void RecalculateAllBiases()
    {
        if (_positionLog.Count == 0)
        {
            ApplyNaturalDecay();
            return;
        }

        // 위치 기반 통계
        float meleeRatio  = 0f;
        float rangedRatio = 0f;
        float closeTeamRatio = 0f;
        float farTeamRatio   = 0f;
        int   count = 0;

        foreach (var sample in _positionLog)
        {
            float avgDist = (sample.DistP1ToBoss + sample.DistP2ToBoss) * 0.5f;
            if (avgDist < _meleeThreshold)  meleeRatio  += 1f;
            if (avgDist > _rangedThreshold) rangedRatio += 1f;
            if (sample.DistP1ToP2 < _teamCloseThreshold) closeTeamRatio += 1f;
            if (sample.DistP1ToP2 > _teamFarThreshold)   farTeamRatio   += 1f;
            count++;
        }

        if (count > 0)
        {
            meleeRatio     /= count;
            rangedRatio    /= count;
            closeTeamRatio /= count;
            farTeamRatio   /= count;
        }

        // 이벤트 기반 통계
        int attackCount = 0, skillCount = 0, evadeCount = 0;

        foreach (var (time, type) in _eventLog)
        {
            switch (type)
            {
                case EventType.P1Attack:
                case EventType.P2Attack:
                    attackCount++;
                    break;
                case EventType.P1SkillUse:
                case EventType.P2SkillUse:
                    skillCount++;
                    break;
                case EventType.P1Evade:
                case EventType.P2Evade:
                    evadeCount++;
                    break;
            }
        }

        int totalActions = attackCount + skillCount + evadeCount;
        float attackRatio = totalActions > 0 ? (float)attackCount / totalActions : 0f;
        float skillRatio  = totalActions > 0 ? (float)skillCount  / totalActions : 0f;
        float evadeRatio  = totalActions > 0 ? (float)evadeCount  / totalActions : 0f;

        // 편향 갱신
        // 0: 근접 선호
        float meleeDelta = meleeRatio > 0.6f ? _strongRise : meleeRatio > 0.3f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.MeleePrefer, meleeDelta);

        // 1: 원거리 유지
        float rangeDelta = rangedRatio > 0.6f ? _strongRise : rangedRatio > 0.3f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.RangeKeep, rangeDelta);

        // 2: 공격 집중
        float atkDelta = attackRatio > 0.6f ? _strongRise : attackRatio > 0.3f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.AttackFocus, atkDelta);

        // 3: 생존 우선
        float survDelta = evadeRatio > 0.4f ? _strongRise : evadeRatio > 0.2f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.SurvivalFirst, survDelta);

        // 4: 패링 의존 — 샘플링 기반 fallback (패링 시스템 미구현)
        ApplyDelta((int)PlayerBiasType.ParryDepend, _naturalDecay);

        // 5: 로프 기동 — 샘플링 기반 fallback (로프 시스템 미구현)
        ApplyDelta((int)PlayerBiasType.RopeManeuver, _naturalDecay);

        // 6: 스킬 중심
        float skillDelta = skillRatio > 0.5f ? _strongRise : skillRatio > 0.2f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.SkillCentric, skillDelta);

        // 7: 팀 밀착
        float clusterDelta = closeTeamRatio > 0.6f ? _strongRise : closeTeamRatio > 0.3f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.TeamCluster, clusterDelta);

        // 8: 팀 분산
        float spreadDelta = farTeamRatio > 0.6f ? _strongRise : farTeamRatio > 0.3f ? _normalRise : _normalFall;
        ApplyDelta((int)PlayerBiasType.TeamSpread, spreadDelta);
    }

    private void ApplyNaturalDecay()
    {
        for (int i = 0; i < BiasCount; i++)
            _biases[i] = Mathf.Clamp01(_biases[i] + _naturalDecay);
    }

    private void ApplyDelta(int index, float delta)
    {
        _biases[index] = Mathf.Clamp01(_biases[index] + delta);
    }

    // ══════════════════════════════════════════════════════════
    // 외부 조회
    // ══════════════════════════════════════════════════════════

    public float GetBias(PlayerBiasType type) => _biases[(int)type];

    public float GetBias(int index) => index >= 0 && index < BiasCount ? _biases[index] : 0f;

    public void GetAllBiases(float[] output)
    {
        int len = Mathf.Min(output.Length, BiasCount);
        System.Array.Copy(_biases, output, len);
    }

    public void ResetAll()
    {
        for (int i = 0; i < BiasCount; i++)
            _biases[i] = 0f;
        _positionLog.Clear();
        _eventLog.Clear();
    }
}

// ══════════════════════════════════════════════════════════════
// 편향 타입 열거형
// ══════════════════════════════════════════════════════════════
public enum PlayerBiasType
{
    MeleePrefer   = 0,  // 근접 선호
    RangeKeep     = 1,  // 원거리 유지
    AttackFocus   = 2,  // 공격 집중
    SurvivalFirst = 3,  // 생존 우선
    ParryDepend   = 4,  // 패링 의존 (미구현 — 0 고정)
    RopeManeuver  = 5,  // 로프 기동 (미구현 — 0 고정)
    SkillCentric  = 6,  // 스킬 중심
    TeamCluster   = 7,  // 팀 밀착
    TeamSpread    = 8   // 팀 분산
}
