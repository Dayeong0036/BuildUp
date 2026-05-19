using System.Collections.Generic;
using UnityEngine;

// 스킬 실행 및 쿨타임 관리
// Host-authoritative 구조이므로 실제 판정은 Host 에서만 실행되어야 한다
// 컴포넌트로 붙여 PlayerController / BossController 가 참조해서 사용
public class SkillExecutor : MonoBehaviour
{
    [Header("디버그")]
    [SerializeField] private bool _logExecution = true;

    // SkillId → 마지막 사용 시각
    private readonly Dictionary<string, float> _lastUseTimes = new();

    // ── 적중 기록 (BossObservationCollector 용) ─────────────────
    private readonly Dictionary<string, int> _attemptCounts = new();
    private readonly Dictionary<string, int> _hitCounts     = new();
    private readonly List<string>            _skillHistory  = new();

    // ── 페이즈 쿨다운 배율 ──────────────────────────────────
    private float _cooldownMultiplier = 1f;
    public float CooldownMultiplier
    {
        get => _cooldownMultiplier;
        set => _cooldownMultiplier = Mathf.Max(0.01f, value);
    }

    public float GetEffectiveCooldown(SkillDefinition skill)
        => skill != null ? skill.Cooldown * _cooldownMultiplier : 0f;

    // ── 쿨타임 조회 ──────────────────────────────────────────
    public bool CanUse(SkillDefinition skill)
    {
        if (skill == null || !skill.IsReady) return false;
        return Time.time - GetLastUseTime(skill) >= GetEffectiveCooldown(skill);
    }

    public float GetRemainingCooldown(SkillDefinition skill)
    {
        if (skill == null) return 0f;
        return Mathf.Max(0f, GetEffectiveCooldown(skill) - (Time.time - GetLastUseTime(skill)));
    }

    // ── 실행 ─────────────────────────────────────────────────
    // 성공 시 true 반환, 쿨타임 중이거나 RuntimeStep 미주입이면 false
    public bool Execute(SkillDefinition skill, SkillContext ctx)
    {
        if (!CanUse(skill)) return false;

        ctx.CurrentTime = Time.time;
        ctx.RefreshSnapshot();

        if (skill.RuntimeCondition != null && !skill.RuntimeCondition(ctx))
            return false;

        _lastUseTimes[skill.SkillId] = Time.time;
        RecordAttempt(skill.SkillId);
        ctx.HitRecorded = false;
        string capturedId = skill.SkillId;
        ctx.OnHitRecorded = () =>
        {
            if (!ctx.HitRecorded)
            {
                ctx.HitRecorded = true;
                RecordHit(capturedId);
            }
        };
        skill.RuntimeStep.Invoke(ctx);

        if (_logExecution)
        {
            string target = ctx.PrimaryTarget != null
                ? $"{ctx.PrimaryTarget.Transform.name}(HP:{ctx.PrimaryTarget.CurrentHPPercent * 100f:F0}%)"
                : "없음";
            string caster = ctx.Caster?.Transform.name ?? "?";
            Debug.Log($"[Skill] {caster} → <b>{skill.DisplayName}</b>({skill.SkillId}) | 대상: {target} | CD: {GetEffectiveCooldown(skill):F1}s (base {skill.Cooldown}s ×{_cooldownMultiplier:F2})\n" +
                      $"  실행 로그: {string.Join(" → ", ctx.GetLog())}");
        }

        return true;
    }

    // ── 쿨타임 강제 초기화 (패링 보상, 퍼크 등) ─────────────
    public void ResetCooldown(string skillId) =>
        _lastUseTimes.Remove(skillId);

    // ── 적중 기록 — 내부 ──────────────────────────────────────
    private void RecordAttempt(string skillId)
    {
        _attemptCounts[skillId] = GetUseCount(skillId) + 1;
        _skillHistory.Add(skillId);
    }

    private void RecordHit(string skillId)
    {
        _hitCounts.TryGetValue(skillId, out int h);
        _hitCounts[skillId] = h + 1;
        TotalHitCount++;
    }

    public int TotalHitCount { get; private set; }

    public void ResetAll()
    {
        _lastUseTimes.Clear();
        _attemptCounts.Clear();
        _hitCounts.Clear();
        _skillHistory.Clear();
        TotalHitCount = 0;
        _cooldownMultiplier = 1f;
    }

    // ── 적중 기록 — 외부 조회 ───────────────────────────────────
    public float GetHitRate(string skillId)
    {
        int attempts = GetUseCount(skillId);
        if (attempts == 0) return 0f;
        _hitCounts.TryGetValue(skillId, out int hits);
        return (float)hits / attempts;
    }

    public int GetUseCount(string skillId) =>
        _attemptCounts.TryGetValue(skillId, out int a) ? a : 0;

    public string[] GetLastNSkillIds(int count)
    {
        var result = new string[count];
        int start  = Mathf.Max(0, _skillHistory.Count - count);
        int len    = Mathf.Min(count, _skillHistory.Count);
        for (int i = 0; i < len; i++)
            result[count - len + i] = _skillHistory[start + i];
        return result;
    }

    public int TotalUseCount => _skillHistory.Count;

    // ── 내부 ─────────────────────────────────────────────────
    private float GetLastUseTime(SkillDefinition skill) =>
        _lastUseTimes.TryGetValue(skill.SkillId, out float t) ? t : float.NegativeInfinity;
}
