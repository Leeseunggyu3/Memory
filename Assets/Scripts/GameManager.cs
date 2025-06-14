﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Player
{
    public int id;
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
    public GameObject LoadingPopup;
    public GameObject PlayerLeftPopup;

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
    /// <summary>
    /// 두 개 이상의 카드 선택 방지
    /// </summary>
    private CardUI secondCard = null;

    private bool _myTurn = false;
    /// <summary>
    /// 카드 체크 코루틴 중복호출 방지
    /// </summary>
    private bool isProcessing = false;
    /// <summary>
    /// 카드 생성 중 카드 선택 방지
    /// </summary>
    private bool isCardGenerated = false;

    private int currentPlayer = 0;
    private int[] playerScores = new int[2];

    #region Server
    private const string IP = "127.0.0.1";
    private const int PORT = 8888;

    private TcpClient _client;
    private NetworkStream _stream;
    #endregion

    Card[] _cards = new Card[MAX_CARD_COUNT];
    Player[] _players = new Player[PLAYER_COUNT];

    void Awake()
    {
        Application.runInBackground = true; // 백그라운드에서도 동작하게끔.
    }
    async void Start()
    {
        TryConnect();
        _ = CheckConnected();

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

        PlayerLeftPopup.SetActive(false);

        LoadingPopup.SetActive(true);
        await WaitForGameStart();
    }

    void TryConnect()
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(IP, PORT);
            _stream = _client.GetStream();

            Debug.Log("서버에 연결되었습니다.");

            byte[] buffer = new byte[255];
            int read = _stream.Read(buffer, 0, buffer.Length);

            string message = Encoding.UTF8.GetString(buffer, 0, read);
            Debug.Log("서버로부터 수신: " + message);
        }
        catch (Exception e)
        {
            Debug.LogError("서버 연결 실패: " + e.Message);
        }
    }

    void TryDisconnect()
    {
        try
        {
            _stream.Close();
            _client.Close();
            Debug.Log("서버 연결 종료 완료");
        }
        catch (Exception e)
        {
            Debug.LogError("서버 연결 종료 중 오류 발생: " + e.Message);
        }
    }

    async Task CheckConnected()
    {
        while (true)
        {
            await Task.Delay(1000);

            if (_client == null || _client.Client == null || _client.Connected == false)
            {
                PlayerLeftPopup.SetActive(true);
                break;
            }

            try
            {
                Socket socket = _client.Client;
                if (socket.Poll(1, SelectMode.SelectRead) && socket.Available == 0)
                {
                    PlayerLeftPopup.SetActive(true);
                    break;
                }
            }
            catch
            {
                PlayerLeftPopup.SetActive(true);
                break;
            }
        }
    }

    async Task WaitForGameStart()
    {
        Debug.Log("대기 중...\n플레이 인원이 모일 때까지 기다려주세요.");
        // TODO: "플레이 인원이 모일 때까지 기다려주세요."를 안내해주는 UI

        byte[] buffer = await Task.Run(() => RecvByte(1)); // 다른 쓰레드에서 실행, RecvByte가 블로킹 함수이기 때문
        if (buffer == null)
            return;

        byte message = buffer[0];
        Debug.Log("서버로부터 수신: " + (char)message);

        if (message == START_GAME)
        {
            LoadingPopup.SetActive(false);
            GenerateCards();
            UpdateTurnUI();

            await WaitForMyTurn();
        }
    }

    async Task WaitForMyTurn()  // 현재 누구 차례인지 서버로부터 받아야 함
    {
        Debug.Log("플레이어 차례 대기 중...");

        SendByte(WAIT_FOR_MY_TURN);

        byte[] buffer = await Task.Run(() => RecvByte(1));
        if (buffer == null)
            return;

        byte message = buffer[0];
        Debug.Log("서버로부터 수신: " + (char)message);

        // 차례를 기다리는 동안, 다른 플레이어의 플레이를 보여준다.
        switch (message)
        {
            case YOUR_TURN:
                Debug.Log($"현재 니 턴 맞습니다.");
                _myTurn = true;
                break;

            case PICK_CARD:
                ShowOpponentPickedCard();
                break;

            case EXIT:
                Debug.Log($"다른 플레이어가 나갔습니다.");
                PlayerLeftPopup.SetActive(true);
                break;
        }
        UpdateTurnUI();
    }

    async void ShowOpponentPickedCard()
    {
        //byte[] buffer = await Task.Run(() => RecvByte(4));    // 전달한 index는 byte 받아서 char형이다. 정수형 데이터로 받으면 안되니까 주석 처리
        byte[] buffer = await Task.Run(() => RecvByte(1));
        if (buffer == null)
            return;

        //int index = BitConverter.ToInt32(buffer, 0);  // 위의 주석과 이하동문
        int index = buffer[0];
        //Debug.Log($"서버로부터 수신: card index {index}");

        CardUI card = allCards[index];

        audioSource.PlayOneShot(flipSound);
        card.FlipFront();

        if (firstCard == null)
        {
            firstCard = card;
            SendByte(UPDATE);
            await WaitForMyTurn();
        }
        else
        {
            secondCard = card;
            StartCoroutine(CheckMatch(false, async () =>
            {
                SendByte(UPDATE);
                await WaitForMyTurn();
            }));
        }
    }

    void GenerateCards()
    {
        _cards = RecvByteToStruct<Card>(_stream, MAX_CARD_COUNT);

        for (int i = 0; i < MAX_CARD_COUNT; i++)
        {
            //Debug.Log($"서버로부터 수신: card id {_cards[i].id}");

            #region Card UI 생성
            GameObject cardObj = Instantiate(cardPrefab, cardParent);
            RectTransform rect = cardObj.GetComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero; // 중앙에서 시작

            CardUI card = cardObj.GetComponent<CardUI>();
            card.Setup(_cards[i].id, frontSprites[_cards[i].id], backSprite, this);

            cardTargets[card] = rect.anchoredPosition; // 목표 위치는 GridLayoutGroup 정렬 이후 설정 예정
            allCards.Add(card);
            #endregion
        }

        StartCoroutine(DistributeCardsSmoothly());
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
        if (_myTurn == false)
        {
            Debug.Log("Nope");
            return;
        }

        if (!isCardGenerated || isProcessing || clickedCard.IsFlipped || secondCard != null)
        {
            Debug.Log($"isCardGenerated : {isCardGenerated}");
            Debug.Log($"isProcessing : {isProcessing}");
            Debug.Log($"clickedCard.IsFlipped : {clickedCard.IsFlipped}");
            Debug.Log($"secondCard : {secondCard}");

            return;
        }

        #region Server
        int index = allCards.IndexOf(clickedCard);
        //Debug.Log($"card index {index} (id: {clickedCard.CardId})");

        SendByte(PICK_CARD);
        SendByte((byte)index);  // 선택한 카드 인덱스 서버에 전송
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

    IEnumerator CheckMatch(bool myTurn = true, Action callback = null)    // 카드를 선택했을 때
    {
        isProcessing = true;
        yield return new WaitForSeconds(1f);
        bool match = false;

        if (firstCard.CardId == secondCard.CardId) // 카드가 같다면
        {
            _players = RecvByteToStruct<Player>(_stream, PLAYER_COUNT); // 각 플레이어 정보 불러옴

            for (int i = 0; i < PLAYER_COUNT; i++) // 플레이어 개수에 따라
            {
                playerScores[i] = _players[i].score; // 받아온 점수 업데이트
                Debug.Log($"플레이어{i}의 점수 : {playerScores[currentPlayer]}");
            }
            match = true;
            audioSource.PlayOneShot(matchSound);

            audioSource.PlayOneShot(matchSound);
            firstCard.PlayMatchEffect();
            secondCard.PlayMatchEffect();

            firstCard.Lock();
            secondCard.Lock();

        }
        else
        {
            _players = _players = RecvByteToStruct<Player>(_stream, PLAYER_COUNT); // 각 플레이어 정보 불러옴
            UpdateTurnUI();
            audioSource.PlayOneShot(failSound);
            firstCard.FlipBack();
            secondCard.FlipBack();
        }

        firstCard = null;
        secondCard = null;
        isProcessing = false;

        callback?.Invoke();

        if (CheckGameEnd())
            yield break;

        if (match == false && myTurn) // 카드를 못맞추고, 내 턴이 아닐때만 턴 전환을 함.
            SwitchTurn();
    }

    void UpdateTurnUI()
    {
        if (_players[0].id == 1 && _players[0].myturn == 1)
            turnText.text = "플레이어2 차례";
        else if (_players[1].id == 2 && _players[1].myturn == 1)
            turnText.text = "플레이어1 차례";
        player1ScoreText.text = $"P1: {playerScores[0]}점";
        player2ScoreText.text = $"P2: {playerScores[1]}점";
    }

    bool CheckGameEnd()
    {
        foreach (CardUI card in allCards)
        {
            if (!card.IsLocked)
                return false;
        }

        if (gameEndSound != null)
            audioSource.PlayOneShot(gameEndSound);

        string winner = playerScores[0] > playerScores[1] ? "Player 1 승리!" :
                        playerScores[0] < playerScores[1] ? "Player 2 승리!" : "무승부!";
        turnText.text = $"게임 종료\n{winner}";

        return true;
    }

    async void SwitchTurn()
    {
        _myTurn = false;
        currentPlayer = 0;

        UpdateTurnUI();

        await WaitForMyTurn();
    }

    public void Restart()
    {
        SceneManager.LoadScene("GameScene");
    }

    public void QuitGame()
    {
        SendByte(EXIT);
        TryDisconnect();

        Debug.Log("게임 종료 시도");
        ExitGame();
    }

    public void ExitGame()
    {
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
        int total = 0;

        try
        {
            while (total < buffer.Length)
            {
                int read = _stream.Read(buffer, total, buffer.Length - total);   // 블로킹
                if (read <= 0)
                {
                    Debug.LogWarning("서버 연결 끊김 또는 오류");
                    return null;
                }

                total += read;
            }

            return buffer;
        }
        catch (IOException e)
        {
            Debug.LogWarning("Recv 중단됨: " + e.Message);
            return null;
        }
    }

    T[] RecvByteToStruct<T>(NetworkStream stream, int count) where T : struct
    {
        int structSize = Marshal.SizeOf<T>();
        byte[] buffer = new byte[structSize * count];
        int byteread = 0;

        while (byteread < buffer.Length)
        {
            int read = stream.Read(buffer, byteread, buffer.Length - byteread);
            if (read <= 0)
            {
                Debug.LogWarning("서버 연결 끊김 또는 오류");
                return null;
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

    void SendByte(byte data)
    {
        try
        {
            _stream.WriteByte(data);
        }
        catch
        {
            Debug.Log($"다른 플레이어가 나갔습니다.");
            PlayerLeftPopup.SetActive(true);
        }
    }

    void SendByte(byte[] bytes)
    {
        // int형 자료가 아닌 byte로만 보내게 바꿧으므로 바이트 정렬 변환이 필요없음
        //if (BitConverter.IsLittleEndian) 
        //    Array.Reverse(bytes);

        _stream.Write(bytes, 0, bytes.Length);
    }
    #endregion
}
