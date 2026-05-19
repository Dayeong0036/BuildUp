using static SkillComponents;

// 스킬 조립 코드 — SkillDefinition.RuntimeStep 에 주입할 SkillStep 을 부품으로 조립
// SKILL_DESIGN.md 의 조합식을 코드로 변환한다
//
// 사용법:
//   skillDef.RuntimeStep = SkillLibrary.ErosionField_Boss();
//
// 참고사항:
//  • LaunchProjectile : 기획식 (사거리, 속도) → 코드 (speed, range) 로 변환
//  • homing 플래그    : 기획식에 있으나 코드 시그니처에 없음 — 무시
//  • 비율형 값(%)     : ApplyVulnerability/AntiHeal/Slow 등은 0~1 decimal로 변환 (5 → 0.05f)
//  • 정수형 값(%)     : ApplyDamageUp/DefenseDown 등은 그대로 전달 (15 → 15f)
//  • "최대HP의X%"     : 런타임에 ctx.Caster.MaxHP 기반으로 계산
public static class SkillLibrary
{
    // ══════════════════════════════════════════════════════════════
    // ───────────────  플레이어 공용 (12종)  ───────────────
    // ══════════════════════════════════════════════════════════════

    // 처형 송곳 — 좁은 범위 정확 타격
    public static SkillStep ExecutionSpike() =>
        ctx =>
        {
            DealDirectionalHit(100f, 15f, 32f).Invoke(ctx);
            TriggerOnHit(
                onHit: hit =>
                {
                    ApplyVulnerability(2f, 0.05f).Invoke(hit);
                    ExecuteBelowHP(30f, 20f).Invoke(hit);
                }
            ).Invoke(ctx);
        };

    // 분쇄 연타 — 4단 다단 히트, 적중 시 실드 파괴 추가타
    public static SkillStep CrushingBarrage() =>
        ctx =>
        {
            DealDirectionalHit(28f, 13.6f, 38f).Invoke(ctx);
            TriggerOnHit(
                onHit: hit =>
                {
                    DealMultiHitDamage(28f, 4).Invoke(hit);
                    DealShieldBreakDamage(45f, 1.2f).Invoke(hit);
                }
            ).Invoke(ctx);
        };

    // 침식 장판 (플레이어) — 투사체 명중 지점에 2중 장판 생성
    public static SkillStep ErosionField() =>
        LaunchProjectile(28f, 42f, false,
            impact =>
            {
                SpawnPersistentArea(4f, 9f, AreaShape.Circle, 1f,
                    tick =>
                    {
                        ApplyDamageOverTime(1f, 7f).Invoke(tick);
                        ApplyAntiHeal(1f, 0.20f).Invoke(tick);
                    }
                ).Invoke(impact);

                SpawnPersistentArea(4f, 20f, AreaShape.Circle, 1f,
                    tick => ApplyDamageOverTime(1f, 7f).Invoke(tick)
                ).Invoke(impact);
            }
        );

    // 사냥 표식 — 단일 투사체, 명중 시 피해 + 표식 디버프
    public static SkillStep HuntingMark() =>
        LaunchProjectile(35f, 48f, false,
            hit =>
            {
                DealDamage(60f).Invoke(hit);
                ApplyDebuff(4f, DebuffType.Mark, 1f).Invoke(hit);
            }
        );

    // 생존 맥동 — 저체력 시 회복 + 재생 + 정화
    public static SkillCondition SurvivalPulseCondition() =>
        ctx => ctx.Caster != null && ctx.Caster.CurrentHPPercent < 0.3f;

    public static SkillStep SurvivalPulse() =>
        TriggerOnCondition("LowHP",
            cond => cond.Caster != null && cond.Caster.CurrentHPPercent < 0.3f,
            onTrue: ctx =>
            {
                float maxHp = ctx.Caster?.MaxHP ?? 0f;
                RecoverHP(maxHp * 0.06f).Invoke(ctx);
                ApplyHPRegen(3f, maxHp * 0.012f).Invoke(ctx);
                CleanseStatus(CleanseType.All, 1).Invoke(ctx);
            }
        );

    // 요새 장갑 — 전방 타격, 적중 시 실드 획득
    public static SkillStep FortressArmor() =>
        ctx =>
        {
            DealDirectionalHit(75f, 14.4f, 38f).Invoke(ctx);
            TriggerOnHit(
                onHit: hit =>
                {
                    float maxHp = hit.Caster?.MaxHP ?? 0f;
                    GainShield(maxHp * 0.06f).Invoke(hit);
                }
            ).Invoke(ctx);
        };

    // 봉쇄 사슬 — 유도 투사체, 적중 시 피해 + 경직 + 침묵
    public static SkillStep SealChain() =>
        LaunchProjectile(30f, 48f, false,
            hit =>
            {
                DealDamage(50f).Invoke(hit);
                ApplyHitStun(0.08f).Invoke(hit);
                ApplySilence(0.5f).Invoke(hit);
            }
        );

    // 붕괴 포효 — AoE 2연타
    public static SkillStep CollapseRoar() =>
        ctx =>
        {
            ApplyInArea(12f, AreaShape.Circle,
                inner =>
                {
                    DealMultiHitDamage(80f, 2).Invoke(inner);
                    ApplyDefenseDown(2f, 5f).Invoke(inner);
                }
            ).Invoke(ctx);
        };

    // 방벽 파쇄 — 실드 파괴 + 방어력 감소 + 본 피해
    /// <summary>
    /// /
    /// </summary>
    /// <returns></returns>
    public static SkillStep BarrierBreaker() =>
        LaunchProjectile(30f, 42f, false,
            hit =>
            {
                DealShieldBreakDamage(60f, 1.4f).Invoke(hit);
                ApplyDefenseDown(3f, 6f).Invoke(hit);
                DealDamage(88f).Invoke(hit);
            }
        );

    // 과충전 모드 — 저체력/페이즈 진입 시 공격 강화 + 자해형 디버프
    public static SkillCondition OverchargeModeCondition() =>
        ctx => ctx.Caster != null && ctx.Caster.CurrentHPPercent < 0.5f;

    public static SkillStep OverchargeMode() =>
        TriggerOnCondition("CombatPhaseOrLowHP",
            cond => cond.Caster != null && cond.Caster.CurrentHPPercent < 0.5f,
            onTrue: ctx =>
            {
                ApplyDamageUp(6f, 15f).Invoke(ctx);
                ApplyDebuff(6f, DebuffType.SelfDefenseDown, 10f).Invoke(ctx);
            }
        );

    // 관통 저격 — 직선 관통 투사체
    // NoCoreHit 재발사는 약점 판정 시스템 구현 후 복원
    public static SkillStep PiercingShot() =>
        LaunchProjectile(40f, 60f, true,
            hit =>
            {
                ApplyDefenseDown(3f, 8f).Invoke(hit);
                DealDamage(110f).Invoke(hit);
            }
        );

    // 파열 탄창 — 폭발 투사체
    // NoCoreHit 재발사는 약점 판정 시스템 구현 후 복원
    public static SkillStep RuptureMagazine() =>
        LaunchProjectile(30f, 48f, false,
            hit =>
            {
                ApplyVulnerability(3f, 0.10f).Invoke(hit);
                DealDamage(95f).Invoke(hit);
            }
        );

    // ══════════════════════════════════════════════════════════════
    // ───────────────  플레이어 전용 (6종)  ───────────────
    // ══════════════════════════════════════════════════════════════

    // 응수 태세 — 미구현
    // 사유: StatManager.BeginParryWindow() 호출부(패링 입력 바인딩)가 없어 IsParrying 이 영원히 false
    //       → CheckParry() 가 HitLanded=false → 후속 보상 미발동. 패링 입력 시스템 구축 후 복원.
    public static SkillStep CounterStance() => null;

    // 패링 강화 — 패링 윈도우 + 보상 강화 버프
    public static SkillStep ParryEnhance() =>
        ctx =>
        {
            ApplyBuff(5f, BuffType.ParryWindowUp, 15f).Invoke(ctx);
            ApplyBuff(5f, BuffType.ParryRewardUp, 10f).Invoke(ctx);
        };

    // 반격의 일섬 — 미구현
    // 사유: CounterStance 와 동일 — 패링 입력 바인딩 부재
    public static SkillStep CounterSlash() => null;

    // 와이어 훅 — 미구현
    // 사유: RopeLanding 조건 판정 시스템(로프 이동 완료 이벤트)이 없음. MoveSelf 는 가능하나
    //       착지 후 분기 발동이 불가능 — 로프 이벤트 시스템 구축 후 복원.
    public static SkillStep WireHook() => null;

    // 로프 충격파 — 미구현
    // 사유: WireHook 와 동일 — RopeLanding 이벤트 부재
    public static SkillStep RopeShockwave() => null;

    // 붕괴 타격 — 미구현
    // 사유: ParrySuccessRecent (최근 패링 성공 이력) 추적 시스템 부재. 패링 입력 바인딩도 없음.
    public static SkillStep CollapseStrike() => null;

    // ══════════════════════════════════════════════════════════════
    // ───────────────  보스 공용 (11종)  ───────────────
    // ══════════════════════════════════════════════════════════════

    // 처형 송곳 (보스)
    public static SkillStep ExecutionSpike_Boss() =>
        ctx =>
        {
            DealDirectionalHit(76f, 18f, 38f).Invoke(ctx);
            TriggerOnHit(
                onHit: hit =>
                {
                    ApplyVulnerability(2.5f, 0.06f).Invoke(hit);
                    ExecuteBelowHP(30f, 26f).Invoke(hit);
                }
            ).Invoke(ctx);
        };

    // 분쇄 연타 (보스)
    public static SkillStep CrushingBarrage_Boss() =>
        ctx =>
        {
            DealDirectionalHit(20f, 15f, 40f).Invoke(ctx);
            TriggerOnHit(
                onHit: hit =>
                {
                    DealMultiHitDamage(20f, 4).Invoke(hit);
                    DealShieldBreakDamage(55f, 1.35f).Invoke(hit);
                }
            ).Invoke(ctx);
        };

    // 침식 장판 (보스) — 시전 위치에 2중 원형 장판
    public static SkillStep ErosionField_Boss() =>
        ctx =>
        {
            SpawnPersistentArea(4f, 8f, AreaShape.Circle, 1f,
                tick =>
                {
                    ApplyDamageOverTime(1f, 8f).Invoke(tick);
                    ApplyAntiHeal(1f, 0.20f).Invoke(tick);
                }
            ).Invoke(ctx);

            SpawnPersistentArea(4f, 20f, AreaShape.Circle, 1f,
                tick => ApplyDamageOverTime(1f, 8f).Invoke(tick)
            ).Invoke(ctx);
        };

    // 생존 맥동 (보스)
    public static SkillCondition SurvivalPulseCondition_Boss() =>
        ctx => ctx.Caster != null && ctx.Caster.CurrentHPPercent < 0.3f;

    public static SkillStep SurvivalPulse_Boss() =>
        TriggerOnCondition("LowHP",
            cond => cond.Caster != null && cond.Caster.CurrentHPPercent < 0.3f,
            onTrue: ctx =>
            {
                float maxHp = ctx.Caster?.MaxHP ?? 0f;
                RecoverHP(maxHp * 0.05f).Invoke(ctx);
                ApplyHPRegen(3f, maxHp * 0.010f).Invoke(ctx);
                CleanseStatus(CleanseType.All, 1).Invoke(ctx);
            }
        );

    // 요새 장갑 (보스)
    public static SkillStep FortressArmor_Boss() =>
        ctx =>
        {
            DealDirectionalHit(65f, 16f, 35f).Invoke(ctx);
            TriggerOnHit(
                onHit: hit =>
                {
                    float maxHp = hit.Caster?.MaxHP ?? 0f;
                    GainShield(maxHp * 0.08f).Invoke(hit);
                }
            ).Invoke(ctx);
        };

    // 붕괴 포효 (보스) — AoE 55×2hit
    public static SkillStep CollapseRoar_Boss() =>
        ctx =>
        {
            ApplyInArea(12f, AreaShape.Circle,
                inner =>
                {
                    DealMultiHitDamage(55f, 2).Invoke(inner);
                    ApplyHitStun(0.16f).Invoke(inner);
                    ApplyDefenseDown(3f, 6f).Invoke(inner);
                }
            ).Invoke(ctx);
        };

    // 과충전 모드 (보스)
    public static SkillCondition OverchargeModeCondition_Boss() =>
        ctx => ctx.Caster != null && ctx.Caster.CurrentHPPercent < 0.5f;

    public static SkillStep OverchargeMode_Boss() =>
        TriggerOnCondition("CombatPhaseOrLowHP",
            cond => cond.Caster != null && cond.Caster.CurrentHPPercent < 0.5f,
            onTrue: ctx =>
            {
                ApplyDamageUp(6f, 12f).Invoke(ctx);
                ApplyDebuff(6f, DebuffType.SelfDefenseDown, 12f).Invoke(ctx);
            }
        );

    // 표식 파동 (보스) — 전방 부채꼴 65도
    public static SkillStep MarkWave_Boss() =>
        ApplyInArea(22f, AreaShape.Cone,
            inner =>
            {
                DealDamage(50f).Invoke(inner);
                ApplyDebuff(4f, DebuffType.Mark, 1f).Invoke(inner);
            },
            angleDeg: 65f
        );

    // 봉쇄 사슬 (보스) — 미구현
    // 사유: 스펙 "4방향 원거리 패턴" 을 위한 멀티 디렉션 투사체 래퍼 부재.
    //       단일 투사체로 축약 시 기획 의도(회피 강제)를 훼손 → 래퍼 추가 후 복원.
    public static SkillStep SealChain_Boss() => null;

    // 방벽 파쇄 (보스)
    public static SkillStep BarrierBreaker_Boss() =>
        LaunchProjectile(19f, 40f, true,
            hit =>
            {
                DealDamage(62f).Invoke(hit);
                DealShieldBreakDamage(70f, 1.5f).Invoke(hit);
                ApplyDefenseDown(4f, 8f).Invoke(hit);
            }
        );

    // 파열 탄창 (보스) — 미구현
    // 사유: 스펙 "8방향 폭발 패턴" 을 위한 멀티 디렉션 래퍼 부재. 단일 축약은 기획 의도 훼손.
    public static SkillStep RuptureMagazine_Boss() => null;
}
