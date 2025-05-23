using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Net.Sockets;
using System;
using System.Runtime.InteropServices;
using System.Text;
using static Define;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Card // 카드 전송을 위해 만듦
{
    public int id; // 카드 id 번호
    [MarshalAs(UnmanagedType.I1)]
    public byte isFlip; // 카드가 앞면인가? (뒤집혔는가)
    [MarshalAs(UnmanagedType.I1)]
    public byte isLock; // 맞춘 카드인가
};

public class GameManager : MonoBehaviour
{
    public const int CARD_COUNT = 24;

    [Header("카드 설정")]
    public GameObject cardPrefab;
    public Transform cardParent;         // GridLayoutGroup이 붙은 Panel
    public Sprite[] frontSprites;
    public Sprite backSprite;

    [Header("UI 요소")]
    public TextMeshProUGUI turnText;
    public TextMeshProUGUI player1ScoreText;
    public TextMeshProUGUI player2ScoreText;

    [Header("사운드")]
    public AudioClip startSound;
    public AudioClip mainSound;
    public AudioClip flipSound;
    public AudioClip matchSound;
    public AudioClip failSound;
    public AudioClip gameEndSound;

    private AudioSource audioSource;
    private AudioSource bgmSource;
    private AudioSource sfxSource;

    private List<CardUI> allCards = new List<CardUI>();
    private Dictionary<CardUI, Vector2> cardTargets = new Dictionary<CardUI, Vector2>();

    private CardUI firstCard = null;
    private CardUI secondCard = null;
    private bool isProcessing = false;
    private bool isCardGenerated = false;

    private int currentPlayer = 0;
    private int[] playerScores = new int[2];

    #region Server
    const string IP = "172.30.1.89";
    const int PORT = 8888;

    TcpClient Client;
    NetworkStream Stream;
    #endregion

    Card[] Cards = new Card[CARD_COUNT];

    void Start()
    {
        TryConnect();

        AudioSource[] sources = GetComponents<AudioSource>();
        bgmSource = sources[0];
        sfxSource = sources[1];
        audioSource = GetComponent<AudioSource>();

        if (mainSound != null)
        {
            bgmSource.clip = mainSound;
            bgmSource.loop = true;
            bgmSource.Play();
        }

        if (startSound != null)
            audioSource.PlayOneShot(startSound);

        WaitForGameStart();

        GenerateCards();
        StartCoroutine(DistributeCardsSmoothly());
        UpdateTurnUI();
    }

    void TryConnect()
    {
        try
        {
            Client = new TcpClient();
            Client.Connect(IP, PORT);
            Stream = Client.GetStream();
            
            Debug.Log("서버에 연결되었습니다.");

            byte[] buffer = new byte[255];
            int read = Stream.Read(buffer, 0, buffer.Length);
            
            string message = Encoding.UTF8.GetString(buffer, 0, read);
            Debug.Log("서버로부터 수신: " + message);
        }
        catch (Exception e)
        {
            Debug.LogError("서버 연결 실패: " + e.Message);
        }
    }

    void WaitForGameStart()
    {
        while (true)
        {
            byte[] buffer = new byte[1];
            int read = Stream.Read(buffer, 0, buffer.Length);

            if (read <= 0)
            {
                Debug.LogWarning("서버 연결 끊김 또는 오류");
                break;
            }

            char message = (char)buffer[0];
            Debug.Log("서버로부터 수신: " + message);

            if (message == START_GAME)
                break;
        }
    }

    void GenerateCards()
    {
        //int[] ids = new int[24];
        //for (int i = 0; i < 12; i++) { ids[i * 2] = i; ids[i * 2 + 1] = i; }
        //Shuffle(ids);

        //for (int i = 0; i < ids.Length; i++)
        //{
        //    GameObject cardObj = Instantiate(cardPrefab, cardParent);
        //    RectTransform rect = cardObj.GetComponent<RectTransform>();
        //    rect.anchoredPosition = Vector2.zero; // 중앙에서 시작

        //    CardUI card = cardObj.GetComponent<CardUI>();
        //    card.Setup(ids[i], frontSprites[ids[i]], backSprite, this);

        //    cardTargets[card] = rect.anchoredPosition; // 목표 위치는 GridLayoutGroup 정렬 이후 설정 예정
        //    allCards.Add(card);
        //}

        int bytesRead = 0;
        int cardSize = Marshal.SizeOf<Card>();
        byte[] buffer = new byte[cardSize * CARD_COUNT];

        while (bytesRead < buffer.Length)
        {
            int read = Stream.Read(buffer, bytesRead, buffer.Length - bytesRead);
            
            if (read <= 0)
            {
                Debug.LogWarning("서버 연결 끊김 또는 오류");
                break;
            }

            bytesRead += read;
        }

        for (int i = 0; i < CARD_COUNT; i++)
        {
            byte[] slice = new byte[cardSize];
            Array.Copy(buffer, i * cardSize, slice, 0, cardSize); // 받은 byte 잘라서 각 배열 원소에 저장

            GCHandle handle = GCHandle.Alloc(slice, GCHandleType.Pinned); // 가비지 컬렉션 호출 막음
            Cards[i] = Marshal.PtrToStructure<Card>(handle.AddrOfPinnedObject()); // 받은 byte 배열을 Card 구조체 형태로 변환
            handle.Free();

            Debug.Log($"서버로부터 수신: card id {Cards[i].id}");

            #region Card UI 생성
            GameObject cardObj = Instantiate(cardPrefab, cardParent);
            RectTransform rect = cardObj.GetComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero; // 중앙에서 시작

            CardUI card = cardObj.GetComponent<CardUI>();
            card.Setup(Cards[i].id, frontSprites[Cards[i].id], backSprite, this);

            cardTargets[card] = rect.anchoredPosition; // 목표 위치는 GridLayoutGroup 정렬 이후 설정 예정
            allCards.Add(card);
            #endregion
        }
    }

    IEnumerator DistributeCardsSmoothly()
    {
        yield return null; // GridLayoutGroup이 정렬 완료되도록 한 프레임 대기

        // 카드 위치 저장 (정렬된 위치 기준)
        foreach (CardUI card in allCards)
        {
            RectTransform rect = card.GetComponent<RectTransform>();
            cardTargets[card] = rect.anchoredPosition;
            rect.anchoredPosition = Vector2.zero; // 모두 중앙으로 이동시킴
            rect.localScale = Vector3.zero;
        }

        int i = 0;
        foreach (CardUI card in allCards)
        {
            RectTransform rect = card.GetComponent<RectTransform>();
            Vector2 target = cardTargets[card];

            LeanTween.move(rect, target, 0.5f).setEaseOutBack();
            LeanTween.scale(rect, Vector3.one, 0.5f).setEaseOutBack();

            if (flipSound != null)
                sfxSource.PlayOneShot(flipSound);

            i++;
            yield return new WaitForSeconds(0.04f);
        }

        isCardGenerated = true;
    }

    //void Shuffle(int[] array)
    //{
    //    for (int i = array.Length - 1; i > 0; i--)
    //    {
    //        int rand = UnityEngine.Random.Range(0, i + 1);
    //        (array[i], array[rand]) = (array[rand], array[i]);
    //    }
    //}

    public void OnCardClicked(CardUI clickedCard)
    {
        if (!isCardGenerated || isProcessing || clickedCard.IsFlipped || secondCard != null) return;

        audioSource.PlayOneShot(flipSound);
        clickedCard.FlipFront();

        if (firstCard == null)
            firstCard = clickedCard;
        else
        {
            secondCard = clickedCard;
            StartCoroutine(CheckMatch());
        }
    }

    IEnumerator CheckMatch()
    {
        isProcessing = true;
        yield return new WaitForSeconds(1f);

        if (firstCard.CardId == secondCard.CardId)
        {
            audioSource.PlayOneShot(matchSound);
            playerScores[currentPlayer]++;
            firstCard.PlayMatchEffect();
            secondCard.PlayMatchEffect();
            firstCard.Lock();
            secondCard.Lock();
        }
        else
        {
            audioSource.PlayOneShot(failSound);
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

    void UpdateTurnUI()
    {
        turnText.text = $"Player {currentPlayer + 1}의 턴";
        player1ScoreText.text = $"P1: {playerScores[0]}점";
        player2ScoreText.text = $"P2: {playerScores[1]}점";
    }

    void CheckGameEnd()
    {
        foreach (CardUI card in allCards)
        {
            if (!card.IsLocked)
                return;
        }

        if (gameEndSound != null)
            audioSource.PlayOneShot(gameEndSound);

        string winner = playerScores[0] > playerScores[1] ? "Player 1 승리!" :
                        playerScores[0] < playerScores[1] ? "Player 2 승리!" : "무승부!";
        turnText.text = $"게임 종료\n{winner}";
    }
}
