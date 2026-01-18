using Unity.Netcode;
using UnityEngine;

namespace Demo.Scripts.Runtime.Network
{
    /// <summary>
    /// Simple UI for testing network functionality.
    /// Add this to a GameObject in your scene along with NetworkManager.
    /// </summary>
    public class NetworkTestUI : MonoBehaviour
    {
        [Header("UI Settings")]
        [SerializeField] private KeyCode toggleUIKey = KeyCode.N;
        [SerializeField] private bool showOnStart = true;

        private bool _showUI = true;
        private string _ipAddress = "127.0.0.1";
        private string _port = "7777";

        private void Start()
        {
            _showUI = showOnStart;
            EnsureTransportExists();
        }

        private void EnsureTransportExists()
        {
            if (NetworkManager.Singleton == null) return;

            // Check if transport is assigned
            if (NetworkManager.Singleton.NetworkConfig.NetworkTransport == null)
            {
                // Try to find existing transport
                var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();

                if (transport == null)
                {
                    // Add transport component
                    transport = NetworkManager.Singleton.gameObject.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                    Debug.Log("[Network] Added UnityTransport component");
                }

                // Assign it to NetworkManager
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = transport;
                Debug.Log("[Network] Assigned transport to NetworkManager");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleUIKey))
            {
                _showUI = !_showUI;
            }

            // Keyboard shortcuts for network actions
            if (NetworkManager.Singleton == null) return;

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                // H = Start Host
                if (Input.GetKeyDown(KeyCode.H))
                {
                    SetConnectionData();
                    NetworkManager.Singleton.StartHost();
                    Debug.Log("[Network] Started as HOST");
                }
                // J = Start Server
                if (Input.GetKeyDown(KeyCode.J))
                {
                    SetConnectionData();
                    NetworkManager.Singleton.StartServer();
                    Debug.Log("[Network] Started as SERVER");
                }
                // K = Start Client
                if (Input.GetKeyDown(KeyCode.K))
                {
                    SetConnectionData();
                    NetworkManager.Singleton.StartClient();
                    Debug.Log("[Network] Started as CLIENT");
                }
            }
            else
            {
                // L = Disconnect
                if (Input.GetKeyDown(KeyCode.L))
                {
                    NetworkManager.Singleton.Shutdown();
                    Debug.Log("[Network] Disconnected");
                }
            }
        }

        private void OnGUI()
        {
            if (!_showUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 400));
            GUILayout.BeginVertical("box");

            GUILayout.Label("<b>Network Test Panel</b> (Press N to toggle)");
            GUILayout.Space(10);

            if (NetworkManager.Singleton == null)
            {
                GUILayout.Label("<color=red>NetworkManager not found!</color>");
                GUILayout.Label("Add NetworkManager to your scene.");
                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
            {
                // Not connected - show connection options
                GUILayout.Label("IP Address:");
                _ipAddress = GUILayout.TextField(_ipAddress);

                GUILayout.Label("Port:");
                _port = GUILayout.TextField(_port);

                GUILayout.Space(10);

                if (GUILayout.Button("Start Host (Server + Client)", GUILayout.Height(40)))
                {
                    SetConnectionData();
                    NetworkManager.Singleton.StartHost();
                }

                if (GUILayout.Button("Start Server Only", GUILayout.Height(30)))
                {
                    SetConnectionData();
                    NetworkManager.Singleton.StartServer();
                }

                if (GUILayout.Button("Start Client (Connect to Server)", GUILayout.Height(30)))
                {
                    SetConnectionData();
                    NetworkManager.Singleton.StartClient();
                }

                GUILayout.Space(10);
                GUILayout.Label("<b>Keyboard Shortcuts:</b>");
                GUILayout.Label("  H = Start Host");
                GUILayout.Label("  J = Start Server");
                GUILayout.Label("  K = Start Client");
                GUILayout.Label("  L = Disconnect (when connected)");
            }
            else
            {
                // Connected - show status and disconnect option
                GUILayout.Label("<color=green><b>Connected!</b></color>");
                GUILayout.Space(5);

                string role = "";
                if (NetworkManager.Singleton.IsHost) role = "Host";
                else if (NetworkManager.Singleton.IsServer) role = "Server";
                else if (NetworkManager.Singleton.IsClient) role = "Client";

                GUILayout.Label($"Role: <b>{role}</b>");
                GUILayout.Label($"Client ID: <b>{NetworkManager.Singleton.LocalClientId}</b>");
                GUILayout.Label($"Connected Clients: <b>{NetworkManager.Singleton.ConnectedClientsIds.Count}</b>");

                if (NetworkManager.Singleton.IsServer)
                {
                    GUILayout.Space(5);
                    GUILayout.Label("Connected Client IDs:");
                    foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
                    {
                        GUILayout.Label($"  - Client {clientId}");
                    }
                }

                GUILayout.Space(10);

                if (GUILayout.Button("Disconnect", GUILayout.Height(30)))
                {
                    NetworkManager.Singleton.Shutdown();
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void SetConnectionData()
        {
            if (ushort.TryParse(_port, out ushort portNum))
            {
                var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (transport != null)
                {
                    transport.ConnectionData.Address = _ipAddress;
                    transport.ConnectionData.Port = portNum;
                }
            }
        }
    }
}
