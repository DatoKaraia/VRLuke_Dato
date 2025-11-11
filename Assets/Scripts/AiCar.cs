using System.Collections;
using UnityEngine;
using UnityEngine.AI;
public class AiCar : MonoBehaviour
{
    public Transform[] patrolPoints;           // patrol points (set in inspector)
    public bool loopPatrol = true;

    [Header("Speeds")]
    public float patrolSpeed = 6f;
    public float chaseSpeed = 10f;
    public float ramSpeedMultiplier = 2.5f;    // multiplier for ram force

    [Header("Detection / Ram")]
    public float detectionRadius = 25f;        // detection distance at which chasing begins
    public float ramDistance = 8f;             // distance to player that triggers a ram
    public float ramDuration = 1.0f;           // duration of the physical ram (seconds)
    public float ramCooldownTime = 3.0f;       // cooldown between rams (seconds)

    [Header("Physics ram settings")]
    public ForceMode ramForceMode = ForceMode.VelocityChange;
    public float baseRamForce = 20f;           // base force used in calculation

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

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
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
                // ничего в Update — управляется корутиной
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
        // while cooling down — either chase slowly or return to patrol
        if (distToPlayer <= detectionRadius)
        {
            currentState = State.Chase;
            agent.speed = chaseSpeed * 0.6f; // чуть медленнее пока остывает
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

    rb.AddForce(dir * ramForce, ramForceMode);

    float t = 0f;
    while (t < ramDuration)
    {
        Vector3 vel = rb.linearVelocity;
        if (vel.sqrMagnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(new Vector3(vel.x, 0, vel.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 6f);
        }

        t += Time.deltaTime;
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

    currentState = State.Recover;
}
    void OnCollisionEnter(Collision collision)
    {
        // Если таранил игрока — пробуем вызвать метод TakeDamage или иной обработчик (опционально)
        if (collision.transform == player)
        {
            // пытаемся вызвать метод на игроке, если он есть
            var comp = player.GetComponent<MonoBehaviour>();
            if (comp != null && !string.IsNullOrEmpty(damageableMethodName))
            {
                // безопасный вызов (если метод существует)
                var mi = comp.GetType().GetMethod(damageableMethodName);
                if (mi != null)
                {
                    mi.Invoke(comp, new object[] { 10f }); // пример: отсылаем 10 урона
                }
            }
        }
    }

    // Рисуем радиус в инспекторе, чтобы удобнее было настроить
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, ramDistance);
    }
}
