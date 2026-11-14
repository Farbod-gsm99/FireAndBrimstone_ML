using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

namespace FireAndBrimstone
{
    /// <summary>
    /// Core ML-Agents script for the "Fire and Brimstone" agent.
    /// Uses Discrete Actions (3 branches: Forward/Back, Strafe Left/Right, Rotate Left/Right),
    /// 3D RayPerceptionSensors (Stacked = 4), 12 normalized Vector Observations,
    /// and Potential-Based Progress Rewards to prevent reward-farming oscillations.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CubeAgent : Agent
    {
        [Header("Arena References")]
        public CurriculumManager curriculumManager;
        public Transform exitDoorTransform;
        public Transform spawnPointTransform;

        [Header("Movement Settings")]
        public float moveSpeed = 6.5f;
        public float strafeSpeed = 5.0f;
        public float rotationSpeed = 160f;
        public float spawnJitterX = 2.5f;

        [Header("Reward Shaping Settings")]
        [Tooltip("Maximum potential progress reward for advancing from start to Exit Door.")]
        public float maxAgentProgressReward = 0.5f;
        [Tooltip("Maximum potential progress reward for pushing Magma Core to Geyser/Dragon.")]
        public float maxCoreProgressReward = 0.8f;
        [Tooltip("Per-step time penalty applied only in Rooms 2, 3, and 4 (Room 1 has 0 penalty to encourage patience).")]
        public float stepPenaltyRooms2To4 = -0.0003f;

        private Rigidbody _rb;
        private int _currentRoomIndex;

        // Potential-based tracking (only rewards achieving a NEW closest distance)
        private float _bestAgentDistToExit;
        private float _initialAgentDistToExit;
        private float _bestCoreDistToTarget;
        private float _initialCoreDistToTarget;
        private bool _objectiveBonusAwarded;

        public override void Initialize()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnEpisodeBegin()
        {
            // 1. Reset Agent velocity and spawn position with slight X-axis jitter
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            Vector3 baseSpawn = spawnPointTransform != null
                ? spawnPointTransform.localPosition
                : new Vector3(0f, 0.6f, -7.2f);

            float randomX = Random.Range(-spawnJitterX, spawnJitterX);
            transform.localPosition = new Vector3(baseSpawn.x + randomX, baseSpawn.y, baseSpawn.z);
            transform.localRotation = Quaternion.identity;

            // 2. Reset active room via CurriculumManager
            _currentRoomIndex = curriculumManager != null
                ? curriculumManager.ResetArenaForNewEpisode(this)
                : 0;

            _objectiveBonusAwarded = false;

            // 3. Initialize potential-based distance baselines
            if (exitDoorTransform != null)
            {
                _initialAgentDistToExit = Vector3.Distance(transform.localPosition, exitDoorTransform.localPosition);
                _bestAgentDistToExit = _initialAgentDistToExit;
            }

            MagmaCore activeCore = curriculumManager != null ? curriculumManager.GetActiveMagmaCore() : null;
            Transform activeTarget = curriculumManager != null ? curriculumManager.GetActiveObjectiveTarget() : null;

            if (activeCore != null && activeTarget != null)
            {
                _initialCoreDistToTarget = Vector3.Distance(activeCore.transform.localPosition, activeTarget.localPosition);
                _bestCoreDistToTarget = _initialCoreDistToTarget;
            }
        }

        /// <summary>
        /// Collects 12 normalized floats so the single brain knows:
        /// - Which room it is in (4 floats: one-hot encoding for Rooms 1-4)
        /// - Its own normalized local velocity (2 floats: X, Z)
        /// - Relative direction to the Exit Door (2 floats: X, Z) and whether Exit is unlocked (1 float)
        /// - Relative direction to Magma Core (2 floats: X, Z) and Core normalized heat timer (1 float)
        /// Total Vector Observation Size = 12.
        /// </summary>
        public override void CollectObservations(VectorSensor sensor)
        {
            // 1-4: One-hot room index (4 floats)
            for (int i = 0; i < 4; i++)
            {
                sensor.AddObservation(_currentRoomIndex == i ? 1.0f : 0.0f);
            }

            // 5-6: Normalized local velocity X and Z (2 floats)
            Vector3 localVel = transform.InverseTransformDirection(_rb.linearVelocity);
            sensor.AddObservation(Mathf.Clamp(localVel.x / moveSpeed, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(localVel.z / moveSpeed, -1f, 1f));

            // 7-8: Direction to Exit Door in agent's local space (2 floats)
            if (exitDoorTransform != null)
            {
                Vector3 dirToExit = (exitDoorTransform.localPosition - transform.localPosition).normalized;
                Vector3 localDirExit = transform.InverseTransformDirection(dirToExit);
                sensor.AddObservation(localDirExit.x);
                sensor.AddObservation(localDirExit.z);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(1f);
            }

            // 9: Exit unlocked flag (1 float)
            bool exitUnlocked = curriculumManager == null || curriculumManager.IsExitUnlocked;
            sensor.AddObservation(exitUnlocked ? 1.0f : 0.0f);

            // 10-12: Magma Core relative direction (2 floats) & normalized heat timer (1 float)
            MagmaCore activeCore = curriculumManager != null ? curriculumManager.GetActiveMagmaCore() : null;
            if (activeCore != null && activeCore.gameObject.activeInHierarchy)
            {
                Vector3 dirToCore = (activeCore.transform.localPosition - transform.localPosition).normalized;
                Vector3 localDirCore = transform.InverseTransformDirection(dirToCore);
                sensor.AddObservation(localDirCore.x);
                sensor.AddObservation(localDirCore.z);
                sensor.AddObservation(activeCore.NormalizedCycleProgress);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }

        /// <summary>
        /// Discrete Action Space (3 Branches):
        /// Branch 0 (Forward/Back): 0 = None, 1 = Forward (+Z), 2 = Backward (-Z)
        /// Branch 1 (Strafe):       0 = None, 1 = Left (-X),    2 = Right (+X)
        /// Branch 2 (Rotate):       0 = None, 1 = Turn Left,    2 = Turn Right
        /// </summary>
        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            int moveAction = actionBuffers.DiscreteActions[0];
            int strafeAction = actionBuffers.DiscreteActions[1];
            int rotateAction = actionBuffers.DiscreteActions[2];

            float forwardInput = 0f;
            if (moveAction == 1) forwardInput = 1f;
            else if (moveAction == 2) forwardInput = -0.75f;

            float strafeInput = 0f;
            if (strafeAction == 1) strafeInput = -1f;
            else if (strafeAction == 2) strafeInput = 1f;

            float rotateInput = 0f;
            if (rotateAction == 1) rotateInput = -1f;
            else if (rotateAction == 2) rotateInput = 1f;

            // Apply rotation
            transform.Rotate(Vector3.up, rotateInput * rotationSpeed * Time.fixedDeltaTime);

            // Apply snappy velocity on X/Z while preserving gravity on Y
            Vector3 desiredMove = (transform.forward * (forwardInput * moveSpeed)) +
                                  (transform.right * (strafeInput * strafeSpeed));
            _rb.linearVelocity = new Vector3(desiredMove.x, _rb.linearVelocity.y, desiredMove.z);

            // Evaluate rewards & failure conditions
            EvaluateStepRewards();
        }

        private void EvaluateStepRewards()
        {
            // Fall into lava pit check
            if (transform.localPosition.y < -0.5f)
            {
                AddReward(-1.0f);
                EndEpisode();
                return;
            }

            // Episode timeout check
            if (curriculumManager != null && curriculumManager.RemainingTime <= 0f)
            {
                AddReward(-0.5f);
                EndEpisode();
                return;
            }

            // Room 1 has NO step penalty so the agent learns patience on safe tiles
            if (_currentRoomIndex > 0)
            {
                AddReward(stepPenaltyRooms2To4);
            }

            // Potential-based progress toward Exit Door (when Exit is unlocked or in Rooms 1-2)
            bool exitUnlocked = curriculumManager == null || curriculumManager.IsExitUnlocked;
            if (exitDoorTransform != null && exitUnlocked && _initialAgentDistToExit > 0.1f)
            {
                float currentDist = Vector3.Distance(transform.localPosition, exitDoorTransform.localPosition);
                if (currentDist < _bestAgentDistToExit)
                {
                    float delta = _bestAgentDistToExit - currentDist;
                    float progressReward = (delta / _initialAgentDistToExit) * maxAgentProgressReward;
                    AddReward(progressReward);
                    _bestAgentDistToExit = currentDist;
                }
            }

            // Potential-based progress for pushing the Magma Core toward Geyser (Room 3) or Dragon Jaw (Room 4)
            if ((_currentRoomIndex == 2 || _currentRoomIndex == 3) && !exitUnlocked)
            {
                MagmaCore activeCore = curriculumManager.GetActiveMagmaCore();
                Transform activeTarget = curriculumManager.GetActiveObjectiveTarget();

                if (activeCore != null && activeTarget != null && _initialCoreDistToTarget > 0.1f)
                {
                    float currentCoreDist = Vector3.Distance(activeCore.transform.localPosition, activeTarget.localPosition);
                    if (currentCoreDist < _bestCoreDistToTarget)
                    {
                        float delta = _bestCoreDistToTarget - currentCoreDist;
                        float coreReward = (delta / _initialCoreDistToTarget) * maxCoreProgressReward;
                        AddReward(coreReward);
                        _bestCoreDistToTarget = currentCoreDist;
                    }
                }
            }
        }

        /// <summary>
        /// Called by hazard triggers, projectiles, AoE bursts, or the Dragon breath.
        /// </summary>
        public void KillAgentWithFire()
        {
            AddReward(-1.0f);
            EndEpisode();
        }

        /// <summary>
        /// Called when Room 3 Geyser is plugged (+0.5 intermediate bonus) or Room 4 Dragon is stunned (+2.0 bonus).
        /// Does NOT end the episode—the agent must still walk through the Exit Door!
        /// </summary>
        public void GrantObjectiveBonus(float bonusAmount)
        {
            if (_objectiveBonusAwarded) return;
            _objectiveBonusAwarded = true;
            AddReward(bonusAmount);

            // Reset agent distance baseline from current position so walking to the newly unlocked exit is rewarded
            if (exitDoorTransform != null)
            {
                _initialAgentDistToExit = Mathf.Max(1f, Vector3.Distance(transform.localPosition, exitDoorTransform.localPosition));
                _bestAgentDistToExit = _initialAgentDistToExit;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("FireHitbox"))
            {
                KillAgentWithFire();
            }
            else if (other.CompareTag("ExitDoor"))
            {
                bool exitUnlocked = curriculumManager == null || curriculumManager.IsExitUnlocked;
                if (exitUnlocked)
                {
                    AddReward(1.0f);
                    EndEpisode();
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.collider.CompareTag("FireHitbox"))
            {
                KillAgentWithFire();
            }
            else if (collision.collider.CompareTag("ExitDoor"))
            {
                bool exitUnlocked = curriculumManager == null || curriculumManager.IsExitUnlocked;
                if (exitUnlocked)
                {
                    AddReward(1.0f);
                    EndEpisode();
                }
            }
        }

        /// <summary>
        /// Allows you to test all 4 rooms manually using WASD + Q/E keys when Behavior Type is set to "Heuristic Only".
        /// </summary>
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var discreteActions = actionsOut.DiscreteActions;

            // Branch 0: W/S or Up/Down
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) discreteActions[0] = 1;
            else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) discreteActions[0] = 2;
            else discreteActions[0] = 0;

            // Branch 1: A/D strafe
            if (Input.GetKey(KeyCode.A)) discreteActions[1] = 1;
            else if (Input.GetKey(KeyCode.D)) discreteActions[1] = 2;
            else discreteActions[1] = 0;

            // Branch 2: Q/E or Left/Right Arrow rotation
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow)) discreteActions[2] = 1;
            else if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.RightArrow)) discreteActions[2] = 2;
            else discreteActions[2] = 0;
        }
    }
}