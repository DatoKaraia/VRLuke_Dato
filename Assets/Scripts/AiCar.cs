using System.Collections;
using UnityEngine;
using UnityEngine.AI;
public class AiCar : MonoBehaviour
{
   public Transform[] patrolPoints;           // точки патруля (в инспекторе)
    public bool loopPatrol = true;

    [Header("Speeds")]
    public float patrolSpeed = 6f;
    public float chaseSpeed = 10f;
    public float ramSpeedMultiplier = 2.5f;    // умножитель для силы удара

    [Header("Detection / Ram")]
    public float detectionRadius = 25f;        // когда начинает гнаться
    public float ramDistance = 8f;             // дистанция до игрока для рывка
    public float ramDuration = 1.0f;           // время физического тарана (сек)
    public float ramCooldownTime = 3.0f;       // перезарядка между таранчиками (сек)

    [Header("Physics ram settings")]
    public ForceMode ramForceMode = ForceMode.VelocityChange;
    public float baseRamForce = 20f;           // базовая сила для расчёта

    [Header("References (auto-find если не указаны)")]
    public Transform player;                   // можно указать вручную, иначе найдёт по тегу "Player"

    // Optional: interface name для нанесения урона при столкновении
    public string damageableMethodName = "TakeDamage"; // вызовет метод на объекте игрока при таране, если есть

    // внутренности
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

        // Подготовка: используем NavMeshAgent для перемещений, Rigidbody в кинематике пока
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

        // таймер перезарядки
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
        // если увидел — переключаемся в погоню
        if (distToPlayer <= detectionRadius)
        {
            currentState = State.Chase;
            agent.speed = chaseSpeed;
            return;
        }

        // движение между точками
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

        // если близко и есть заряд — рвёмся таранить
        if (distToPlayer <= ramDistance && ramCooldown <= 0f)
        {
            StartCoroutine(RamRoutine());
        }
        else if (distToPlayer > detectionRadius * 1.2f) // если игрок удирает далеко — возвращаемся к патрулю
        {
            currentState = State.Patrol;
            agent.speed = patrolSpeed;
            agent.SetDestination(patrolPoints[patrolIndex].position);
        }
    }

    void RecoverUpdate(float distToPlayer)
    {
        // пока в кулдауне — либо гони за игроком (медленно), либо возвращаемся на патруль
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

        // выключаем NavMeshAgent и включаем физический режим
        agent.isStopped = true;
        agent.enabled = false;

        rb.isKinematic = false;

        // направление к игроку (по горизонтали)
        Vector3 dir = (player.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
        dir.Normalize();

        // рассчёт силы: базовая + модификатор от текущей скорости агента
        float agentSpeedEstimate = chaseSpeed; // можно более точно, но хватит
        float ramForce = baseRamForce * ramSpeedMultiplier + agentSpeedEstimate * ramSpeedMultiplier;

        // даём резкий импульс
        rb.AddForce(dir * ramForce, ramForceMode);

        // включаем ротацию в направлении движения (чтобы выглядело красиво)
        float t = 0f;
        while (t < ramDuration)
        {
            // стремимся повернуть тело в направлении скорости (если есть скорость)
            Vector3 vel = rb.linearVelocity;
            if (vel.sqrMagnitude > 0.1f)
            {
                Quaternion targetRot = Quaternion.LookRotation(new Vector3(vel.x, 0, vel.z));
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 6f);
            }

            t += Time.deltaTime;
            yield return null;
        }

        // тормозим машину аккуратно
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // выключаем физику, включаем агент снова
        rb.isKinematic = true;
        agent.enabled = true;
        agent.isStopped = false;

        // после тарана — восстанавливаемся
        currentState = State.Recover;

        yield break;
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
