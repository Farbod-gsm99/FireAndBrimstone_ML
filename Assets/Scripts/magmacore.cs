using UnityEngine;

namespace FireAndBrimstone
{
    /// <summary>
    /// Controls the pushable "Unstable Magma Core" in Room 3 (The Geyser Plug) and Room 4 (Boss Fight).
    /// - Rotation axes X, Y, Z are strictly frozen so the block slides solidly without tipping or spinning.
    /// - Cycles through Safe (MagmaCore_Safe) -> Warning (MagmaCore_Warning) -> AoE Fire Burst (FireHitbox).
    /// - Heats up 2x faster when absorbing the Dragon's fire stream in Room 4.
    /// - Snaps into the geyser hole and shuts off its explosion timer in Room 3.
    /// - Shatters/despawns after stunning the Dragon in Room 4 so the bridge is clear to the Exit Door.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class MagmaCore : MonoBehaviour
    {
        public enum CoreState
        {
            Safe,
            Warning,
            Exploding,
            PluggedInert
        }

        [Header("Cycle Timings (Seconds)")]
        public float safeDuration = 3.5f;
        public float warningDuration = 1.4f;
        public float explosionDuration = 0.8f;

        [Header("Spawn & Jitter Settings")]
        public Vector3 baseSpawnLocalPosition = new Vector3(0f, 0.75f, -2.5f);
        public float spawnJitterX = 1.2f;

        [Header("Visuals & Hitboxes")]
        public Renderer coreMeshRenderer;
        public Material safeMaterial;
        public Material warningMaterial;
        public Material explodingMaterial;
        [Tooltip("Spherical Trigger Child object tagged 'FireHitbox' (radius ~2.8m) enabled during Exploding state.")]
        public GameObject aoeFireBurstTrigger;
        public ParticleSystem warningSparksVFX;
        public ParticleSystem explosionBurstVFX;

        [Header("References")]
        public CubeAgent arenaAgent;
        public BossDragonController bossAndGeyserController;

        public CoreState CurrentState { get; private set; } = CoreState.Safe;
        public bool IsHeatingDoubled { get; set; } = false;

        /// <summary>
        /// Returns 0.0 to 1.0 indicating how close the Core is to exploding (fed into CubeAgent Vector Observations).
        /// </summary>
        public float NormalizedCycleProgress
        {
            get
            {
                if (CurrentState == CoreState.PluggedInert) return 0f;
                if (CurrentState == CoreState.Exploding) return 1f;
                float totalLeadTime = safeDuration + warningDuration;
                float elapsed = (CurrentState == CoreState.Safe)
                    ? _stateTimer
                    : (safeDuration + _stateTimer);
                return Mathf.Clamp01(elapsed / Mathf.Max(0.1f, totalLeadTime));
            }
        }

        private Rigidbody _rb;
        private float _stateTimer;
        private bool _isBossRoom;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            ConfigureSolidRigidbody();
        }

        private void ConfigureSolidRigidbody()
        {
            if (_rb == null) return;
            _rb.mass = 3.5f;
            _rb.linearDamping = 4.0f;
            _rb.angularDamping = 10f;
            // Freeze all rotation axes so the block never tips or spins, plus Y position so it slides cleanly
            _rb.constraints = RigidbodyConstraints.FreezeRotationX
                            | RigidbodyConstraints.FreezeRotationY
                            | RigidbodyConstraints.FreezeRotationZ
                            | RigidbodyConstraints.FreezePositionY;
        }

        public void ResetCore(bool isBossRoom)
        {
            _isBossRoom = isBossRoom;
            gameObject.SetActive(true);

            if (_rb == null) _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = false;
            ConfigureSolidRigidbody();
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            float jitterX = Random.Range(-spawnJitterX, spawnJitterX);
            transform.localPosition = new Vector3(
                baseSpawnLocalPosition.x + jitterX,
                baseSpawnLocalPosition.y,
                baseSpawnLocalPosition.z
            );
            transform.localRotation = Quaternion.identity;

            IsHeatingDoubled = false;
            EnterState(CoreState.Safe);
        }

        private void Update()
        {
            if (CurrentState == CoreState.PluggedInert) return;

            // In Room 4, absorbing the Dragon's fire breath heats the block 2x faster
            float heatRate = (IsHeatingDoubled && CurrentState != CoreState.Exploding) ? 2.0f : 1.0f;
            _stateTimer += Time.deltaTime * heatRate;

            switch (CurrentState)
            {
                case CoreState.Safe:
                    if (_stateTimer >= safeDuration)
                    {
                        EnterState(CoreState.Warning);
                    }
                    break;

                case CoreState.Warning:
                    if (_stateTimer >= warningDuration)
                    {
                        EnterState(CoreState.Exploding);
                    }
                    break;

                case CoreState.Exploding:
                    if (_stateTimer >= explosionDuration)
                    {
                        EnterState(CoreState.Safe);
                    }
                    break;
            }

            // Reset episode if the agent wedges the block irreversibly into a back corner
            if (Mathf.Abs(transform.localPosition.x) > 5.3f && transform.localPosition.z < -7.5f)
            {
                if (arenaAgent != null)
                {
                    arenaAgent.AddReward(-0.5f);
                    arenaAgent.EndEpisode();
                }
            }
        }

        private void EnterState(CoreState newState)
        {
            CurrentState = newState;
            _stateTimer = 0f;

            switch (newState)
            {
                case CoreState.Safe:
                    gameObject.tag = "MagmaCore_Safe";
                    if (coreMeshRenderer != null && safeMaterial != null)
                        coreMeshRenderer.sharedMaterial = safeMaterial;
                    if (aoeFireBurstTrigger != null)
                        aoeFireBurstTrigger.SetActive(false);
                    if (warningSparksVFX != null && warningSparksVFX.isPlaying)
                        warningSparksVFX.Stop();
                    break;

                case CoreState.Warning:
                    gameObject.tag = "MagmaCore_Warning";
                    if (coreMeshRenderer != null && warningMaterial != null)
                        coreMeshRenderer.sharedMaterial = warningMaterial;
                    if (aoeFireBurstTrigger != null)
                        aoeFireBurstTrigger.SetActive(false);
                    if (warningSparksVFX != null && !warningSparksVFX.isPlaying)
                        warningSparksVFX.Play();
                    break;

                case CoreState.Exploding:
                    gameObject.tag = "MagmaCore_Warning";
                    if (coreMeshRenderer != null && explodingMaterial != null)
                        coreMeshRenderer.sharedMaterial = explodingMaterial;
                    if (aoeFireBurstTrigger != null)
                        aoeFireBurstTrigger.SetActive(true);
                    if (explosionBurstVFX != null)
                        explosionBurstVFX.Play();

                    // Check if this AoE burst hits the Dragon's jaw in Room 4
                    if (_isBossRoom && bossAndGeyserController != null)
                    {
                        bossAndGeyserController.CheckDragonStunFromCoreExplosion(transform.localPosition);
                    }
                    break;

                case CoreState.PluggedInert:
                    gameObject.tag = "Cover";
                    if (coreMeshRenderer != null && safeMaterial != null)
                        coreMeshRenderer.sharedMaterial = safeMaterial;
                    if (aoeFireBurstTrigger != null)
                        aoeFireBurstTrigger.SetActive(false);
                    if (warningSparksVFX != null)
                        warningSparksVFX.Stop();
                    break;
            }
        }

        /// <summary>
        /// Called by BossDragonController when the Magma Core reaches the Geyser vent in Room 3.
        /// Snaps the block into the geyser hole, disables its explosion cycle, and lets the agent walk to the exit.
        /// </summary>
        public void LockIntoGeyserVent(Vector3 geyserLocalPos)
        {
            EnterState(CoreState.PluggedInert);
            _rb.linearVelocity = Vector3.zero;
            _rb.isKinematic = true;
            // Sink slightly into the floor vent so it visually plugs the lava geyser
            transform.localPosition = new Vector3(geyserLocalPos.x, 0.35f, geyserLocalPos.z);
        }

        /// <summary>
        /// Called by BossDragonController when the Magma Core explodes under the Dragon's jaw in Room 4.
        /// Shatters/despawns the core so the bridge path to the Exit Door is completely clear.
        /// </summary>
        public void ShatterAndDespawnOnBossStun()
        {
            if (aoeFireBurstTrigger != null)
                aoeFireBurstTrigger.SetActive(false);
            gameObject.SetActive(false);
        }
    }
}