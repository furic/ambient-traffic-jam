using UnityEngine;

namespace FuR.AmbientTraffic
{
    /// <summary>One jammed traffic lane: a fixed |x| position, a side (which sign of x), and a max
    /// crawl speed shared by every car spawned into it. Cars pack bumper-to-bumper and follow the
    /// car ahead. Car count is auto-filled from the window and per-prefab length, so the jam stays
    /// bumper-to-bumper regardless of prefab size.</summary>
    [System.Serializable]
    public class TrafficLane
    {
        [Tooltip("Lane |x| from road center (positive; side is applied via rightSide).")]
        public float laneX = 5.1f;

        [Tooltip("true = right side (+x), driving WITH the camera forward (facing +z). " +
            "false = left side (-x), oncoming (facing -z).")]
        public bool rightSide = true;

        [Tooltip("Max creep speed for cars in this lane (traffic-jam crawl). Keep this well below " +
            "the camera/player travel speed so the jam reads as near-stationary traffic.")]
        public float crawlSpeed = 6f;

        [Tooltip("Vehicles used for this lane only (assign at least one). Different lanes may use " +
            "different prefab sets, e.g. buses/trucks kept to the outer lane.")]
        public GameObject[] prefabs;
    }
}
