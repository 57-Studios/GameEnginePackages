using UnityEngine;

namespace Studios57.DeformableSnow.Testing
{
    /// <summary>
    /// Phase 1 test harness only. Moves a two-footed rig along a straight patrol line across
    /// the snow volume so the heightfield can be inspected. It is not gameplay code: no
    /// character controller, animation, or footstep events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowTestWalker : MonoBehaviour
    {
        [SerializeField] private Terrain terrain;
        [SerializeField] private Transform leftFoot;
        [SerializeField] private Transform rightFoot;

        [Header("Patrol")]
        [SerializeField] private Vector3 patrolStart = new Vector3(-3f, 0f, -1.2f);
        [SerializeField] private Vector3 patrolEnd = new Vector3(3f, 0f, 1.2f);
        [Min(0.1f)] [SerializeField] private float speed = 1.1f;

        [Header("Gait")]
        [Min(0.1f)] [SerializeField] private float stepFrequency = 1.8f;
        [Min(0f)] [SerializeField] private float stepLength = 0.3f;
        [Tooltip("Peak swing height. Must clear the volume's contact band decisively, or the swinging foot is still captured and smears the print forward into its neighbour until the trail is one continuous rail.")]
        [Min(0f)] [SerializeField] private float stepLift = 0.14f;
        [Tooltip("Shapes the lift curve. 1 is a sine; lower values leave the ground faster and spend less of the swing inside the contact band.")]
        [Range(0.2f, 1f)] [SerializeField] private float liftSharpness = 0.4f;
        [Min(0f)] [SerializeField] private float groundClearance = 0.02f;

        private Vector3 center;
        private Vector3 lastPosition;

        private void Start()
        {
            center = transform.position;
            lastPosition = transform.position;
        }

        private void Update()
        {
            if (terrain == null)
                return;

            Vector3 a = center + patrolStart;
            Vector3 b = center + patrolEnd;
            float length = Vector3.Distance(a, b);
            if (length < 0.01f)
                return;

            float t = Mathf.PingPong(Time.time * speed / length, 1f);
            Vector3 position = Vector3.Lerp(a, b, t);
            position.y = terrain.SampleHeight(position) + terrain.transform.position.y + groundClearance;
            transform.position = position;

            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.00001f)
                transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            lastPosition = position;

            // Triangle-wave gait: during stance the foot moves backwards relative to the
            // body at exactly the body speed, so it is stationary on the ground and leaves
            // a print instead of a smear. The swing half lifts and returns the foot.
            float phase = Mathf.Repeat(Time.time * stepFrequency, 1f);
            SetFoot(leftFoot, phase);
            SetFoot(rightFoot, Mathf.Repeat(phase + 0.5f, 1f));
        }

        private void SetFoot(Transform foot, float phase)
        {
            if (foot == null)
                return;

            float halfStep = stepLength * 0.5f;
            float z;
            float lift;
            if (phase < 0.5f)
            {
                float t = phase / 0.5f;
                z = Mathf.Lerp(halfStep, -halfStep, t);
                lift = 0f;
            }
            else
            {
                float t = (phase - 0.5f) / 0.5f;
                z = Mathf.Lerp(-halfStep, halfStep, t);
                // A plain sine spends most of the swing just above the ground, so at this
                // step length the foot stays inside the contact band and drags a smear
                // between prints. Raising the sine to a fractional power flattens it near
                // the edges, so the foot leaves and re-enters the ground quickly and only
                // the stance phase writes into the heightfield.
                float sine = Mathf.Sin(t * Mathf.PI);
                lift = Mathf.Pow(sine, liftSharpness) * stepLift;
            }

            Vector3 local = foot.localPosition;
            local.y = groundClearance + lift;
            local.z = z;
            foot.localPosition = local;
        }

        public void Configure(Terrain targetTerrain, Transform left, Transform right, Vector3 start, Vector3 end, float moveSpeed)
        {
            terrain = targetTerrain;
            leftFoot = left;
            rightFoot = right;
            patrolStart = start;
            patrolEnd = end;
            speed = Mathf.Max(0.1f, moveSpeed);
        }

        /// <summary>
        /// Typed entry point for authoring tools. The gait fields are private and serialized,
        /// so tools would otherwise have to poke them through SerializedProperty by name and
        /// silently do nothing after a rename.
        /// </summary>
        public void ConfigureGait(float frequency, float length, float lift, float sharpness, float clearance)
        {
            stepFrequency = Mathf.Max(0.1f, frequency);
            stepLength = Mathf.Max(0f, length);
            stepLift = Mathf.Max(0f, lift);
            liftSharpness = Mathf.Clamp(sharpness, 0.2f, 1f);
            groundClearance = Mathf.Max(0f, clearance);
        }

        /// <summary>
        /// Patrol line in world space. "center" is captured in Start, so before the first
        /// frame it is still zero; fall back to the transform so editor tools can read the
        /// patrol without having entered Play Mode.
        /// </summary>
        public Vector3 PatrolOrigin => center == Vector3.zero ? transform.position : center;

        public Vector3 PatrolStartWorld => PatrolOrigin + patrolStart;

        public Vector3 PatrolEndWorld => PatrolOrigin + patrolEnd;
    }
}

