# 🎮 VibeToExtreme_Client
**Unity 기반 Zero-Allocation 실시간 네트워크 동기화 클라이언트**

본 리포지토리는 C++ 서버와 실시간으로 통신하며, 대규모 패킷 수신 환경에서도 프레임 드랍 없이 부드러운 캐릭터 움직임을 구현하는 Unity 네트워크 클라이언트입니다.

## 🛠 Tech Stack
- **Engine:** Unity 2022.3.62f3
- **Language:** C#
- **Networking:** System.Net.Sockets (TCP)

## ✨ Key Features
- **Zero-Allocation Deserialization:** `unsafe` 블록과 `fixed` 포인터를 활용하여 수신 버퍼에서 구조체로 직접 메모리 캐스팅을 수행, GC 스파이크 및 프레임 드랍 방지.
- **Dead Reckoning:** 서버로부터 수신한 좌표를 기반으로 선형 보간(`Vector3.Lerp`)을 적용하여 네트워크 지연 상황에서도 부드러운 이동 동기화 구현.
- **TCP_NODELAY:** 네이글 알고리즘(Nagle's Algorithm) 비활성화를 통해 이동 패킷의 즉각적인 반응성 확보.
- **Sticky Packet Defense:** TCP 스트림 파싱 로직을 통해 패킷 뭉침 및 쪼개짐 현상을 완벽하게 방어하는 버퍼 관리 시스템.

## 🎥 Demonstration
- **Multi-Client Sync Video:** [YouTube Link](https://youtu.be/GW1OP3ZNZ0g)

## 🏗 System Architecture
클라이언트는 초당 수천 개의 패킷을 수신하는 극한의 상황을 가정하여 설계되었습니다. 수신부의 모든 파싱 로직은 메모리 복사를 최소화하는 방향으로 최적화되어 있습니다.

## 📂 Related Repository
- [VibeToExtreme_Server (C++)](https://github.com/sshikut/VibeToExtreme_Server)
