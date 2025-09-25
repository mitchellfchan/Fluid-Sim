using UnityEngine;
using Seb.Fluid.Simulation;

public class CollisionObjectTester : MonoBehaviour
{
    [Header("Test Objects")]
    public FluidSim fluidSim;
    public Transform testSphere1;
    public Transform testSphere2;
    public Transform testBox1;
    public Transform testBox2;
    
    [Header("Test Settings")]
    public bool addObjectsOnStart = true;
    public bool enableDebugInfo = true;

    void Start()
    {
        if (addObjectsOnStart && fluidSim != null)
        {
            StartCoroutine(AddTestObjectsDelayed());
        }
    }

    System.Collections.IEnumerator AddTestObjectsDelayed()
    {
        // Wait for fluid sim to initialize
        yield return new WaitForSeconds(0.5f);
        
        AddTestObjects();
    }

    [ContextMenu("Add Test Objects")]
    public void AddTestObjects()
    {
        if (fluidSim == null) 
        {
            Debug.LogError("FluidSim reference not set!");
            return;
        }

        Debug.Log("🧪 Adding test collision objects...");

        // Test Sphere 1: Bouncy Ball
        if (testSphere1 != null)
        {
            var bouncySphere = CollisionObjectExtensions.CreateSphere(testSphere1, 1.0f, 1.0f);
            bouncySphere.bounciness = 0.9f;        // Very bouncy
            bouncySphere.friction = 0.95f;         // Low friction
            bouncySphere.enableMomentumTransfer = true;
            
            if (fluidSim.AddCollisionObject(bouncySphere))
            {
                Debug.Log("✅ Added bouncy sphere!");
            }
        }

        // Test Sphere 2: Heavy Sphere
        if (testSphere2 != null)
        {
            var heavySphere = CollisionObjectExtensions.CreateSphere(testSphere2, 0.8f, 5.0f);
            heavySphere.bounciness = 0.3f;         // Less bouncy
            heavySphere.friction = 0.7f;           // More friction
            heavySphere.enableMomentumTransfer = true;
            
            if (fluidSim.AddCollisionObject(heavySphere))
            {
                Debug.Log("✅ Added heavy sphere!");
            }
        }

        // Test Box 1: Static Box
        if (testBox1 != null)
        {
            var staticBox = CollisionObjectExtensions.CreateBox(testBox1, Vector3.one, 10.0f);
            staticBox.bounciness = 0.5f;           // Medium bounce
            staticBox.friction = 0.8f;             // High friction
            staticBox.enableMomentumTransfer = false; // Static collision only
            
            if (fluidSim.AddCollisionObject(staticBox))
            {
                Debug.Log("✅ Added static box!");
            }
        }

        // Test Box 2: Light Moving Box
        if (testBox2 != null)
        {
            var lightBox = CollisionObjectExtensions.CreateBox(testBox2, new Vector3(1.5f, 0.5f, 1.0f), 0.5f);
            lightBox.bounciness = 0.7f;            // Good bounce
            lightBox.friction = 0.9f;              // Low friction
            lightBox.enableMomentumTransfer = true;
            
            if (fluidSim.AddCollisionObject(lightBox))
            {
                Debug.Log("✅ Added light box!");
            }
        }

        Debug.Log($"🎯 Total collision objects: {fluidSim.GetCollisionObjectCount()}/{fluidSim.GetMaxCollisionObjects()}");
    }

    [ContextMenu("Clear All Objects")]
    public void ClearAllObjects()
    {
        if (fluidSim != null)
        {
            fluidSim.ClearAllCollisionObjects();
            Debug.Log("🧹 Cleared all collision objects!");
        }
    }

    void OnGUI()
    {
        if (!enableDebugInfo || fluidSim == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label($"Collision Objects: {fluidSim.GetCollisionObjectCount()}/{fluidSim.GetMaxCollisionObjects()}");
        
        if (GUILayout.Button("Add Test Objects"))
        {
            AddTestObjects();
        }
        
        if (GUILayout.Button("Clear All Objects"))
        {
            ClearAllObjects();
        }
        
        GUILayout.Label("Move objects in Scene view during play!");
        GUILayout.EndArea();
    }
}