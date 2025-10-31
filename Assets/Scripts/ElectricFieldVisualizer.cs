using System;
using System.Collections.Generic;
using UnityEngine;

public class ElectricFieldVisualizer : MonoBehaviour
{
    [Header("Charge Motion")]
    [SerializeField] private bool moveCharges = true;
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [SerializeField] private float angularSpeed = 30f;
    [SerializeField] private Vector3 centerPoint = Vector3.zero;

    [Header("Visualizer Config")]
    [SerializeField] private float simulationScale = 1f;
    [SerializeField] private List<PointCharge> pointCharges;
    [SerializeField] private float lineWidth = 0.05f;
    [SerializeField] private int fieldLinesPerCharge = 64;
    [SerializeField] private int maxSteps = 128;
    [SerializeField] private float traceStepSize = 0.5f;
    [SerializeField] private Color negativeSignColor = Color.cyan;
    [SerializeField] private Color positiveSignColor = new Color(1f, 0.3f, 0.1f);

    [Header("Charge Oscillation")]
    [SerializeField] private bool oscillateCharge = true;
    [SerializeField] private float chargeOscillationSpeed = 1f;

    private const float Permittivity = 8.85e-12f;

    private List<LineRenderer> fieldLines;
    private GameObject fieldLinesParent;
    private float[] initialChargeValues;
    private float[] randomOscillationSeeds;
    private readonly List<GameObject> runtimeChargeMarkers = new List<GameObject>();

    #region Unity Callbacks

    private void Start()
    {
        GenerateFieldLines();
        DrawPointChargesRuntime();

        initialChargeValues = new float[pointCharges.Count];
        randomOscillationSeeds = new float[pointCharges.Count];

        for (int i = 0; i < pointCharges.Count; i++)
        {
            initialChargeValues[i] = pointCharges[i].Charge;
            randomOscillationSeeds[i] = UnityEngine.Random.Range(0f, 1000f); // random noise offset
        }
    }

    private void Update()
    {
        if (pointCharges == null || pointCharges.Count == 0) return;

        if (moveCharges) MoveChargesRK4(Time.deltaTime);
        if (oscillateCharge) OscillateChargeValues();

        GenerateFieldLines();

        if (Time.frameCount % 10 == 0)
            DrawPointChargesRuntime();
    }

    private void OnDestroy()
    {
        if (fieldLinesParent != null)
        {
            if (Application.isPlaying) Destroy(fieldLinesParent);
            else DestroyImmediate(fieldLinesParent);
        }
    }

    private void OnDrawGizmos()
    {
        DrawBoundingCube();
        DrawPointCharges();
    }

    #endregion

    #region Charge Motion

    private void MoveChargesRK4(float dt)
    {
        for (int i = 0; i < pointCharges.Count; i++)
        {
            PointCharge charge = pointCharges[i];

            Func<Vector3, Vector3> velocityFunc = pos =>
            {
                Vector3 r = pos - centerPoint;
                return Vector3.Cross(rotationAxis.normalized, r).normalized * (angularSpeed * Mathf.Deg2Rad);
            };

            Vector3 p = charge.Position;
            Vector3 k1 = velocityFunc(p);
            Vector3 k2 = velocityFunc(p + 0.5f * dt * k1);
            Vector3 k3 = velocityFunc(p + 0.5f * dt * k2);
            Vector3 k4 = velocityFunc(p + dt * k3);

            charge.Position += (dt / 6f) * (k1 + 2 * k2 + 2 * k3 + k4);
        }
    }

    #endregion

    #region Field Line Generation

    private void GenerateFieldLines()
    {
        ClearFieldLines();
        if (pointCharges == null || pointCharges.Count == 0) return;

        fieldLinesParent = new GameObject("FieldLines");
        fieldLinesParent.transform.SetParent(transform);
        fieldLinesParent.transform.localPosition = Vector3.zero;
        fieldLines = new List<LineRenderer>();

        foreach (var charge in pointCharges)
        {
            if (charge.Charge <= 0) continue;

            for (int i = 0; i < fieldLinesPerCharge; i++)
            {
                float phi = Mathf.Acos(1 - 2 * (i + 0.5f) / fieldLinesPerCharge);
                float theta = Mathf.PI * (1 + Mathf.Sqrt(5)) * i;

                Vector3 startDir = new Vector3(
                    Mathf.Sin(phi) * Mathf.Cos(theta),
                    Mathf.Sin(phi) * Mathf.Sin(theta),
                    Mathf.Cos(phi)
                );

                TraceFieldLine(charge.Position, startDir, charge.Charge);
            }
        }
    }

    private void OscillateChargeValues()
    {
        if (pointCharges == null || pointCharges.Count == 0 || initialChargeValues == null) return;

        float t = Time.time * chargeOscillationSpeed;

        for (int i = 0; i < pointCharges.Count; i++)
        {
            float baseCharge = initialChargeValues[i];
            float sign = Mathf.Sign(baseCharge);
            float magnitude = Mathf.Abs(baseCharge);

            float minMag = Mathf.Max(0.1f, magnitude * 0.05f);
            float maxMag = magnitude;

            float noiseValue = Mathf.PerlinNoise(randomOscillationSeeds[i], t);
            float smoothValue = Mathf.Lerp(minMag, maxMag, noiseValue);

            pointCharges[i].Charge = smoothValue * sign;
        }
    }

    private void TraceFieldLine(Vector3 startPos, Vector3 initialDirection, float chargeSign)
    {
        List<Vector3> points = new List<Vector3>();
        Vector3 currentPos = startPos + initialDirection * 0.1f;
        float h = traceStepSize;
        bool hitOppositeCharge = false;

        for (int step = 0; step < maxSteps; step++)
        {
            points.Add(transform.position + currentPos);

            Vector3 k1 = CalculateElectricFieldAt(currentPos).normalized;
            Vector3 k2 = CalculateElectricFieldAt(currentPos + 0.5f * h * k1).normalized;
            Vector3 k3 = CalculateElectricFieldAt(currentPos + 0.5f * h * k2).normalized;
            Vector3 k4 = CalculateElectricFieldAt(currentPos + h * k3).normalized;

            Vector3 delta = (h / 6f) * (k1 + 2 * k2 + 2 * k3 + k4);
            currentPos += delta;

            if (delta.magnitude < 0.001f) break;

            foreach (var charge in pointCharges)
            {
                if (charge.Charge * chargeSign < 0 && Vector3.Distance(currentPos, charge.Position) < 1f)
                {
                    points.Add(transform.position + charge.Position);
                    hitOppositeCharge = true;
                    break;
                }
            }

            if (hitOppositeCharge) break;
        }

        if (points.Count < 2) return;

        GameObject lineObj = new GameObject($"FieldLine_{fieldLines.Count}");
        lineObj.transform.SetParent(fieldLinesParent.transform);
        lineObj.transform.localPosition = Vector3.zero;

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());
        lr.startWidth = lr.endWidth = lineWidth;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.useWorldSpace = true;

        Gradient gradient = new Gradient();
        Color startColor = chargeSign > 0 ? positiveSignColor : negativeSignColor;
        Color endColor = hitOppositeCharge
            ? (chargeSign > 0 ? negativeSignColor : positiveSignColor)
            : startColor;

        gradient.SetKeys(
            new[] { new GradientColorKey(startColor, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) }
        );

        lr.colorGradient = gradient;
        fieldLines.Add(lr);
    }

    private void ClearFieldLines()
    {
        if (fieldLinesParent != null)
        {
            if (Application.isPlaying) Destroy(fieldLinesParent);
            else DestroyImmediate(fieldLinesParent);
        }
    }

    private void DrawPointChargesRuntime()
    {
        foreach (var marker in runtimeChargeMarkers)
            if (marker != null) Destroy(marker);

        runtimeChargeMarkers.Clear();

        foreach (var charge in pointCharges)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"ChargeMarker_{charge.GetHashCode()}";
            marker.transform.SetParent(transform);
            marker.transform.localPosition = charge.Position;
            marker.transform.localScale = Vector3.one * 0.5f;

            var renderer = marker.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Unlit/Color"));
            renderer.material.color = charge.Charge > 0
                ? positiveSignColor
                : charge.Charge < 0
                    ? negativeSignColor
                    : Color.white;

            runtimeChargeMarkers.Add(marker);
        }
    }

    #endregion

    #region Electric Field Calculation

    private Vector3 CalculateElectricFieldAt(Vector3 position)
    {
        Vector3 eField = Vector3.zero;

        foreach (var charge in pointCharges)
        {
            Vector3 r = position - charge.Position;
            float distSqr = r.sqrMagnitude + 0.001f;
            eField += (charge.Charge / (4 * Mathf.PI * Permittivity)) * r.normalized / distSqr;
        }

        return eField;
    }

    #endregion

    #region Gizmos

    private void DrawBoundingCube()
    {
        if (pointCharges == null || pointCharges.Count == 0) return;

        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;

        foreach (var c in pointCharges)
        {
            min = Vector3.Min(min, c.Position);
            max = Vector3.Max(max, c.Position);
        }

        Vector3 cubeCenter = (min + max) * 0.5f + transform.position;
        Vector3 cubeSize = (max - min) + Vector3.one * 2f; // small padding

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(cubeCenter, cubeSize);
    }

    private void DrawPointCharges()
    {
        if (pointCharges == null) return;

        foreach (var charge in pointCharges)
        {
            Gizmos.color = charge.Charge > 0 ? Color.red :
                charge.Charge < 0 ? Color.blue : Color.white;
            Gizmos.DrawSphere(transform.position + charge.Position, 0.5f);
        }
    }

    #endregion
}
