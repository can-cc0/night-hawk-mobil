using SunTemple;
using UnityEngine;

public class InputManager : MonoBehaviour {

    PlayerControls playerControls;

    AnimatorManager animatorManager;

    PlayerMovement playerMovement;

    public static InputManager instance;

    public float moveAmount;

    public Vector2 movementInput;

    public float verticalInput;
    public float horizontalInput;

    private Vector2 cameraInput;
    public float cameraInputX;
    public float cameraInputY;

    [Header("Input Button Flag")]
    public bool sprintInput;
    public bool fireInput;
    public bool scopeInput;
    public bool reloadInput;
    public bool switchWeaponInput;
    public bool pauseGameInput;
    public bool interactInput;
    public bool crouchInput;
    public bool assassinateInput;
    public bool lootInput;

    [Header("Pause Flag")]
    public bool isPaused = false;

    private void Awake() {
        animatorManager = GetComponent<AnimatorManager>();
        playerMovement = GetComponent<PlayerMovement>();
        instance = this;
    }

    private void OnEnable() {
        if (playerControls == null) {
            playerControls = new PlayerControls();
            playerControls.PlayerMovement.Movement.performed += i => movementInput = i.ReadValue<Vector2>();
            playerControls.PlayerMovement.CameraMovement.performed += i => cameraInput = i.ReadValue<Vector2>();
            playerControls.PlayerActions.Sprint.performed += i => sprintInput = true;
            playerControls.PlayerActions.Sprint.canceled += i => sprintInput = false;
            playerControls.PlayerActions.Fire.performed += i => fireInput = true;
            playerControls.PlayerActions.Fire.canceled += i => fireInput = false;
            playerControls.PlayerActions.Scope.performed += i => scopeInput = true;
            playerControls.PlayerActions.Scope.canceled += i => scopeInput = false;
            playerControls.PlayerActions.Reload.performed += i => reloadInput = true;
            playerControls.PlayerActions.Reload.canceled += i => reloadInput = false;
            playerControls.PlayerActions.SwitchWeapon.performed += i => switchWeaponInput = true;
            playerControls.PlayerActions.PauseGame.performed += i => pauseGameInput = true;
            playerControls.PlayerActions.Interact.performed += i => interactInput = true;
            playerControls.PlayerActions.Crouch.performed += i => crouchInput = true;
            playerControls.PlayerActions.Assassinate.performed += i => assassinateInput = true;
            playerControls.PlayerActions.Assassinate.canceled += i => assassinateInput = false;
            playerControls.PlayerActions.Loot.performed += i => lootInput = true;
        }
        playerControls.Enable();
    }

    private void OnDisable() {
        playerControls.Disable();
    }

    public void HandleAllInputs() {
        HandlePauseGameInput();
        HandleMovementInput(); ;
        BayraklariZamanla();
    }

    // Interact ve Loot bayraklari 0.2 sn sonra kendiliginden sonuyor. Onceki
    // surumde bu is her karede iki StartCoroutine + iki new WaitForSeconds ile
    // yapiliyordu: 60 fps'te saniyede 120 nesne, yani surekli cop toplama ve
    // gozle gorulur takilma. Ayni davranis zaman damgasiyla, sifir ayirma ile.
    private float interactSonme, lootSonme;

    private void BayraklariZamanla() {
        if (interactInput) {
            if (interactSonme <= 0f) interactSonme = Time.time + 0.2f;
            else if (Time.time >= interactSonme) { interactInput = false; interactSonme = 0f; }
        }
        else interactSonme = 0f;

        if (lootInput) {
            if (lootSonme <= 0f) lootSonme = Time.time + 0.2f;
            else if (Time.time >= lootSonme) { lootInput = false; lootSonme = 0f; }
        }
        else lootSonme = 0f;
    }

    private void HandleMovementInput() {
        verticalInput = movementInput.y;
        horizontalInput = movementInput.x;

        if (isPaused) {
            cameraInputX = 0;
            cameraInputY = 0;
        }
        else {
            cameraInputX = cameraInput.x;
            cameraInputY = cameraInput.y;
        }

        moveAmount = Mathf.Clamp01(Mathf.Abs(horizontalInput) + Mathf.Abs(verticalInput));
        animatorManager.UpdateAnimationValues(horizontalInput, verticalInput, playerMovement.isRunning);
    }

    private void HandlePauseGameInput() {
        // if (pauseGameInput == true) {
        //     pauseGameInput = false;
        // }
    }

    public void SwitchWeaponDone() {
        switchWeaponInput = false;
    }

    public void SwitchCrouchDone() {
        crouchInput = false;
    }


}
