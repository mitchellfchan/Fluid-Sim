using UnityEngine;

public class MoveBackAndForth : MonoBehaviour
{

    public Vector3 travelDistance;
    public float period;
    private Vector3 startPos;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startPos = transform.position;
    }

    // Update is called once per frame
    void Update()
    {
        float t = Time.time/period;
        transform.position = startPos + Mathf.Sin(t) * travelDistance;
        

    }
}
