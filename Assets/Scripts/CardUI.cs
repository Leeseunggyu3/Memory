// 📄 CardUI.cs
// 역할: 개별 카드 오브젝트의 앞/뒷면 전환 및 상태 제어

using UnityEngine;
using UnityEngine.UI;

public class CardUI : MonoBehaviour
{
    public int CardId { get; private set; }      // 카드 종류를 구분하는 ID
    public bool IsFlipped { get; private set; }  // 현재 앞면이 보이는 상태인가?
    public bool IsLocked { get; private set; }   // 맞춘 카드인가?

    private Image image;                         // 카드의 Image 컴포넌트
    private Sprite frontSprite;                  // 앞면 이미지
    private Sprite backSprite;                   // 뒷면 이미지
    private GameManager manager;                 // 게임 매니저 참조

    void Awake()
    {
        image = GetComponent<Image>();
    }

    // 카드 초기 설정 함수 (외부에서 셋업함)
    public void Setup(int id, Sprite front, Sprite back, GameManager gm)
    {
        CardId = id;
        frontSprite = front;
        backSprite = back;
        manager = gm;
        FlipBack(); // 처음에는 뒷면으로 시작
    }

    // 클릭 시 호출되는 함수 (UI Button에 연결)
    public void OnClick()
    {
        if (IsLocked || IsFlipped) return;
        manager.OnCardClicked(this);
    }

    // 앞면 보이기
    public void FlipFront()
    {
        image.sprite = frontSprite;
        IsFlipped = true;
    }

    // 뒷면 보이기
    public void FlipBack()
    {
        image.sprite = backSprite;
        IsFlipped = false;
    }

    // 맞춘 카드로 고정시키기
    public void Lock()
    {
        IsLocked = true;
    }
}
