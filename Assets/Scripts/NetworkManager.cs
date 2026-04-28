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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct S2C_LoginPacket
{
    public ushort size;
    public ushort id;      // 5
    public int mySessionId;
}

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance; // 싱글톤 인스턴스

    void Awake()
    {
        Instance = this;
    }

    private int writePos = 0; 
    private int readPos = 0;
    // 1. 메모리 풀링: 프로그램 시작부터 끝까지 재사용할 단 하나의 송신 버퍼!
    private byte[] sendBuffer = new byte[1024];
    private byte[] recvBuffer = new byte[65535];
    private Socket serverSocket;
    private int mySessionId = -1; // 임시 테스트용

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
            // 1. 남은 공간(recvBuffer.Length - writePos)만큼만 안전하게 받습니다.
            int recvBytes = serverSocket.Receive(recvBuffer, writePos, recvBuffer.Length - writePos, SocketFlags.None);
            if (recvBytes <= 0) return; // 서버가 끊김

            writePos += recvBytes; // OS가 데이터를 넣어줬으니 쓰기 커서 전진!

            unsafe
            {
                fixed (byte* basePtr = recvBuffer)
                {
                    // 2. 뭉쳐서 온 패킷들을 모두 썰어내는 while 루프!
                    while (true)
                    {
                        int dataSize = writePos - readPos;
                        if (dataSize < sizeof(PacketHeader)) break;

                        PacketHeader* header = (PacketHeader*)(basePtr + readPos);
                        if (header->size <= 0 || header->size > recvBuffer.Length) { Disconnect(); return; }
                        if (dataSize < header->size) break;

                        byte* packetPtr = basePtr + readPos;

                        // ==============================================================
                        // 여기서부터 패킷 분기 처리
                        // ==============================================================
                        if (header->id == 1 || header->id == 2) // 이동
                        {
                            // ★ MovePacket은 내부에 size, id가 없으므로 헤더를 건너뜁니다! (+ sizeof(PacketHeader))
                            C2S_MovePacket* movePkt = (C2S_MovePacket*)(packetPtr + sizeof(PacketHeader));

                            if (otherPlayers.TryGetValue(movePkt->sessionId, out PlayerController target))
                            {
                                target.SetTargetPosition(movePkt->posX, movePkt->posY, movePkt->dirX, movePkt->dirY);
                            }
                        }
                        else if (header->id == 3) // 퇴장
                        {
                            // ★ LeavePacket은 구조체 내부에 size, id가 있으므로 건너뛰지 않습니다!
                            S2C_LeavePacket* leavePkt = (S2C_LeavePacket*)packetPtr;

                            if (otherPlayers.TryGetValue(leavePkt->sessionId, out PlayerController target))
                            {
                                Destroy(target.gameObject);
                                otherPlayers.Remove(leavePkt->sessionId);
                            }
                        }
                        else if (header->id == 4) // 스폰
                        {
                            // ★ SpawnPacket도 건너뛰지 않습니다!
                            S2C_SpawnPacket* spawnPkt = (S2C_SpawnPacket*)packetPtr;
                            int newSessionId = spawnPkt->sessionId;

                            if (newSessionId != mySessionId && !otherPlayers.ContainsKey(newSessionId))
                            {
                                Vector3 spawnPos = new Vector3(spawnPkt->spawnX, spawnPkt->spawnY, 0);
                                GameObject newObj = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
                                PlayerController newPc = newObj.GetComponent<PlayerController>();
                                otherPlayers.Add(newSessionId, newPc);
                            }
                        }
                        else if (header->id == 5) // 로그인 통보
                        {
                            // ★ LoginPacket도 건너뛰지 않습니다!
                            S2C_LoginPacket* loginPkt = (S2C_LoginPacket*)packetPtr;
                            mySessionId = loginPkt->mySessionId;
                            Debug.Log($"[성공] 서버 접속 완료! 서버가 부여한 내 진짜 ID: {mySessionId}");
                        }

                        readPos += header->size;
                    }
                }
            }

            // 3. 다 처리하고 남은 짜투리 데이터를 버퍼 맨 앞으로 복사
            int remaining = writePos - readPos;
            if (remaining > 0 && readPos > 0)
            {
                Array.Copy(recvBuffer, readPos, recvBuffer, 0, remaining);
            }
            writePos = remaining;
            readPos = 0;
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
        // 방어막 동작 확인 로그
        if (mySessionId == -1)
        {
            Debug.LogWarning("아직 서버로부터 ID(5번 패킷)를 발급받지 못해 이동 패킷을 쏠 수 없습니다!");
            return;
        }

        Debug.Log($"[송신] 내 ID({mySessionId}) 이동 패킷 쏩니다! X:{x}, Y:{y}");

        PacketHeader header = new PacketHeader { size = 24, id = 1 };
        C2S_MovePacket movePkt = new C2S_MovePacket
        {
            sessionId = mySessionId,
            posX = x,
            posY = y,
            dirX = dx,
            dirY = dy
        };

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
