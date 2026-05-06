－ 2026 한국정보기술학회 하계 종합학술대회 논문집 －

매치업별 특화 강화학습을 활용한 적응형 보스 AI 설계 및 분석

백승열*, 지도교수**

Adaptive Boss AI Design and Analysis through Matchup-Specialized Reinforcement Learning

Seungyoul Baek*, Advisor**

요 약

본 연구는 Unity ML-Agents PPO를 활용하여 2인 협동 보스전 환경에서 플레이어 빌드에 따라 보상 함수를 분리 설계하는 매치업별 특화 강화학습 방법을 제안한다. 55차원 관측 공간과 2-브랜치 이산 행동 공간을 기반으로 원거리, 근접, 하이브리드, CC 매치업 4종을 실험한 결과, VsRanged에서 84.1%, VsMelee에서 59.5%의 승률을 달성하였으나 VsCC에서는 10.3%에 머물러 보상-성과 지표 간 불일치를 확인하였다. 실험을 통해 매치업별 보상 설계의 유효성과 함께, 환경 설계와 스탯 밸런스가 강화학습 성능의 전제 조건임을 확인하였다.

Abstract

This study proposes a matchup-specialized reinforcement learning method for boss AI in a 2-player cooperative boss battle environment using Unity ML-Agents PPO. With a 55-dimensional observation space and a 2-branch discrete action space (movement 4 × skill 4), four matchup types were tested: Ranged, Melee, Hybrid, and CC. VsRanged achieved an 84.1% win rate and VsMelee reached 59.5%, while VsCC showed only 10.3% despite increasing cumulative rewards, suggesting a reward-task metric misalignment. Results demonstrate that matchup-specific reward design is effective, but environment design and stat balance are prerequisites for learning.

Key words

reinforcement learning, boss AI, reward shaping, matchup specialization, Unity ML-Agents, PPO

Ⅰ. 서 론

게임 AI 분야에서 보스 전투(boss battle)는 플레이어 경험의 핵심 요소이다. 기존의 보스 AI는 주로 행동 트리(Behavior Tree)나 유한상태기계(FSM)에 기반하여 사전 정의된 패턴을 반복하는 방식으로 구현된다[1]. 이러한 규칙 기반 방식은 구현과 디버깅에 유리하지만, 플레이어가 패턴을 학습한 이후에는 전투의 긴장감이 급격히 저하된다는 한계를 가진다.

최근 강화학습(Reinforcement Learning, RL) 기반 게임 AI 연구가 활발히 진행되고 있다. AlphaStar[2]는 StarCraft II에서 프로 게이머 수준의 성능을 달성하였고, OpenAI Five[3]는 Dota 2에서 5 대 5 협동 전투 AI를 학습시키는 데 성공하였다. 그러나 이러한 연구들은 대규모 컴퓨팅 자원을 전제로 하며, 상용 게임 개발 환경에서 직접 적용하기에는 제약이 존재한다.

특히 협동형 보스전에서는 두 플레이어의 위치 관계, 스킬 조합, 교전 거리, 군중제어(Crowd Control, CC) 여부 등 복합적 요인이 전투 양상을 결정하기 때문에, 단일 보상 함수로 모든 상황에 대응하기 어렵다. 이에 본 연구는 플레이어 빌드를 원거리(Ranged), 근접(Melee), 하이브리드(Hybrid), CC의 4개 유형으로 분류하고, 각 매치업에 특화된 보상 함수를 설계하여 PPO(Proximal Policy Optimization)[4] 알고리즘으로 학습시키는 방법을 제안한다.

본 연구의 주요 기여는 세 가지이다. 첫째, 매치업별 보상 함수 분리 설계의 유효성을 실험적으로 검증하였다. 둘째, 보상 설계와 환경 설계(이동속도, 스탯 밸런스)의 상호작용이 학습 성능에 미치는 영향을 분석하였다. 셋째, 보상-성과 지표 간 불일치 현상을 실제 게임 환경에서 관찰하고 그 원인을 분석하였다.

Ⅱ. 실험 환경 및 방법

2.1 게임 환경 및 학습 설정

실험 환경은 Unity 2022 LTS 기반 3D 탑다운 액션 게임 테네브리스(Tenebris)에서 구현되었다. 강화학습 프레임워크로 Unity ML-Agents 2.0(Release 21)[6]을 사용하였으며, 학습 알고리즘은 PPO이다. 주요 하이퍼파라미터를 표 1에 정리하였다.

표 1. PPO 학습 하이퍼파라미터  
Table 1. PPO Training Hyperparameters

| 항목 | 값 | 항목 | 값 |
|------|-----|------|-----|
| Batch | 1,024 | Lambda | 0.95 |
| Buffer | 10,240 | Gamma | 0.99 |
| LR | 3e-4 (lin.) | Epoch | 3 |
| Beta | 0.005 | Hidden | 128 × 2 |
| Epsilon | 0.2 | Steps | 5,000,000 |

전투 환경은 반지름 약 50m의 원형 아레나이며, 보스 1체와 규칙 기반 플레이어 봇 2체가 대전한다. 에피소드 종료 조건은 플레이어 전원 사망(보스 승리), 보스 사망(보스 패배), 시간 초과의 세 가지이다. 에피소드 제한 시간은 매치업에 따라 60~90초로 설정하였다.

2.2 관측 공간과 행동 공간

보스 에이전트는 자기 상태(HP, 위치, 전방벡터), 양 플레이어의 위치·HP·거리·각도, 12개 스킬 쿨다운, 8방향 맵 경계 거리, 전투 진행 상황(남은 시간, 누적 대미지 비율) 등 총 55차원의 연속 관측값을 입력으로 받는다. 행동 공간은 이동 브랜치(크기 4: 대기, 전진, 좌회전, 우회전)와 스킬 브랜치(크기 4: 대기, 스킬1, 스킬2, 스킬3)의 2개 이산 브랜치로 구성되어 총 16가지 행동 조합이 가능하다.

2.3 매치업별 보상 함수 설계

보스 에이전트는 17개의 보상 신호를 가중합하여 학습한다. 각 매치업은 서로 다른 플레이어 특성에 대응하기 위해 보상 가중치를 다르게 설정하였다. 플레이어는 규칙 기반 행동 그래프로 동작하며, 매치업별 특성과 보상 설계 의도를 표 2에 정리하였다.

표 2. 매치업별 플레이어 특성 및 주요 보상 가중치  
Table 2. Player Profiles and Key Reward Weights by Matchup

| 항목 | VsRanged | VsMelee | VsHybrid | VsCC |
|------|----------|---------|----------|------|
| 특성 | 장거리 | 근접 | 거리 전환 | 행동 억제 |
| 목표 | 추격 | 반격 | 전환 대응 | CC 돌파 |
| Damage | 0.75 | 0.75 | 0.60 | 0.80 |
| Range | 0.15 | 0.15 | 0.15 | 0.12 |
| OOR | 0.15 | 0.08 | 0.12 | 0.08 |
| Counter | 0.06 | 0.12 | 0.08 | 0.10 |
| Kill | 0.50 | 0.60 | 0.40 | 0.60 |

VsRanged는 거리 유지 보상과 사거리 밖 발사 벌점을 높게 설정하여 원거리 카이팅에 대한 접근을 유도하였다. VsMelee는 반격 보상을 전 매치업 중 가장 높게 설정하고, 밀착 정렬 보너스(0.005)와 후퇴 억제 벌점(0.006)을 추가하여 근접 압박을 유지하도록 설계하였다. VsCC는 대미지 보상과 플레이어 처치 보상을 가장 높게 설정하여 CC 상황에서도 적극적 공격을 유도하였다.

2.4 학습 파이프라인

학습은 2단계 전이학습(Transfer Learning) 구조로 진행하였다.

통합 기초 학습 단계에서는 4종 매치업을 혼합하여 기본 이동, 추적, 스킬 사용 등 공통 전투 기초를 학습시킨다.

이후 매치업별 특화 학습 단계에서는 통합 기초 학습 단계의 체크포인트를 초기 가중치로 사용하여 VsRanged, VsMelee, VsHybrid, VsCC 각각에 대해 매치업별 보상 프리셋을 적용한 특화 학습을 진행한다.

이 방식은 처음부터 별도 모델을 훈련하는 것보다 학습 안정성을 높이고, 매치업별 성능 차이를 보상 구조의 영향으로 분리하여 분석할 수 있다는 장점이 있다.

그림 1. 2단계 전이학습 파이프라인  
Fig. 1. Two-Stage Transfer Learning Pipeline  
통합 기초 학습 → 체크포인트 → VsRanged / VsMelee / VsHybrid / VsCC (특화)

Ⅲ. 실험 결과 및 분석

3.1 매치업별 최종 성능 비교

4종 매치업의 최종 성능을 표 3에 정리하였다. 매치업에 따라 승률(Win Rate, WR)이 10.3%에서 84.1%까지 큰 차이를 보였다.
에피소드 수는 VsRanged 176, VsMelee 2,030, VsHybrid 385, VsCC 281이다.

표 3. 매치업별 최종 성능 비교  
Table 3. Final Performance Comparison by Matchup

| 매치업 | WR (Q4) | HR (Q4) | 거리(P1) | TO |
|--------|---------|---------|---------|----|
| VsRanged | 84.1% | 81.9% | 22.0m | 23.9% |
| VsMelee | 59.5% | 46.8% | 16.2m | 4.8% |
| VsHybrid | 42.6% | 49.9% | 24.1m | 48.6% |
| VsCC | 10.3% | 24.3% | 31.8m | 89.0% |

※ WR, HR은 최종 학습 구간(Q4, 전체 에피소드의 마지막 25%) 기준이다.  
※ 거리(P1)는 보스-플레이어1 기준, TO는 Timeout 비율이다.

Phase 1 통합 학습 baseline(동일 플레이어 조합 추출, 표본 27~31 에피소드)과 비교하면, VsRanged는 +66.2%p, VsMelee는 +20.8%p, VsHybrid는 +20.4%p의 성능 향상을 보여 매치업별 특화 학습의 유효성을 확인하였다. 반면 VsCC는 -13.8%p로 오히려 하락하여 해당 보상 구조의 한계를 시사한다.

VsRanged가 84.1%로 가장 높은 승률을 기록하였고, VsMelee는 59.5%로 50%를 상회하였다. 반면 VsCC는 10.3%에 머물렀으며 Timeout 비율이 89.0%로, 보스가 대부분의 에피소드에서 시간 내에 플레이어를 처치하지 못하였다.

그림 2. 매치업별 최종 승률 비교 (Q4, 마지막 25% 구간 기준)  
Fig. 2. Final Win Rate Comparison by Matchup (Q4, Last 25% of Episodes)

3.2 VsRanged: 환경 조정과 성능 회복 패턴

VsRanged는 원거리 플레이어가 42~66m 사거리 스킬을 사용하며 30~40m 거리를 유지하기 때문에 근접 중심 보스에게 구조적으로 불리한 매치업이다.

초기 환경(v1, 383 에피소드)에서는 보스 이동속도가 부족하여 승률이 0.8%에 불과했다. 보스 이동속도를 상향하고 원거리 스킬 대미지를 15% 하향한 v2 환경에서는 승률이 73.9%(전체), Q4 84.1%로 상승하였다. 표 4에 v1과 v2의 성능 비교를 정리하였다.

표 4. VsRanged 환경 조정 전후 성능 비교  
Table 4. Performance Comparison Before and After Environment Adjustment in VsRanged

| 지표 | v1 (383) | v2 (176) |
|------|----------|----------|
| Win Rate | 0.8% | 73.9% |
| Hit Rate | 8.8% | 48.1% |
| 접촉률 | 9.7% | 80.1% |
| 거리 | 35.7m | 30.8m |

이는 보상 함수 설계 이전에 물리적으로 도달 가능한 환경이 먼저 보장되어야 학습이 성립함을 보여주는 결과이다.

v2 환경에서는 중간 구간의 성능 하락 이후 다시 회복하는 비단조 학습 패턴이 관찰되었다. Q1에서 84.1%였던 승률이 중간 구간에서 59.1%까지 하락한 뒤 Q4에서 동일 수준으로 회복되었으며, 명중률과 평균 거리에서도 유사한 패턴이 나타났다. 이는 보스가 중간 구간에서 다양한 추격 전략을 탐색한 뒤 최적 전략으로 수렴한 것으로 해석된다.

3.3 VsMelee: 스탯 밸런스의 영향

VsMelee는 2,030 에피소드에 걸쳐 학습이 진행되었으며, 스탯 밸런스가 학습 성능의 전제 조건임을 보여준 매치업이다.

스탯 밸런스 문제: v1 학습(3,449 에피소드)에서는 보스 HP(2,000)가 근접 플레이어 2명의 평균 누적 딜량(~2,423)보다 낮아 승률이 5.9%에 불과했다. HP 상향 조정 후 v2에서 58.3%로 개선되었다.

수렴 양상: 구간별 승률은 Q1 54.2% → Q3 60.9% → Q4 59.5%로, 60% 부근에서 수렴하였다. 누적 보상은 −102.6에서 −33.0으로 지속 개선되어, VsCC와 달리 보상-승률 정렬이 양호하게 나타났다. 다만 추가 학습 재개 시 오버트레이닝 현상(Timeout 급증, 승률 49%까지 하락)이 관찰되어 해당 시점에서 학습을 종료하였다.

VsMelee의 보스 사망 비율은 36.9%로 VsRanged(2.3%)보다 크게 높다. VsRanged에서는 "접근만 하면 이기는" 구도인 반면, VsMelee에서는 접근 자체가 상호 피해를 수반하므로, 보상 설계에 따라 매치업 간 전략 분화가 실제로 발생하고 있음을 보여준다.

3.4 VsHybrid와 VsCC

VsHybrid: Q4 승률은 42.6%로 제한적이었으나, 명중률이 34.6%에서 49.9%로 증가(+15.3%p)하고 전투 시간이 74.5초에서 52.0초로 약 30% 단축되었다. 보스가 "죽지 않으면서 더 자주 맞히는 방향"으로는 학습되었으나, 하이브리드 플레이어의 거리 전환 특성으로 인해 승리 전환까지 이어지지 못하였다.

VsCC: 가장 낮은 Q4 승률(10.3%)을 기록하였다. 흥미로운 점은 누적 보상이 Q1 9.3에서 Q4 14.6까지 꾸준히 증가했으나 승률은 정체되었다는 것이다. 이는 Pan et al.[5]이 정의한 보상-성과 지표 간 불일치(reward misalignment)를 시사하며, 보상 함수가 실제 목표인 "플레이어 처치"와 충분히 정렬되지 않았을 가능성을 보여준다. CC 스킬이 보스의 접근을 지속적으로 방해하여 평균 거리가 31.8m까지 증가했고, Timeout 비율이 89.0%에 달했다.

그림 3. VsCC 누적 보상과 승률 추이  
Fig. 3. Cumulative Reward and Win Rate Trends in VsCC

그림 4. 매치업별 전투 종료 원인 비교 (전체 에피소드 누적 기준)  
Fig. 4. Comparison of Match Termination Causes by Matchup (Cumulative Over All Episodes)

Ⅳ. 결 론

본 연구는 Unity 기반 2인 협동 보스전에서 매치업별 보상 함수를 분리 설계하는 강화학습 접근을 제안하고, 4종 매치업에 대한 실험을 통해 그 유효성과 한계를 분석하였다.

실험 결과, VsRanged(84.1%)와 VsMelee(59.5%)에서는 매치업별 보상 설계가 유효하게 작동하여 서로 다른 전략이 학습됨을 확인하였다. 통합 기초 학습 baseline과 비교하였을 때도 VsRanged, VsMelee, VsHybrid는 성능이 개선되었으나, VsCC(10.3%)에서는 보상이 증가함에도 승률이 정체되거나 baseline 이하로 나타나는 보상-성과 지표 간 불일치 현상이 관찰되었다. 또한 VsRanged v1(0.8%)과 VsMelee v1(5.9%)에서 환경 조정 후 승률이 크게 개선된 사례를 통해, 보상 함수 설계보다 환경 설계(이동속도, 스킬 밸런스)와 스탯 밸런스(HP)가 학습 가능성의 전제 조건임을 확인하였다.

향후 연구로는 VsCC 보상 구조 재설계(CC 내성 메커니즘, Timeout 패널티 조정), 런타임에서 플레이어 빌드를 자동 감지하여 특화 모델로 전환하는 시스템 구현, 그리고 실제 플레이어 대상 체감 평가를 계획하고 있다.

참 고 문 헌

[1] I. Millington and J. Funge, "Artificial Intelligence for Games," CRC Press, 3rd ed., 2019.

[2] O. Vinyals et al., "Grandmaster level in StarCraft II using multi-agent reinforcement learning," Nature, vol. 575, pp. 350-354, Nov. 2019.

[3] C. Berner et al., "Dota 2 with large scale deep reinforcement learning," arXiv:1912.06680, 2019.

[4] J. Schulman, F. Wolski, P. Dhariwal, A. Radford, and O. Klimov, "Proximal policy optimization algorithms," arXiv:1707.06347, 2017.

[5] A. Pan, K. Bhatia, and J. Steinhardt, "The effects of reward misspecification: Mapping and mitigating misaligned models," in Proc. ICLR, 2022.

[6] A. Juliani et al., "Unity: A general platform for intelligent agents," arXiv:1809.02627, 2020.

*소속, Email  
**소속, Email (교신저자)

그래프 배치 가이드

본 논문에서 사용하는 그래프 파일과 삽입 위치를 아래에 정리하였다. 실제 제출 시 해당 위치에 이미지를 삽입한다.

| 삽입 위치 | 파일명 | 캡션 |
|----------|--------|------|
| 그림 1 (Ⅱ-2.4) | graphs/fig1_pipeline.png | 2단계 전이학습 파이프라인 / Fig. 1. Two-Stage Transfer Learning Pipeline |
| 그림 2 (Ⅲ-3.1) | graphs/graph1_final_winrate.png | 매치업별 최종 승률 비교 (Q4) / Fig. 2. Final Win Rate Comparison by Matchup |
| 그림 3 (Ⅲ-3.4) | graphs/graph5_vscc_reward_wr.png | VsCC 누적 보상과 승률 추이 / Fig. 3. Cumulative Reward and Win Rate Trends in VsCC |
| 그림 4 (Ⅲ-3.4) | graphs/graph6_end_reason.png | 매치업별 전투 종료 원인 비교 / Fig. 4. Comparison of Match Termination Causes by Matchup |

추가 후보 그래프 (페이지 여유 시 삽입):

| 파일명 | 캡션 | 근거 |
|--------|------|------|
| graph2_vsranged_wr_hr.png | VsRanged 성능 회복 추이 | 3.2절 시각적 보강 |
| graph3_vsranged_distance.png | VsRanged 구간별 평균 거리 변화 | V자 곡선 보강 설명 |
| graph7_vsranged_v1v2.png | VsRanged v1/v2 환경 조정 비교 | 표 4 시각화 |
| graph4_vshybrid_hr_dur.png | VsHybrid 명중률·전투시간 개선 | 내부 지표 개선 설명 |
| graph8_vsmelee_wr_reward.png | VsMelee 승률 수렴 및 보상 개선 추이 | VsMelee 별도 강조 시 교체 가능 |
