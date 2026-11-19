using UnityEngine;

/// <summary>
/// Verify-the-loop heartbeat. Proves the pipeline works end to end:
/// Claude writes C# -> Unity compiles -> you press Play -> it runs.
///
/// Attach this to any GameObject in the scene (e.g. the Main Camera),
/// press Play, and watch the Console: it logs once per second.
/// </summary>
public class HelloLoop : MonoBehaviour
{
    private float _nextLogTime;
    private int _heartbeats;

    private void Start()
    {
        Debug.Log("[Dark Skies] HelloLoop started — the pipeline works. ✅");
    }

    private void Update()
    {
        // Log roughly once per second instead of every frame.
        if (Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + 1f;
            _heartbeats++;
            Debug.Log($"[Dark Skies] heartbeat #{_heartbeats} — {Time.time:F1}s elapsed, frame {Time.frameCount}");
        }
    }
}
