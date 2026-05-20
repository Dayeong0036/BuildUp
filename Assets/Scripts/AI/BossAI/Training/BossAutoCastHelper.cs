using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 보스 전용 자동 스킬 시전 (학습 환경용)
// 텔레그래프: CastStart → wait(duration) → Execute → CastEnd
// BossInferenceAgent가 이동만 제어, 스킬 시전은 이 헬퍼가 담당
[RequireComponent(typeof(SkillManager))]
[RequireComponent(typeof(SkillExecutor))]
public class BossAutoCastHelper : MonoBehaviour
{
    public struct CastTelemetry
    {
        public SkillDefinition Skill;
        public float Distance;
        public int HitsBefore;
        public int HitsAfter;
    }

    public System.Action<CastTelemetry> OnCastFired;

    [Header("참조")]
    [SerializeField] private SkillManager  _skillManager;
    [SerializeField] private SkillExecutor _executor;
    [SerializeField] private BossController _boss;
    [SerializeField] private StatManager   _statManager;
    [SerializeField] private StateManager  _stateManager;

    [Header("Phase Telegraph Scale (§7)")]
    [SerializeField] private float[] _phaseTelegraphScale = { 1f, 0.9f, 0.8f, 0.75f };

    private ICombatant _p1Combatant;
    private ICombatant _p2Combatant;

    private bool _enabled;
    private Coroutine _castRoutine;

    private readonly Dictionary<int, WaitForSeconds> _waitCache = new();

    private void Awake()
    {
        if (_skillManager == null) _skillManager = GetComponent<SkillManager>();
        if (_executor == null)     _executor     = GetComponent<SkillExecutor>();
        if (_boss == null)         _boss         = GetComponent<BossController>();
        if (_statManager == null)  _statManager  = GetComponent<StatManager>();
        if (_stateManager == null) _stateManager = GetComponent<StateManager>();
    }

    public void Initialize(GameObject p1, GameObject p2)
    {
        StopCastRoutine();
        _p1Combatant = p1 != null ? p1.GetComponent<ICombatant>() : null;
        _p2Combatant = p2 != null ? p2.GetComponent<ICombatant>() : null;

        if (_skillManager != null) _skillManager.SetAutoCast(false);
        _enabled = false;
    }

    public void SetEnabled(bool on)
    {
        _enabled = on;
        if (!on) StopCastRoutine();
    }

    private void OnDisable()
    {
        StopCastRoutine();
    }

    private void StopCastRoutine()
    {
        if (_castRoutine != null)
        {
            StopCoroutine(_castRoutine);
            _castRoutine = null;
        }
        _stateManager?.NotifyCastEnd();
    }

    private void Update()
    {
        if (!_enabled) return;
        if (_castRoutine != null) return;
        if (!CanBossCast()) return;

        var slots = _skillManager.Slots;
        int count = _skillManager.MaxSlots;

        for (int i = 0; i < count; i++)
        {
            SkillDefinition skill = slots[i];
            if (skill == null) continue;
            if (!_executor.CanUse(skill)) continue;

            ICombatant target = null;
            if (skill.TargetType != TargetType.Self)
            {
                target = FindNearestPlayer();
                if (target == null || !target.IsAlive) continue;

                float dist = Vector3.Distance(transform.position, target.Transform.position);
                if (dist > skill.Range) continue;
            }

            var ctx = new SkillContext
            {
                Caster        = _boss,
                PrimaryTarget = target,
                CastPosition  = transform.position,
                CastDirection = target != null
                    ? (target.Transform.position - transform.position).normalized
                    : transform.forward,
            };
            ctx.RefreshSnapshot();

            if (skill.RuntimeCondition != null && !skill.RuntimeCondition(ctx))
                continue;

            float castDist = target != null
                ? Vector3.Distance(transform.position, target.Transform.position)
                : 0f;
            int hitsBefore = _executor.TotalHitCount;

            float teleDuration = SkillTelegraphTable.GetBaseDuration(skill.SkillId)
                                 * GetTelegraphScale();

            if (teleDuration > 0f)
            {
                if (_stateManager != null) _stateManager.NotifyCastStart();
                _castRoutine = StartCoroutine(
                    CastAfterTelegraph(skill, ctx, teleDuration, castDist, hitsBefore));
                break;
            }

            // telegraph 0 — 즉시 시전 (SurvivalPulse, OverchargeMode)
            if (_stateManager != null) _stateManager.NotifyCastStart();
            bool fired = _executor.Execute(skill, ctx);
            if (_stateManager != null) _stateManager.NotifyCastEnd();

            if (fired)
            {
                EmitTelemetry(skill, castDist, hitsBefore);
                break;
            }
        }
    }

    private IEnumerator CastAfterTelegraph(
        SkillDefinition skill, SkillContext ctx, float duration,
        float castDist, int hitsBefore)
    {
        yield return GetWait(duration);

        if (_statManager != null && _statManager.IsAlive)
        {
            bool fired = _executor.Execute(skill, ctx);
            if (fired)
                EmitTelemetry(skill, castDist, hitsBefore);
        }

        if (_stateManager != null) _stateManager.NotifyCastEnd();
        _castRoutine = null;
    }

    private float GetTelegraphScale()
    {
        int phase = _boss != null ? _boss.CurrentPhase : 0;
        if (_phaseTelegraphScale == null || _phaseTelegraphScale.Length == 0) return 1f;
        int idx = Mathf.Clamp(phase, 0, _phaseTelegraphScale.Length - 1);
        return _phaseTelegraphScale[idx];
    }

    private WaitForSeconds GetWait(float seconds)
    {
        int key = Mathf.RoundToInt(seconds * 1000f);
        if (!_waitCache.TryGetValue(key, out var wait))
            _waitCache[key] = wait = new WaitForSeconds(key / 1000f);
        return wait;
    }

    private void EmitTelemetry(SkillDefinition skill, float castDist, int hitsBefore)
    {
        int hitsAfter = _executor.TotalHitCount;
        int newHits = hitsAfter - hitsBefore;

        if (newHits > 0)
            Debug.Log($"[BossCast] <color=orange>{skill.DisplayName}</color> → HIT ×{newHits} | dist={castDist:F1}");
        else
            Debug.Log($"[BossCast] {skill.DisplayName} → MISS | dist={castDist:F1}");

        OnCastFired?.Invoke(new CastTelemetry
        {
            Skill      = skill,
            Distance   = castDist,
            HitsBefore = hitsBefore,
            HitsAfter  = hitsAfter,
        });
    }

    private bool CanBossCast()
    {
        if (_statManager == null || !_statManager.IsAlive) return false;
        if (_statManager.IsCasting) return false;
        if (_statManager.IsParrying) return false;
        if (_statManager.HasStatus(StatusType.Stunned)) return false;
        if (_statManager.HasStatus(StatusType.HitStun)) return false;
        if (_statManager.HasStatus(StatusType.Silence)) return false;
        if (_statManager.HasStatus(StatusType.Rooted)) return false;
        if (_stateManager != null && !_stateManager.CanCast) return false;
        return true;
    }

    private ICombatant FindNearestPlayer()
    {
        ICombatant nearest = null;
        float bestDist = float.MaxValue;

        CheckCandidate(_p1Combatant, ref nearest, ref bestDist);
        CheckCandidate(_p2Combatant, ref nearest, ref bestDist);

        return nearest;
    }

    private void CheckCandidate(ICombatant c, ref ICombatant nearest, ref float bestDist)
    {
        if (c == null || !c.IsAlive) return;
        float dist = Vector3.Distance(transform.position, c.Transform.position);
        if (dist < bestDist)
        {
            bestDist = dist;
            nearest  = c;
        }
    }
}
