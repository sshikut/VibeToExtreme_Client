using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private Vector3 targetPosition;
    private float moveSpeed = 5.0f; // 봇들의 이동 속도
    private SpriteRenderer spriteRenderer;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        targetPosition = transform.position;

        // ★ 핵심: 똑같은 클론 봇들이라도 걷는 속도(3.0 ~ 7.0)를 다르게 부여하여 
        // 시간이 지날수록 서로 엉키며 자연스럽게 흩어지게 만듭니다!
        moveSpeed = UnityEngine.Random.Range(3.0f, 7.0f);
    }

    // NetworkManager가 패킷을 파싱한 후 호출해 주는 함수
    public void SetTargetPosition(float x, float y, float dx, float dy)
    {
        targetPosition = new Vector3(x, y, 0);

        // 바라보는 방향(dirX)에 따라 스프라이트 좌우 반전 처리
        if (dx < 0) spriteRenderer.flipX = true;
        else if (dx > 0) spriteRenderer.flipX = false;
    }

    void Update()
    {
        // ★ 핵심: 매 프레임마다 내 현재 위치에서 목표 위치를 향해 부드럽게 이동합니다!
        // Vector3.MoveTowards는 지정된 속도(moveSpeed)로 일정한 보폭으로 걸어갑니다.
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        // (참고) 만약 얼음판 위를 미끄러지듯 이동하게 하려면 아래의 Lerp를 사용합니다.
        // transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * moveSpeed);
    }
}