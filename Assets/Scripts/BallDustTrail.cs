using UnityEngine;

/// <summary>
/// Nubes de polvo que salen de la bola mientras rueda por la pista.
/// Crea su propio sistema de partículas al iniciar: solo hay que asignar la bola y un material.
/// - Solo emite cuando la bola fue lanzada y está tocando el piso.
/// - Cuanto más rápida va la bola, más nubes suelta.
/// - Las nubes se quedan en el lugar donde salieron, crecen un poco, suben y se desvanecen.
/// Pensado para Meta Quest: pocas partículas, sin sombras y con material sin iluminación.
/// </summary>
public class BallDustTrail : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private BallLauncher ball;
    [Tooltip("Material de partículas: shader Universal Render Pipeline/Particles/Unlit, transparente")]
    [SerializeField] private Material dustMaterial;

    [Header("Cantidad")]
    [Tooltip("Nubes por cada metro que recorre la bola")]
    [SerializeField] private float cloudsPerMeter = 6f;
    [Tooltip("Por debajo de esta velocidad (m/s) no sale polvo")]
    [SerializeField] private float minSpeed = 0.5f;
    [Tooltip("Máximo de nubes vivas al mismo tiempo (cuida el rendimiento)")]
    [SerializeField] private int maxClouds = 150;

    [Header("Apariencia")]
    [SerializeField] private Color cloudColor = new Color(0.95f, 0.92f, 0.86f, 0.55f);
    [Tooltip("Tamaño inicial de cada nube (m), mínimo y máximo")]
    [SerializeField] private Vector2 cloudSize = new Vector2(0.08f, 0.16f);
    [Tooltip("Cuántas veces crece la nube durante su vida")]
    [SerializeField] private float growth = 2.2f;
    [Tooltip("Segundos que dura cada nube, mínimo y máximo")]
    [SerializeField] private Vector2 cloudLifetime = new Vector2(0.7f, 1.3f);
    [Tooltip("Qué tanto suben las nubes (negativo = suben)")]
    [SerializeField] private float riseGravity = -0.03f;

    [Header("Detección del piso")]
    [Tooltip("Radio de la bola (m)")]
    [SerializeField] private float ballRadius = 0.108f;
    [Tooltip("Margen extra para considerar que la bola toca el piso (m)")]
    [SerializeField] private float groundMargin = 0.05f;

    private ParticleSystem dust;
    private ParticleSystem.EmissionModule emission;
    private Transform emitter;

    private void Awake()
    {
        if (ball == null)
        {
            Debug.LogError("BallDustTrail: falta asignar la bola (Ball).", this);
            enabled = false;
            return;
        }

        if (dustMaterial == null)
        {
            Debug.LogWarning("BallDustTrail: falta el material del polvo; las nubes se verán rosadas.", this);
        }

        CreateParticleSystem();
    }

    private void CreateParticleSystem()
    {
        // El emisor es un objeto aparte (no hijo de la bola), para que no gire con ella.
        var go = new GameObject("Polvo de la bola");
        go.transform.SetParent(transform, false);
        emitter = go.transform;

        dust = go.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = dust.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // las nubes quedan atrás
        main.startLifetime = new ParticleSystem.MinMaxCurve(cloudLifetime.x, cloudLifetime.y);
        main.startSize = new ParticleSystem.MinMaxCurve(cloudSize.x, cloudSize.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = cloudColor;
        main.gravityModifier = riseGravity;
        main.maxParticles = maxClouds;

        emission = dust.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        // Salen de una media esfera pequeña en el punto donde la bola toca la pista.
        ParticleSystem.ShapeModule shape = dust.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.06f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // la media esfera apunta hacia arriba

        // Crecen mientras viven.
        ParticleSystem.SizeOverLifetimeModule size = dust.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, growth));

        // Aparecen suave y se desvanecen.
        ParticleSystem.ColorOverLifetimeModule color = dust.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        // Se frenan poco a poco y giran despacio, para que parezcan nubes.
        ParticleSystem.LimitVelocityOverLifetimeModule limit = dust.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 0.3f;
        limit.dampen = 0.2f;

        ParticleSystem.RotationOverLifetimeModule rotation = dust.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = dustMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.maxParticleSize = 0.5f;

        dust.Play();
    }

    private void LateUpdate()
    {
        if (dust == null) return;

        Vector3 ballPos = ball.transform.position;

        // El emisor sigue el punto de contacto de la bola con la pista.
        emitter.position = ballPos + Vector3.down * ballRadius;

        bool active = ball.gameObject.activeInHierarchy && ball.IsLaunched && ball.Speed >= minSpeed && IsOnGround(ballPos);

        // Más velocidad = más nubes (nubes por segundo = velocidad × nubes por metro).
        emission.rateOverTime = active ? ball.Speed * cloudsPerMeter : 0f;
    }

    private bool IsOnGround(Vector3 ballPos)
    {
        return Physics.Raycast(ballPos, Vector3.down, ballRadius + groundMargin, ~0, QueryTriggerInteraction.Ignore);
    }
}