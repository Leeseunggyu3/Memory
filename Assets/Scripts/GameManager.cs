// 📄 GameManager.cs
// 역할: 메모리 카드 게임의 핵심 로직 담당 (카드 셔플, 클릭 처리, 턴 전환, 점수 관리)

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshPro를 위한 네임스페이스

public class GameManager : MonoBehaviour
{
    [Header("카드 설정")]
    public GameObject cardPrefab;             // 카드 프리팹
    public Transform cardParent;              // 카드를 배치할 Panel (GridLayoutGroup이 붙은 오브젝트)
    public Sprite[] frontSprites;             // 앞면 이미지들 (12개)
    public Sprite backSprite;                 // 뒷면 공통 이미지

    [Header("UI 요소")]
    public TextMeshProUGUI turnText;          // 턴 안내 텍스트
    public TextMeshProUGUI player1ScoreText;  // 플레이어1 점수 표시
    public TextMeshProUGUI player2ScoreText;  // 플레이어2 점수 표시

    private List<CardUI> allCards = new List<CardUI>();
    private CardUI firstCard = null;
    private CardUI secondCard = null;
    private bool isProcessing = false;        // 비교 중일 때 입력 막기

    private int currentPlayer = 0;            // 0: Player1, 1: Player2
    private int[] playerScores = new int[2];


    public AudioClip startSound;
    public AudioClip mainSound;
    public AudioClip flipSound;
    public AudioClip matchSound;
    public AudioClip failSound;
    public AudioClip gameEndSound;

    private AudioSource audioSource;
    private AudioSource bgmSource;
    private AudioSource sfxSource;



    void Start()
    {
        AudioSource[] sources = GetComponents<AudioSource>();
        bgmSource = sources[0];
        sfxSource = sources[1];

        if (mainSound != null)
        {
            bgmSource.clip = mainSound;
            bgmSource.loop = true;
            bgmSource.Play();
        }

        audioSource = GetComponent<AudioSource>();
        if (startSound != null)
            audioSource.PlayOneShot(startSound);

        GenerateCards();
        UpdateTurnUI();
    }

    // 카드 24장을 생성하고 앞면 이미지를 섞는 함수
    void GenerateCards()
    {
        List<int> cardIds = new List<int>();
        for (int i = 0; i < frontSprites.Length; i++)
        {
            cardIds.Add(i); // 쌍 하나 생성
            cardIds.Add(i); // 쌍 둘 생성 → 총 24장
        }

        Shuffle(cardIds);

        for (int i = 0; i < cardIds.Count; i++)
        {
            GameObject cardObj = Instantiate(cardPrefab, cardParent);
            CardUI card = cardObj.GetComponent<CardUI>();
            card.Setup(cardIds[i], frontSprites[cardIds[i]], backSprite, this);
            allCards.Add(card);
        }
    }

    // 카드 순서를 섞는 함수 (Fisher-Yates 알고리즘)
    void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int rand = Random.Range(0, i + 1);
            (list[i], list[rand]) = (list[rand], list[i]);
        }
    }

    // 카드 클릭 시 호출됨
    public void OnCardClicked(CardUI clickedCard)
    {

        if (isProcessing || clickedCard.IsFlipped || secondCard != null) return;

        audioSource.PlayOneShot(flipSound); // 🔊 카드 선택 효과음
        clickedCard.FlipFront();

        if (firstCard == null)
        {
            firstCard = clickedCard;
        }
        else
        {
            secondCard = clickedCard;
            StartCoroutine(CheckMatch());
        }
    }

    // 두 장의 카드가 같은지 확인하는 코루틴
    IEnumerator CheckMatch()
    {
        isProcessing = true;
        yield return new WaitForSeconds(1f);

        if (firstCard.CardId == secondCard.CardId)
        {
            audioSource.PlayOneShot(matchSound); // ✅ 맞췄을 때
            playerScores[currentPlayer]++;
            firstCard.PlayMatchEffect();
            secondCard.PlayMatchEffect();
            firstCard.Lock();
            secondCard.Lock();
        }
        else
        {
            audioSource.PlayOneShot(failSound); // ❌ 틀렸을 때
            firstCard.FlipBack();
            secondCard.FlipBack();
            currentPlayer = (currentPlayer + 1) % 2;
        }

        firstCard = null;
        secondCard = null;
        isProcessing = false;

        UpdateTurnUI();
        CheckGameEnd();
    }


    // 현재 턴 및 점수 UI 업데이트
    void UpdateTurnUI()
    {
        turnText.text = $"Player {currentPlayer + 1}의 턴";
        player1ScoreText.text = $"P1: {playerScores[0]}점";
        player2ScoreText.text = $"P2: {playerScores[1]}점";
    }

    // 게임 종료 체크 (모든 카드가 뒤집혔는지)
    void CheckGameEnd()
    {
        foreach (CardUI card in allCards)
        {
            if (!card.IsLocked)
                return;
        }

        if (gameEndSound != null)
            audioSource.PlayOneShot(gameEndSound); // 🏁 게임 끝 사운드

        string winner;
        if (playerScores[0] > playerScores[1])
            winner = "Player 1 승리!";
        else if (playerScores[0] < playerScores[1])
            winner = "Player 2 승리!";
        else
            winner = "무승부!";

        turnText.text = $"게임 종료\n{winner}";
    }

}
