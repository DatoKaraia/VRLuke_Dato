using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SocialPlatforms.Impl;

public class AiCar : MonoBehaviour
{

    [SerializeField]
     ScoreSystem scoreSystem;

    public Transform[] patrolPoints;           // patrol points (set in inspector)
    public bool loopPatrol = true;

    [Header("Speeds")]
    public float patrolSpeed = 6f;
    public float chaseSpeed = 10f;
    public float ramSpeedMultiplier = 2.5f;    // multiplier for ram force

    [Header("Detection / Ram")]
    public float detectionRadius = 25f;        // detection distance at which chasing begins
    public float ramDistance = 8f;             // distance to player that triggers a ram
    public float ramDuration = 1.0f;           // duration of the active ram (seconds)
    public float ramCooldownTime = 3.0f;       // cooldown between rams (seconds)

    [Header("Physics ram settings")]
    public ForceMode ramForceMode = ForceMode.VelocityChange;
    public float baseRamForce = 20f;           // base force used in calculation

    [Header("Post-Ram Behaviour")]
    [Tooltip("How long after the ram the car keeps sliding with physics and AI logic is OFF.")]
    public float postRamInertiaTime = 10f;     // время, когда физика катается сама по себе

    [Tooltip("How long the AI backs away from the player after recovering.")]
    public float backOffDuration = 1f;         // сколько секунд отъезжать назад

    [Tooltip("How far away from the player the car tries to move when backing off.")]
    public float backOffDistance = 5f;         // дистанция отъезда назад

    [Header("References (auto-find if not assigned)")]
    public Transform player;                   // can be assigned manually; otherwise found by tag "Player"

    // Optional: method name for applying damage on collision
    public string damageableMethodName = "TakeDamage"; // will invoke this method on the player object when ramming, if present

    // internal fields
    NavMeshAgent agent;
    Rigidbody rb;
    int patrolIndex = 0;
    float ramCooldown = 0f;

    enum State { Patrol, Chase, Ram, Recover }
    State currentState = State.Patrol;

    // post-ram / backoff internal data
    float backOffTimer = 0f;
    Vector3 backOffTarget;

    // colliders for anti-sticky logic
    Collider myCollider;
    Collider playerCollider;
    bool isIgnoringPlayer = false;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();
        myCollider = GetComponent<Collider>();

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }

        if (player != null)
        {
            playerCollider = player.GetComponent<Collider>();
        }

        // Setup: use NavMeshAgent for movement; keep Rigidbody kinematic for now
        agent.updateRotation = true;
        agent.updateUpAxis = true;
        rb.isKinematic = true;
    }

    void Start()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Debug.LogWarning($"[{name}] Patrol points not set. Enemy will stay put.");
            agent.isStopped = true;
            enabled = false;
            return;
        }

        agent.speed = patrolSpeed;
        agent.SetDestination(patrolPoints[patrolIndex].position);
    }

    void Update()
    {
        if (player == null)
            return;

        // cooldown timer
        if (ramCooldown > 0f) ramCooldown -= Time.deltaTime;

        float distToPlayer = Vector3.Distance(transform.position, player.position);

        switch (currentState)
        {
            case State.Patrol:
                PatrolUpdate(distToPlayer);
                break;
            case State.Chase:
                ChaseUpdate(distToPlayer);
                break;
            case State.Recover:
                RecoverUpdate(distToPlayer);
                break;
            case State.Ram:
                // no logic here – controlled by coroutine
                break;
        }
    }

    void PatrolUpdate(float distToPlayer)
    {
        // if the player is detected — switch to chase
        if (distToPlayer <= detectionRadius)
        {
            currentState = State.Chase;
            agent.speed = chaseSpeed;
            return;
        }

        // move between patrol points
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.5f)
        {
            NextPatrolPoint();
        }
    }

    void NextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;
        patrolIndex++;
        if (patrolIndex >= patrolPoints.Length)
        {
            if (loopPatrol) patrolIndex = 0;
            else
            {
                patrolIndex = patrolPoints.Length - 1;
                agent.isStopped = true;
                return;
            }
        }
        agent.SetDestination(patrolPoints[patrolIndex].position);
    }

    void ChaseUpdate(float distToPlayer)
    {
        // follow the player
        agent.SetDestination(player.position);

        // if close enough and off cooldown — start ramming
        if (distToPlayer <= ramDistance && ramCooldown <= 0f)
        {
            StartCoroutine(RamRoutine());
        }
        else if (distToPlayer > detectionRadius * 1.2f) // if the player has fled far away — return to patrol
        {
            currentState = State.Patrol;
            agent.speed = patrolSpeed;
            agent.SetDestination(patrolPoints[patrolIndex].position);
        }
    }

    void RecoverUpdate(float distToPlayer)
    {
        // BACK-OFF PHASE: отъезжаем назад от игрока
        if (backOffTimer > 0f)
        {
            backOffTimer -= Time.deltaTime;

            // если уже почти доехали до цели — просто ждём остаток времени
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                // стоим, пока таймер не дойдёт до нуля
            }

            if (backOffTimer <= 0f)
            {
                // Backoff finished → возвращаемся к старой логике
                if (distToPlayer <= detectionRadius)
                {
                    currentState = State.Chase;
                    agent.speed = chaseSpeed;
                }
                else
                {
                    currentState = State.Patrol;
                    agent.speed = patrolSpeed;
                    agent.SetDestination(patrolPoints[patrolIndex].position);
                }
            }

            return;
        }

        // safety fallback, если по какой-то причине попали в Recover без backOffTimer
        if (distToPlayer <= detectionRadius)
        {
            currentState = State.Chase;
            agent.speed = chaseSpeed;
        }
        else
        {
            currentState = State.Patrol;
            agent.speed = patrolSpeed;
            agent.SetDestination(patrolPoints[patrolIndex].position);
        }
    }

    IEnumerator RamRoutine()
    {
        currentState = State.Ram;
        ramCooldown = ramCooldownTime;

        // disable NavMeshAgent and enable physics
        agent.isStopped = true;
        agent.ResetPath();
        agent.enabled = false;

        rb.isKinematic = false;

        // direction to the player (horizontal only)
        Vector3 dir = (player.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
        dir.Normalize();

        float agentSpeedEstimate = chaseSpeed;
        float ramForce = baseRamForce * ramSpeedMultiplier + agentSpeedEstimate * ramSpeedMultiplier;

        // push toward the player
        rb.AddForce(dir * ramForce, ramForceMode);

        // total time: active ram + residual inertia
        float totalTime = 0f;
        float totalDuration = ramDuration + postRamInertiaTime;

        while (totalTime < totalDuration)
        {
            Vector3 vel = rb.linearVelocity;
            if (vel.sqrMagnitude > 0.1f)
            {
                // align visual rotation with movement direction
                Quaternion targetRot = Quaternion.LookRotation(new Vector3(vel.x, 0, vel.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 6f);
            }

            totalTime += Time.deltaTime;
            yield return null;
        }

        // stop physics
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        // try to return the agent to the NavMesh
        NavMeshHit hit;
        bool onMesh = NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas);

        agent.enabled = true;

        if (onMesh)
        {
            // warp exactly onto the NavMesh
            agent.Warp(hit.position);
            agent.ResetPath();
            agent.isStopped = false;
        }
        else
        {
            Debug.LogWarning($"[{name}] Couldn't return AiCar to NavMesh after ram. It's positioned off the NavMesh.");
            // in that case at least don't crash:
            agent.isStopped = true;
        }

        // SETUP BACKOFF
        SetupBackOff();

        currentState = State.Recover;
    }

    void SetupBackOff()
    {
        if (player != null)
        {
            // направление ОТ игрока
            Vector3 away = (transform.position - player.position);
            away.y = 0f;
            if (away.sqrMagnitude < 0.1f) away = -transform.forward; // на всякий
            away.Normalize();

            backOffTarget = transform.position + away * backOffDistance;
        }
        else
        {
            // нет игрока? просто откатываемся назад по своей оси
            backOffTarget = transform.position - transform.forward * backOffDistance;
        }

        backOffTimer = backOffDuration;

        if (agent.enabled)
        {
            agent.speed = patrolSpeed; // можно и chaseSpeed, но без фанатизма
            agent.SetDestination(backOffTarget);
        }
    }

    IEnumerator TemporaryIgnorePlayerCollision()
    {
        if (myCollider == null || playerCollider == null)
            yield break;

        if (isIgnoringPlayer)
            yield break;

        isIgnoringPlayer = true;
        Physics.IgnoreCollision(myCollider, playerCollider, true);

        // игнорим коллизию, пока идёт инерция
        yield return new WaitForSeconds(postRamInertiaTime);

        Physics.IgnoreCollision(myCollider, playerCollider, false);
        isIgnoringPlayer = false;
    }

    void OnCollisionEnter(Collision collision)
    {

        scoreSystem.AddCarDamaged(1);
        // ударились в игрока
        if (collision.transform == player)
        {
            // если это таран — выключаем "липкость"
            if (currentState == State.Ram)
            {
                StartCoroutine(TemporaryIgnorePlayerCollision());
            }

            // пытаемся вызвать метод на игроке, если он есть
            var comp = player.GetComponent<MonoBehaviour>();
            if (comp != null && !string.IsNullOrEmpty(damageableMethodName))
            {
                var mi = comp.GetType().GetMethod(damageableMethodName);
                if (mi != null)
                {
                    mi.Invoke(comp, new object[] { 10f }); // пример: отсылаем 10 урона
                }
            }
        }
    }

    // Draw detection gizmos
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, ramDistance);
    }
}
