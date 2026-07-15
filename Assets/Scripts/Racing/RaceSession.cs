// PRD-001
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Ybsw.Racing
{
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider))]
    public sealed class RaceSession : NetworkBehaviour
    {
        const float SpawnLaneSpacing = 2.2f;
        const float SpawnPositionZ = -20f;

        readonly NetworkVariable<RacePhase> phase = new NetworkVariable<RacePhase>(RacePhase.Waiting);
        readonly NetworkVariable<double> countdownEndsAt = new NetworkVariable<double>();
        readonly NetworkVariable<double> raceStartedAt = new NetworkVariable<double>();
        readonly NetworkVariable<int> finishedPlayerCount = new NetworkVariable<int>();
        readonly List<GuineaPigPlayer> players = new List<GuineaPigPlayer>(RaceRules.MaximumPlayers);

        public static RaceSession Instance { get; private set; }

        public RacePhase Phase => phase.Value;
        public int FinishedPlayerCount => finishedPlayerCount.Value;

        public double ElapsedRaceTimeInSeconds
        {
            get
            {
                if (!IsSpawned || phase.Value != RacePhase.Racing)
                {
                    return 0d;
                }

                return RaceTimeUtility.CalculateElapsed(
                    raceStartedAt.Value,
                    NetworkManager.ServerTime.Time);
            }
        }

        public double RemainingCountdownInSeconds
        {
            get
            {
                if (!IsSpawned || phase.Value != RacePhase.Countdown)
                {
                    return 0d;
                }

                return System.Math.Max(0d, countdownEndsAt.Value - NetworkManager.ServerTime.Time);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (IsServer)
            {
                GuineaPigPlayer[] spawnedPlayers = FindObjectsByType<GuineaPigPlayer>();
                for (int index = 0; index < spawnedPlayers.Length; index++)
                {
                    GuineaPigPlayer player = spawnedPlayers[index];
                    if (player != null && player.IsSpawned)
                    {
                        RegisterPlayer(player);
                    }
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            players.Clear();
        }

        public void RegisterPlayer(GuineaPigPlayer player)
        {
            if (!IsServer || player == null || players.Contains(player))
            {
                return;
            }

            players.Add(player);
            ResetPlayerAtLane(player, players.Count - 1);
        }

        public void UnregisterPlayer(GuineaPigPlayer player)
        {
            if (!IsServer || player == null)
            {
                return;
            }

            players.Remove(player);
        }

        public void RestartRace()
        {
            if (!IsServer)
            {
                return;
            }

            finishedPlayerCount.Value = 0;
            phase.Value = RacePhase.Waiting;
            countdownEndsAt.Value = 0d;
            raceStartedAt.Value = 0d;

            for (int index = 0; index < players.Count; index++)
            {
                GuineaPigPlayer player = players[index];
                if (player != null && player.IsSpawned)
                {
                    ResetPlayerAtLane(player, index);
                }
            }
        }

        public void RequestRestartRace()
        {
            if (!IsSpawned)
            {
                return;
            }

            RequestRestartRaceRpc();
        }

        void Update()
        {
            if (!IsServer)
            {
                return;
            }

            int connectedPlayerCount = NetworkManager.ConnectedClientsIds.Count;

            if (phase.Value == RacePhase.Waiting && RaceRules.CanStart(connectedPlayerCount))
            {
                phase.Value = RacePhase.Countdown;
                countdownEndsAt.Value = NetworkManager.ServerTime.Time + RaceRules.CountdownDurationInSeconds;
                return;
            }

            if (phase.Value == RacePhase.Countdown)
            {
                if (!RaceRules.CanStart(connectedPlayerCount))
                {
                    phase.Value = RacePhase.Waiting;
                    countdownEndsAt.Value = 0d;
                }
                else if (NetworkManager.ServerTime.Time >= countdownEndsAt.Value)
                {
                    phase.Value = RacePhase.Racing;
                    raceStartedAt.Value = NetworkManager.ServerTime.Time;
                }
            }
        }

        [Rpc(SendTo.Server)]
        void RequestRestartRaceRpc()
        {
            RestartRace();
        }

        void OnTriggerEnter(Collider other)
        {
            if (!IsServer || phase.Value != RacePhase.Racing)
            {
                return;
            }

            GuineaPigPlayer player = other.GetComponentInParent<GuineaPigPlayer>();
            if (player == null || player.FinishPlace > 0)
            {
                return;
            }

            int place = RaceRules.GetNextPlace(finishedPlayerCount.Value);
            finishedPlayerCount.Value = place;
            double elapsedTimeInSeconds = RaceTimeUtility.CalculateElapsed(
                raceStartedAt.Value,
                NetworkManager.ServerTime.Time);
            player.FinishOnServer(place, elapsedTimeInSeconds);

            if (finishedPlayerCount.Value >= players.Count && players.Count > 0)
            {
                phase.Value = RacePhase.Finished;
            }
        }

        static Vector3 GetSpawnPosition(int laneIndex)
        {
            float centeredLane = laneIndex - ((RaceRules.MaximumPlayers - 1) * 0.5f);
            return new Vector3(centeredLane * SpawnLaneSpacing, 0.05f, SpawnPositionZ);
        }

        static void ResetPlayerAtLane(GuineaPigPlayer player, int laneIndex)
        {
            player.ResetOnServer(GetSpawnPosition(laneIndex), Quaternion.identity);
        }
    }
}
