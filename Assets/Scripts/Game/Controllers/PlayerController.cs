﻿using System;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class PlayerController : GravityObject
{
    // Exposed variables
    [Header("Movement settings")] public float walkSpeed = 8;
    public float runSpeed = 14;
    public float jumpForce = 20;
    public float vSmoothTime = 0.1f;
    public float airSmoothTime = 0.5f;
    public float stickToGroundForce = 8;

    public float maxEnergy = 100;

    public bool isFlying = false;

    public AtmosphereTrigger atmosphereTrigger;
    public CelestialBody celestialBody;

    public float flyForce = .01f;
    
    public bool isWithinPlanetRange = true;

    [Header("Mouse settings")] public float mouseSensitivityMultiplier = 1;
    public float maxMouseSmoothTime = 0.3f;
    public Vector2 pitchMinMax = new(-40, 85);
    public InputSettings inputSettings;

    [Header("Other")] public float mass = 70;
    public LayerMask walkableMask;
    public Transform feet;

    [Header("Energy System")] public GameObject sun;
    public float energy;
    private Vignette _vignette;

// Private
    Rigidbody rb;

    Ship spaceship;

    float yaw;
    float pitch;
    float smoothYaw;
    float smoothPitch;

    float yawSmoothV;
    float pitchSmoothV;

    Vector3 targetVelocity;
    Vector3 cameraLocalPos;
    Vector3 smoothVelocity;
    Vector3 smoothVRef;

    CelestialBody referenceBody;

    Camera cam;
    bool readyToFlyShip;
    bool debug_playerFrozen;
    Animator animator;

    void Awake()
    {
        cam = GetComponentInChildren<Camera>();
        cameraLocalPos = cam.transform.localPosition;
        spaceship = FindObjectOfType<Ship>();
        InitRigidbody();

        animator = GetComponentInChildren<Animator>();
        inputSettings.Begin();
    }

    void InitRigidbody() {
	rb = GetComponent<Rigidbody>();
	rb.interpolation = RigidbodyInterpolation.Interpolate;
	rb.useGravity = false;
	rb.isKinematic = false;
	rb.mass = mass;
 }
   void HandleMovement()
{
    HandleEditorInput();
    if (Time.timeScale == 0)
    {
        return;
    }

    // Look input
    yaw += Input.GetAxisRaw("Mouse X") * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
    pitch -= Input.GetAxisRaw("Mouse Y") * inputSettings.mouseSensitivity / 10 * mouseSensitivityMultiplier;
    pitch = Mathf.Clamp(pitch, pitchMinMax.x, pitchMinMax.y);
    float mouseSmoothTime = Mathf.Lerp(0.01f, maxMouseSmoothTime, inputSettings.mouseSmoothing);
    smoothPitch = Mathf.SmoothDampAngle(smoothPitch, pitch, ref pitchSmoothV, mouseSmoothTime);
    float smoothYawOld = smoothYaw;
    smoothYaw = Mathf.SmoothDampAngle(smoothYaw, yaw, ref yawSmoothV, mouseSmoothTime);

    if (!debug_playerFrozen && Time.timeScale > 0)
    {
        cam.transform.localEulerAngles = Vector3.right * smoothPitch;
        transform.Rotate(Vector3.up * Mathf.DeltaAngle(smoothYawOld, smoothYaw), Space.Self);
    }

    // Movement
    bool isGrounded = IsGrounded();
    Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
    bool running = Input.GetKey(KeyCode.LeftShift);
    targetVelocity = transform.TransformDirection(input.normalized) * ((running) ? runSpeed : walkSpeed);
    smoothVelocity = Vector3.SmoothDamp(smoothVelocity, targetVelocity, ref smoothVRef, (isGrounded) ? vSmoothTime : airSmoothTime);

    // Flying mode
    if (Input.GetKey(KeyCode.Space) && energy > 0)
    {
        energy -= 0.1f;
        rb.AddForce(transform.up * flyForce*0.05f, ForceMode.VelocityChange);
        Debug.Log("Flying");
        isFlying = true;
    }
    else
    {
        // Apply small downward force to prevent player from bouncing when going down slopes
        rb.AddForce(-transform.up * stickToGroundForce*0.2f, ForceMode.VelocityChange);
        isFlying = false;
    }
}
 




    private void Start()
    {
        var volume = GetComponentInChildren<PostProcessVolume>();
        volume.profile.TryGetSettings(out _vignette);
    }


    void Update()
    {
        HandleMovement();
        UpdateEnergy();
    }

   

    bool IsGrounded()
    {
        // Sphere must not overlay terrain at origin otherwise no collision will be detected
        // so rayRadius should not be larger than controller's capsule collider radius
        const float rayRadius = .3f;
        const float groundedRayDst = .2f;
        bool grounded = false;

        if (referenceBody)
        {
            var relativeVelocity = rb.velocity - referenceBody.velocity;
            // Don't cast ray down if player is jumping up from surface
            if (relativeVelocity.y <= jumpForce * .5f)
            {
                RaycastHit hit;
                Vector3 offsetToFeet = (feet.position - transform.position);
                Vector3 rayOrigin = rb.position + offsetToFeet + transform.up * rayRadius;
                Vector3 rayDir = -transform.up;

                grounded = Physics.SphereCast(rayOrigin, rayRadius, rayDir, out hit, groundedRayDst, walkableMask);
            }
        }

        return grounded;
    }

     void UpdateEnergy()
{
    RaycastHit hit;
    Vector3 directionToSun = (sun.transform.position - transform.position).normalized;
    float angleToSun = Vector3.Angle(directionToSun, transform.up);

    if (Physics.Linecast(transform.position, sun.transform.position, out hit))
    {
        if (hit.collider.name != "Terrain Mesh" && angleToSun < 100)
        {
            energy += isFlying ? -0.005f : 0.05f; // Adjust these values to modify energy consumption and replenishment rates
        }
        else
        {
            energy -= 0.05f;
        }
        energy = Mathf.Clamp(energy, 0, maxEnergy);

        if (_vignette != null)
        {
            _vignette.intensity.value = Math.Min(energy / maxEnergy, 0.68f);
        }
    }
}



    void FixedUpdate()
    {
        CelestialBody[] bodies = NBodySimulation.Bodies;
        Vector3 gravityOfNearestBody = Vector3.zero;
        float nearestSurfaceDst = float.MaxValue;


        RaycastHit hit;
        if (Physics.Linecast(transform.position, sun.transform.position, out hit))
        {
            /* if (hit.collider.name != "Terrain Mesh")
            {
                if (energy < maxEnergy && !isFlying)
                {
                    energy += 0.0025f;
                }
                else
                {
                    //energy = maxEnergy;
                }
            }
            else
            {
                if (energy > 0)
                {
                    energy -= 0.025f;
                }
                else
                {
                    //energy = 0;
                }
            }
            if (_vignette != null)
            {
                _vignette.intensity.value = Math.Min(energy / maxEnergy, 0.68f);
            } */
        }

        // Gravity
        foreach (CelestialBody body in bodies)
        {
            float sqrDst = (body.Position - rb.position).sqrMagnitude;
            Vector3 forceDir = (body.Position - rb.position).normalized;
            Vector3 acceleration = forceDir * Universe.gravitationalConstant * body.mass / sqrDst;
            rb.AddForce(acceleration, ForceMode.Acceleration);

            float dstToSurface = Mathf.Sqrt(sqrDst) - body.radius;

            // Find body with strongest gravitational pull 
            if (dstToSurface < nearestSurfaceDst)
            {
                nearestSurfaceDst = dstToSurface;
                gravityOfNearestBody = acceleration;
                referenceBody = body;
            }
        }

        // Rotate to align with gravity up
        Vector3 gravityUp = -gravityOfNearestBody.normalized;
        rb.rotation = Quaternion.FromToRotation(transform.up, gravityUp) * rb.rotation;

        // Move
        rb.MovePosition(rb.position + smoothVelocity * Time.fixedDeltaTime);
    }

    void HandleEditorInput()
    {
        if (Application.isEditor)
        {
            if (Input.GetKeyDown(KeyCode.O))
            {
                Debug.Log("Debug mode: Toggle freeze player");
                debug_playerFrozen = !debug_playerFrozen;
            }
        }
    }

    public void SetVelocity(Vector3 velocity)
    {
        rb.velocity = velocity;
    }

    public void ExitFromSpaceship()
    {
        cam.transform.parent = transform;
        cam.transform.localPosition = cameraLocalPos;
        smoothYaw = 0;
        yaw = 0;
        smoothPitch = cam.transform.localEulerAngles.x;
        pitch = smoothPitch;
    }

    public Camera Camera
    {
        get { return cam; }
    }

    public Rigidbody Rigidbody
    {
        get { return rb; }
    }
}