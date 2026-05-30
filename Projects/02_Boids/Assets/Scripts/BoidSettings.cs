using System;
using UnityEngine;

[Serializable]
public class BoidSettings
{
    [Header("Speed")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;

    [Header("Perception")]
    public float perceptionRadius = 3f;
    public float separationRadius = 1.5f;

    [Header("Weights")]
    public float separationWeight = 1.5f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;
}
