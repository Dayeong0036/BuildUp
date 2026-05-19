using System;
using UnityEngine;

public enum SkillCategoryFlag : byte
{
    None        = 0,
    Directional = 1,
    AoE         = 2,
    Projectile  = 3,
    Self        = 4,
}

// 스킬 하나의 메타데이터 + 런타임 실행 트리
// 메타데이터(SO 직렬화) : 드래프트 UI, AI 선택, SkillRegistry 조회에 사용
// RuntimeStep(NonSerialized) : SkillLibrary 에서 조립 후 주입
[CreateAssetMenu(menuName = "Tenebris/SkillDefinition", fileName = "NewSkill")]
public class SkillDefinition : ScriptableObject
{
    [Header("기본 정보")]
    public string SkillId;
    public string DisplayName;
    [TextArea] public string Description;
    public Sprite Icon;

    [Header("전투 설정")]
    public float      Cooldown;
    public float      Range;
    public TargetType TargetType;

    [Header("AI / 드래프트 메타")]
    // 스킬의 성격 태그 (Burst, DOT, Shield, Parry, Zone ...)
    public string[] RoleTags;
    // 이 스킬이 유리한 상대 태그 — 보스 카운터 선택에 사용
    public string[] CounterTags;

    [Header("AI Observation Hints")]
    public float AIHint_ConeOrAoE;
    public SkillCategoryFlag AIHint_Category;

    // 런타임 실행 트리 — 직렬화 불가, SkillLibrary 에서 주입
    [NonSerialized] public SkillStep RuntimeStep;

    // 실행 전 조건 — null 이면 항상 실행, false 반환 시 쿨타임 미소모
    [NonSerialized] public SkillCondition RuntimeCondition;

    public bool IsReady => RuntimeStep != null;
}
