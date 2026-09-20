using UnityEngine;

/// <summary>
/// WASD mover with mouse look. Supports first-person and third-person (character model visible).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class TerrainFirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Transform cameraTransform;

    [Header("Move")]
    [SerializeField] private float walkSpeed = 6f;
    [SerializeField] private float runSpeed = 10f;
    [SerializeField] private float gravity = -24f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float stickToGroundForce = -4f;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 2.2f;
    [SerializeField] private float minPitch = -35f;
    [SerializeField] private float maxPitch = 70f;
    [SerializeField] private bool thirdPerson = false;
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 1.6f, 0.08f);
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.35f, 0f);

    [Header("Ground")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundCheckExtra = 0.35f;

    private float _pitch;
    private float _yaw;
    private Vector3 _velocity;
    private bool _grounded;

    public Transform CameraTransform => cameraTransform;

    private void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null)
                cameraTransform = cam.transform;
        }

        _yaw = transform.eulerAngles.y;
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (characterController == null || !characterController.enabled)
            return;

        UpdateGrounded();
        Look();
        Move();
    }

    private void LateUpdate()
    {
        if (thirdPerson)
            UpdateThirdPersonCamera();
    }

    private void UpdateGrounded()
    {
        float radius = Mathf.Max(0.05f, characterController.radius * 0.9f);
        Vector3 origin = transform.position + characterController.center;
        float castDist = characterController.height * 0.5f + groundCheckExtra;

        _grounded = characterController.isGrounded
                    || Physics.SphereCast(
                        origin,
                        radius,
                        Vector3.down,
                        out _,
                        castDist,
                        groundMask,
                        QueryTriggerInteraction.Ignore);

        if (_grounded && _velocity.y < 0f)
            _velocity.y = stickToGroundForce;
    }

    private void Look()
    {
        if (cameraTransform == null)
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
        _yaw += mx;
        _pitch = Mathf.Clamp(_pitch - my, minPitch, maxPitch);

        // Rotate the body (and parent Player root if present).
        Transform yawTarget = transform.parent != null && transform.parent.name == "Player"
            ? transform.parent
            : transform;
        yawTarget.rotation = Quaternion.Euler(0f, _yaw, 0f);

        if (!thirdPerson)
            cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void UpdateThirdPersonCamera()
    {
        if (cameraTransform == null)
            return;

        Transform yawTarget = transform.parent != null && transform.parent.name == "Player"
            ? transform.parent
            : transform;

        Vector3 pivot = yawTarget.position + Vector3.up * lookAtOffset.y;
        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 desired = pivot + orbit * cameraOffset;

        // Keep camera out of terrain a little.
        Vector3 toCam = desired - pivot;
        float dist = toCam.magnitude;
        if (dist > 0.001f)
        {
            Vector3 dir = toCam / dist;
            if (Physics.SphereCast(pivot, 0.2f, dir, out RaycastHit hit, dist, groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                desired = pivot + dir * Mathf.Max(0.6f, hit.distance - 0.25f);
            }
        }

        cameraTransform.position = desired;

        Vector3 lookPoint = yawTarget.position + lookAtOffset;
        Vector3 lookDir = lookPoint - cameraTransform.position;
        if (lookDir.sqrMagnitude > 0.0001f)
            cameraTransform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }

    private void Move()
    {
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        Vector3 input = new Vector3(x, 0f, z);
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        Transform yawTarget = transform.parent != null && transform.parent.name == "Player"
            ? transform.parent
            : transform;

        bool running = Input.GetKey(KeyCode.LeftShift);
        float speed = running ? runSpeed : walkSpeed;
        Vector3 wish = yawTarget.right * input.x + yawTarget.forward * input.z;

        if (Input.GetButtonDown("Jump") && _grounded)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _velocity.y += gravity * Time.deltaTime;
        Vector3 motion = wish * speed + Vector3.up * _velocity.y;
        characterController.Move(motion * Time.deltaTime);
        SyncHierarchyAfterMove();
    }

    /// <summary>
    /// Elman prefab keeps CharacterController on a child. Snap parent so the hierarchy stays together.
    /// </summary>
    private void SyncHierarchyAfterMove()
    {
        if (transform.parent == null || transform.parent.name != "Player")
            return;

        Vector3 world = transform.position;
        Quaternion worldRot = Quaternion.Euler(0f, _yaw, 0f);
        transform.parent.SetPositionAndRotation(world, worldRot);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void Configure(CharacterController controller, Transform cam)
    {
        Configure(controller, cam, thirdPerson, cameraOffset, lookAtOffset);
    }

    public void Configure(
        CharacterController controller,
        Transform cam,
        bool thirdPerson,
        Vector3 cameraOffset,
        Vector3 lookAtOffset)
    {
        characterController = controller;
        cameraTransform = cam;
        this.thirdPerson = thirdPerson;
        this.cameraOffset = cameraOffset;
        this.lookAtOffset = lookAtOffset;
        minPitch = thirdPerson ? -35f : -85f;
        maxPitch = thirdPerson ? 70f : 85f;
    }
}
