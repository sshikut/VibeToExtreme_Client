using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PacketHeader
{
    public ushort size; // C++ uint16_t = C# ushort (2바이트)
    public ushort id;   // C++ uint16_t = C# ushort (2바이트)
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct C2S_MovePacket
{
    public int sessionId; // C++ int32_t = C# int (4바이트)
    public float posX;    // (4바이트)
    public float posY;    // (4바이트)
    public float dirX;    // (4바이트)
    public float dirY;    // (4바이트)
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct S2C_SpawnPacket
{
    public ushort size;
    public ushort id;        // 4 (예: 입장 패킷 ID)
    public int sessionId;  // 새로 들어온 놈의 번호
    public float spawnX;       // 스폰 X 위치
    public float spawnY;       // 스폰 Y 위치
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct S2C_LeavePacket
{
    public ushort size;      // 8바이트 (size + id + sessionId)
    public ushort id;        // 3 (예: 퇴장 패킷 ID)
    public int sessionId;  // 방금 나간 놈의 번호
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct S2C_LoginPacket
{
    public ushort size;
    public ushort id;      // 5
    public int mySessionId;
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance;

    private const int BUFFER_SIZE = 65535 * 10;
    private byte[] receiveBuffer = new byte[BUFFER_SIZE];
    private byte[] processBuffer = new byte[BUFFER_SIZE];

    private int receiveWritePos = 0;
    private int processWritePos = 0;
    private int processReadPos = 0;
    private readonly object bufferLock = new object();

    private Thread receiveThread;
    private bool isRunning = false;

    private byte[] sendBuffer = new byte[1024];
    private Socket serverSocket;
    private int mySessionId = -1;

    // ★ [GC 원인 1 완벽 제거] Dictionary 대신 압도적인 O(1) 속도의 정적 배열 사용!
    public PlayerController[] otherPlayers = new PlayerController[10005];

    public GameObject playerPrefab;
    private Queue<GameObject> playerPool = new Queue<GameObject>();

    public TMP_InputField ipInput;
    public GameObject ipPanel;

    void Awake()
    {
        Instance = this;

        // ★ [프레임 고정 최적화]
        // 1. 모니터 주사율 강제 동기화(VSync)를 끕니다. (유니티가 강제로 프레임을 널뛰게 만드는 주범)
        QualitySettings.vSyncCount = 0;

        // 2. 상용 게임의 표준인 60프레임으로 콘크리트처럼 고정합니다! (PC 게임이라면 144도 좋습니다)
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        // ConnectToServer("127.0.0.1", 7777);
    }

    public void StartClient()
    {
        string ip = ipInput.text;
        if (ip != null)
        {
            ConnectToServer(ip, 7777);
        }
        else
        {
            Debug.Log("IP를 적으시오.");
        }
        
    }

    public void ConnectToServer(string ip, int port)
    {
        try
        {
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.NoDelay = true;
            serverSocket.Connect(ip, port);
            Debug.Log("서버 연결 성공! (백그라운드 수신 스레드 기동)");

            isRunning = true;
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            ipPanel.SetActive(false);
        }
        catch (Exception e)
        {
            Debug.LogError($"서버 연결 실패: {e.Message}");
        }
    }

    private GameObject GetPlayerFromPool(Vector3 pos)
    {
        if (playerPool.Count > 0)
        {
            GameObject obj = playerPool.Dequeue();
            obj.transform.position = pos;
            obj.SetActive(true);
            return obj;
        }
        return Instantiate(playerPrefab, pos, Quaternion.identity);
    }

    private void ReturnPlayerToPool(GameObject obj)
    {
        obj.SetActive(false);
        playerPool.Enqueue(obj);
    }

    private void ReceiveLoop()
    {
        byte[] tempBuf = new byte[65535];
        while (isRunning && serverSocket != null)
        {
            try
            {
                int bytes = serverSocket.Receive(tempBuf, 0, tempBuf.Length, SocketFlags.None);
                if (bytes <= 0) break;

                lock (bufferLock)
                {
                    if (receiveWritePos + bytes <= BUFFER_SIZE)
                    {
                        Buffer.BlockCopy(tempBuf, 0, receiveBuffer, receiveWritePos, bytes);
                        receiveWritePos += bytes;
                    }
                }
            }
            catch (Exception) { break; }
        }
    }

    void Update()
    {
        lock (bufferLock)
        {
            if (receiveWritePos > 0)
            {
                if (processWritePos + receiveWritePos <= BUFFER_SIZE)
                {
                    Buffer.BlockCopy(receiveBuffer, 0, processBuffer, processWritePos, receiveWritePos);
                    processWritePos += receiveWritePos;
                    receiveWritePos = 0;
                }
            }
        }

        if (processWritePos > 0)
        {
            unsafe
            {
                fixed (byte* basePtr = processBuffer)
                {
                    while (true)
                    {
                        int dataSize = processWritePos - processReadPos;
                        if (dataSize < sizeof(PacketHeader)) break;

                        PacketHeader* header = (PacketHeader*)(basePtr + processReadPos);

                        if (header->size <= 0 || header->size > processBuffer.Length) { Disconnect(); return; }
                        if (dataSize < header->size) break;

                        byte* packetPtr = basePtr + processReadPos;

                        // -------------- [라우팅 로직 (Zero-GC Array 방식)] --------------
                        if (header->id == 1 || header->id == 2)
                        {
                            C2S_MovePacket* movePkt = (C2S_MovePacket*)(packetPtr + sizeof(PacketHeader));
                            int tId = movePkt->sessionId;

                            // ★ 박싱/언박싱/해시계산이 단 1도 없는 완벽한 배열 인덱싱
                            if (tId >= 0 && tId < 10000 && otherPlayers[tId] != null)
                            {
                                otherPlayers[tId].SetTargetPosition(movePkt->posX, movePkt->posY, movePkt->dirX, movePkt->dirY);
                            }
                            else if (tId != mySessionId && tId >= 0 && tId < 10000)
                            {
                                Vector3 spawnPos = new Vector3(movePkt->posX, movePkt->posY, 0);
                                GameObject newObj = GetPlayerFromPool(spawnPos);
                                otherPlayers[tId] = newObj.GetComponent<PlayerController>();

                                // ★ [GC 원인 3 완벽 제거] 무자비한 문자열 할당을 유발하던 Debug.Log 완전 삭제!
                            }
                        }
                        else if (header->id == 3)
                        {
                            S2C_LeavePacket* leavePkt = (S2C_LeavePacket*)packetPtr;
                            int tId = leavePkt->sessionId;

                            if (tId >= 0 && tId < 10000 && otherPlayers[tId] != null)
                            {
                                ReturnPlayerToPool(otherPlayers[tId].gameObject);
                                otherPlayers[tId] = null; // 메모리에서 흔적 지우기
                            }
                        }
                        else if (header->id == 4)
                        {
                            S2C_SpawnPacket* spawnPkt = (S2C_SpawnPacket*)packetPtr;
                            int tId = spawnPkt->sessionId;

                            if (tId != mySessionId && tId >= 0 && tId < 10000 && otherPlayers[tId] == null)
                            {
                                Vector3 spawnPos = new Vector3(spawnPkt->spawnX, spawnPkt->spawnY, 0);
                                GameObject newObj = GetPlayerFromPool(spawnPos);
                                otherPlayers[tId] = newObj.GetComponent<PlayerController>();
                            }
                        }
                        else if (header->id == 5)
                        {
                            S2C_LoginPacket* loginPkt = (S2C_LoginPacket*)packetPtr;
                            mySessionId = loginPkt->mySessionId;
                            Debug.Log("[성공] 서버 접속 완료! 내 ID: " + mySessionId); // 1번만 실행되므로 안전
                        }
                        // --------------------------------------------------------------

                        processReadPos += header->size;
                    }
                }
            }

            int remaining = processWritePos - processReadPos;
            if (remaining > 0 && processReadPos > 0)
            {
                Array.Copy(processBuffer, processReadPos, processBuffer, 0, remaining);
            }
            processWritePos = remaining;
            processReadPos = 0;
        }
    }

    public unsafe void SendMovePacket(float x, float y, float dx, float dy)
    {
        if (mySessionId == -1) return;

        // ★ [GC 원인 3 완벽 제거] 이동할 때마다 문자열 쓰레기를 뿜어내던 Debug.Log($"[송신] ...") 삭제!

        PacketHeader header = new PacketHeader { size = 24, id = 1 };
        C2S_MovePacket movePkt = new C2S_MovePacket { sessionId = mySessionId, posX = x, posY = y, dirX = dx, dirY = dy };

        fixed (byte* ptr = sendBuffer)
        {
            *(PacketHeader*)ptr = header;
            *(C2S_MovePacket*)(ptr + sizeof(PacketHeader)) = movePkt;
        }

        if (serverSocket != null && serverSocket.Connected)
        {
            serverSocket.Send(sendBuffer, 0, header.size, SocketFlags.None);
        }
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }

    public void Disconnect()
    {
        isRunning = false;
        if (serverSocket != null)
        {
            try
            {
                if (serverSocket.Connected) serverSocket.Shutdown(SocketShutdown.Both);
                serverSocket.Close();
                serverSocket = null;
                Debug.Log("서버와의 연결을 안전하게 종료했습니다.");
            }
            catch (Exception e) { Debug.LogError($"소켓 종료 중 에러 발생: {e.Message}"); }
        }
    }
}
