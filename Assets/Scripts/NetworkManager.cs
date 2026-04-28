using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance; // 싱글톤 인스턴스

    void Awake()
    {
        Instance = this;
    }

    // 1. 메모리 풀링: 프로그램 시작부터 끝까지 재사용할 단 하나의 송신 버퍼!
    private byte[] sendBuffer = new byte[1024];
    private byte[] recvBuffer = new byte[65535];
    private Socket serverSocket;
    private int mySessionId = 1; // 임시 테스트용

    public Dictionary<int, PlayerController> otherPlayers = new Dictionary<int, PlayerController>();
    public GameObject playerPrefab; // 인스펙터에서 연결할 다른 유저 프리팹

    void Start()
    {
        ConnectToServer("127.0.0.1", 7777); // 로컬 테스트용 IP와 포트 번호
    }

    void Update()
    {
        if (serverSocket != null && serverSocket.Connected && serverSocket.Poll(0, SelectMode.SelectRead))
        {
            int recvBytes = serverSocket.Receive(recvBuffer);

            if (recvBytes > 0)
            {
                // 안전벨트를 풉니다! (Zero-Allocation 역직렬화)
                unsafe
                {
                    fixed (byte* ptr = recvBuffer)
                    {
                        PacketHeader* header = (PacketHeader*)ptr;
                        if (recvBytes < sizeof(PacketHeader) || recvBytes < header->size) return;

                        // ==========================================
                        // 1번: 누군가 이동했다! (C2S_MOVE / S2C_MOVE_BROAD)
                        // ==========================================
                        if (header->id == 1 || header->id == 2)
                        {
                            C2S_MovePacket* movePkt = (C2S_MovePacket*)(ptr + sizeof(PacketHeader));
                            if (otherPlayers.TryGetValue(movePkt->sessionId, out PlayerController target))
                            {
                                target.SetTargetPosition(movePkt->posX, movePkt->posY, movePkt->dirX, movePkt->dirY);
                            }
                        }
                        // ==========================================
                        // 3번: 누군가 나갔다! (S2C_LEAVE)
                        // ==========================================
                        else if (header->id == 3)
                        {
                            S2C_LeavePacket* leavePkt = (S2C_LeavePacket*)(ptr + sizeof(PacketHeader));
                            int leftSessionId = leavePkt->sessionId;

                            // 방금 배운 완벽한 '우아한 삭제' 로직
                            if (otherPlayers.TryGetValue(leftSessionId, out PlayerController target))
                            {
                                Destroy(target.gameObject);         // 1. 화면에서 파괴!
                                otherPlayers.Remove(leftSessionId); // 2. 딕셔너리 명부에서 삭제!
                            }
                        }
                        // ==========================================
                        // 4번: 누군가 들어왔다! (S2C_SPAWN)
                        // ==========================================
                        else if (header->id == 4)
                        {
                            S2C_SpawnPacket* spawnPkt = (S2C_SpawnPacket*)(ptr + sizeof(PacketHeader));
                            int newSessionId = spawnPkt->sessionId;

                            // 내 번호가 아니고, 명부에 없는 뉴비라면 화면에 스폰!
                            if (newSessionId != mySessionId && !otherPlayers.ContainsKey(newSessionId))
                            {
                                Vector3 spawnPos = new Vector3(spawnPkt->spawnX, spawnPkt->spawnY, 0);
                                GameObject newObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
                                PlayerController newPc = newObj.GetComponent<PlayerController>();

                                otherPlayers.Add(newSessionId, newPc);
                            }
                        }
                    }
                }
            }
        }
    }

    public void ConnectToServer(string ip, int port)
    {
        try
        {
            // TCP 소켓 생성
            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // 작성자님이 찾아낸 바로 그 옵션! (네이글 알고리즘 해제)
            serverSocket.NoDelay = true;

            // 서버로 연결 시도
            serverSocket.Connect(ip, port);
            Debug.Log("서버 연결 성공! (TCP_NODELAY 활성화)");

            // TODO: 수신(Receive) 대기 로직이 여기에 들어가야 함
        }
        catch (Exception e)
        {
            Debug.LogError($"서버 연결 실패: {e.Message}");
        }
    }

    // 2. unsafe 키워드: C++처럼 포인터를 사용하여 메모리 할당(new) 없이 직렬화합니다.
    public unsafe void SendMovePacket(float x, float y, float dx, float dy)
    {
        // 보낼 데이터 세팅
        PacketHeader header = new PacketHeader { size = 24, id = 1 }; // 1은 C2S_MOVE
        C2S_MovePacket movePkt = new C2S_MovePacket
        {
            sessionId = mySessionId,
            posX = x,
            posY = y,
            dirX = dx,
            dirY = dy
        };

        // 3. fixed 키워드: 가비지 컬렉터가 이 배열의 메모리 주소를 옮기지 못하게 '고정'시킵니다.
        fixed (byte* ptr = sendBuffer)
        {
            // 포인터 캐스팅을 이용해 구조체를 sendBuffer 메모리에 직접 덮어씌웁니다. (C++과 동일!)
            *(PacketHeader*)ptr = header; // 0~3번 인덱스에 헤더 기록

            // 헤더 크기(4바이트)만큼 주소를 이동한 뒤 바디 기록
            *(C2S_MovePacket*)(ptr + sizeof(PacketHeader)) = movePkt;
        }

        // 4. 단 1바이트의 쓰레기(GC)도 만들지 않고 전송 완료!
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
        if (serverSocket != null)
        {
            try
            {
                // 소켓이 연결되어 있다면, 먼저 우아하게 송수신 채널을 차단합니다.
                if (serverSocket.Connected)
                {
                    serverSocket.Shutdown(SocketShutdown.Both);
                }

                // 소켓 리소스를 완전히 해제합니다.
                serverSocket.Close();
                serverSocket = null;
                Debug.Log("서버와의 연결을 안전하게 종료했습니다.");
            }
            catch (Exception e)
            {
                Debug.LogError($"소켓 종료 중 에러 발생: {e.Message}");
            }
        }
    }
}
