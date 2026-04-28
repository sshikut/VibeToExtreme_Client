using UnityEngine;

public class LocalPlayer : MonoBehaviour
{
    public float moveSpeed = 5f;
    private float sendTimer = 0f;
    private float sendInterval = 0.05f; // 초당 20번(0.05초) 패킷 전송

    void Update()
    {
        // 1. 키보드 입력 받기 (WASD 또는 방향키)
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // 2. 내 화면에서 먼저 내 캐릭터를 이동시킴 (Client-Side Prediction)
        Vector3 moveDir = new Vector3(h, v, 0).normalized;
        transform.position += moveDir * moveSpeed * Time.deltaTime;

        // 3. 움직임이 있을 때만 서버로 패킷 전송!
        if (moveDir.sqrMagnitude > 0)
        {
            sendTimer += Time.deltaTime;
            if (sendTimer >= sendInterval) // 너무 미친듯이 쏘지 않게 조절
            {
                // NetworkManager 싱글톤을 통해 안전하게 패킷 발사!
                NetworkManager.Instance.SendMovePacket(
                    transform.position.x,
                    transform.position.y,
                    h,
                    v
                );
                sendTimer = 0f;
            }
        }
    }
}