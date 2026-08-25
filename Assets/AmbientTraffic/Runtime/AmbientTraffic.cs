using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FuR.AmbientTraffic
{
/// <summary>
/// Cosmetic stop-and-go background traffic. Cars pack bumper-to-bumper in lanes, follow the car
/// ahead (accelerate to close the gap, brake to a stop), and flip between random go/stop phases so
/// pauses ripple backward as real stop-and-go waves. A camera-anchored window recycles cars off the
/// rear edge back to the front to keep the belt full. Colliders are OFF during normal play; a call
/// to TriggerImpactCollisions can wake nearby colliders (e.g. on a death/impact). Private RNG only
/// (spawn / prefab pick / phase timing), so any seeded gameplay stays deterministic.
/// </summary>
public class AmbientTraffic : MonoBehaviour
{
    [Header("Lanes")]
    [Tooltip("One lane per side. Position each lane's |x| so traffic sits clear of wherever your " +
        "gameplay happens; the middle is typically left open for the player. Each lane auto-fills " +
        "bumper-to-bumper (no carCount).")]
    public TrafficLane[] lanes = new TrafficLane[]
    {
        new TrafficLane { laneX = 5.1f, rightSide = true,  crawlSpeed = 6f },
        new TrafficLane { laneX = 5.1f, rightSide = false, crawlSpeed = 6f },
    };
    public float groundY = 0f;

    [Header("Jam")]
    [Tooltip("Bumper-to-bumper gap kept between a car's nose and the tail of the car ahead " +
        "(world units). This is the one density knob: larger = looser jam / fewer cars / less " +
        "rendering cost; smaller = tighter gridlock. A car brakes to a full stop exactly this far " +
        "behind the car ahead.")]
    public float gapDesired = 2.5f;
    [Tooltip("How fast a car eases UP to its crawl speed (u/s^2). Gentle = relaxed creep.")]
    public float accel = 4f;
    [Tooltip("How fast a car eases DOWN to a stop (u/s^2). Also the safe-following brake used to " +
        "guarantee a car never overruns the one ahead.")]
    public float brake = 12f;
    [Tooltip("Braking distance -- how close a car's nose gets to the tail of the car ahead before " +
        "it has to brake to a stop (world units). Deliberately MUCH smaller than gapDesired: a car " +
        "sitting at the packed spacing (gapDesired) still has (gapDesired - brakeGap) of headroom to " +
        "creep at crawl, so the line behaves as a moving conveyor instead of a gridlock. Keep it " +
        "small (a fraction of gapDesired) so a lane keeps conveying instead of freezing.")]
    public float brakeGap = 0.6f;
    [Tooltip("How far a car travels each time it moves, in MULTIPLES OF ITS OWN LENGTH (a random " +
        "pick per move, private RNG). This is DISTANCE, not time: the budget is consumed only by " +
        "actual forward progress, so a car blocked bumper-to-bumper still travels its full pull-up " +
        "once the gap ahead finally opens, instead of many tiny lurches. 1.0 = exactly one car " +
        "length. Bigger = longer surges between stops (more flowing); smaller = shorter shuffles.")]
    [MinMaxRange(0.2f, 4f)]
    public MinMaxRange moveDistance = new MinMaxRange(0.8f, 2.0f);
    [Tooltip("Random 'stop' phase length (seconds) a car pauses after finishing a move. Staggered " +
        "per car so the line stops and starts in backward-traveling waves, not in unison. Short = " +
        "flowing jam; lengthen for heavier gridlock. (A car can also sit still longer than this " +
        "when it's simply blocked by a stopped car ahead -- that isn't a stop phase, it's waiting.)")]
    [MinMaxRange(0.1f, 6f)]
    public MinMaxRange stopDuration = new MinMaxRange(0.4f, 1.2f);

    [Header("Colour variety")]
    [Tooltip("Give each car a random body colour for variety. Implemented as a small pool of " +
        "colour variants of the body material (same URP/Lit shader), assigned to the body-paint " +
        "slot only -- so the SRP Batcher still batches every car and there is no extra draw-call " +
        "cost (per-object MaterialPropertyBlock would instead BREAK SRP batching, which is why we " +
        "use variants). Wheels, lights and glass keep their atlas look. Colour multiplies the " +
        "shared city atlas, so vehicles with a baked livery (taxi/police/ambulance) tint less " +
        "cleanly than plain bodies.")]
    public bool randomTint = true;
    [Tooltip("Palette a car's body colour is picked from at random (one variant material per " +
        "entry). Realistic car distribution: weighted toward neutrals (white/silver/greys/black) " +
        "with a few muted colours. Colour MULTIPLIES the shared city atlas, so keep values in a " +
        "realistic range -- pure-bright entries read as light grey, not white. Duplicate an entry " +
        "to make it more common.")]
    public Color[] tintColors =
    {
        new Color(0.90f, 0.90f, 0.92f), // white
        new Color(0.90f, 0.90f, 0.92f), // white (weighted)
        new Color(0.80f, 0.81f, 0.83f), // silver
        new Color(0.80f, 0.81f, 0.83f), // silver (weighted)
        new Color(0.64f, 0.65f, 0.68f), // light grey
        new Color(0.46f, 0.47f, 0.50f), // grey
        new Color(0.27f, 0.28f, 0.31f), // dark grey
        new Color(0.14f, 0.14f, 0.16f), // near-black
        new Color(0.14f, 0.14f, 0.16f), // near-black (weighted)
        new Color(0.52f, 0.17f, 0.17f), // dark red
        new Color(0.20f, 0.26f, 0.42f), // navy
        new Color(0.34f, 0.45f, 0.57f), // steel blue
        new Color(0.24f, 0.35f, 0.27f), // dark green
        new Color(0.60f, 0.53f, 0.40f), // tan / beige
        new Color(0.40f, 0.15f, 0.21f), // burgundy
    };

    [Header("Impact collision")]
    [Tooltip("Enable optional impact colliders. Colliders are OFF for the whole normal run (zero " +
        "physics cost); call TriggerImpactCollisions(target, duration) to wake box colliders on the " +
        "cars near a target for a few seconds (e.g. a death/impact). Needs the traffic layer to exist " +
        "and to collide with your object's layer in the physics matrix.")]
    public bool impactCollision = false;
    [Tooltip("Cars within this radius of the impact target get their collider enabled (refreshed " +
        "each frame, so it follows the target).")]
    public float colliderWakeRadius = 8f;
    [Tooltip("Default seconds to keep waking nearby colliders (used when TriggerImpactCollisions is " +
        "called with duration <= 0).")]
    public float impactColliderDuration = 5f;
    [Tooltip("Physics layer assigned to the car colliders. Must exist in Project Settings > Tags and " +
        "Layers.")]
    public string trafficLayerName = "Traffic";
    [Tooltip("Optional: a layer name the traffic layer should be made to collide with at runtime " +
        "(Physics.IgnoreLayerCollision(..., false)). Leave empty to configure the physics matrix " +
        "yourself.")]
    public string collideWithLayerName = "";
    [Tooltip("Optional default impact target. TriggerImpactCollisions overrides this when passed a " +
        "non-null target.")]
    public Transform impactTarget;

    [Header("Window (relative to anchor)")]
    [Tooltip("Optional anchor the recycling window follows. Leave empty to use Camera.main; the " +
        "window (and audio listener distance) track this transform's z.")]
    public Transform windowAnchor;
    [Tooltip("The window follows the anchor; make windowAhead reach as far as the camera can see " +
        "so the jam doesn't visibly end at the window edge. Cars that fall off the rear edge " +
        "recycle to the front to keep the belt full.")]
    public float windowAhead = 280f;
    public float windowBehind = 40f;

    [Header("Move SFX")]
    [Tooltip("Steady, low, loopable engine/tyre-roll beds, one picked at random per car " +
        "(private RNG, so seeded gameplay stays deterministic). These are NOT whoosh one-shots: " +
        "each car's source LOOPS and its volume is faded in when the car starts creeping and out " +
        "when it stops, so the sound plays for exactly as long as the car is moving. A stopped " +
        "car in the jam is silent even right next to the listener; only a car actually creeping " +
        "is heard.")]
    public AudioClip[] passByClips;
    [Tooltip("Fade in/out time (seconds) for a car's move loop as it starts/stops creeping. Short " +
        "= snappy; longer = gentler swell. The whole clip is constant-volume, so this envelope is " +
        "the only volume shaping.")]
    public float passByFadeTime = 0.25f;
    [Tooltip("Random pitch per car so shared clips don't sound identical. Pitch is already dropped " +
        "in the clips themselves (low-speed engine, not a highway whoosh); this only adds mild " +
        "per-car variation around 1.")]
    [MinMaxRange(0.6f, 1.5f)]
    public MinMaxRange passByPitch = new MinMaxRange(0.9f, 1.1f);
    [Tooltip("Keep these as background ambience -- higher reads as annoying/foreground.")]
    [Range(0f, 1f)] public float passByVolume = 0.29f;
    [Tooltip("Linear rolloff. The listener is on the camera (~10-13 units from a passing car), " +
        "so min must cover that or every car sounds pre-attenuated. maxDistance also gates which " +
        "moving cars get an active (playing) loop, so only cars within earshot cost a voice.")]
    public float passByMinDistance = 12f;
    public float passByMaxDistance = 30f;

    [Header("References")]
    public Transform parent;

    // A car whose current speed is below this (u/s) counts as "stopped" and never fires pass-by SFX.
    private const float MovingEps = 0.3f;
    // Backstop so a tiny prefab / misconfigured gap can't spawn a runaway number of cars per lane.
    private const int MaxCarsPerLane = 200;
    private const float DefaultHalfLen = 2f; // fallback when a prefab has no renderers
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private Transform _anchor;
    private System.Random _rng;

    private class Car
    {
        public Transform T;
        public float HalfLen;   // half the car's z-extent (measured from renderer bounds at spawn)
        public float Speed;     // current speed magnitude (>= 0); travel direction is Dir
        public int Dir;         // +1 = with-player (+z), -1 = oncoming (-z)
        public float CrawlSpeed;
        public bool Rolling;        // true = mid-move (go phase); false = paused (stop phase)
        public float MoveRemaining; // world-units of forward progress still owed this move (go phase)
        public float StopTimer;     // seconds left in the current stop phase
        public AudioSource PassSfx; // looping move-SFX bed; null when no clips assigned
        public BoxCollider Col;     // impact-collision box (disabled until TriggerImpactCollisions wakes it); null if unused
    }

    // One lane's cars, kept ordered by ascending world z. Order is stable except at recycle
    // (cars never overtake -- a follower always brakes before reaching the car ahead), so the
    // recycled (lowest-z) car simply pops off the front and is appended as the new highest-z car.
    private class LaneRT
    {
        public int Dir;
        public float X;
        public float CrawlSpeed;
        public readonly List<Car> Cars = new();
    }

    private readonly List<LaneRT> _lanes = new();
    private readonly List<Car> _cars = new(); // flat view of every car (used by ClearTraffic)
    private readonly List<Material> _tintPool = new(); // runtime body-colour variants (shared across cars)
    private int _trafficLayer = -1;
    private bool _cleared;
    private Coroutine _impactRoutine;

    /// <summary>Wake box colliders on the cars near <paramref name="target"/> for
    /// <paramref name="duration"/> seconds, so an object moving through the jam bumps cars instead of
    /// passing through. Colliders are otherwise disabled (zero physics cost). No-op unless
    /// impactCollision is enabled and the traffic layer resolved at startup. Pass duration &lt;= 0 to
    /// use impactColliderDuration. Safe to call repeatedly; the follow window restarts.</summary>
    public void TriggerImpactCollisions(Transform target, float duration = 0f)
    {
        if (!impactCollision || _cleared || _trafficLayer < 0) return;
        Transform t = target != null ? target : impactTarget;
        if (t == null) t = _anchor;
        float d = duration > 0f ? duration : impactColliderDuration;
        if (_impactRoutine != null) StopCoroutine(_impactRoutine);
        _impactRoutine = StartCoroutine(WakeNearbyColliders(t, d));
    }

    IEnumerator WakeNearbyColliders(Transform target, float duration)
    {
        float end = Time.time + duration;
        float r2 = colliderWakeRadius * colliderWakeRadius;
        while (Time.time < end)
        {
            Vector3 p = target != null ? target.position : _anchor.position;
            foreach (var car in _cars)
            {
                if (car.Col == null || car.Col.enabled || car.T == null) continue;
                if ((car.T.position - p).sqrMagnitude <= r2) car.Col.enabled = true;
            }
            yield return null;
        }
        _impactRoutine = null;
    }

    void OnDestroy()
    {
        foreach (var m in _tintPool) if (m != null) Destroy(m);
        _tintPool.Clear();
    }

    /// <summary>Despawns all ambient traffic (e.g. when the player finishes / reaches the end of
    /// the level). Per-round reset is the scene reload (this component and its spawned cars are
    /// recreated fresh each round).</summary>
    public void ClearTraffic()
    {
        _cleared = true;
        foreach (var car in _cars)
            if (car.T != null) car.T.gameObject.SetActive(false);
    }

    void Start()
    {
        if (lanes == null || lanes.Length == 0) { enabled = false; return; }
        if (parent == null) parent = transform;
        _anchor = windowAnchor != null ? windowAnchor
                : (Camera.main != null ? Camera.main.transform : transform);
        _rng = new System.Random();

        if (impactCollision) SetupImpactCollisionLayers();

        float rearZ = _anchor.position.z - windowBehind;
        float frontZ = _anchor.position.z + windowAhead;

        foreach (var lane in lanes)
        {
            if (lane.prefabs == null || lane.prefabs.Length == 0)
            {
                Debug.LogWarning("[AmbientTraffic] Lane at x=" + lane.laneX + " has no prefabs assigned; skipped.", this);
                continue;
            }

            int dir = lane.rightSide ? 1 : -1;
            float x = dir * lane.laneX;
            var laneRT = new LaneRT { Dir = dir, X = x, CrawlSpeed = lane.crawlSpeed };

            // Auto-fill: pack cars up the window, each gapDesired behind the previous, until the
            // next car's tail would clear the front edge. Lengths are measured per prefab so a
            // mix of cars/buses/trucks still packs bumper-to-bumper.
            float cursorZ = rearZ; // z where the current car's REAR sits
            while (laneRT.Cars.Count < MaxCarsPerLane)
            {
                var prefab = lane.prefabs[_rng.Next(lane.prefabs.Length)];
                var go = Instantiate(prefab, Vector3.zero,
                    Quaternion.Euler(0f, dir > 0 ? 0f : 180f, 0f), parent);
                bool hasBounds = TryMeasureBounds(go, out Bounds wb);
                float halfLen = hasBounds && wb.extents.z > 0.01f ? wb.extents.z : DefaultHalfLen;
                float centerZ = cursorZ + halfLen;
                if (centerZ - halfLen > frontZ) { Destroy(go); break; } // window full

                // Add the collider while the car is still at the origin, so InverseTransformPoint
                // yields a correct LOCAL box centre; the box then rides along when the car is moved.
                var col = (impactCollision && _trafficLayer >= 0 && hasBounds) ? AddImpactCollider(go, wb) : null;
                go.transform.position = new Vector3(x, groundY, centerZ);
                if (randomTint) ApplyTint(go);

                bool startRolling = _rng.NextDouble() < 0.6;
                var car = new Car
                {
                    T = go.transform,
                    HalfLen = halfLen,
                    Col = col,
                    Speed = 0f,
                    Dir = dir,
                    CrawlSpeed = lane.crawlSpeed,
                    Rolling = startRolling,
                    MoveRemaining = startRolling ? RandMoveDist(halfLen) : 0f,
                    StopTimer = startRolling ? 0f : RandRange(stopDuration),
                    PassSfx = AttachPassByAudio(go),
                };
                laneRT.Cars.Add(car);
                _cars.Add(car);

                cursorZ = centerZ + halfLen + gapDesired; // rear of the next car
            }

            if (laneRT.Cars.Count > 0) _lanes.Add(laneRT);
        }
    }

    /// <summary>Combined world-space renderer bounds of a spawned car (used for both spacing and
    /// the impact-collision box). False when the prefab has no renderers.</summary>
    bool TryMeasureBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return false;
        bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) bounds.Encapsulate(rends[i].bounds);
        return true;
    }

    /// <summary>Resolves the traffic layer and, if collideWithLayerName is set, makes it collide
    /// with that layer at runtime (e.g. to unblock an object the traffic layer otherwise ignores).
    /// No-op (with a warning) if the traffic layer is missing.</summary>
    void SetupImpactCollisionLayers()
    {
        _trafficLayer = LayerMask.NameToLayer(trafficLayerName);
        if (_trafficLayer < 0)
        {
            Debug.LogWarning("[AmbientTraffic] Layer '" + trafficLayerName + "' not found; impact " +
                "collision disabled. Add it in Project Settings > Tags and Layers.", this);
            return;
        }
        if (!string.IsNullOrEmpty(collideWithLayerName))
        {
            int other = LayerMask.NameToLayer(collideWithLayerName);
            if (other >= 0) Physics.IgnoreLayerCollision(_trafficLayer, other, false);
        }
    }

    /// <summary>Adds a disabled kinematic box collider sized to the car's bounds, on the traffic
    /// layer. Kinematic (not static) because the car moves every frame -- a bare static collider
    /// moving would force PhysX to rebuild its static tree constantly. Stays disabled until
    /// TriggerImpactCollisions wakes it.</summary>
    BoxCollider AddImpactCollider(GameObject go, Bounds wb)
    {
        var t = go.transform;
        var box = go.AddComponent<BoxCollider>();
        var ls = t.lossyScale;
        box.center = t.InverseTransformPoint(wb.center);
        box.size = new Vector3(
            Mathf.Abs(wb.size.x / Mathf.Max(1e-4f, ls.x)),
            Mathf.Abs(wb.size.y / Mathf.Max(1e-4f, ls.y)),
            Mathf.Abs(wb.size.z / Mathf.Max(1e-4f, ls.z)));
        box.enabled = false;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        go.layer = _trafficLayer;
        return box;
    }

    /// <summary>Assigns a random body colour to a car by swapping its body-paint material slot to
    /// one of a shared pool of colour variants (built lazily from the body material). Variants
    /// share the URP/Lit shader, so all cars stay SRP-batched -- no draw-call cost. Only the body
    /// renderer's slot 0 is touched; wheels, lights and glass keep the atlas material.</summary>
    void ApplyTint(GameObject car)
    {
        var body = FindBodyRenderer(car);
        if (body == null) return;
        var slots = body.sharedMaterials;
        if (slots.Length == 0 || slots[0] == null) return;

        EnsureTintPool(slots[0]);
        if (_tintPool.Count == 0) return;

        slots[0] = _tintPool[_rng.Next(_tintPool.Count)];
        body.sharedMaterials = slots;
    }

    /// <summary>The body renderer = the one carrying more than one material (body paint + lights);
    /// its slot 0 is the paint. Falls back to the first renderer if none is multi-material.</summary>
    Renderer FindBodyRenderer(GameObject car)
    {
        Renderer first = null;
        foreach (var r in car.GetComponentsInChildren<Renderer>())
        {
            if (r.sharedMaterials.Length > 1) return r;
            if (first == null) first = r;
        }
        return first;
    }

    /// <summary>Builds the shared colour-variant pool once, one variant material per tintColors
    /// entry (each multiplies the body atlas by that colour).</summary>
    void EnsureTintPool(Material src)
    {
        if (_tintPool.Count > 0 || src == null || tintColors == null) return;
        foreach (var col in tintColors)
        {
            var m = new Material(src);
            m.SetColor(BaseColorId, col);
            m.enableInstancing = true;
            _tintPool.Add(m);
        }
    }

    float RandRange(MinMaxRange r) => r.Min + (float)_rng.NextDouble() * (r.Max - r.Min);

    /// <summary>A fresh move budget in world units: a random multiple (moveDistance, in car
    /// lengths) of this car's own length (2 * halfLen).</summary>
    float RandMoveDist(float halfLen) => 2f * halfLen * RandRange(moveDistance);

    /// <summary>Looping move-SFX source: random clip + random pitch per car (via _rng -- never the
    /// engine's global RNG, which seeded gameplay owns). Starts silent and NOT playing; Update
    /// fades its volume in/out and starts/stops the loop as the car creeps/stops. Returns null
    /// when no clips are assigned so the SFX pass in Update short-circuits.</summary>
    AudioSource AttachPassByAudio(GameObject car)
    {
        if (passByClips == null || passByClips.Length == 0) return null;
        var clip = passByClips[_rng.Next(passByClips.Length)];
        if (clip == null) return null;

        var src = car.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.volume = 0f; // faded up when the car starts moving
        src.pitch = passByPitch.Min + (float)_rng.NextDouble() * (passByPitch.Max - passByPitch.Min);
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = passByMinDistance;
        src.maxDistance = passByMaxDistance;
        src.dopplerLevel = 0f; // creep is slow; no doppler
        return src;
    }

    void Update()
    {
        if (_cleared) return; // cleared -- cars despawned, nothing to move
        if (_anchor == null) _anchor = transform; // reacquire if a runtime windowAnchor was destroyed

        float minZ = _anchor.position.z - windowBehind;
        float maxZ = _anchor.position.z + windowAhead;
        float dt = Time.deltaTime;

        // Move SFX is faded in/out by distance to the listener (the camera/AudioListener).
        Vector3 listener = _anchor != null ? _anchor.position : transform.position;

        foreach (var lane in _lanes)
        {
            AdvanceLane(lane, dt);
            RecycleLane(lane, minZ, maxZ);
            if (passByClips != null && passByClips.Length > 0)
                UpdateLaneSfx(lane, listener, dt);
        }
    }

    /// <summary>Car-following for one lane. Processed lead -> rear so each follower reacts to the
    /// leader's already-updated position this frame. "Ahead" is the neighbor at index i+Dir
    /// (Dir=+1 lane: ahead = higher z; Dir=-1 lane: ahead = lower z). Travel is measured in
    /// s = Dir*z so "forward" is always +s for both directions.</summary>
    void AdvanceLane(LaneRT lane, float dt)
    {
        var cars = lane.Cars;
        int dir = lane.Dir;
        int count = cars.Count;
        // Iterate from the lead toward the rear (Dir=+1 lead is the last index; Dir=-1 lead is 0).
        int start = dir > 0 ? count - 1 : 0;
        int end = dir > 0 ? -1 : count;
        int stepI = dir > 0 ? -1 : 1;

        for (int i = start; i != end; i += stepI)
        {
            var car = cars[i];
            float s = dir * car.T.position.z;

            int aheadIdx = i + dir;
            bool hasAhead = aheadIdx >= 0 && aheadIdx < count;
            float gap = Mathf.Infinity;
            float aheadTailS = 0f;
            if (hasAhead)
            {
                var ahead = cars[aheadIdx];
                aheadTailS = dir * ahead.T.position.z - ahead.HalfLen;
                gap = aheadTailS - (s + car.HalfLen);
            }

            // Safe-following cap: brake toward a stop only as the nose closes to within brakeGap
            // of the car ahead. Because brakeGap << gapDesired (the packed spacing), a car at rest
            // spacing still has headroom to creep at crawl -- the line conveys instead of gridlocking.
            float safeSpeed = Mathf.Sqrt(2f * brake * Mathf.Max(0f, gap - brakeGap));

            // Distance-committed go/stop. A stopped car counts down its pause, then commits to a
            // fresh move budget; a rolling car targets crawl until that budget -- consumed only by
            // ACTUAL forward progress below -- is spent. Blocked waiting never burns the budget, so
            // every move covers its full length no matter how long the car waited for a gap to open.
            if (!car.Rolling)
            {
                car.StopTimer -= dt;
                if (car.StopTimer <= 0f)
                {
                    car.Rolling = true;
                    car.MoveRemaining = RandMoveDist(car.HalfLen);
                }
            }

            float desired = Mathf.Min(safeSpeed, car.Rolling ? car.CrawlSpeed : 0f);
            float rate = desired > car.Speed ? accel : brake;
            car.Speed = Mathf.MoveTowards(car.Speed, desired, rate * dt);

            float s0 = s;
            s += car.Speed * dt;

            // Hard safety clamp: never let a car's nose pass into the brakeGap zone of the car
            // ahead (guards against float drift / a just-recycled neighbor).
            if (hasAhead)
            {
                float maxFrontS = aheadTailS - brakeGap;
                if (s + car.HalfLen > maxFrontS)
                {
                    s = maxFrontS - car.HalfLen;
                    car.Speed = 0f;
                }
            }

            // Consume the move budget by real forward progress only; end the move when it's spent.
            if (car.Rolling)
            {
                car.MoveRemaining -= Mathf.Max(0f, s - s0);
                if (car.MoveRemaining <= 0f)
                {
                    car.Rolling = false;
                    car.StopTimer = RandRange(stopDuration);
                }
            }

            var p = car.T.position;
            p.z = dir * s;
            car.T.position = p;
        }
    }

    /// <summary>Player is always faster than the crawl, so cars drift toward the window's rear
    /// edge (minZ). The exiting car is always the lane's lowest-z car -- pop it and re-place it
    /// bumper-to-bumper just above the current top car, keeping the belt full and the ascending-z
    /// order intact with no sort. The maxZ case is a defensive guard for a rare backward camera
    /// move.</summary>
    void RecycleLane(LaneRT lane, float minZ, float maxZ)
    {
        var cars = lane.Cars;

        // Off the rear edge -> re-enter at the front.
        while (cars.Count > 0)
        {
            var c = cars[0];
            if (c.T.position.z >= minZ) break;
            var top = cars[cars.Count - 1];
            float newZ = top.T.position.z + top.HalfLen + gapDesired + c.HalfLen;
            var p = c.T.position; p.z = newZ; c.T.position = p;
            c.Speed = 0f;
            cars.RemoveAt(0);
            cars.Add(c);
        }

        // Off the front edge (camera moved backward) -> re-enter at the rear.
        while (cars.Count > 0)
        {
            var c = cars[cars.Count - 1];
            if (c.T.position.z <= maxZ) break;
            var bottom = cars[0];
            float newZ = bottom.T.position.z - bottom.HalfLen - gapDesired - c.HalfLen;
            var p = c.T.position; p.z = newZ; c.T.position = p;
            c.Speed = 0f;
            cars.RemoveAt(cars.Count - 1);
            cars.Insert(0, c);
        }
    }

    /// <summary>Move SFX for one lane. Each car's looping bed is faded UP while the car is
    /// actually creeping (Rolling &amp; Speed &gt; MovingEps) AND within earshot of the listener, and
    /// faded DOWN otherwise -- so the sound plays for exactly as long as the car moves. A car
    /// sitting still in the jam is silent even right beside the character. The loop is started
    /// on first audible frame and stopped once fully faded out, so only nearby moving cars cost
    /// a playing voice.</summary>
    void UpdateLaneSfx(LaneRT lane, Vector3 listener, float dt)
    {
        float maxDelta = passByVolume / Mathf.Max(0.01f, passByFadeTime) * dt;
        float earshotSq = passByMaxDistance * passByMaxDistance * 1.3f; // small margin past rolloff

        foreach (var car in lane.Cars)
        {
            var src = car.PassSfx;
            if (src == null) continue;

            bool moving = car.Rolling && car.Speed > MovingEps;
            bool near = (car.T.position - listener).sqrMagnitude < earshotSq;
            float target = (moving && near) ? passByVolume : 0f;

            float v = Mathf.MoveTowards(src.volume, target, maxDelta);
            src.volume = v;

            if (v > 0.0005f) { if (!src.isPlaying) src.Play(); }
            else if (src.isPlaying) src.Stop();
        }
    }
}
}
