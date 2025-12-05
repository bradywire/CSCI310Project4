using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(CharacterController))]
public class WanderingAI : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float baseSpeed = 3.5f; // Slightly faster
    [SerializeField] private float turnSpeed = 450f; // Faster turns
    
    [Header("Patrol")]
    [SerializeField] private float avoidDistance = 3f; // Wider avoidance
    [SerializeField] private float detectionRadius = 0.6f;
    [SerializeField, Range(45f, 135f)] private float maxTurnAngle = 90f;
    [SerializeField] private float randomWanderInterval = 3f;

    [Header("Smart Detection & Chase (Tuned for Easier Detection)")]
    [SerializeField] private float detectionRange = 25f; // MUCH larger (was 12)
    [SerializeField, Range(90f, 180f)] private float fovAngle = 160f; // MUCH wider (was 110)
    [SerializeField] private float eyeHeight = 1.2f;
    [SerializeField] private LayerMask obstacleLayers = -1;
    [SerializeField] private bool requireLineOfSight = true; // Toggle OFF for distance-only detection

    [Header("Attack")]
    [SerializeField] private GameObject fireballPrefab;
    [SerializeField] private float fireballSpeed = 1.2f;
    [SerializeField] private float attackCooldown = 1.2f;

    [Header("Health & Death")]
    [SerializeField] private bool isAlive = true;
    [SerializeField] private GameObject deathEffectPrefab;
    public UnityEvent onDeath;

    [Header("Events")]
    public UnityEvent onPlayerDetected;
    public UnityEvent onPlayerLost;

    // Runtime
    private CharacterController controller;
    private Transform playerTransform;
    private float currentSpeed;
    private float verticalVelocity;
    private float nextAttackTime;
    private float nextWanderTime;
    private bool playerInSight;
    private Vector3 horizontalMove;
    public float SpeedMultiplier { get; set; } = 1f;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        playerTransform = GameObject.FindGameObjectWithTag("Player")?.transform;
        if (playerTransform == null)
        {
            Debug.LogError($"{name}: NO 'Player' TAG FOUND! Fix player tag.", this);
            return;
        }
        Debug.Log($"{name}: Player found at {playerTransform.name}");

        nextWanderTime = Time.time;
        nextAttackTime = Time.time;
        verticalVelocity = -2f;
    }

    private void Update()
    {
        verticalVelocity += Physics.gravity.y * Time.deltaTime;
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (!isAlive)
        {
            controller.Move((horizontalMove + Vector3.up * verticalVelocity) * Time.deltaTime);
            return;
        }

        currentSpeed = baseSpeed * SpeedMultiplier;
        horizontalMove = Vector3.zero;

        bool currentVisible = DetectPlayer();
        if (currentVisible != playerInSight)
        {
            playerInSight = currentVisible;
            Debug.Log($"{name}: Player {(playerInSight ? "DETECTED (chase!)" : "LOST")}");
            if (playerInSight) onPlayerDetected?.Invoke();
            else onPlayerLost?.Invoke();
        }

        if (playerInSight)
        {
            Chase();
            HandleAttack();
        }
        else
        {
            Patrol();
        }

        Vector3 move = horizontalMove + Vector3.up * verticalVelocity;
        controller.Move(move * Time.deltaTime);
    }

    private bool DetectPlayer()
    {
        if (playerTransform == null) return false;

        Vector3 toPlayer = playerTransform.position - transform.position;
        float dist = toPlayer.magnitude;
        if (dist > detectionRange)
        {
            // Debug.Log($"{name}: Player too far ({dist:F1}m > {detectionRange}m)");
            return false;
        }

        Vector3 dir = toPlayer.normalized;
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > fovAngle * 0.5f)
        {
            // Debug.Log($"{name}: Outside FOV (angle {angle:F0}° > {fovAngle*0.5f:F0}°)");
            return false;
        }

        if (!requireLineOfSight) return true; // Skip LOS for testing

        Vector3 rayOrigin = transform.position + Vector3.up * eyeHeight;
        bool losClear = true;
        if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, dist, obstacleLayers))
        {
            losClear = hit.collider.transform == playerTransform;
            // Debug.Log($"{name}: LOS HIT {hit.collider.name} (clear: {losClear})");
        }
        return losClear;
    }

    // ... (Rest of methods unchanged: Patrol, Chase, RotateTowardsPlayer, HandleAttack, ShootAtPlayer, etc.)
    private void Patrol()
    {
        if (Time.time > nextWanderTime)
        {
            TryRandomTurn();
            nextWanderTime = Time.time + randomWanderInterval * Random.Range(0.5f, 1.5f);
        }

        if (CheckForwardObstacle())
        {
            AvoidObstacle(false);
        }

        horizontalMove = transform.forward * currentSpeed;
    }

    private void Chase()
    {
        RotateTowardsPlayer();

        if (CheckForwardObstacle())
        {
            AvoidObstacle(true);
        }

        horizontalMove = transform.forward * currentSpeed * 1.2f; // 20% faster when chasing
    }

    private void RotateTowardsPlayer()
    {
        if (playerTransform == null) return;
        Vector3 dir = (playerTransform.position - transform.position).normalized;
        dir.y = 0; // Flat turn
        Quaternion targetRotation = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * 0.02f * Time.deltaTime);
    }

    private void HandleAttack()
    {
        if (Time.time < nextAttackTime) return;
        ShootAtPlayer();
        nextAttackTime = Time.time + attackCooldown;
    }

    private void ShootAtPlayer()
	{
		if (playerTransform == null || fireballPrefab == null) return;

		Vector3 offset = transform.TransformDirection(new Vector3(0f, 1.2f, 0.8f));
		Vector3 spawnPos = transform.position + offset;

		GameObject fb = Instantiate(fireballPrefab, spawnPos, transform.rotation);

		// FIXED: Use rb.velocity (Unity 2022.3) NOT linearVelocity (Unity 6+ only)
		if (fb.TryGetComponent<Rigidbody>(out var rb))
		{
			Vector3 dir = (playerTransform.position - spawnPos).normalized;
			rb.velocity = dir * fireballSpeed;  // ← NOW COMPILES + ULTRA SLOW!
			rb.useGravity = true;  // Optional arc
		}

		// KILL fireball's own movement script (the REAL speed thief)
		MonoBehaviour[] scripts = fb.GetComponents<MonoBehaviour>();
		foreach (var script in scripts)
		{
			string name = script.GetType().Name.ToLower();
			if (name.Contains("fireball") || name.Contains("projectile") || name.Contains("move") || name.Contains("hurts"))
			{
				DestroyImmediate(script);  // Instant kill, no override possible
			}
		}
	}

    private bool CheckForwardObstacle()
    {
        return Physics.SphereCast(transform.position, detectionRadius, transform.forward,
                                  out _, avoidDistance, obstacleLayers);
    }

    private void AvoidObstacle(bool chasing)
    {
        float angle;
        if (chasing && playerTransform != null)
        {
            Vector3 toPlayerFlat = (playerTransform.position - transform.position);
            toPlayerFlat.y = 0f;
            Vector3 toPlayerDir = toPlayerFlat.normalized;
            float cross = Vector3.Cross(transform.forward, toPlayerDir).y;
            angle = Mathf.Sign(cross) * Random.Range(45f, 90f);
        }
        else
        {
            angle = Random.Range(-maxTurnAngle, maxTurnAngle);
        }
        transform.Rotate(0f, angle, 0f);
    }

    private void TryRandomTurn()
    {
        if (Random.value < 0.4f)
        {
            float smallAngle = Random.Range(-30f, 30f);
            transform.Rotate(0f, smallAngle, 0f);
        }
    }

    public void SetAlive(bool alive)
    {
        if (isAlive == alive) return;
        isAlive = alive;
        if (!alive)
        {
            onDeath?.Invoke();
            if (deathEffectPrefab != null)
                Instantiate(deathEffectPrefab, transform.position, transform.rotation);
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
    }

    public void SetSpeedMultiplier(float multiplier)
    {
        SpeedMultiplier = multiplier;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.yellow;
        Vector3 viewLeft = Quaternion.Euler(0, -fovAngle * 0.5f, 0) * transform.forward * detectionRange;
        Vector3 viewRight = Quaternion.Euler(0, fovAngle * 0.5f, 0) * transform.forward * detectionRange;
        Gizmos.DrawRay(transform.position, viewLeft);
        Gizmos.DrawRay(transform.position, viewRight);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * avoidDistance, detectionRadius);

        if (playerTransform != null)
        {
            Gizmos.color = DetectPlayer() ? Color.green : Color.red;
            Vector3 dir = (playerTransform.position - transform.position).normalized;
            Vector3 origin = transform.position + Vector3.up * eyeHeight;
            Gizmos.DrawRay(origin, dir * Mathf.Min(detectionRange, Vector3.Distance(transform.position, playerTransform.position)));
        }
    }
}