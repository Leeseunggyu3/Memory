using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System.Net.Sockets;
using System;
using System.Runtime.InteropServices;
using System.Text;
using static Define;
using System.IO;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Card // 카드 전송을 위해 만듦
{
    public int id; // 카드 id 번호
    [MarshalAs(UnmanagedType.I1)]
    public byte isFlip; // 카드가 앞면인가? (뒤집혔는가)
    [MarshalAs(UnmanagedType.I1)]
    public byte isLock; // 맞춘 카드인가
};
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Player
{
	public int score;
	[MarshalAs(UnmanagedType.I1)]
	public byte myturn;
};
public class GameManager : MonoBehaviour
{
    public const int MAX_CARD_COUNT = 24;
    public const int PLAYER_COUNT = 2;

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
    const string IP = "169.254.94.105";
    const int PORT = 8888;

    TcpClient Client;
    NetworkStream Stream;
    #endregion

    Card[] Cards = new Card[MAX_CARD_COUNT];
    Player[] Players = new Player[PLAYER_COUNT];

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

        WaitForMyTurn();
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
            byte[] buffer = RecvByte(1);

            if (buffer == null)
                break;

            char message = (char)buffer[0];
            Debug.Log("서버로부터 수신: " + message);

            if (message == START_GAME)
                break;
        }
    }

    void WaitForMyTurn()
    {
        while (true)
        {
            byte[] buffer = RecvByte(1);

            if (buffer == null)
                break;

            char message = (char)buffer[0];
            Debug.Log("서버로부터 수신: " + message);

            if (message == YOUR_TURN)
                break;
        }
    }

    void GenerateCards()
    {
        Cards = RecvByteToStruct<Card>(Stream, MAX_CARD_COUNT);

        for (int i = 0; i < MAX_CARD_COUNT; i++)
        {
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

        Debug.Log($"card id {clickedCard.CardId} 클릭");

        #region Server
        int index = allCards.IndexOf(clickedCard);
        byte[] bytes = BitConverter.GetBytes(index);

        if (BitConverter.IsLittleEndian == false)
            Array.Reverse(bytes);

        Debug.Log($"card index {index}");
        Stream.Write(bytes, 0, bytes.Length);   // 선택한 카드 인덱스 서버에 전송
        #endregion

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

    IEnumerator CheckMatch() // 카드를 선택했을 때
    {
        isProcessing = true;
        yield return new WaitForSeconds(1f);

        Players = RecvByteToStruct<Player>(Stream, PLAYER_COUNT); // 각 플레이어 정보 불러옴

        if (firstCard.CardId == secondCard.CardId) // 카드가 같다면
        {
            audioSource.PlayOneShot(matchSound);

            for (int i = 0; i < PLAYER_COUNT; i++) // 플레이어 개수에 따라
            {
                playerScores[currentPlayer] = Players[i].score; // 받아온 점수 업데이트
                Debug.Log($"플레이어{i}의 점수 : {playerScores[currentPlayer]}");
            }

            audioSource.PlayOneShot(matchSound);
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

            SwitchTurn();
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

    void SwitchTurn()
    {
        currentPlayer = (currentPlayer + 1) % 2;
    }

    public void QuitGame()
    {
        Debug.Log("게임 종료 시도");
        #if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    #region Util
    /// <summary>
    /// 서버에게 데이터를 받는다.
    /// </summary>
    /// <param name="size">버퍼 크기</param>
    /// <returns></returns>
    byte[] RecvByte(int size)
    {
        byte[] buffer = new byte[size];
        int read = Stream.Read(buffer, 0, buffer.Length);

        if (read <= 0)
        {
            Debug.LogWarning("서버 연결 끊김 또는 오류");
            return null;
        }

        return buffer;
    }

    T[] RecvByteToStruct<T>(NetworkStream stream, int count) where T : struct
    {
        int structSize = Marshal.SizeOf<Card>();
        byte[] buffer = new byte[structSize * count];
        int byteread = 0;

        while (byteread < buffer.Length)
        {
            int read = stream.Read(buffer, byteread, buffer.Length - byteread);
            if (read <= 0)
            {
                Debug.LogWarning("서버 연결 끊김 또는 오류");
                break;
            }

            byteread += read;
        }

        T[] result = new T[count];

        for (int i = 0; i < count; i++)
        {
            byte[] slice = new byte[structSize];
            Array.Copy(buffer, i * structSize, slice, 0, structSize);

            GCHandle handle = GCHandle.Alloc(slice, GCHandleType.Pinned);
            result[i] = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            handle.Free();
        }

        return result;
    }
    #endregion
}
