using Unity.Netcode;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private Rigidbody2D rb;
    [SerializeField] private AudioSource footstepAudioSource;

    [Header("Role Settings")]
    public bool isImpostor = false;
    public bool isIncapacitated = false;

    private Vector2 moveInput;

    private void Update()
    {
        if (!IsOwner || isIncapacitated) return;

        moveInput.x = Input.GetAxisRaw("Horizontal");
        moveInput.y = Input.GetAxisRaw("Vertical");
        moveInput = moveInput.normalized;

        // Dynamic Surface Footsteps
        if (moveInput.magnitude > 0.1f)
        {
            if (footstepAudioSource != null && !footstepAudioSource.isPlaying) footstepAudioSource.Play();
        }
        else
        {
            if (footstepAudioSource != null && footstepAudioSource.isPlaying) footstepAudioSource.Stop();
        }

        // Emergency Meeting Trigger
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.CallEmergencyMeetingServerRpc("Player " + OwnerClientId);
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner || isIncapacitated) return;

        rb.MovePosition(rb.position + moveInput * moveSpeed * Time.fixedDeltaTime);
    }

    public void AssignRole(bool impostorRole)
    {
        isImpostor = impostorRole;
        Debug.Log(isImpostor ? "You are the IMPOSTOR!" : "You are a CREWMATE!");
    }

    [ServerRpc]
    public void IncapacitatePlayerServerRpc(ulong targetClientId)
    {
        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { targetClientId } }
        };
        IncapacitateClientRpc(targetParams);
    }

    [ClientRpc]
    private void IncapacitateClientRpc(ClientRpcParams clientRpcParams = default)
    {
        isIncapacitated = true;
        GetComponent<SpriteRenderer>().color = new Color(0.3f, 0.3f, 0.3f, 0.6f); // Non-graphic down state
    }
}