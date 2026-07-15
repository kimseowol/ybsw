// PRD-001
using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Ybsw.Racing
{
    [RequireComponent(typeof(NetworkManager))]
    [RequireComponent(typeof(UnityTransport))]
    public sealed class RaceLauncher : MonoBehaviour
    {
        NetworkManager networkManager;
        UnityTransport transport;
        string serverAddress = "127.0.0.1";
        bool isSwitchingToClient;
        GUIStyle countdownStyle;
        GUIStyle countdownShadowStyle;
        int displayedCountdownNumber = -1;
        string countdownText = string.Empty;
        bool networkResourcesReleased;

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            networkManager.NetworkConfig.ConnectionApproval = true;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
        }

        void OnEnable()
        {
            networkResourcesReleased = false;
            networkManager.ConnectionApprovalCallback = HandleConnectionApproval;
        }

        void Start()
        {
            string[] commandLineArguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < commandLineArguments.Length; index++)
            {
                if (commandLineArguments[index] == "-raceHost")
                {
                    StartHost();
                    return;
                }

                if (commandLineArguments[index] == "-raceClient")
                {
                    if (index + 1 < commandLineArguments.Length)
                    {
                        serverAddress = commandLineArguments[index + 1];
                    }

                    StartClient();
                    return;
                }
            }

            StartHost();
        }

        void OnDisable()
        {
            ReleaseNetworkResources();
        }

        void OnDestroy()
        {
            ReleaseNetworkResources();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18f, 18f, 360f, 360f), GUI.skin.box);
            GUILayout.Label("Guinea Pig Multiplayer Race");
            GUILayout.Space(8f);

            if (!networkManager.IsListening)
            {
                GUILayout.Label("Host address");
                serverAddress = GUILayout.TextField(serverAddress, 64);

                if (GUILayout.Button("Host Race"))
                {
                    StartHost();
                }

                if (GUILayout.Button("Join Race"))
                {
                    StartClient();
                }

                GUILayout.Space(8f);
                GUILayout.Label("The race starts immediately. Maximum: four players.");
                GUILayout.Label("Controls: touch stick, WASD, or arrow keys.");
                GUILayout.Label("Camera: drag outside the move stick.");
            }
            else
            {
                DrawSessionStatus();
            }

            GUILayout.EndArea();
            DrawCountdownOverlay();
        }

        void StartHost()
        {
            transport.SetConnectionData("127.0.0.1", RaceRules.DefaultPort, "0.0.0.0");
            if (!networkManager.IsListening)
            {
                networkResourcesReleased = false;
                networkManager.StartHost();
            }
        }

        void StartClient()
        {
            string address = string.IsNullOrWhiteSpace(serverAddress) ? "127.0.0.1" : serverAddress.Trim();
            transport.SetConnectionData(address, RaceRules.DefaultPort);
            if (!networkManager.IsListening)
            {
                networkResourcesReleased = false;
                networkManager.StartClient();
            }
        }

        void ReleaseNetworkResources()
        {
            if (networkResourcesReleased)
            {
                return;
            }

            networkResourcesReleased = true;
            if (networkManager != null
                && networkManager.ConnectionApprovalCallback == HandleConnectionApproval)
            {
                networkManager.ConnectionApprovalCallback = null;
            }

            if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
            else if (transport != null)
            {
                transport.Shutdown();
            }
        }

        void DrawSessionStatus()
        {
            GUILayout.Label(networkManager.IsHost ? "Role: Host" : "Role: Client");
            GUILayout.Label("Players: " + networkManager.ConnectedClientsIds.Count + "/" + RaceRules.MaximumPlayers);

            RaceSession session = RaceSession.Instance;
            if (session != null)
            {
                GUILayout.Label("Race: " + session.Phase);
                if (session.Phase == RacePhase.Countdown)
                {
                    GUILayout.Label("Starting in: " + session.RemainingCountdownInSeconds.ToString("0.0"));
                }
                else if (session.Phase == RacePhase.Racing)
                {
                    GUILayout.Label("Time: " + RaceTimeUtility.FormatElapsed(session.ElapsedRaceTimeInSeconds));
                }
            }

            NetworkObject localPlayerObject = networkManager.LocalClient != null
                ? networkManager.LocalClient.PlayerObject
                : null;
            if (localPlayerObject != null)
            {
                GuineaPigPlayer localPlayer = localPlayerObject.GetComponent<GuineaPigPlayer>();
                if (localPlayer != null && localPlayer.FinishPlace > 0)
                {
                    GUILayout.Space(8f);
                    GUILayout.Label("FINISH! Place #" + localPlayer.FinishPlace);
                    GUILayout.Label("Record: " + RaceTimeUtility.FormatElapsed(localPlayer.FinishTimeInSeconds));

                    if (session != null && GUILayout.Button("Race Again"))
                    {
                        session.RequestRestartRace();
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Controls: touch stick, WASD, or arrow keys.");
            GUILayout.Label("Camera: drag outside the move stick.");

            if (networkManager.IsHost && session != null && GUILayout.Button("Restart Race Now"))
            {
                session.RestartRace();
            }

            if (networkManager.IsHost)
            {
                GUILayout.Label("Join another host address");
                serverAddress = GUILayout.TextField(serverAddress, 64);
                GUI.enabled = !isSwitchingToClient;
                if (GUILayout.Button("Join Another Host"))
                {
                    StartCoroutine(SwitchToClient());
                }

                GUI.enabled = true;
            }

            if (GUILayout.Button("Disconnect"))
            {
                networkManager.Shutdown();
            }
        }

        void DrawCountdownOverlay()
        {
            RaceSession session = RaceSession.Instance;
            if (session == null || session.Phase != RacePhase.Countdown)
            {
                return;
            }

            int countdownNumber = RaceTimeUtility.GetCountdownDisplayNumber(
                session.RemainingCountdownInSeconds);
            if (countdownNumber <= 0)
            {
                return;
            }

            if (countdownStyle == null)
            {
                countdownStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                countdownStyle.normal.textColor = Color.white;

                countdownShadowStyle = new GUIStyle(countdownStyle);
                countdownShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.72f);
            }

            int fontSize = Mathf.Max(72, Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.18f));
            countdownStyle.fontSize = fontSize;
            countdownShadowStyle.fontSize = fontSize;

            if (displayedCountdownNumber != countdownNumber)
            {
                displayedCountdownNumber = countdownNumber;
                countdownText = countdownNumber.ToString();
            }

            Rect screenRect = new Rect(0f, 0f, Screen.width, Screen.height);
            Rect shadowRect = new Rect(4f, 4f, Screen.width, Screen.height);
            GUI.Label(shadowRect, countdownText, countdownShadowStyle);
            GUI.Label(screenRect, countdownText, countdownStyle);
        }

        IEnumerator SwitchToClient()
        {
            isSwitchingToClient = true;
            networkManager.Shutdown();

            while (networkManager.ShutdownInProgress)
            {
                yield return null;
            }

            yield return null;
            StartClient();
            isSwitchingToClient = false;
        }

        void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            int otherPendingConnections = Mathf.Max(0, networkManager.PendingClients.Count - 1);
            int occupiedSlots = networkManager.ConnectedClientsIds.Count + otherPendingConnections;
            bool approved = RaceRules.CanApproveConnection(occupiedSlots);

            response.Approved = approved;
            response.CreatePlayerObject = approved;
            response.Pending = false;
            response.Reason = approved ? string.Empty : "The race is full.";
        }
    }
}
