using UnityEngine;

public class MoveInCircle : MonoBehaviour
{
    public float radius = 1f;
    public float period = 2f;

    private Vector3 startPosition;
    private float angle;

    void Start()
    {
        startPosition = transform.position;
    }

    void Update()
    {
        if (period <= 0f) return;
        angle += (2 * Mathf.PI / period) * Time.deltaTime;
        float x = Mathf.Cos(angle) * radius;
        float z = Mathf.Sin(angle) * radius;
        transform.position = startPosition + new Vector3(x, 0, z);
    }
}
