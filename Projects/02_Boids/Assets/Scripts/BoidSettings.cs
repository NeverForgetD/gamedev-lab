using System;
using UnityEngine;

[Serializable]
public class BoidSettings
{
    [Header("Speed")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;

    [Header("Perception")]
    public float perceptionRadius = 2.5f;
    public float separationRadius = 1f;

    [Header("Weights")]
    public float separationWeight = 1f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;

    [Header("Steering")]
    public float maxSteerForce = 3f;
}
