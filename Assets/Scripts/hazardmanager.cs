using System.Collections.Generic;
using UnityEngine;

namespace FireAndBrimstone
{
    /// <summary>
    /// Manages hazards for Room 1 (The Magma Grid) and Room 2 (Crossfire Courtyard).
    /// - Room 1: 5x5 grid of grates over lava. A sweeping heat wave erupts row-by-row with a shifting
    ///   1-tile safe pocket so the agent cannot blindly sprint straight through.
    /// - Room 2: Side-wall Gargoyles track the agent with a visible laser LineRenderer, lock aim with an
    ///   orange/red flash, and fire a flame beam blocked by obsidian Cover pillars.
    /// </summary>
    public class HazardManager : MonoBehaviour
    {
        [System.Serializable]
        public class GridTileSlot
        {
            public int row; // 0 (near start) to 4 (near exit)
            public int col; // 0 (left) to 4 (right)
            public GameObject tileObject;
            public Renderer tileRenderer;
            public GameObject firePillarHitbox; // Child object tagged "FireHitbox"
        }

        [System.Serializable]
        public class GargoyleTurretSlot
        {
            public Transform muzzleTransform;
            public LineRenderer aimLaserLine;
            public ParticleSystem fireBeamVFX;
            public float fireInterval = 2.4f;
            public float aimWarningDuration = 0.65f;
            public float beamActiveDuration = 0.45f;
            [HideInInspector] public float timer;
            [HideInInspector] public Vector3 lockedTargetWorldPos;
        }

        [Header("Shared Materials (Room 1 Grid)")]
        public Material tileSafeMaterial;
        public Material tileWarningMaterial;
        public Material tileEruptingMaterial;

        [Header("Room 1: 5x5 Magma Grid Configuration")]
        public List<GridTileSlot> room1GridTiles = new List<GridTileSlot>();
        public float warningPhaseDuration = 1.0f;
        public float eruptPhaseDuration = 1.8f;

        [Header("Room 2: Crossfire Gargoyle Configuration")]
        public List<GargoyleTurretSlot> room2Gargoyles = new List<GargoyleTurretSlot>();
        public LayerMask gargoyleObstructionMask = ~0; // Hits Cover, Wall, and Agent
        public Color laserTrackingColor = new Color(1f, 0.85f, 0.1f, 0.55f);
        public Color laserLockedWarningColor = new Color(1f, 0.15f, 0.0f, 0.95f);
        public Color laserFiringColor = new Color(1f, 0.4f, 0.0f, 1.0f);

        [Header("References")]
        public CurriculumManager curriculumManager;
        public CubeAgent arenaAgent;

        // Room 1 internal state
        private int _activeWaveRow = 0;
        private int _safeColumnInWaveRow = 2;
        private float _waveTimer = 0f;
        private bool _isWaveErupting = false;
        private Transform _trackedAgentTransform;

        public void ResetRoom1MagmaGrid()
        {
            // Randomize starting wave row & safe pocket column (prevents blind keystroke memorization)
            _activeWaveRow = Random.Range(0, 5);
            _safeColumnInWaveRow = Random.Range(0, 5);
            _waveTimer = 0f;
            _isWaveErupting = false;

            ApplyRoom1GridStates();
        }

        public void ResetRoom2Crossfire(Transform agentTransform)
        {
            _trackedAgentTransform = agentTransform;

            for (int i = 0; i < room2Gargoyles.Count; i++)
            {
                var g = room2Gargoyles[i];
                // Stagger gargoyle timers slightly + small random jitter
                g.timer = (i * 0.45f) + Random.Range(0f, 0.35f);
                if (g.aimLaserLine != null)
                {
                    g.aimLaserLine.enabled = true;
                    g.aimLaserLine.positionCount = 2;
                }
                if (g.fireBeamVFX != null && g.fireBeamVFX.isPlaying)
                {
                    g.fireBeamVFX.Stop();
                }
            }
        }

        private void Update()
        {
            if (curriculumManager == null) return;

            if (curriculumManager.CurrentRoomIndex == 0)
            {
                UpdateRoom1MagmaGrid();
            }
            else if (curriculumManager.CurrentRoomIndex == 1)
            {
                UpdateRoom2Crossfire();
            }
        }

        private void UpdateRoom1MagmaGrid()
        {
            _waveTimer += Time.deltaTime;

            if (!_isWaveErupting)
            {
                // Warning Phase (1.0s) -> switch to Erupting Phase
                if (_waveTimer >= warningPhaseDuration)
                {
                    _waveTimer = 0f;
                    _isWaveErupting = true;
                    ApplyRoom1GridStates();
                }
            }
            else
            {
                // Erupting Phase (1.8s) -> advance wave toward the agent and shift safe pocket by at most 1 column
                if (_waveTimer >= eruptPhaseDuration)
                {
                    _waveTimer = 0f;
                    _isWaveErupting = false;

                    // Wave sweeps from exit row (4) down to start row (0) so it meets the advancing agent
                    _activeWaveRow = (_activeWaveRow + 4) % 5;
                    int colShift = Random.Range(-1, 2); // -1, 0, or +1 so the safe pocket is always reachable
                    _safeColumnInWaveRow = Mathf.Clamp(_safeColumnInWaveRow + colShift, 0, 4);

                    ApplyRoom1GridStates();
                }
            }
        }

        private void ApplyRoom1GridStates()
        {
            // Secondary hazard row (2 rows offset) forces continuous awareness across the 5x5 grid
            int secondaryRow = (_activeWaveRow + 2) % 5;
            int secondarySafeCol = (4 - _safeColumnInWaveRow);

            foreach (var slot in room1GridTiles)
            {
                if (slot.tileObject == null) continue;

                bool isInPrimaryRow = (slot.row == _activeWaveRow && slot.col != _safeColumnInWaveRow);
                bool isInSecondaryRow = (slot.row == secondaryRow && slot.col != secondarySafeCol);
                bool isHazardTile = isInPrimaryRow || isInSecondaryRow;

                if (!isHazardTile)
                {
                    slot.tileObject.tag = "GridTile_Safe";
                    if (slot.tileRenderer != null && tileSafeMaterial != null)
                        slot.tileRenderer.sharedMaterial = tileSafeMaterial;
                    if (slot.firePillarHitbox != null)
                        slot.firePillarHitbox.SetActive(false);
                }
                else if (!_isWaveErupting)
                {
                    // Pulse bright orange warning
                    slot.tileObject.tag = "GridTile_Warning";
                    if (slot.tileRenderer != null && tileWarningMaterial != null)
                        slot.tileRenderer.sharedMaterial = tileWarningMaterial;
                    if (slot.firePillarHitbox != null)
                        slot.firePillarHitbox.SetActive(false);
                }
                else
                {
                    // Erupt with a pillar of fire!
                    slot.tileObject.tag = "GridTile_Warning";
                    if (slot.tileRenderer != null && tileEruptingMaterial != null)
                        slot.tileRenderer.sharedMaterial = tileEruptingMaterial;
                    if (slot.firePillarHitbox != null)
                        slot.firePillarHitbox.SetActive(true);
                }
            }
        }

        private void UpdateRoom2Crossfire()
        {
            if (_trackedAgentTransform == null && arenaAgent != null)
                _trackedAgentTransform = arenaAgent.transform;
            if (_trackedAgentTransform == null) return;

            float totalCycle = 0f;

            foreach (var g in room2Gargoyles)
            {
                if (g.muzzleTransform == null) continue;

                totalCycle = g.fireInterval + g.aimWarningDuration + g.beamActiveDuration;
                g.timer += Time.deltaTime;
                if (g.timer >= totalCycle)
                {
                    g.timer -= totalCycle;
                    if (g.fireBeamVFX != null && g.fireBeamVFX.isPlaying)
                        g.fireBeamVFX.Stop();
                }

                Vector3 origin = g.muzzleTransform.position;
                Vector3 currentAgentCenter = _trackedAgentTransform.position + Vector3.up * 0.4f;

                if (g.timer < g.fireInterval)
                {
                    // Phase 1: Tracking Agent with yellow laser
                    g.lockedTargetWorldPos = currentAgentCenter;
                    UpdateGargoyleLaser(g, origin, g.lockedTargetWorldPos, laserTrackingColor, 0.04f, isFiringBeam: false);
                }
                else if (g.timer < g.fireInterval + g.aimWarningDuration)
                {
                    // Phase 2: Aim locked! Flash bright red warning line so the agent can duck behind Cover
                    UpdateGargoyleLaser(g, origin, g.lockedTargetWorldPos, laserLockedWarningColor, 0.09f, isFiringBeam: false);
                }
                else
                {
                    // Phase 3: Fire flame beam along the locked line of sight!
                    if (g.fireBeamVFX != null && !g.fireBeamVFX.isPlaying)
                    {
                        g.fireBeamVFX.transform.rotation = Quaternion.LookRotation((g.lockedTargetWorldPos - origin).normalized);
                        g.fireBeamVFX.Play();
                    }
                    UpdateGargoyleLaser(g, origin, g.lockedTargetWorldPos, laserFiringColor, 0.22f, isFiringBeam: true);
                }
            }
        }

        private void UpdateGargoyleLaser(
            GargoyleTurretSlot g,
            Vector3 origin,
            Vector3 targetPos,
            Color beamColor,
            float width,
            bool isFiringBeam)
        {
            Vector3 dir = (targetPos - origin).normalized;
            float maxDist = 22f;
            Vector3 endPoint = origin + dir * maxDist;

            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, gargoyleObstructionMask, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;

                // If firing and the beam hits the Agent (not blocked by an obsidian Cover pillar), kill the agent
                if (isFiringBeam && arenaAgent != null &&
                    (hit.collider.gameObject == arenaAgent.gameObject || hit.collider.transform.IsChildOf(arenaAgent.transform)))
                {
                    arenaAgent.KillAgentWithFire();
                }
            }

            if (g.aimLaserLine != null)
            {
                g.aimLaserLine.startWidth = width;
                g.aimLaserLine.endWidth = width;
                g.aimLaserLine.startColor = beamColor;
                g.aimLaserLine.endColor = beamColor;
                g.aimLaserLine.SetPosition(0, origin);
                g.aimLaserLine.SetPosition(1, endPoint);
            }
        }
    }
}