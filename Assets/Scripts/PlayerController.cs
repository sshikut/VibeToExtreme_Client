using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private Vector3 targetPos;
    private float lerpSpeed = 15f; // 보간 속도 (값이 클수록 빨리 따라감)

    void Start()
    {
        // 처음 태어났을 때의 위치를 목표 위치로 초기화
        targetPos = transform.position;
    }

    public void SetTargetPosition(float x, float y, float dx, float dy)
    {
        targetPos = new Vector3(x, y, 0);

        // TODO: 나중에 dx, dy를 사용해 애니메이터(Animator)의 파라미터를 바꿔주면 됩니다.
        // GetComponent<Animator>().SetFloat("DirX", dx);
    }

    void Update()
    {
        // 순간이동(transform.position = targetPos) 하지 않고, 
        // 현재 위치에서 목표 위치로 부드럽게 미끄러지듯 이동합니다 (선형 보간)
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * lerpSpeed);
    }
}