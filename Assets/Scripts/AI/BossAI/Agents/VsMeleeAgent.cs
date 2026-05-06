using System.Collections.Generic;
using Unity.Behavior;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.AI;

// Phase 3 — SkillIntro (VsMelee 특화)
// MeleeAggro 플레이스타일 대응 특화 에이전트
// 근거리 교전 보상 강화 · 반격 보상 · 후퇴 억제
//
// BehaviorParameters:
//   Behavior Name           : SkillIntro_VsMelee
//   Vector Observation Size : 55 (Phase3 28 + touch 2 + playerSkills 18 + extra 7)
//   Discrete Branches       : 2 (B0=4 이동, B1=4 스킬)
//   Max Step                : 8000 (권장)
public class VsMeleeAgent : Agent
{
    private const int TouchObsCount = 2;
    private const int PlayerSkillObsCount = 18;
    private const int PlayerSkillSlots = 3;
    private const int ExtraObsCount = 7;

    [Header("이동")]
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _rotationSpeed = 200f;

    [Header("스폰 설정")]
    [SerializeField] private Transform _bossSpawnPoint;
    [SerializeField] private GameObject _p1Object;
    [SerializeField] private GameObject _p2Object;
    [SerializeField] private List<Transform> _playerSpawnPoints;

    [Header("참조")]
    [SerializeField] private BossController _bossController;
    [SerializeField] private BossObservationCollector _collector;
    [SerializeField] private TrainingSkillManager _trainingSkillManager;
    [SerializeField] private SkillManager _skillManager;
    [SerializeField] private SkillExecutor _skillExecutor;

    [Header("플레이어 스킬 분배")]
    [SerializeField] private TrainingSkillManager _p1TrainingSkillMgr;
    [SerializeField] private TrainingSkillManager _p2TrainingSkillMgr;

    [Header("스킬풀 랜덤 분배")]
    [SerializeField] private SkillPoolSO[] _bossSkillPools;
    [SerializeField] private PlayerProfile[] _playerProfiles;
    [SerializeField] private int _statsLogInterval = 50;

    [System.Serializable]
    public struct PlayerProfile
    {
        public SkillPoolSO SkillPool;
        public BehaviorGraph MoveGraph;

        [Header("이동 파라미터")]
        public float DangerRange;
        public float OptimalMin;
        public float OptimalMax;
        public float FleeDistance;
        public float FlankRadius;
        public float StrafeRadius;
        public float StrafeAngleStep;
        public float MinSpacing;

        [Header("거리 전환 (AdjustRange용)")]
        public float AttackRangeMin;
        public float AttackRangeMax;
        public float SafeRangeMin;
        public float SafeRangeMax;

        [Header("협공 (IsFlankAngleLow용)")]
        public float MinFlankAngle;
    }

    [Header("더블 터치")]
    [SerializeField] private float _proximityRadius = 10f;
    [SerializeField] private float _touchReward = 0.2f;
    [SerializeField] private float _fastDoubleTouchBonus = 0.3f;
    [SerializeField] private float _doubleTouchWindow = 3f;
    [SerializeField] private bool _touchEndEnabled = true;
    [SerializeField] private float _noTouchPenalty = -0.5f;
    [SerializeField] private float _partialTouchPenalty = -0.2f;

    [Header("보상 튜닝 (VsMelee)")]
    [SerializeField] private float _wStepPenalty = 0.0005f;
    [SerializeField] private float _wFireReward = 0.05f;
    [SerializeField] private float _wMissPenalty = 0.03f;
    [SerializeField] private float _wOutOfRangePenalty = 0.1f;
    [SerializeField] private float _wDamageReward = 0.7f;
    [SerializeField] private float _wShieldDamageReward = 0.3f;
    [SerializeField] private float _wHitReward = 0.3f;
    [SerializeField] private float _wProgress = 0.15f;
    [SerializeField] private float _wAlign = 0.005f;
    [SerializeField] private float _wIdlePenalty = 0.003f;
    [SerializeField] private float _alignDotThreshold = 0.7f;
    [SerializeField] private float _minMoveDelta = 0.05f;

    [Header("터치 후 행동")]
    [SerializeField] private float _wRangeMaintain = 0.2f;
    [SerializeField] private float _rangeTolerance = 3f;

    [Header("근거리 특화")]
    [SerializeField] private float _wCounterAttack = 0.12f;
    [SerializeField] private float _wCloseAlignBonus = 0.002f;
    [SerializeField] private float _wRetreatPenalty = 0.003f;
    [SerializeField] private float _counterWindow = 1.0f;

    [Header("벽 충돌")]
    [SerializeField] private float _wWallHitPenalty = 0.05f;
    [SerializeField] private float _wWallStayPenalty = 0.01f;

    [Header("종료 조건")]
    [SerializeField] private float _episodeMaxDuration = 60f;
    [SerializeField] private float _outOfBoundsY = -5f;
    [SerializeField] private float _outOfBoundsPenalty = -1.0f;

    [Header("사망 종료")]
    [SerializeField] private float _bossDiedPenalty = 1.0f;
    [SerializeField] private float _allKilledReward = 1.0f;
    [SerializeField] private float _playerKilledReward = 0.5f;
    [SerializeField] private float _hitDeadPlayerPenalty = 0.3f;

    private Renderer _renderer;
    private float _episodeStartTime;
    private float _prevTargetDist = -1f;
    private Vector3 _prevBossPos;
    private bool _prevPosValid;
    private int _currentEpisode;

    private string _endReason;
    private float _p1DeathTime;
    private float _p2DeathTime;
    private float _bossTravelDist;
    private string _behaviorName;

    private bool _p1Touched;
    private bool _p2Touched;
    private float _p1TouchTime;
    private float _p2TouchTime;

    private bool _p1DeathHandled;
    private bool _p2DeathHandled;

    private StatManager _p1StatManager;
    private StatManager _p2StatManager;
    private SkillExecutor _p1SkillExecutor;
    private SkillExecutor _p2SkillExecutor;
    private float _prevP1Hp;
    private float _prevP2Hp;
    private float _prevP1Shield;
    private float _prevP2Shield;
    private int _prevTotalHits;

    private ProjectilePool _projectilePool;
    private PersistentAreaPool _areaPool;

    private SkillPoolSO _currentBossPool;
    private SkillPoolSO _currentP1Pool;
    private SkillPoolSO _currentP2Pool;
    private string _currentP1Move = "Default";
    private string _currentP2Move = "Default";

    private readonly Dictionary<string, MatchupRecord> _matchupStats = new();
    private readonly Dictionary<string, MatchupRecord> _bossPoolStats = new();
    private readonly Dictionary<string, MatchupRecord> _playerPoolStats = new();
    private string _csvPath;

    private int _behavFrames;
    private float _sumDistBP1;
    private float _sumDistBP2;
    private float _sumDistP1P2;
    private int _aliveFramesP1;
    private int _aliveFramesP2;
    private int _aliveFramesBoth;
    private float _minDistBP1;
    private float _maxDistBP1;
    private float _minDistBP2;
    private float _maxDistBP2;
    private float _minDistP1P2;
    private float _maxDistP1P2;
    private Vector3 _bossMinPos;
    private Vector3 _bossMaxPos;
    private Vector3 _p1MinPos;
    private Vector3 _p1MaxPos;
    private Vector3 _p2MinPos;
    private Vector3 _p2MaxPos;
    private int _targetSwitches;
    private bool _prevActiveWasP1;
    private bool _prevActiveValid;
    private int _facingFrames;
    private int _cooldownFrames;
    private int _actIdle;
    private int _actFwd;
    private int _actLeft;
    private int _actRight;
    private float _sumCastDist;
    private int _castCount;
    private float _wallTime;

    private int _counterAttacks;
    private int _closeAlignFrames;
    private int _retreatFrames;

    private float _p1LastCastTime = -10f;
    private float _p2LastCastTime = -10f;
    private int _prevP1Casts;
    private int _prevP2Casts;

    private class MatchupRecord
    {
        public int Wins;
        public int Total;
        public float TotalDuration;
        public float TotalBossDmg;
        public float TotalPlayerDmg;

        public float WinRate => Total > 0 ? (float)Wins / Total * 100f : 0f;
        public float AvgDuration => Total > 0 ? TotalDuration / Total : 0f;
        public float AvgBossDmg => Total > 0 ? TotalBossDmg / Total : 0f;
        public float AvgPlayerDmg => Total > 0 ? TotalPlayerDmg / Total : 0f;
    }

    public override void Initialize()
    {
        _renderer = GetComponent<Renderer>();

        if (_bossController == null) _bossController = GetComponent<BossController>();
        if (_collector == null) _collector = GetComponent<BossObservationCollector>();
        if (_trainingSkillManager == null) _trainingSkillManager = GetComponent<TrainingSkillManager>();
        if (_skillManager == null) _skillManager = GetComponent<SkillManager>();
        if (_skillExecutor == null) _skillExecutor = GetComponent<SkillExecutor>();

        _projectilePool = FindAnyObjectByType<ProjectilePool>();
        _areaPool = FindAnyObjectByType<PersistentAreaPool>();

        _bossController.TrainingMode = true;
        _skillManager.SetAutoCast(false);
        _skillManager.RoundRobinEnabled = true;

        var bp = GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
        _behaviorName = bp != null ? bp.BehaviorName : "Unknown";

        _csvPath = System.IO.Path.Combine(Application.dataPath, "..", $"matchup_log_{_behaviorName}.csv");
        string header = "Episode,Result,EndReason,Duration,BossPool,P1Pool,P2Pool,BossDmgDealt,PlayerDmgDealt,BossHpLeft,P1HpLeft,P2HpLeft,BossHits,BossCasts,P1Hits,P2Hits,CumulativeReward,FirstTouchP1,FirstTouchP2,P1DeathTime,P2DeathTime,BossTravelDist,UnlockedSkills,AvgDistBP1,MinDistBP1,MaxDistBP1,AvgDistBP2,MinDistBP2,MaxDistBP2,AvgDistP1P2,MinDistP1P2,MaxDistP1P2,BossAreaXZ,P1AreaXZ,P2AreaXZ,TargetSwitches,IdleRatio,FwdRatio,RotRatio,FacingRatio,CdWaitRatio,AvgCastDist,WallTime,CounterAttacks,CloseAlignFrames,RetreatFrames";
        if (!System.IO.File.Exists(_csvPath))
        {
            System.IO.File.WriteAllText(_csvPath, header + "\n");
        }
        else
        {
            string separator = $"\n# === Session {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n{header}\n";
            try { System.IO.File.AppendAllText(_csvPath, separator); } catch { }
        }
    }

    public override void OnEpisodeBegin()
    {
        if (_renderer != null) _renderer.material.color = Color.red;

        CleanupPreviousEpisode();
        AssignRandomPools();
        SpawnObjects();
        ResetEpisodeState();

        _currentEpisode++;
    }

    private void CleanupPreviousEpisode()
    {
        if (_projectilePool != null) _projectilePool.ReturnAll();
        if (_areaPool != null) _areaPool.ReturnAll();
    }

    private void AssignRandomPools()
    {
        if (_bossSkillPools != null && _bossSkillPools.Length > 0)
        {
            _currentBossPool = _bossSkillPools[Random.Range(0, _bossSkillPools.Length)];
            _trainingSkillManager.SetSkillPool(_currentBossPool);
        }

        if (_playerProfiles != null && _playerProfiles.Length > 0)
        {
            int p1Idx = Random.Range(0, _playerProfiles.Length);
            int p2Idx = Random.Range(0, _playerProfiles.Length);
            if (_playerProfiles.Length > 1)
            {
                while (p2Idx == p1Idx)
                    p2Idx = Random.Range(0, _playerProfiles.Length);
            }

            var p1Profile = _playerProfiles[p1Idx];
            var p2Profile = _playerProfiles[p2Idx];

            _currentP1Pool = p1Profile.SkillPool;
            _currentP2Pool = p2Profile.SkillPool;
            _currentP1Move = p1Profile.MoveGraph != null ? p1Profile.MoveGraph.name : "Default";
            _currentP2Move = p2Profile.MoveGraph != null ? p2Profile.MoveGraph.name : "Default";

            if (_p1TrainingSkillMgr != null) _p1TrainingSkillMgr.SetSkillPool(_currentP1Pool);
            if (_p2TrainingSkillMgr != null) _p2TrainingSkillMgr.SetSkillPool(_currentP2Pool);

            if (p1Profile.MoveGraph != null) SwapPlayerGraph(_p1Object, p1Profile);
            if (p2Profile.MoveGraph != null) SwapPlayerGraph(_p2Object, p2Profile);
        }
    }

    private void SwapPlayerGraph(GameObject player, PlayerProfile profile)
    {
        if (player == null) return;

        var agent = player.GetComponent<BehaviorGraphAgent>();
        if (agent == null) return;

        agent.Graph = profile.MoveGraph;
        agent.Init();

        var nav = player.GetComponent<NavMeshAgent>();
        agent.SetVariableValue("Boss", gameObject);
        agent.SetVariableValue("Self", player);
        agent.SetVariableValue("Agent", nav);

        GameObject ally = player == _p1Object ? _p2Object : _p1Object;
        agent.SetVariableValue("Ally", ally);

        agent.SetVariableValue("DangerRange", profile.DangerRange);
        agent.SetVariableValue("OptimalMin", profile.OptimalMin);
        agent.SetVariableValue("OptimalMax", profile.OptimalMax);
        agent.SetVariableValue("FleeDistance", profile.FleeDistance);
        agent.SetVariableValue("FlankRadius", profile.FlankRadius);
        agent.SetVariableValue("StrafeRadius", profile.StrafeRadius);
        agent.SetVariableValue("StrafeAngleStep", profile.StrafeAngleStep);
        agent.SetVariableValue("MinSpacing", profile.MinSpacing);
        agent.SetVariableValue("AttackRangeMin", profile.AttackRangeMin);
        agent.SetVariableValue("AttackRangeMax", profile.AttackRangeMax);
        agent.SetVariableValue("SafeRangeMin", profile.SafeRangeMin);
        agent.SetVariableValue("SafeRangeMax", profile.SafeRangeMax);
        agent.SetVariableValue("MinFlankAngle", profile.MinFlankAngle);

        agent.Restart();
    }

    private string GetMatchupKey()
    {
        string boss = _currentBossPool != null ? _currentBossPool.name : "Default";
        string p1 = _currentP1Pool != null ? _currentP1Pool.name : "Default";
        string p2 = _currentP2Pool != null ? _currentP2Pool.name : "Default";
        return $"{boss} vs {p1}({_currentP1Move})+{p2}({_currentP2Move})";
    }

    private void RecordMatchResult(bool bossWon)
    {
        float duration = Time.time - _episodeStartTime;
        string bossName = _currentBossPool != null ? _currentBossPool.name : "Default";
        string p1Name = _currentP1Pool != null ? _currentP1Pool.name : "Default";
        string p2Name = _currentP2Pool != null ? _currentP2Pool.name : "Default";

        float bossHpLeft = _bossController.StatMgr != null ? _bossController.StatMgr.GetHPPercent() : 0f;
        float p1HpLeft = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
        float p2HpLeft = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;

        int bossHits = _skillExecutor.TotalHitCount;
        int bossCasts = _skillExecutor.TotalUseCount;
        int p1Hits = _p1SkillExecutor != null ? _p1SkillExecutor.TotalHitCount : 0;
        int p2Hits = _p2SkillExecutor != null ? _p2SkillExecutor.TotalHitCount : 0;

        float bossMaxHp = _bossController.StatMgr != null ? _bossController.StatMgr.GetMaxHP() : 1f;
        float p1MaxHp = _p1StatManager != null ? _p1StatManager.GetMaxHP() : 1f;
        float p2MaxHp = _p2StatManager != null ? _p2StatManager.GetMaxHP() : 1f;

        float bossDmgDealt = (1f - p1HpLeft) * p1MaxHp + (1f - p2HpLeft) * p2MaxHp;
        float playerDmgDealt = (1f - bossHpLeft) * bossMaxHp;

        float cumReward = GetCumulativeReward();
        float touchP1 = _p1TouchTime > 0f ? _p1TouchTime - _episodeStartTime : -1f;
        float touchP2 = _p2TouchTime > 0f ? _p2TouchTime - _episodeStartTime : -1f;
        int unlockedSlots = _trainingSkillManager != null ? _trainingSkillManager.UnlockedCount : 0;

        float avgDP1 = _aliveFramesP1 > 0 ? _sumDistBP1 / _aliveFramesP1 : 0f;
        float avgDP2 = _aliveFramesP2 > 0 ? _sumDistBP2 / _aliveFramesP2 : 0f;
        float avgDP1P2 = _aliveFramesBoth > 0 ? _sumDistP1P2 / _aliveFramesBoth : 0f;
        float mnDP1 = _minDistBP1 < float.MaxValue ? _minDistBP1 : 0f;
        float mnDP2 = _minDistBP2 < float.MaxValue ? _minDistBP2 : 0f;
        float mnDP1P2 = _minDistP1P2 < float.MaxValue ? _minDistP1P2 : 0f;
        Vector3 bd = _bossMaxPos - _bossMinPos;
        float bossArea = bd.x * bd.z;
        Vector3 p1d = _p1MaxPos - _p1MinPos;
        float p1Area = p1d.x * p1d.z;
        Vector3 p2d = _p2MaxPos - _p2MinPos;
        float p2Area = p2d.x * p2d.z;
        float idleR = _behavFrames > 0 ? (float)_actIdle / _behavFrames : 0f;
        float fwdR = _behavFrames > 0 ? (float)_actFwd / _behavFrames : 0f;
        float rotR = _behavFrames > 0 ? (float)(_actLeft + _actRight) / _behavFrames : 0f;
        float faceR = _behavFrames > 0 ? (float)_facingFrames / _behavFrames : 0f;
        float cdR = _behavFrames > 0 ? (float)_cooldownFrames / _behavFrames : 0f;
        float avgCast = _castCount > 0 ? _sumCastDist / _castCount : 0f;

        string csvLine = $"{_currentEpisode},{(bossWon ? "BossWin" : "BossLose")},{_endReason},{duration:F1},{bossName},{p1Name},{p2Name},{bossDmgDealt:F0},{playerDmgDealt:F0},{bossHpLeft:F2},{p1HpLeft:F2},{p2HpLeft:F2},{bossHits},{bossCasts},{p1Hits},{p2Hits},{cumReward:F3},{touchP1:F1},{touchP2:F1},{_p1DeathTime:F1},{_p2DeathTime:F1},{_bossTravelDist:F1},{unlockedSlots},{avgDP1:F1},{mnDP1:F1},{_maxDistBP1:F1},{avgDP2:F1},{mnDP2:F1},{_maxDistBP2:F1},{avgDP1P2:F1},{mnDP1P2:F1},{_maxDistP1P2:F1},{bossArea:F1},{p1Area:F1},{p2Area:F1},{_targetSwitches},{idleR:F3},{fwdR:F3},{rotR:F3},{faceR:F3},{cdR:F3},{avgCast:F1},{_wallTime:F2},{_counterAttacks},{_closeAlignFrames},{_retreatFrames}";
        try { System.IO.File.AppendAllText(_csvPath, csvLine + "\n"); } catch { }

        string matchupKey = GetMatchupKey();
        UpdateRecord(_matchupStats, matchupKey, bossWon, duration, bossDmgDealt, playerDmgDealt);
        UpdateRecord(_bossPoolStats, bossName, bossWon, duration, bossDmgDealt, playerDmgDealt);
        UpdateRecord(_playerPoolStats, p1Name, bossWon, duration, bossDmgDealt, playerDmgDealt);
        UpdateRecord(_playerPoolStats, p2Name, bossWon, duration, bossDmgDealt, playerDmgDealt);

        if (_currentEpisode > 0 && _currentEpisode % _statsLogInterval == 0)
            LogMatchupStats();
    }

    private void UpdateRecord(Dictionary<string, MatchupRecord> dict, string key, bool bossWon, float duration, float bossDmg, float playerDmg)
    {
        if (!dict.TryGetValue(key, out var rec))
        {
            rec = new MatchupRecord();
            dict[key] = rec;
        }

        if (bossWon) rec.Wins++;
        rec.Total++;
        rec.TotalDuration += duration;
        rec.TotalBossDmg += bossDmg;
        rec.TotalPlayerDmg += playerDmg;
    }

    private void LogMatchupStats()
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"[VsMelee] EP#{_currentEpisode} ══════════════════════════");
        sb.AppendLine("── 보스 풀별 승률 ──");
        foreach (var kvp in _bossPoolStats)
        {
            sb.AppendLine($"  {kvp.Key,-20} | 승률:{kvp.Value.WinRate,5:F1}% ({kvp.Value.Wins}/{kvp.Value.Total}) | 평균:{kvp.Value.AvgDuration:F1}s | 보스딜:{kvp.Value.AvgBossDmg:F0} 피딜:{kvp.Value.AvgPlayerDmg:F0}");
        }

        sb.AppendLine("── 플레이어 풀별 (상대한 보스 기준) ──");
        foreach (var kvp in _playerPoolStats)
        {
            sb.AppendLine($"  {kvp.Key,-20} | 보스승률:{kvp.Value.WinRate,5:F1}% ({kvp.Value.Wins}/{kvp.Value.Total}) | 평균:{kvp.Value.AvgDuration:F1}s");
        }

        sb.AppendLine("── 매치업별 상세 ──");
        foreach (var kvp in _matchupStats)
        {
            sb.AppendLine($"  {kvp.Key} | 보스승:{kvp.Value.WinRate,5:F1}% ({kvp.Value.Wins}/{kvp.Value.Total}) | 평균:{kvp.Value.AvgDuration:F1}s | 보스딜:{kvp.Value.AvgBossDmg:F0} 피딜:{kvp.Value.AvgPlayerDmg:F0}");
        }

        Debug.Log(sb.ToString());
    }

    private void ResetEpisodeState()
    {
        _bossController.StatMgr.ResetForTraining();
        _bossController.StateMgr?.ForceReset();

        ResetPlayer(_p1Object);
        ResetPlayer(_p2Object);

        _skillExecutor.ResetAll();
        _trainingSkillManager.ResetForEpisode();

        if (_p1TrainingSkillMgr != null) _p1TrainingSkillMgr.ResetForEpisode();
        if (_p2TrainingSkillMgr != null) _p2TrainingSkillMgr.ResetForEpisode();

        _prevTargetDist = -1f;
        _prevPosValid = false;
        _prevBossPos = transform.position;
        _episodeStartTime = Time.time;
        _prevTotalHits = 0;

        _p1Touched = false;
        _p2Touched = false;
        _p1TouchTime = -1f;
        _p2TouchTime = -1f;
        _p1DeathHandled = false;
        _p2DeathHandled = false;

        _endReason = "Unknown";
        _p1DeathTime = -1f;
        _p2DeathTime = -1f;
        _bossTravelDist = 0f;

        _behavFrames = 0;
        _sumDistBP1 = _sumDistBP2 = _sumDistP1P2 = 0f;
        _aliveFramesP1 = _aliveFramesP2 = _aliveFramesBoth = 0;
        _minDistBP1 = _minDistBP2 = _minDistP1P2 = float.MaxValue;
        _maxDistBP1 = _maxDistBP2 = _maxDistP1P2 = 0f;
        _bossMinPos = _bossMaxPos = transform.position;
        _p1MinPos = _p1MaxPos = _p1Object != null ? _p1Object.transform.position : Vector3.zero;
        _p2MinPos = _p2MaxPos = _p2Object != null ? _p2Object.transform.position : Vector3.zero;
        _targetSwitches = 0;
        _prevActiveValid = false;
        _facingFrames = 0;
        _cooldownFrames = 0;
        _actIdle = _actFwd = _actLeft = _actRight = 0;
        _sumCastDist = 0f;
        _castCount = 0;
        _wallTime = 0f;

        _counterAttacks = 0;
        _closeAlignFrames = 0;
        _retreatFrames = 0;

        _p1LastCastTime = -10f;
        _p2LastCastTime = -10f;
        _prevP1Casts = 0;
        _prevP2Casts = 0;

        CachePlayerStats();
    }

    private void ResetPlayer(GameObject player)
    {
        if (player == null) return;

        if (player.TryGetComponent(out StatManager stat))
            stat.ResetForTraining();

        if (player.TryGetComponent(out StateManager state))
            state.ForceReset();

        if (player.TryGetComponent(out SkillExecutor executor))
            executor.ResetAll();

        UnfreezePlayer(player);
    }

    private void FreezePlayer(GameObject player)
    {
        if (player == null) return;

        foreach (var mb in player.GetComponents<MonoBehaviour>())
            mb.StopAllCoroutines();

        if (player.TryGetComponent(out NavMeshAgent nav) && nav.enabled)
        {
            nav.ResetPath();
            nav.velocity = Vector3.zero;
            nav.isStopped = true;
        }

        if (player.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    private void UnfreezePlayer(GameObject player)
    {
        if (player == null) return;

        if (player.TryGetComponent(out NavMeshAgent nav))
            nav.isStopped = false;

        if (player.TryGetComponent(out Rigidbody rb))
            rb.isKinematic = false;
    }

    private void CachePlayerStats()
    {
        _p1StatManager = _p1Object != null ? _p1Object.GetComponent<StatManager>() : null;
        _p2StatManager = _p2Object != null ? _p2Object.GetComponent<StatManager>() : null;
        _p1SkillExecutor = _p1Object != null ? _p1Object.GetComponent<SkillExecutor>() : null;
        _p2SkillExecutor = _p2Object != null ? _p2Object.GetComponent<SkillExecutor>() : null;

        _prevP1Hp = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
        _prevP2Hp = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;
        _prevP1Shield = _p1StatManager != null ? _p1StatManager.GetShield() : 0f;
        _prevP2Shield = _p2StatManager != null ? _p2StatManager.GetShield() : 0f;
    }

    private void SpawnObjects()
    {
        if (_bossSpawnPoint != null)
            transform.SetPositionAndRotation(_bossSpawnPoint.position, _bossSpawnPoint.rotation);
        else
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

        if (_p1Object == null || _p2Object == null) return;
        if (_playerSpawnPoints == null || _playerSpawnPoints.Count < 2) return;

        int idx1 = Random.Range(0, _playerSpawnPoints.Count);
        int idx2;
        do { idx2 = Random.Range(0, _playerSpawnPoints.Count); } while (idx2 == idx1);

        PlaceOnSpawn(_p1Object, _playerSpawnPoints[idx1]);
        PlaceOnSpawn(_p2Object, _playerSpawnPoints[idx2]);

        _collector.SetPlayers(_p1Object, _p2Object);
    }

    private void PlaceOnSpawn(GameObject target, Transform spawn)
    {
        if (target == null || spawn == null) return;

        foreach (var mb in target.GetComponents<MonoBehaviour>())
            mb.StopAllCoroutines();

        if (target.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (target.TryGetComponent(out NavMeshAgent nav) && nav.enabled)
        {
            nav.Warp(spawn.position);
            target.transform.rotation = spawn.rotation;
            nav.ResetPath();
            nav.velocity = Vector3.zero;
        }
        else
        {
            target.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        int totalObs = BossObservationCollector.Phase3Size + TouchObsCount + PlayerSkillObsCount + ExtraObsCount;

        if (_collector == null)
        {
            sensor.AddObservation(new float[totalObs]);
            return;
        }

        _collector.CollectUpToPhase3(sensor);
        sensor.AddObservation(_p1Touched ? 1f : 0f);
        sensor.AddObservation(_p2Touched ? 1f : 0f);
        CollectPlayerSkillObs(sensor);
        CollectExtraObs(sensor);
    }

    private void CollectPlayerSkillObs(VectorSensor sensor)
    {
        float maxDist = 55f;
        float maxCd = 30f;
        int ttCount = System.Enum.GetValues(typeof(TargetType)).Length;
        float ttDivisor = Mathf.Max(ttCount - 1, 1);

        CollectOnePlayerSkills(sensor, _p1TrainingSkillMgr, _p1SkillExecutor, _p1StatManager, maxDist, maxCd, ttDivisor);
        CollectOnePlayerSkills(sensor, _p2TrainingSkillMgr, _p2SkillExecutor, _p2StatManager, maxDist, maxCd, ttDivisor);
    }

    private void CollectOnePlayerSkills(
        VectorSensor sensor,
        TrainingSkillManager tsMgr,
        SkillExecutor executor,
        StatManager statMgr,
        float maxDist,
        float maxCd,
        float ttDivisor)
    {
        bool alive = statMgr != null && statMgr.IsAlive;

        for (int i = 0; i < PlayerSkillSlots; i++)
        {
            SkillDefinition skill = alive && tsMgr != null ? tsMgr.GetEquippedSkill(i) : null;

            float range = skill != null ? Mathf.Clamp01(skill.Range / maxDist) : 0f;
            sensor.AddObservation(range);

            float cdRatio = 0f;
            if (skill != null && executor != null)
            {
                float remaining = executor.GetRemainingCooldown(skill);
                float total = skill.Cooldown;
                cdRatio = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
            }
            sensor.AddObservation(cdRatio);

            float tt = skill != null ? (float)skill.TargetType / ttDivisor : 0f;
            sensor.AddObservation(tt);
        }
    }

    private void CollectExtraObs(VectorSensor sensor)
    {
        const float maxSpeed = 12f;
        const float maxBurstDmg = 50f;

        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;

        sensor.AddObservation(p1Alive && _p1StatManager.IsCasting ? 1f : 0f);
        sensor.AddObservation(p2Alive && _p2StatManager.IsCasting ? 1f : 0f);

        sensor.AddObservation(p1Alive ? Mathf.Clamp01(_collector.P1AvgSpeed / maxSpeed) : 0f);
        sensor.AddObservation(p2Alive ? Mathf.Clamp01(_collector.P2AvgSpeed / maxSpeed) : 0f);

        int p1Unlocked = _p1TrainingSkillMgr != null ? _p1TrainingSkillMgr.UnlockedCount : 0;
        sensor.AddObservation(Mathf.Clamp01((float)p1Unlocked / PlayerSkillSlots));
        int p2Unlocked = _p2TrainingSkillMgr != null ? _p2TrainingSkillMgr.UnlockedCount : 0;
        sensor.AddObservation(Mathf.Clamp01((float)p2Unlocked / PlayerSkillSlots));

        sensor.AddObservation(Mathf.Clamp01(_collector.RecentBurstDamage / maxBurstDmg));
    }

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        _trainingSkillManager.Tick();

        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;
        if (_p1TrainingSkillMgr != null && p1Alive) _p1TrainingSkillMgr.Tick();
        if (_p2TrainingSkillMgr != null && p2Alive) _p2TrainingSkillMgr.Tick();

        TrackPlayerCasts();

        int moveAction = actionBuffers.DiscreteActions[0];
        int skillAction = actionBuffers.DiscreteActions[1];

        switch (moveAction)
        {
            case 1:
                transform.position += transform.forward * (_moveSpeed * Time.deltaTime);
                break;
            case 2:
                transform.Rotate(0f, -_rotationSpeed * Time.deltaTime, 0f);
                break;
            case 3:
                transform.Rotate(0f, _rotationSpeed * Time.deltaTime, 0f);
                break;
        }

        if (moveAction == 0) _actIdle++;
        else if (moveAction == 1) _actFwd++;
        else if (moveAction == 2) _actLeft++;
        else _actRight++;
        _behavFrames++;

        bool isMoving = moveAction == 1;
        _bossController.StateMgr?.NotifyMovementInput(isMoving);

        if (skillAction >= 1 && skillAction <= 3)
        {
            int slot = skillAction - 1;
            TryExecuteSkill(slot);
        }

        ApplyStepRewards();
        CheckTermination();
    }

    private void TryExecuteSkill(int slot)
    {
        if (!_trainingSkillManager.CanUseSlot(slot)) return;

        SkillDefinition skill = _trainingSkillManager.GetEquippedSkill(slot);
        if (skill == null) return;

        Transform activeTarget = GetActiveTarget();

        if (activeTarget == null && skill.TargetType != TargetType.Self)
        {
            bool bothDead = (_p1StatManager != null && !_p1StatManager.IsAlive)
                         && (_p2StatManager != null && !_p2StatManager.IsAlive);
            if (!bothDead)
                AddReward(-_hitDeadPlayerPenalty);
            return;
        }

        ICombatant target = activeTarget != null ? activeTarget.GetComponent<ICombatant>() : null;

        float dist = activeTarget != null
            ? Vector3.Distance(transform.position, activeTarget.position)
            : float.MaxValue;

        var ctx = new SkillContext
        {
            Caster = _bossController,
            PrimaryTarget = target,
            CastPosition = transform.position,
            CastDirection = transform.forward,
        };
        ctx.RefreshSnapshot();

        int hitsBefore = _skillExecutor.TotalHitCount;
        bool fired = _skillExecutor.Execute(skill, ctx);

        if (fired && skill.TargetType != TargetType.Self && dist < float.MaxValue)
        {
            _sumCastDist += dist;
            _castCount++;
        }

        if (fired)
        {
            if (skill.TargetType != TargetType.Self && dist > skill.Range)
            {
                AddReward(-_wOutOfRangePenalty);
            }
            else
            {
                if (_wFireReward > 0f) AddReward(_wFireReward);

                int hitsAfter = _skillExecutor.TotalHitCount;
                if (hitsAfter == hitsBefore && skill.TargetType != TargetType.Self)
                    AddReward(-_wMissPenalty);
            }
        }
    }

    private bool ActiveIsP1(float distP1, float distP2)
    {
        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;

        if (!p1Alive && p2Alive) return false;
        if (p1Alive && !p2Alive) return true;

        if (_p1Touched && !_p2Touched) return false;
        if (_p2Touched && !_p1Touched) return true;
        if (_p1Touched && _p2Touched) return distP1 >= distP2;
        return distP1 <= distP2;
    }

    private Transform GetActiveTarget()
    {
        if (_p1Object == null && _p2Object == null) return null;

        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;
        if (!p1Alive && !p2Alive) return null;

        if (_p1Object == null) return p2Alive ? _p2Object.transform : null;
        if (_p2Object == null) return p1Alive ? _p1Object.transform : null;

        if (!p1Alive) return _p2Object.transform;
        if (!p2Alive) return _p1Object.transform;

        float distP1 = Vector3.Distance(transform.position, _p1Object.transform.position);
        float distP2 = Vector3.Distance(transform.position, _p2Object.transform.position);

        return ActiveIsP1(distP1, distP2) ? _p1Object.transform : _p2Object.transform;
    }

    private void TrackPlayerCasts()
    {
        int p1Casts = _p1SkillExecutor != null ? _p1SkillExecutor.TotalUseCount : 0;
        int p2Casts = _p2SkillExecutor != null ? _p2SkillExecutor.TotalUseCount : 0;
        if (p1Casts > _prevP1Casts) _p1LastCastTime = Time.time;
        if (p2Casts > _prevP2Casts) _p2LastCastTime = Time.time;
        _prevP1Casts = p1Casts;
        _prevP2Casts = p2Casts;
    }

    private bool CheckProximityTouch(float distP1, float distP2)
    {
        float now = Time.time;
        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;

        if (!_p1Touched && p1Alive && distP1 < _proximityRadius)
        {
            _p1Touched = true;
            _p1TouchTime = now;
            AddReward(_touchReward);
            _prevTargetDist = -1f;
        }
        if (!_p2Touched && p2Alive && distP2 < _proximityRadius)
        {
            _p2Touched = true;
            _p2TouchTime = now;
            AddReward(_touchReward);
            _prevTargetDist = -1f;
        }

        if (!_p1Touched || !_p2Touched) return false;
        if (!_touchEndEnabled) return false;

        float gap = Mathf.Abs(_p1TouchTime - _p2TouchTime);
        bool isFast = gap <= _doubleTouchWindow;
        if (isFast) AddReward(_fastDoubleTouchBonus);

        if (_renderer != null) _renderer.material.color = isFast ? Color.green : Color.cyan;
        EndEpisode();
        return true;
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        for (int slot = 0; slot < 3; slot++)
        {
            int actionIndex = slot + 1;
            if (!_trainingSkillManager.CanUseSlot(slot))
            {
                actionMask.SetActionEnabled(1, actionIndex, false);
            }
        }
    }

    private void ApplyStepRewards()
    {
        AddReward(-_wStepPenalty);

        _bossMinPos = Vector3.Min(_bossMinPos, transform.position);
        _bossMaxPos = Vector3.Max(_bossMaxPos, transform.position);
        bool allCd = true;
        for (int i = 0; i < 3; i++)
        {
            if (_trainingSkillManager.CanUseSlot(i))
            {
                allCd = false;
                break;
            }
        }
        if (allCd) _cooldownFrames++;

        ApplyDamageRewards();

        if (_p1Object == null && _p2Object == null) return;

        bool p1Alive = _p1StatManager != null && _p1StatManager.IsAlive;
        bool p2Alive = _p2StatManager != null && _p2StatManager.IsAlive;
        if (!p1Alive && !p2Alive) return;

        Vector3 bossPos = transform.position;

        float distP1 = _p1Object != null ? Vector3.Distance(bossPos, _p1Object.transform.position) : float.MaxValue;
        float distP2 = _p2Object != null ? Vector3.Distance(bossPos, _p2Object.transform.position) : float.MaxValue;

        if (p1Alive && distP1 < float.MaxValue)
        {
            _sumDistBP1 += distP1;
            _minDistBP1 = Mathf.Min(_minDistBP1, distP1);
            _maxDistBP1 = Mathf.Max(_maxDistBP1, distP1);
            _aliveFramesP1++;
        }
        if (p2Alive && distP2 < float.MaxValue)
        {
            _sumDistBP2 += distP2;
            _minDistBP2 = Mathf.Min(_minDistBP2, distP2);
            _maxDistBP2 = Mathf.Max(_maxDistBP2, distP2);
            _aliveFramesP2++;
        }
        if (p1Alive && p2Alive && _p1Object != null && _p2Object != null)
        {
            float dp = Vector3.Distance(_p1Object.transform.position, _p2Object.transform.position);
            _sumDistP1P2 += dp;
            _minDistP1P2 = Mathf.Min(_minDistP1P2, dp);
            _maxDistP1P2 = Mathf.Max(_maxDistP1P2, dp);
            _aliveFramesBoth++;
        }
        if (_p1Object != null)
        {
            Vector3 pp = _p1Object.transform.position;
            _p1MinPos = Vector3.Min(_p1MinPos, pp);
            _p1MaxPos = Vector3.Max(_p1MaxPos, pp);
        }
        if (_p2Object != null)
        {
            Vector3 pp = _p2Object.transform.position;
            _p2MinPos = Vector3.Min(_p2MinPos, pp);
            _p2MaxPos = Vector3.Max(_p2MaxPos, pp);
        }

        if (CheckProximityTouch(distP1, distP2)) return;

        bool activeIsP1 = ActiveIsP1(distP1, distP2);
        if (_prevActiveValid && _prevActiveWasP1 != activeIsP1) _targetSwitches++;
        _prevActiveWasP1 = activeIsP1;
        _prevActiveValid = true;
        float targetDist = activeIsP1 ? distP1 : distP2;
        Vector3 targetPos = activeIsP1 ? _p1Object.transform.position : _p2Object.transform.position;
        Vector3 targetDir = (targetPos - bossPos).normalized;
        Vector3 fwd = transform.forward;

        bool bothTouched = _p1Touched && _p2Touched;

        if (bothTouched)
        {
            float optimalRange = GetAverageSkillRange();
            float deviation = Mathf.Abs(targetDist - optimalRange);
            if (deviation < _rangeTolerance)
                AddReward(_wRangeMaintain);
            else
                AddReward(-_wRangeMaintain * 0.5f);
        }
        else if (_prevTargetDist > 0f)
        {
            float delta = _prevTargetDist - targetDist;
            AddReward(delta * _wProgress);
        }

        if (!bothTouched && _wRetreatPenalty > 0f
            && _prevTargetDist > 0f && targetDist > _prevTargetDist)
        {
            AddReward(-_wRetreatPenalty);
            _retreatFrames++;
        }

        _prevTargetDist = targetDist;

        float dot = Vector3.Dot(fwd, targetDir);
        if (dot > _alignDotThreshold)
        {
            AddReward(_wAlign);
            _facingFrames++;

            if (_wCloseAlignBonus > 0f)
            {
                AddReward(_wCloseAlignBonus);
                _closeAlignFrames++;
            }
        }

        if (_prevPosValid)
        {
            float moveDelta = Vector3.Distance(bossPos, _prevBossPos);
            _bossTravelDist += moveDelta;
            if (moveDelta < _minMoveDelta)
                AddReward(-_wIdlePenalty);
        }
        _prevBossPos = bossPos;
        _prevPosValid = true;
    }

    private void ApplyDamageRewards()
    {
        float curP1Hp = _p1StatManager != null ? _p1StatManager.GetHPPercent() : 0f;
        float curP2Hp = _p2StatManager != null ? _p2StatManager.GetHPPercent() : 0f;

        float hpDelta = (_prevP1Hp - curP1Hp) + (_prevP2Hp - curP2Hp);
        if (hpDelta > 0f)
            AddReward(hpDelta * _wDamageReward);

        _prevP1Hp = curP1Hp;
        _prevP2Hp = curP2Hp;

        float curP1Shield = _p1StatManager != null ? _p1StatManager.GetShield() : 0f;
        float curP2Shield = _p2StatManager != null ? _p2StatManager.GetShield() : 0f;

        float shieldDelta = (_prevP1Shield - curP1Shield) + (_prevP2Shield - curP2Shield);
        if (shieldDelta > 0f)
        {
            float p1Max = _p1StatManager != null ? _p1StatManager.GetShieldMax() : 1f;
            float p2Max = _p2StatManager != null ? _p2StatManager.GetShieldMax() : 1f;
            float maxShield = Mathf.Max(p1Max + p2Max, 1f);
            AddReward((shieldDelta / maxShield) * _wShieldDamageReward);
        }

        _prevP1Shield = curP1Shield;
        _prevP2Shield = curP2Shield;

        int curHits = _skillExecutor.TotalHitCount;
        if (curHits > _prevTotalHits)
        {
            int newHits = curHits - _prevTotalHits;
            AddReward(newHits * _wHitReward);

            bool recentlyCast = (_p1StatManager != null && _p1StatManager.IsAlive && Time.time - _p1LastCastTime < _counterWindow)
                             || (_p2StatManager != null && _p2StatManager.IsAlive && Time.time - _p2LastCastTime < _counterWindow);
            if (recentlyCast)
            {
                AddReward(newHits * _wCounterAttack);
                _counterAttacks += newHits;
            }
        }
        _prevTotalHits = curHits;
    }

    private float GetAverageSkillRange()
    {
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < 3; i++)
        {
            SkillDefinition skill = _trainingSkillManager.GetEquippedSkill(i);
            if (skill != null && skill.TargetType != TargetType.Self)
            {
                sum += skill.Range;
                count++;
            }
        }
        return count > 0 ? sum / count : _proximityRadius;
    }

    private void CheckTermination()
    {
        if (!_bossController.IsAlive)
        {
            Debug.Log($"[VsMelee] EP#{_currentEpisode} 종료: 보스 사망 (elapsed={Time.time - _episodeStartTime:F1}s) | {GetMatchupKey()}");
            AddReward(-_bossDiedPenalty);
            _endReason = "BossDeath";
            RecordMatchResult(false);
            if (_renderer != null) _renderer.material.color = Color.black;
            EndEpisode();
            return;
        }

        bool p1Dead = _p1StatManager != null && !_p1StatManager.IsAlive;
        bool p2Dead = _p2StatManager != null && !_p2StatManager.IsAlive;

        if (p1Dead && !_p1DeathHandled)
        {
            _p1DeathHandled = true;
            _p1DeathTime = Time.time - _episodeStartTime;
            Debug.Log($"[VsMelee] EP#{_currentEpisode} P1 사망 (elapsed={_p1DeathTime:F1}s)");
            AddReward(_playerKilledReward);
            if (!_p1Touched) { _p1Touched = true; _p1TouchTime = Time.time; }
            _prevTargetDist = -1f;
            FreezePlayer(_p1Object);
        }
        if (p2Dead && !_p2DeathHandled)
        {
            _p2DeathHandled = true;
            _p2DeathTime = Time.time - _episodeStartTime;
            Debug.Log($"[VsMelee] EP#{_currentEpisode} P2 사망 (elapsed={_p2DeathTime:F1}s)");
            AddReward(_playerKilledReward);
            if (!_p2Touched) { _p2Touched = true; _p2TouchTime = Time.time; }
            _prevTargetDist = -1f;
            FreezePlayer(_p2Object);
        }

        if (p1Dead && p2Dead)
        {
            Debug.Log($"[VsMelee] EP#{_currentEpisode} 종료: 양쪽 플레이어 사망 (elapsed={Time.time - _episodeStartTime:F1}s) | {GetMatchupKey()}");
            AddReward(_allKilledReward);
            _endReason = "AllPlayerDeath";
            RecordMatchResult(true);
            if (_renderer != null) _renderer.material.color = Color.green;
            EndEpisode();
            return;
        }

        if (transform.position.y < _outOfBoundsY)
        {
            Debug.Log($"[VsMelee] EP#{_currentEpisode} 종료: 맵 밖 추락 y={transform.position.y:F1} (elapsed={Time.time - _episodeStartTime:F1}s) | {GetMatchupKey()}");
            AddReward(_outOfBoundsPenalty);
            _endReason = "OutOfBounds";
            RecordMatchResult(false);
            if (_renderer != null) _renderer.material.color = Color.gray;
            EndEpisode();
            return;
        }

        if (Time.time - _episodeStartTime > _episodeMaxDuration)
        {
            string touchInfo = _p1Touched && _p2Touched ? "양쪽터치"
                : _p1Touched ? "P1만터치" : _p2Touched ? "P2만터치" : "터치없음";
            Debug.Log($"[VsMelee] EP#{_currentEpisode} 종료: 시간초과 {_episodeMaxDuration}s ({touchInfo}) | {GetMatchupKey()}");

            if (!_p1Touched && !_p2Touched)
                AddReward(_noTouchPenalty);
            else if (_p1Touched ^ _p2Touched)
                AddReward(_partialTouchPenalty);

            _endReason = "Timeout";
            RecordMatchResult(false);
            if (_renderer != null) _renderer.material.color = Color.magenta;
            EndEpisode();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Wall")) return;
        AddReward(-_wWallHitPenalty);
        if (_renderer != null) _renderer.material.color = Color.yellow;
    }

    private void OnCollisionStay(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Wall")) return;
        AddReward(-_wWallStayPenalty * Time.fixedDeltaTime);
        _wallTime += Time.fixedDeltaTime;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var d = actionsOut.DiscreteActions;
        d[0] = 0;
        d[1] = 0;

        if (Input.GetKey(KeyCode.W)) d[0] = 1;
        else if (Input.GetKey(KeyCode.A)) d[0] = 2;
        else if (Input.GetKey(KeyCode.D)) d[0] = 3;

        if (Input.GetKey(KeyCode.Alpha1)) d[1] = 1;
        if (Input.GetKey(KeyCode.Alpha2)) d[1] = 2;
        if (Input.GetKey(KeyCode.Alpha3)) d[1] = 3;
    }
}
