using TMPro;
using UnityEngine;

public class CameraManager : MonoBehaviour {
    InputManager inputManager;

    PlayerMovement playerMovement;

    public Transform playerTransform;

    public Transform cameraPivot;
    private Vector3 camFollowVelocity = Vector3.zero;

    [Header("Camera Movement and Rotation")]
    public float camFollowSpeed = 0f;
    public float camLookSpeed = 0.1f;
    public float camPivotSpeed = 0.1f;
    public float lookAngle;
    public float pivotAngle;
    public float minimumPivotAngle = -40f;
    public float maximumPivotAngle = 40f;
    
    [Header("Camera Position Adjustment")]
    private Vector3 defaultPivotLocalPosition;

    [Header("Scoped Settings")]
    public float scopedFOV = 35f;
    public float defaultFOV = 60f;
    public bool isHoldingPistol = false;
    public bool isScoped = false;
    public Camera camera;

    void Awake() {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        playerTransform = FindFirstObjectByType<PlayerManager>().transform;
        inputManager = FindFirstObjectByType<InputManager>();
        playerMovement = FindFirstObjectByType<PlayerMovement>();   // bir kez
        defaultPivotLocalPosition = cameraPivot.localPosition;

        lookAngle = transform.rotation.eulerAngles.y;
    }

    void Update() {
        // Mobil: her karede tum sahneyi taramak pahaliydi, Awake'e alindi.
        if (playerMovement == null) playerMovement = FindFirstObjectByType<PlayerMovement>();
    }

    public void HandleAllCameraMovement() {
        FollowTarget();
        RotateCamera();
        HandleScopedFOV();
    }

    // Make the camera follows the player
    // transform.position: current position
    // playerTransform.position: position of the player
    void FollowTarget() {
        // Calculate a new position for the camera to move to
        Vector3 targetPosition = Vector3.SmoothDamp(transform.position, playerTransform.position, ref camFollowVelocity, camFollowSpeed);
        transform.position = targetPosition;
    }

    void RotateCamera() {
        Vector3 rotation;
        Quaternion targetRotation;

        lookAngle += inputManager.cameraInputX * camLookSpeed;
        pivotAngle += inputManager.cameraInputY * camPivotSpeed;

        pivotAngle = Mathf.Clamp(pivotAngle, minimumPivotAngle, maximumPivotAngle);

        // Adjust camera position based on pivot angle to keep player in view
        AdjustCameraPositionForPivotAngle();

        rotation = Vector3.zero;
        rotation.y = lookAngle;
        targetRotation = Quaternion.Euler(rotation);
        transform.rotation = targetRotation;

        rotation = Vector3.zero;
        rotation.z = pivotAngle;
        targetRotation = Quaternion.Euler(rotation);
        cameraPivot.localRotation = targetRotation;

        if (isScoped == true) {
            camLookSpeed = 0.05f;
            camPivotSpeed = 0.05f;

            playerTransform.rotation = Quaternion.Euler(0, lookAngle-270f, 0);
        }
        else {
            camLookSpeed = 0.1f;
            camPivotSpeed = 0.1f;
        }
    }

    void AdjustCameraPositionForPivotAngle() {
        float normalizedPivotAngle = Mathf.Abs(pivotAngle) / maximumPivotAngle;

        float additionalHeight = 0;
        if (pivotAngle < 0) {               // Looking down 
            additionalHeight = 1.5f * normalizedPivotAngle;
        }
        else if (pivotAngle > 0) {         // Looking up
            additionalHeight = -2.5f * normalizedPivotAngle;
        }

        // Adjust the camera pivot position
        Vector3 targetPivotPosition = defaultPivotLocalPosition;
        targetPivotPosition.y += additionalHeight;
        cameraPivot.localPosition = targetPivotPosition;
    }

    private void HandleScopedFOV() {
        if (inputManager.scopeInput == true && playerMovement.isReloading == false && isHoldingPistol == true) {
            camera.fieldOfView = scopedFOV;
            isScoped = true;
        }
        else {
            camera.fieldOfView = defaultFOV;
            isScoped = false;
        }
    }
}
