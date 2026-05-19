# ML Training Environment Handoff — Arena Combat 2P Boss Fight

> **목적**: 운영(production) 프로젝트와 학습(training) 프로젝트의 ML-Agent 사양을 1:1 동기화. 학습 완료 후 ONNX drop-in 작동 보장.
>
> **수령자**: 학습 클라이언트 담당자/AI
> **작성**: 운영 측 (Arena Combat 본 프로젝트)
> **버전**: BAL-1 (2026-05-18 발란스 갱신 반영, Option A 대칭 obs)

---

## 0. 한 줄 요약

보스의 **이동만** ML로 학습한다. 보스 스킬 선택은 운영 서버의 SkillManager auto-cast가 처리한다. ONNX 모델은 하나(단일 통합), 모든 archetype 페어를 학습한다. Action 1-branch [5], Observation 129 channels.

---

## 1. Architecture Overview

```
[운영(production) — 사람이 실제 플레이]
  Player (HUMAN) × 2  ←→  Boss
                          ├── Movement: BossInferenceAgent + ONNX (ML 추론)
                          └── Skill:    SkillManager.AutoCast (서버 가중치)
                                        BossAdaptiveWeights + BossAIDefinition.slotWeights

[학습(training) — BT 봇이 대신 플레이]
  BTPlayerAgent (P1) × 1 + BTPlayerAgent (P2) × 1  ←→  Boss
                                                     ├── Movement: BossInferenceAgent (학습 중)
                                                     └── Skill:    학습 환경에도 SkillManager.AutoCast 동일하게
```

**핵심 원칙**: 학습 환경에서 보스가 마주칠 상황을 운영 환경과 완전 동일하게 재현. 차이가 있으면 ONNX drop-in 시 정책 부정확.

---

## 2. 수정 가능 / 불가능 항목

### ✅ 학습 측 자유롭게 수정 가능

| 항목 | 비고 |
|------|------|
| BT player agent 내부 로직 | archetype별 행동 패턴, 의사결정 트리 |
| Reward shaping 세부 가중치 | 사양서 §6 기본 형태 따르되 미세 조정 OK |
| Hyperparameters | learning_rate, batch_size 등 |
| 학습 환경 시각화 / 카메라 / 로깅 | 운영 무관 |
| max_steps, summary_freq, checkpoint_interval | 학습 시간 조정 |
| 학습 씬 자체 | training scene 별도 사용 OK |

### ❌ 운영과 정확히 일치 필수 (수정 시 ONNX drop-in 실패)

| 항목 | 값 |
|------|----|
| **VectorObservationSize** | 129 |
| **NumStackedVectorObservations** | 1 |
| **BranchSizes** | [5] |
| **VectorActionSize** | [5] |
| **정규화 상수** | _maxDistance=55, _maxCooldown=30, _maxSpeed=16, _maxBurstDmg=80, _maxBossPhase=4 |
| **Observation 채널 순서/의미** | 본 문서 §5의 정확한 순서 |
| **Action 인덱스 의미** | 본 문서 §4의 정확한 매핑 |
| **속도 값** | Player 14, Boss 8.4 (페이즈별 8.4/8.4/9.2/10.1) |
| **HP 값** | Player 150, Boss 6000 |
| **스킬 값** | 본 문서 §8의 모든 스킬 cone/range/damage/telegraph |
| **카드 드래프트 cadence** | 180s × 4 라운드 |
| **Episode 길이** | 720초 (12분) |
| **페이즈 임계** | 75% / 50% / 25% HP → Phase 2/3/Enrage |
| **페이즈 배율** | cooldown 0.85/0.7/0.5, damage 1.08/1.16/1.25, speed 1.0/1.1/1.2 |
| **SkillDefinition AI hint 필드** | AIHint_ConeOrAoE, AIHint_Category (운영 측에서 23 SO 채움) |

---

## 3. Agent 구조

- **클래스**: `BossInferenceAgent` (단일 통합)
- **변경 사항**: 이전 `SpecializedBossAgent` (4 프리셋 VsMelee/Ranged/Hybrid/CC 분리 학습) **폐기**. VsRanged_Q4 등 학습 자산은 참조용으로만 보관.
- **ONNX**: 1개. 모든 archetype 페어 (10가지) 균등 학습.
- **학습 책임**: 보스 이동만.
- **스킬 선택**: 운영의 SkillManager.AutoCast가 처리 (서버 가중치 — BossAdaptiveWeights + BossAIDefinition.slotWeights). 학습 ML은 관여 X.

---

## 4. Action Space

### BehaviorParameters
```yaml
m_ActionSpec:
  m_NumContinuousActions: 0
  BranchSizes: [5]            # 1 branch
VectorActionSize: [5]
```

### Branch 0 — Movement (5 actions)

| Index | 의미 | 구현 |
|-------|------|------|
| 0 | idle | (아무것도 안함) |
| 1 | forward | `position += transform.forward * _moveSpeed * dt` |
| 2 | turn left | `Rotate(0, -_rotationSpeed * dt, 0)` |
| 3 | turn right | `Rotate(0, +_rotationSpeed * dt, 0)` |
| 4 | **backward** | `position += -transform.forward * _moveSpeed * 0.7 * dt` |

후퇴 속도: forward의 **70%** (Souls 류 retreat 표준).

### 제거된 항목 (학습 측 코드에서 모두 제거)
- 이전 B1 (skill) 브랜치 전체
- `TryExecuteSkill` 메서드
- `WriteDiscreteActionMask`의 skill 마스킹 부분
- `Heuristic`의 Alpha1~5 (skill 키)

---

## 5. Observation Space — 총 129 channels

### VectorObservationSize: 129

### Section 1: Position relations (11 ch)

**Phase 1** (#0~5, 6 ch — P1 관련):
```
#0  P1 direction.x         (boss → P1 단위벡터)
#1  P1 direction.z
#2  P1 distance            / 55 (_maxDistance)
#3  boss forward.x
#4  boss forward.z
#5  dot(boss forward, P1 dir)   범위 -1~1
```

**Phase 2** (#6~10, 5 ch — P2 관련):
```
#6   P2 direction.x
#7   P2 direction.z
#8   P2 distance            / 55
#9   P1 ↔ P2 distance       / 55
#10  dot(boss forward, P2 dir)
```

### Section 2: Boss state (4 ch)

```
#11  boss HP%               0~1
#12  P1 HP%                 0~1
#13  P2 HP%                 0~1
#14  phase / 4              (Phase1~Enrage = 1~4)
```

### Section 3: Boss skill slots (5 슬롯 × 7 ch = 35 ch)

각 슬롯 (slot 0~4) per slot 7 channel:
```
+0  remaining cooldown      / 30 (_maxCooldown)
+1  max cooldown            / 30
+2  range                   / 55
+3  cone/AoE                cone: deg/180, AoE: meters/25
                            (projectile/self: 0)
+4  category dim 0: directional   (one-hot)
+5  category dim 1: aoe            (one-hot)
+6  category dim 2: projectile     (one-hot)
                            (셋 다 0이면 self/buff)
```

채널 매핑:
- slot 0: #15~21
- slot 1: #22~28
- slot 2: #29~35
- slot 3: #36~42
- slot 4: #43~49

### Section 4: Touch flags (2 ch)

```
#50  P1 근접 진입 플래그 (boss → P1 distance < meleeRange)   binary 0/1
#51  P2 근접 진입 플래그                                       binary 0/1
```

`meleeRange` = 5m (운영 PlayerArchetypeClassifier와 동일).

### Section 5: Player skill slots (P1, P2 각 5슬롯 × 7 ch = 70 ch)

**보스 슬롯과 완전 동일 구조** (대칭). 각 슬롯 per 7 channel:
```
+0  range                   / 55
+1  remaining cooldown      / 30
+2  max cooldown            / 30
+3  cone/AoE                cone: deg/180, AoE: meters/25
+4  category dim 0: directional
+5  category dim 1: aoe
+6  category dim 2: projectile
```

채널 매핑:
- P1 slot 0~4: #52~86 (35 ch)
- P2 slot 0~4: #87~121 (35 ch)

이유: 보스가 "플레이어가 무엇을 들고 있는지" 정확히 파악해야 spacing/회피 정책 학습 가능.

### Section 6: Extra (7 ch)

```
#122  P1 IsCasting                    binary
#123  P2 IsCasting                    binary
#124  P1 avg speed                    / 16 (_maxSpeed)
#125  P2 avg speed                    / 16
#126  P1 unlocked slot count          / 5
#127  P2 unlocked slot count          / 5
#128  보스 최근 1초 burst 피격 dmg     / 80 (_maxBurstDmg)
```

### 총 합계 확인
```
11 (position) + 4 (boss state) + 35 (boss slots) + 2 (touch)
+ 70 (player slots) + 7 (extra) = 129 ✓
```

---

## 6. SkillDefinition 필드 추가 (운영 측 진행 — 학습 측 참고)

운영 측이 `SkillDefinition.cs`에 다음 필드 추가:

```csharp
[Header("AI Observation Hints")]
public float AIHint_ConeOrAoE;    // cone: 각도(deg) / AoE: 반경(m) / projectile/self: 0
public SkillCategoryFlag AIHint_Category;

public enum SkillCategoryFlag : byte {
    None = 0,
    Directional = 1,   // DealDirectionalHit 사용
    AoE = 2,           // ApplyInArea / SpawnPersistentArea 사용
    Projectile = 3,    // LaunchProjectile 사용
    Self = 4,          // TriggerOnCondition (Self target)
}
```

운영 측이 23개 SkillDefinition SO에 다음 값으로 채움 (학습 측 참고용 표 — 학습 SO도 동일 값):

### Boss skills (11개)

| Skill | AIHint_ConeOrAoE | AIHint_Category |
|-------|------------------|------------------|
| ExecutionSpike_Boss | 38 (deg) | Directional |
| CrushingBarrage_Boss | 40 | Directional |
| FortressArmor_Boss | 35 | Directional |
| CollapseRoar_Boss | 12 (m) | AoE |
| MarkWave_Boss | 65 | Directional (cone) |
| BarrierBreaker_Boss | 0 | Projectile |
| ErosionField_Boss | 20 (outer AoE m) | AoE |
| SurvivalPulse_Boss | 0 | Self |
| OverchargeMode_Boss | 0 | Self |
| SealChain_Boss | 0 | None (UNIMPL) |
| RuptureMagazine_Boss | 0 | None (UNIMPL) |

### Player skills (12개)

| Skill | AIHint_ConeOrAoE | AIHint_Category |
|-------|------------------|------------------|
| PiercingShot | 0 | Projectile |
| ExecutionSpike | 32 | Directional |
| CrushingBarrage | 38 | Directional |
| FortressArmor | 38 | Directional |
| CollapseRoar | 12 (m) | AoE |
| BarrierBreaker | 0 | Projectile |
| HuntingMark | 0 | Projectile |
| ErosionField | 9 (m, AoE 부분) | Projectile + AoE → Projectile (영향 카테고리만) |
| SealChain | 0 | Projectile |
| RuptureMagazine | 0 | Projectile |
| SurvivalPulse | 0 | Self |
| OverchargeMode | 0 | Self |

---

## 7. Episode 구조

### Episode 시작 (OnEpisodeBegin)

1. **Archetype 랜덤 할당**:
   - P1 archetype 균등 25% 각: {Melee, Ranged, CC, Hybrid}
   - P2 archetype 독립 균등 25% 각
   - 결과: 10가지 페어 (대칭 4 + 혼합 6) 자연 분포
2. **초기 스킬 (slot 0)** 자동 장착:
   - Melee → CrushingBarrage 또는 ExecutionSpike (랜덤 50%)
   - Ranged → PiercingShot 또는 BarrierBreaker (랜덤 50%)
   - CC → SealChain 또는 HuntingMark
   - Hybrid → 위 6개 중 랜덤
3. **Boss spawn**: 맵 중앙, HP 6000
4. **Player spawn**: HP 150, MoveSpeed 14 m/s

### Episode 진행 (12분)

- **T = 180s, 360s, 540s**: P1/P2 각각 카드 드래프트 발동
  - 학습 환경에서는 카드 풀 11개 (initial 제외) 중 archetype별 가중 추출
  - P1/P2 슬롯 1, 2, 3 차례로 채움
- **Boss 페이즈**: 보스 HP 75%/50%/25% 마크에 Phase 2/3/Enrage 전환
  - Phase 1: speed 1.0x (8.4m/s), cd 1.0x, dmg 1.0x, telegraph 1.35~1.6s
  - Phase 2: speed 1.0x, cd 0.85x, dmg 1.08x, telegraph 1.2~1.45s
  - Phase 3: speed 1.1x (9.2m/s), cd 0.7x, dmg 1.16x, telegraph 1.05~1.3s
  - Enrage: speed 1.2x (10.1m/s), cd 0.5x, dmg 1.25x, telegraph 0.95~1.2s

### Episode 종료 조건

| 조건 | outcome | Reward |
|------|---------|--------|
| Boss HP ≤ 0 | "PlayersWin" | -1.0 |
| P1 + P2 모두 사망 | "BossWin" | +1.0 |
| T ≥ 720s | "Timeout" | -0.3 |

---

## 8. 게임 사양 (학습 환경과 운영 환경 완전 동일)

### 속도
- Player MoveSpeed: **14 m/s**
- Boss MoveSpeed: **8.4 m/s** base, 페이즈별 1.0/1.0/1.1/1.2
- Boss rotation: **540°/s**, telegraph 시작 시 lock
- **Boss 텔레그래프(IsBusy) 중 이동 정지** (ApplyMovement에 게이트 필수)

### HP
- Player HP: 150
- Boss HP: 6000

### Boss Skills (11개, 5 슬롯 자동시전)

| Skill | CD | Telegraph(P1) | Range | Cone/AoE | Damage |
|-------|----|------|------|----|----|
| ExecutionSpike_Boss | 10s | 1.35s | 18m | cone 38° | 76 |
| CrushingBarrage_Boss | 10s | 1.2s | 15m | cone 40° | 20 × 4 hits |
| FortressArmor_Boss | 12s | 1.3s | 16m | cone 35° | 65 |
| CollapseRoar_Boss | 14s | 1.6s | 24m gate | AoE 12m | 55 × 2 hits |
| MarkWave_Boss | 12s | 1.4s | 22m | cone 65° | 50 |
| BarrierBreaker_Boss | 14s | 1.4s | 48m gate / 40m projectile | pierce | 62 |
| ErosionField_Boss | 14s | 1.6s | 22m gate | AoE 8m+20m | 8 DoT/s |
| SurvivalPulse_Boss | 20s | 0 | Self | — | HP×5% recover |
| OverchargeMode_Boss | 18s | 0 | Self | — | +12% dmg / -12% def |
| SealChain_Boss | — | — | — | UNIMPLEMENTED | — |
| RuptureMagazine_Boss | — | — | — | UNIMPLEMENTED | — |

### Player Skills (12개, BT가 자동시전)

| Skill | CD | Range | Damage | 형태 |
|-------|----|------|------|------|
| PiercingShot | 10s | 60m | 110 | projectile 40m/s pierce |
| ExecutionSpike | 8s | 24m | 100 | 멜리 cone 32° |
| CrushingBarrage | 6s | 24m | 28 × 4 | 멜리 cone 38° |
| FortressArmor | 8s | 24m | 75 | 멜리 cone 38° |
| CollapseRoar | 10s | 27m | 80 × 2 | AoE 12m |
| BarrierBreaker | 8s | 66m | 88 | projectile 30m/s |
| HuntingMark | 7s | 66m | 60 | projectile 35m/s |
| ErosionField | 10s | 42m | 8 DoT/s | projectile 28m/s + AoE 9m |
| SealChain | 9s | 66m | 50 | projectile 30m/s CC |
| RuptureMagazine | 9s | 66m | 95 | projectile 30m/s |
| SurvivalPulse | 14s | Self | HP × 6% | HP<30% trigger |
| OverchargeMode | 18s | Self | +15% / -10% | HP<50% trigger |

**Projectile detection radius**: 모두 1.0m (기존 0.5m 변경).

---

## 9. BT Player Agent (학습 환경 상대, 자유롭게 구성 가능)

P1, P2 각각 **독립** BT (이전 동일 강제 제거).

### Archetype별 동작 (참고용 — 학습 측 자유롭게 정밀화 가능)

| Archetype | 거리 유지 | 우선 스킬 | 보조 |
|-----------|----------|---------|------|
| **Melee** | 보스 4m 이내 closing | 멜리 (CrushingBarrage, ExecutionSpike, FortressArmor) | CollapseRoar AoE |
| **Ranged** | 보스 12m+ kiting | projectile (PiercingShot, RuptureMagazine) | BarrierBreaker shield break |
| **CC** | 보스 8~14m | CC (SealChain, HuntingMark) | 디버프 추적 |
| **Hybrid** | 동적 (위 3개 mix 30/30/30, Self 10) | 랜덤 가중치 | — |

### 공통 BT 파라미터

```
MoveSpeed: 14 m/s (운영과 동일, 필수)
FleeHPThreshold: 0.3 (HP 30% 미만 후퇴)
ParryTendency: 0.1 (parry 미구현이라 낮게)
TickInterval: 0.1s
```

---

## 10. Reward Shape — 단순화 (사양서 §6)

이전 SpecializedBossAgent의 15+ 풍부 reward 폐기. 다음만 사용:

| 신호 | 값 | 트리거 |
|------|----|----|
| **Win** | +1.0 | 양쪽 플레이어 사망 (AllPlayersDead) |
| **Loss** | -1.0 | 보스 사망 (BossDefeated) |
| **Timeout** | -0.3 | 12분 경과 |
| **Damage dealt** | +0.001 × damage | 보스가 가한 effective 데미지 |
| **Damage taken** | -0.0005 × damage | 보스가 받은 데미지 |
| **Phase progress** | +0.05 | Phase 2/3/Enrage 첫 진입 (각 1회) |
| **Step penalty** | -0.0001 | 매 step (효율 유도) |

**제거**: 거리/정렬/idle/wallStay/counterAttack/retreat penalty 등 모두.
보스가 "직관 정책" 학습 — 너무 많은 reward shaping은 overfit 위험.

---

## 11. Hyperparameters (config.yaml)

```yaml
behaviors:
  BossInferenceAgent:
    trainer_type: ppo
    hyperparameters:
      batch_size: 1024
      buffer_size: 10240
      learning_rate: 3.0e-4
      beta: 5.0e-3
      epsilon: 0.2
      lambd: 0.95
      num_epoch: 3
    network_settings:
      normalize: false       # 우리가 obs 안에서 직접 정규화
      hidden_units: 256
      num_layers: 3
    reward_signals:
      extrinsic:
        gamma: 0.995          # 12분 episode 대응
        strength: 1.0
    time_horizon: 1024
    max_steps: 40000000       # 40M (obs 129 ch, 1.5~3일 GPU)
    summary_freq: 50000
    checkpoint_interval: 200000

init_from: null               # 새 학습 (아키텍처 변경, 이전 가중치 사용 불가)
```

`init_from: null` 이유: hidden_units 변경(128→256) + observation 차원 변경(55→129) + action 변경(2-branch→1-branch). 수학적으로 weight 이어쓰기 불가.

---

## 12. CSV Logging (Episode 종료 시)

`results/episodes.csv` 형식:

```csv
episode_id, p1_archetype, p2_archetype, outcome, duration_sec,
boss_hp_remaining, total_damage_to_boss, total_damage_to_players,
p1_skills_unlocked, p2_skills_unlocked, phase_reached
```

| 필드 | 예시 | 의미 |
|------|------|------|
| episode_id | 12345 | 순번 |
| p1_archetype | "Melee" | P1 archetype |
| p2_archetype | "Ranged" | P2 archetype |
| outcome | "PlayersWin" / "BossWin" / "Timeout" | 결과 |
| duration_sec | 642 | 실제 episode 시간 |
| boss_hp_remaining | 0 (사망) 또는 잔여 HP | |
| total_damage_to_boss | 6000 | 누적 |
| total_damage_to_players | 312 | P1+P2 합 |
| p1_skills_unlocked | 4 | 종료 시점 P1 슬롯 |
| p2_skills_unlocked | 3 | 종료 시점 P2 슬롯 |
| phase_reached | "Enrage" | 도달 최고 페이즈 |

**용도**: 페어별 승률 분석 (대칭 4 + 혼합 6 = 10가지). ONNX 적용 전 모델 평가 필수.

---

## 13. 학습 완료 후 ONNX 적용 흐름

1. **학습 측**:
   - 40M step 학습 완료 (1.5~3일 GPU)
   - TensorBoard에서 Episode Reward 안정 수렴 확인 (보스 승률 30~50% 목표)
   - ONNX export (movement-only 1-branch)
   - 운영 측에 ONNX 파일 전달
2. **운영 측**:
   - `Boss.prefab`의 BossInferenceAgent 컴포넌트 → `_model` 또는 `_brain` 필드에 새 ONNX 할당
   - `BossInferenceAgent.enabled = true` 설정
   - 정합성 체크리스트 (§14) 검증
   - 플레이테스트

---

## 14. 정합성 체크리스트 (ONNX 적용 전 확인)

학습 환경 ↔ 운영 환경이 다음 값들에 정확히 일치하는지 확인:

- [ ] **VectorObservationSize**: 학습 == 운영 == 129
- [ ] **NumStackedVectorObservations**: 1
- [ ] **BranchSizes**: [5] 양측 동일
- [ ] **Observation 채널 순서**: §5의 정확한 순서 (Section 1~6, #0~128)
- [ ] **정규화 상수**: _maxDistance=55, _maxCooldown=30, _maxSpeed=16, _maxBurstDmg=80, _maxBossPhase=4
- [ ] **속도**: Player 14, Boss 8.4 (페이즈별)
- [ ] **HP**: Player 150, Boss 6000
- [ ] **23개 SkillDefinition SO** AIHint_ConeOrAoE, AIHint_Category 값이 §6 표와 일치
- [ ] **모든 스킬 cone/range/damage/telegraph** §8 표와 일치
- [ ] **카드 드래프트 cadence** 180s × 4 라운드
- [ ] **Episode 길이** 720s
- [ ] **페이즈 임계** 75%/50%/25%
- [ ] **CSV 로그**에서 10가지 archetype 페어 모두 ≥ 1000회 학습 노출
- [ ] **비대칭 페어** (MR, MC 등) 승률 ± 15%p 이내
- [ ] **ONNX export** 성공: input 129 float, output 5 discrete

불일치 발견 시 학습 측에서 수정 → 재학습. 운영 측 수정으로 해결하면 정합 깨짐.

---

## 15. 진행 상황 공유 채널

- **운영 측 변경 사항**: 별도 메시지로 운영 진행 단계 공유 (T1C obs 확장, T2 데미지, T3 페이즈 등)
- **학습 측 진행 사항**: 학습 시작/checkpoint/완료 시점 운영 측에 공유 요청
- **양측 동기화 시점**: 운영 측이 SkillDefinition AI hint 필드 + 23 SO 값 확정 후 학습 측에 SO 동기화 (또는 같은 값 수동 입력)

---

## 16. 폐기 / 변경 / 신규 요약

### 폐기 (학습 측 코드에서 제거)
- SpecializedBossAgent.cs (4 프리셋 분리 학습)
- VsMelee/Ranged/Hybrid/CC ONNX 4개 (참조용 보관만, 사용 안 함)
- B1 스킬 액션 브랜치 (DiscreteActions[1])
- TryExecuteSkill, WriteDiscreteActionMask skill 부분, Heuristic Alpha1~5
- 풍부 reward shaping 15+ 항목
- TrainingSkillManager의 15초 간격 자동 해금 (180s draft로 대체)

### 변경
| 항목 | 이전 | 새 |
|------|------|----|
| Action | [4, 4] | [5] |
| Observation | 55 | 129 |
| 보스/플레이어 슬롯 | 3 | 5 |
| Episode | 60s | 720s |
| 카드 시스템 | 15s 자동 해금 | 180s × 4 draft |
| Phase | 1~2 (_maxBossPhase=2) | 1~4 (_maxBossPhase=4) |
| P1/P2 archetype | 동일 강제 | 독립 |
| hidden_units | 128 | 256 |
| max_steps | 5M | 40M |
| gamma | 0.99 | 0.995 |
| time_horizon | 64 | 1024 |

### 신규
- Backward 액션 (idx 4)
- SkillDefinition AI hint 필드 2개 + 23 SO 값 채움
- Touch obs 2 ch
- 5 슬롯 기반 PlayerSkills obs 대칭 (70 ch)
- 129 ch obs space 일관화

---

## 17. 문의 / 명확화 채널

운영 측 사양에 대해 모호한 점, 학습 진행 중 발견된 정합성 위반, 새 요구사항 등은 운영 측 담당자에게 즉시 보고 부탁드립니다.

**중요**: 학습 환경에서 임의로 수정 가능한 항목 (§2)과 운영 일치 필수 항목 (§2)을 혼동하지 않도록 주의. 잘못된 수정은 ONNX drop-in 실패의 원인.

---

**문서 끝**
