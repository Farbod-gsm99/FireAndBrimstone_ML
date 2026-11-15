using UnityEngine;
using Unity.MLAgents;

namespace FireAndBrimstone
{
    public enum ArenaMode
    {
        TrainingCurriculum = -1,
        ForceRoom1_MagmaGrid = 0,
        ForceRoom2_CrossfireCourtyard = 1,
        ForceRoom3_GeyserPlug = 2,
        ForceRoom4_BossFight = 3
    }

    /// <summary>
    /// Manages room activation inside a single RoomEscape_Arena prefab.
    /// Uses pre-instantiated child rooms with SetActive() instead of Destroy/Instantiate
    /// to avoid garbage collection spikes and maximize training FPS across 16 arenas.
    /// </summary>
    public class CurriculumManager : MonoBehaviour
    {
        [Header("Mode Selection (Use ForceRoom for YouTube Recording)")]
        [Tooltip("TrainingCurriculum reads lesson 0-3 from config.yaml. ForceRoom locks a specific stage.")]
        public ArenaMode arenaMode = ArenaMode.TrainingCurriculum;

        [Header("Room Root GameObjects (Inside EnvironmentAnchor)")]
        public GameObject room1MagmaGrid;
        public GameObject room2CrossfireCourtyard;
        public GameObject room3GeyserPlug;
        public GameObject room4BossFight;

        [Header("Room Controllers & Shared References")]
        public HazardManager room1And2HazardManager;
        public MagmaCore room3MagmaCore;
        public MagmaCore room4MagmaCore;
        public BossDragonController room3And4Controller;
        public ArenaHUD arenaHUD;

        [Header("Episode Settings")]
        [Tooltip("Maximum episode time in seconds displayed on the back-wall HUD.")]
        public float maxEpisodeTimeSeconds = 35f;

        public int CurrentRoomIndex { get; private set; } = 0;
        public int AttemptCount { get; private set; } = 0;
        public float RemainingTime { get; private set; } = 35f;
        public bool IsExitUnlocked { get; private set; } = true;

        private GameObject[] _rooms;
        private readonly string[] _stageNames = new string[]
        {
            "STAGE 1: THE MAGMA GRID",
            "STAGE 2: CROSSFIRE COURTYARD",
            "STAGE 3: THE GEYSER PLUG",
            "STAGE 4: DRAGON BOSS FIGHT"
        };

        private void Awake()
        {
            _rooms = new GameObject[]
            {
                room1MagmaGrid,
                room2CrossfireCourtyard,
                room3GeyserPlug,
                room4BossFight
            };
        }

        private void Update()
        {
            if (RemainingTime > 0f)
            {
                RemainingTime -= Time.deltaTime;
                if (RemainingTime < 0f) RemainingTime = 0f;
            }

            if (arenaHUD != null)
            {
                arenaHUD.UpdateTimerDisplay(RemainingTime);
            }
        }

        /// <summary>
        /// Called at the start of every episode by CubeAgent.OnEpisodeBegin().
        /// Reads the curriculum parameter from ML-Agents Academy (or Inspector override)
        /// and resets all hazards, blocks, and doors for that stage.
        /// </summary>
        public int ResetArenaForNewEpisode(CubeAgent agent)
        {
            AttemptCount++;
            RemainingTime = maxEpisodeTimeSeconds;

            if (arenaMode == ArenaMode.TrainingCurriculum)
            {
                float lessonValue = Academy.Instance.EnvironmentParameters.GetWithDefault("room_lesson", 0f);
                CurrentRoomIndex = Mathf.Clamp(Mathf.RoundToInt(lessonValue), 0, 3);
            }
            else
            {
                CurrentRoomIndex = (int)arenaMode;
            }

            // Toggle only the active room on
            for (int i = 0; i < _rooms.Length; i++)
            {
                if (_rooms[i] != null)
                {
                    _rooms[i].SetActive(i == CurrentRoomIndex);
                }
            }

            // Rooms 1 & 2 start with the exit door unlocked; Rooms 3 & 4 require solving the mechanic first
            IsExitUnlocked = (CurrentRoomIndex == 0 || CurrentRoomIndex == 1);

            // Reset stage-specific controllers
            if (CurrentRoomIndex == 0 && room1And2HazardManager != null)
            {
                room1And2HazardManager.ResetRoom1MagmaGrid();
            }
            else if (CurrentRoomIndex == 1 && room1And2HazardManager != null)
            {
                room1And2HazardManager.ResetRoom2Crossfire(agent.transform);
            }
            else if (CurrentRoomIndex == 2)
            {
                if (room3MagmaCore != null) room3MagmaCore.ResetCore(isBossRoom: false);
                if (room3And4Controller != null) room3And4Controller.ResetRoom3Geyser();
            }
            else if (CurrentRoomIndex == 3)
            {
                if (room4MagmaCore != null) room4MagmaCore.ResetCore(isBossRoom: true);
                if (room3And4Controller != null) room3And4Controller.ResetRoom4Boss();
            }
            // Synchronize the physical exit gate with the logical exit state.
            if (room3And4Controller != null)
            {
                room3And4Controller.SetExitDoorBlocked(!IsExitUnlocked);
            }

            if (arenaHUD != null)
            {
                string statusText = IsExitUnlocked ? "REACH THE EXIT" :
                    (CurrentRoomIndex == 2 ? "PLUG THE LAVA GEYSER" : "STUN THE DRAGON WITH CORE");
                arenaHUD.UpdateStageAndAttempt(_stageNames[CurrentRoomIndex], AttemptCount, statusText);
            }

            return CurrentRoomIndex;
        }

        /// <summary>
        /// Unlocks the exit door when the Geyser is plugged (Room 3) or Dragon is stunned (Room 4).
        /// </summary>
        public void NotifyObjectiveCompleted(string hudBannerMessage)
        {
            IsExitUnlocked = true;
            if (arenaHUD != null)
            {
                arenaHUD.SetStatusBanner(hudBannerMessage);
            }
        }

        public MagmaCore GetActiveMagmaCore()
        {
            if (CurrentRoomIndex == 2) return room3MagmaCore;
            if (CurrentRoomIndex == 3) return room4MagmaCore;
            return null;
        }

        public Transform GetActiveObjectiveTarget()
        {
            if (room3And4Controller == null) return null;
            if (CurrentRoomIndex == 2) return room3And4Controller.geyserTargetTransform;
            if (CurrentRoomIndex == 3) return room3And4Controller.dragonJawTargetTransform;
            return null;
        }
    }
}