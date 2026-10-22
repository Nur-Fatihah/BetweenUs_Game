using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CallEmergencyMeetingServerRpc(string reporterName)
    {
        Debug.Log($"Emergency Meeting called by: {reporterName}");
    }
}