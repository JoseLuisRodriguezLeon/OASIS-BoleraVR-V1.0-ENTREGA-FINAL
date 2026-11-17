using System.Collections;
using UnityEngine;

/// <summary>
/// Animación de chuza (strike): explosión de confeti de colores sobre los pinos y frente al
/// jugador, más una vibración en ambos controles. En un spare hace una versión más pequeña.
/// Se suma al texto "¡STRIKE!" del marcador (GameUI) y al sonido (BowlingAudio).
/// Crea su propio sistema de partículas: solo hay que asignar las referencias y un material.
/// </summary>
public class StrikeCelebration : MonoBehaviour
{
    [Header("Referencias")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PinSetter pinSetter;
    [Tooltip("Opcional: punto cercano al jugador donde también sale confeti (por ejemplo, Marcador)")]
    [SerializeField] private Transform nearPoint;
    [Tooltip("Material de partículas SIN textura (URP/Particles/Unlit, Base Map vacío): cada papelito es un cuadrado")]
    [SerializeField] private Material confettiMaterial;

    [Header("Cantidad")]
    [Tooltip("Papelitos en cada punto cuando hay strike")]
    [SerializeField] private int strikeConfetti = 120;
    [Tooltip("Papelitos en cada punto cuando hay spare (0 = sin confeti en spare)")]
    [SerializeField] private int spareConfetti = 40;
    [Tooltip("Altura sobre los pinos donde explota el confeti (m)")]
    [SerializeField] private float heightOverPins = 1f;

    [Header("Movimiento")]
    [Tooltip("Velocidad de la explosión (m/s), mínimo y máximo")]
    [SerializeField] private Vector2 burstSpeed = new Vector2(1.5f, 4f);
    [Tooltip("Qué tan rápido cae el confeti (1 = gravedad normal)")]
    [SerializeField] private float fallGravity = 0.25f;
    [Tooltip("Segundos que dura cada papelito, mínimo y máximo")]
    [SerializeField] private Vector2 lifetime = new Vector2(2.5f, 3.5f);
    [Tooltip("Tamaño de cada papelito (m), mínimo y máximo")]
    [SerializeField] private Vector2 size = new Vector2(0.03f, 0.06f);

    [Header("Colores")]
    [SerializeField] private Color[] colors =
    {
        new Color(1f, 0.85f, 0.2f),   // amarillo
        new Color(1f, 0.35f, 0.35f),  // rojo
        new Color(0.3f, 0.75f, 1f),   // azul
        new Color(0.4f, 0.9f, 0.45f), // verde
        new Color(0.85f, 0.45f, 1f),  // morado
        Color.white
    };

    [Header("Vibración")]
    [SerializeField] private float strikeHapticAmplitude = 0.6f;
    [SerializeField] private float strikeHapticDuration = 0.35f;

    private ParticleSystem confetti;
    private int lastRollCount;

    private void Awake()
    {
        if (gameManager == null || pinSetter == null)
        {
            Debug.LogError("StrikeCelebration: faltan referencias (Game Manager o Pin Setter).", this);
            enabled = false;
            return;
        }

        if (confettiMaterial == null)
        {
            Debug.LogWarning("StrikeCelebration: falta el material del confeti; se verá rosado.", this);
        }

        CreateParticleSystem();
    }

    private void OnEnable()
    {
        if (gameManager != null) gameManager.ScoreChanged += OnScoreChanged;
    }

    private void OnDisable()
    {
        if (gameManager != null) gameManager.ScoreChanged -= OnScoreChanged;
    }

    private void CreateParticleSystem()
    {
        var go = new GameObject("Confeti");
        go.transform.SetParent(transform, false);

        confetti = go.AddComponent<ParticleSystem>();
        confetti.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = confetti.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = fallGravity;
        main.maxParticles = Mathf.Max(strikeConfetti, spareConfetti) * 2 + 50;
        main.startRotation3D = false;

        // Solo se emite a mano (Emit), sin emisión automática.
        ParticleSystem.EmissionModule emission = confetti.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = confetti.shape;
        shape.enabled = false;

        // Frenan con el aire para que floten al caer.
        ParticleSystem.LimitVelocityOverLifetimeModule limit = confetti.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1.2f;
        limit.dampen = 0.15f;

        // Giran mientras caen.
        ParticleSystem.RotationOverLifetimeModule rotation = confetti.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        // Se desvanecen al final.
        ParticleSystem.ColorOverLifetimeModule color = confetti.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = confettiMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---------- Detectar strike y spare ----------

    private void OnScoreChanged()
    {
        BowlingScore score = gameManager.Score;
        if (score == null)
        {
            lastRollCount = 0;
            return;
        }

        int rolls = 0;
        for (int i = 0; i < score.TotalFrames; i++)
        {
            rolls += score.GetRolls(i).Count;
        }

        if (rolls > lastRollCount)
        {
            int frame = score.CurrentFrameIndex;
            if (score.GetRolls(frame).Count == 0 && frame > 0) frame--;

            string[] marks = score.GetFrameMarks(frame);
            if (marks.Length > 0)
            {
                string last = marks[marks.Length - 1];
                if (last == "X") PlayStrike();
                else if (last == "/") PlaySpare();
            }
        }

        lastRollCount = rolls;
    }

    // ---------- Efectos ----------

    /// <summary>Lanza la celebración de strike. Público para poder probarla desde el Inspector.</summary>
    [ContextMenu("Probar strike")]
    public void PlayStrike()
    {
        Burst(PinsPoint(), strikeConfetti);
        if (nearPoint != null) Burst(nearPoint.position, strikeConfetti / 2);

        StartCoroutine(HapticPulse(strikeHapticAmplitude, strikeHapticDuration));
    }

    [ContextMenu("Probar spare")]
    public void PlaySpare()
    {
        if (spareConfetti <= 0) return;
        Burst(PinsPoint(), spareConfetti);
        StartCoroutine(HapticPulse(strikeHapticAmplitude * 0.5f, strikeHapticDuration * 0.5f));
    }

    private Vector3 PinsPoint()
    {
        return pinSetter.transform.position + Vector3.up * heightOverPins;
    }

    private void Burst(Vector3 center, int count)
    {
        if (confetti == null || count <= 0) return;

        var emitParams = new ParticleSystem.EmitParams { applyShapeToPosition = false };

        for (int i = 0; i < count; i++)
        {
            // Explota hacia arriba y hacia los lados, como un cañón de confeti.
            Vector3 direction = Random.onUnitSphere;
            direction.y = Mathf.Abs(direction.y) * 1.5f + 0.3f;

            emitParams.position = center + Random.insideUnitSphere * 0.15f;
            emitParams.velocity = direction.normalized * Random.Range(burstSpeed.x, burstSpeed.y);
            emitParams.startSize = Random.Range(size.x, size.y);
            emitParams.startLifetime = Random.Range(lifetime.x, lifetime.y);
            emitParams.rotation = Random.Range(0f, 360f);
            emitParams.startColor = colors.Length > 0 ? colors[Random.Range(0, colors.Length)] : Color.white;

            confetti.Emit(emitParams, 1);
        }

        if (!confetti.isPlaying) confetti.Play();
    }

    private IEnumerator HapticPulse(float amplitude, float duration)
    {
        OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.RTouch);
        yield return new WaitForSeconds(duration);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
    }
}