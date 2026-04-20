using System;
using System.Net.Sockets;
using UnityEngine;

public class ServerTester : MonoBehaviour
{
    private TcpClient _client;
    private NetworkStream _stream;

    void Start()
    {
        ConnectToServer();
    }

    void ConnectToServer()
    {
        try
        {
            _client = new TcpClient("127.0.0.1", 7777);
            _stream = _client.GetStream();
            Debug.Log("C++ 서버 접속 성공! 폭탄 투하 준비 완료.");
        }
        catch (SocketException e)
        {
            Debug.LogError($"서버 접속 실패: {e.Message}");
        }
    }

    // ★ 이 함수를 유니티의 UI 버튼(Button) OnClick 이벤트에 연결할 것입니다.
    public void SendCrashBomb()
    {
        if (_client == null || !_client.Connected || _stream == null)
        {
            Debug.LogWarning("서버에 연결되어 있지 않아 패킷을 보낼 수 없습니다.");
            return;
        }

        try
        {
            // 1. C++ 서버가 기다리고 있는 PacketType::CRASH_BOMB (999)를 세팅합니다.
            ushort packetType = 999;

            // 2. 999라는 숫자를 네트워크로 전송하기 위해 바이트 배열로 직렬화(Serialization)합니다.
            // C#의 BitConverter는 윈도우 OS에 맞춰 자동으로 리틀 엔디안(Little Endian)으로 변환해 줍니다.
            byte[] buffer = BitConverter.GetBytes(packetType);

            // 3. 서버로 패킷을 발사합니다.
            _stream.Write(buffer, 0, buffer.Length);
            _stream.Flush();

            Debug.Log("[CRASH_BOMB] 악의적 패킷 전송 완료! 3초 뒤 C++ 서버가 데드락에 빠집니다...");
        }
        catch (Exception e)
        {
            Debug.LogError($"패킷 전송 중 오류 발생: {e.Message}");
        }
    }

    // 유니티 C# 코드 (비정상 종료 유발 버튼)
    public void TriggerAbnormalDisconnect()
    {
        if (_client != null && _client.Client != null && _client.Connected)
        {
            Socket rawSocket = _client.Client;

            // 1. "대기 시간 0초, 남은 데이터 다 버리고 즉시 폭파해"
            rawSocket.LingerState = new System.Net.Sockets.LingerOption(true, 0);

            // 2. "보내는 기능, 받는 기능 하드웨어 레벨에서 셧다운"
            rawSocket.Shutdown(System.Net.Sockets.SocketShutdown.Both);

            // 3. 0초의 유예도 주지 않고 닫아버림
            rawSocket.Close(0);

            Debug.Log("C#의 예의를 무시하고 강제 RST 패킷을 발송했습니다.");
        }
    }

    void OnApplicationQuit()
    {
        // 유니티 종료 시 안전하게 소켓과 스트림의 메모리를 해제합니다.
        if (_stream != null) _stream.Close();
        if (_client != null) _client.Close();
    }
}