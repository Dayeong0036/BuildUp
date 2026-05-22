using UnityEngine;

public class LeaderboardTester : MonoBehaviour
{
    [Header("UI 연결")]
    public GameObject entryPrefab;
    public Transform contentArea;

    [Header("테스트 데이터 직접 입력란")]
    public int inputRank = 1;
    public string inputName = "테스터";
    public int inputStage = 5;
    public string inputSkills = "파이어볼, 대시";
    public int inputClearTime = 120;

    // 랜덤 생성용 데이터 풀
    private string[] randomNames = { "용사", "마법사", "궁수", "도적", "성기사", "초보자" };
    private string[] randomSkills = { "파이어볼", "회복", "대시", "연속베기", "은신", "방패치기" };

    // ⭐ 유니티 플레이(▶) 버튼을 누르면 가장 먼저 자동으로 실행되는 부분!
    void Start()
    {
        // 시작하자마자 무작위 데이터를 5번 연속으로 추가합니다.
        for (int i = 0; i < 5; i++)
        {
            AddRandomEntry();
        }
    }

    // (자동 실행용) 무작위 데이터를 생성해서 리스트에 넣는 함수
    private void AddRandomEntry()
    {
        if (entryPrefab == null || contentArea == null) return;

        // 랜덤 데이터 만들기
        string rName = randomNames[Random.Range(0, randomNames.Length)] + Random.Range(1, 100);
        int rStage = Random.Range(1, 21);
        string rSkill = randomSkills[Random.Range(0, randomSkills.Length)] + ", " + randomSkills[Random.Range(0, randomSkills.Length)];
        int rTime = Random.Range(30, 300); // 30초 ~ 300초 사이

        // 프리팹 생성 및 적용
        GameObject newEntry = Instantiate(entryPrefab, contentArea);
        LeaderboardEntryUI entryUI = newEntry.GetComponent<LeaderboardEntryUI>();

        if (entryUI != null)
        {
            entryUI.SetUI(inputRank, rName, rStage, rSkill, rTime);
            inputRank++; // 다음 사람을 위해 순위 1 증가
        }
    }

    // (수동 확인용) 인스펙터에 적은 특정 데이터로 항목 추가하기
    [ContextMenu("입력한 데이터로 항목 추가하기")]
    public void AddEntryToLeaderboard()
    {
        if (entryPrefab == null || contentArea == null)
        {
            Debug.LogError("프리팹이나 Content Area가 연결되지 않았습니다!");
            return;
        }

        GameObject newEntry = Instantiate(entryPrefab, contentArea);
        LeaderboardEntryUI entryUI = newEntry.GetComponent<LeaderboardEntryUI>();

        if (entryUI != null)
        {
            entryUI.SetUI(inputRank, inputName, inputStage, inputSkills, inputClearTime);
        }

        Debug.Log($"{inputRank}위 {inputName} 추가됨!");
        inputRank++;
    }

    [ContextMenu("리스트 모두 지우기")]
    public void ClearLeaderboard()
    {
        foreach (Transform child in contentArea)
        {
            Destroy(child.gameObject);
        }
        inputRank = 1; // 순위도 1위부터 다시 시작
    }
}