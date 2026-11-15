using UnityEngine;

namespace FireAndBrimstone
{
    /// <summary>
    /// Controls the environmental objectives in Room 3 (The Geyser Plug) and Room 4 (The Boss Fight):
    /// - Room 3: Monitors distance between the MagmaCore and the Lava Geyser. When plugged, snaps the core
    ///   into the vent, stops the geyser fire blocking the exit, grants +0.5 bonus, and unlocks the Exit Door.
    /// - Room 4: Controls the Boss Dragon's continuous fire stream down the bridge:
    ///   1) Pushes the MagmaCore backward with physical force and heats it up 2x faster.
    ///   2) Kills the Agent if the Agent steps into the fire stream UNLESS the Agent is shielded in the
    ///      shadow behind the MagmaCore.
    ///   3) Stuns the Dragon (+2.0 reward), extinguishes the fire stream, shatters/despawns the MagmaCore,
    ///      and unlocks the Exit Door when the MagmaCore explodes within the Dragon's jaw range.
    /// </summary>
    public class BossDragonController : MonoBehaviour
    {
        [Header("Shared References")]
        public CurriculumManager curriculumManager;
        public CubeAgent arenaAgent;
        public GameObject exitDoorBarrierVisual; // Iron gate bars that lower/hide when objective is completed

        [Header("Room 3: Geyser Plug Settings")]
        public Transform geyserTargetTransform;
        public GameObject geyserFireColumnHitbox; // Tagged "FireHitbox" until plugged
        public ParticleSystem geyserLavaVFX;
        public float geyserPlugSnapDistance = 1.15f;

        [Header("Room 4: Dragon Boss Settings")]
        public Transform dragonJawTargetTransform;
        public Animator dragonAnimator;
        public ParticleSystem dragonFireBreathVFX;
        public float fireStreamWidth = 4.2f;
        public float fireStreamPushbackForce = 18.0f;
        public float shadowShieldWidthTolerance = 0.85f;
        public float dragonStunExplosionRadius = 3.2f;

        public bool IsGeyserPlugged { get; private set; } = false;
        public bool IsDragonStunned { get; private set; } = false;

        public void ResetRoom3Geyser()
        {
            IsGeyserPlugged = false;
            if (geyserFireColumnHitbox != null) geyserFireColumnHitbox.SetActive(true);
            if (geyserLavaVFX != null && !geyserLavaVFX.isPlaying) geyserLavaVFX.Play();
            if (exitDoorBarrierVisual != null) exitDoorBarrierVisual.SetActive(true);
        }

        public void ResetRoom4Boss()
        {
            IsDragonStunned = false;
            if (dragonFireBreathVFX != null && !dragonFireBreathVFX.isPlaying) dragonFireBreathVFX.Play();
            if (dragonAnimator != null) dragonAnimator.SetBool("IsStunned", false);
            if (exitDoorBarrierVisual != null) exitDoorBarrierVisual.SetActive(true);
        }

        private void FixedUpdate()
        {
            if (curriculumManager == null) return;

            if (curriculumManager.CurrentRoomIndex == 2)
            {
                UpdateRoom3GeyserCheck();
            }
            else if (curriculumManager.CurrentRoomIndex == 3)
            {
                UpdateRoom4DragonFireStream();
            }
        }

        private void UpdateRoom3GeyserCheck()
        {
            if (IsGeyserPlugged) return;

            MagmaCore core = curriculumManager.room3MagmaCore;
            if (core == null || geyserTargetTransform == null) return;

            Vector2 coreXZ = new Vector2(core.transform.localPosition.x, core.transform.localPosition.z);
            Vector2 geyserXZ = new Vector2(geyserTargetTransform.localPosition.x, geyserTargetTransform.localPosition.z);

            if (Vector2.Distance(coreXZ, geyserXZ) <= geyserPlugSnapDistance)
            {
                IsGeyserPlugged = true;

                // 1. Snap block into geyser hole and disable its explosion cycle
                core.LockIntoGeyserVent(geyserTargetTransform.localPosition);

                // 2. Extinguish geyser fire column and open the exit gate
                if (geyserFireColumnHitbox != null) geyserFireColumnHitbox.SetActive(false);
                if (geyserLavaVFX != null) geyserLavaVFX.Stop();
                if (exitDoorBarrierVisual != null) exitDoorBarrierVisual.SetActive(false);

                // 3. Unlock Exit Door and award intermediate objective completion bonus
                curriculumManager.NotifyObjectiveCompleted("GEYSER PLUGGED! EXIT OPEN!");
                if (arenaAgent != null)
                {
                    arenaAgent.GrantObjectiveBonus(0.5f);
                }
            }
        }

        private void UpdateRoom4DragonFireStream()
        {
            if (IsDragonStunned) return;

            MagmaCore core = curriculumManager.room4MagmaCore;
            if (core == null || !core.gameObject.activeInHierarchy || arenaAgent == null || dragonJawTargetTransform == null)
                return;

            float streamCenterX = dragonJawTargetTransform.localPosition.x;
            float dragonZ = dragonJawTargetTransform.localPosition.z;
            float halfStreamW = fireStreamWidth * 0.5f;

            Vector3 corePos = core.transform.localPosition;
            Vector3 agentPos = arenaAgent.transform.localPosition;

            // 1. Check if the Magma Core is inside the Dragon's fire stream
            bool coreInFireStream = Mathf.Abs(corePos.x - streamCenterX) <= halfStreamW && corePos.z <= dragonZ;
            core.IsHeatingDoubled = coreInFireStream;

            if (coreInFireStream)
            {
                Rigidbody coreRb = core.GetComponent<Rigidbody>();
                if (coreRb != null && !coreRb.isKinematic)
                {
                    // Push the block backward (-Z) down the bridge
                    Vector3 pushDir = -curriculumManager.transform.forward;
                    coreRb.AddForce(pushDir * fireStreamPushbackForce, ForceMode.Force);
                }
            }

            // 2. Check if the Agent is in the fire stream corridor
            bool agentInStreamCorridor = Mathf.Abs(agentPos.x - streamCenterX) <= halfStreamW && agentPos.z <= dragonZ;
            if (agentInStreamCorridor)
            {
                // The Magma Core shields the Agent if the Agent is behind the Core (agentPos.z < corePos.z)
                // and aligned with the Core's X width
                bool isShieldedByCore = (agentPos.z < corePos.z) &&
                                        (Mathf.Abs(agentPos.x - corePos.x) <= shadowShieldWidthTolerance);

                if (!isShieldedByCore)
                {
                    // Roasted by the Dragon's fire breath!
                    arenaAgent.KillAgentWithFire();
                }
            }
        }

        /// <summary>
        /// Called by MagmaCore when it enters the Exploding state in Room 4.
        /// If the Core is right under the Dragon's jaw, it stuns the Dragon (+2.0 reward),
        /// shuts off the fire breath, and shatters/despawns the Core so the agent can run to the Exit Door.
        /// </summary>
        public void CheckDragonStunFromCoreExplosion(Vector3 coreLocalPos)
        {
            if (IsDragonStunned || dragonJawTargetTransform == null) return;

            float distToJaw = Vector3.Distance(coreLocalPos, dragonJawTargetTransform.localPosition);
            if (distToJaw <= dragonStunExplosionRadius)
            {
                IsDragonStunned = true;

                // 1. Stop Dragon fire stream & trigger stun animation
                if (dragonFireBreathVFX != null) dragonFireBreathVFX.Stop();
                if (dragonAnimator != null) dragonAnimator.SetBool("IsStunned", true);
                if (exitDoorBarrierVisual != null) exitDoorBarrierVisual.SetActive(false);

                // 2. Shatter/despawn the Magma Core so the bridge is clear to the Exit Door
                MagmaCore core = curriculumManager != null ? curriculumManager.room4MagmaCore : null;
                if (core != null)
                {
                    core.ShatterAndDespawnOnBossStun();
                }

                // 3. Grant the +2.0 Boss Stun reward and unlock the Exit Door (+1.0 waiting at the door)
                if (curriculumManager != null)
                {
                    curriculumManager.NotifyObjectiveCompleted("DRAGON STUNNED! RUN TO EXIT!");
                }
                if (arenaAgent != null)
                {
                    arenaAgent.GrantObjectiveBonus(2.0f);
                }
            }
        }


        public void SetExitDoorBlocked(bool blocked)
        {
            if (exitDoorBarrierVisual != null)
            {
                exitDoorBarrierVisual.SetActive(blocked);
            }
        }
    }
}