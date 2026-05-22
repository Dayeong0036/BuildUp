using UnityEngine;
using TMPro;

public class LeaderboardEntryUI : MonoBehaviour
{
    public TextMeshProUGUI rankText;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI stageText;
    public TextMeshProUGUI skillText;
    public TextMeshProUGUI timeText; // ⭐ 새로 추가된 '클리어 시간' 텍스트 연결용

    // 5개의 데이터를 모두 받아서 텍스트를 변경하는 함수
    public void SetUI(int rank, string playerName, int stage, string skills, int clearTime)
    {
        rankText.text = rank.ToString();
        nameText.text = playerName;
        stageText.text = stage.ToString();
        skillText.text = skills;

        // 시간은 보기 좋게 "초"를 붙여줍니다 (예: 120 -> 120초)
        timeText.text = clearTime.ToString() + "초";
    }
}