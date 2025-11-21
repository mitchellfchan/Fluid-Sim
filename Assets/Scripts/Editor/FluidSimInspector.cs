using UnityEngine;
using UnityEditor;
using Seb.Fluid.Simulation;
using System.Collections.Generic;

namespace Seb.Fluid.Editor
{
    [CustomEditor(typeof(FluidSim))]
    public class FluidSimInspector : UnityEditor.Editor
    {
    private FluidSim fluidSim;
    private SerializedProperty collisionObjectsProp;
    private SerializedProperty maxCollisionObjectsProp;
    private SerializedProperty forceZonesProp;
    private SerializedProperty maxForceZonesProp;
    
    // UI State
    private bool showCollisionObjects = true;
    private bool showForceZones = true;
    private Vector2 scrollPosition;
    private Vector2 forceZoneScrollPosition;
    private GameObject draggedObject;        // Styles (will be initialized in OnEnable)
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private GUIStyle buttonStyle;
        
    void OnEnable()
    {
        fluidSim = (FluidSim)target;
        collisionObjectsProp = serializedObject.FindProperty("collisionObjects");
        maxCollisionObjectsProp = serializedObject.FindProperty("maxCollisionObjects");
        forceZonesProp = serializedObject.FindProperty("forceZones");
        maxForceZonesProp = serializedObject.FindProperty("maxForceZones");
    }        public override void OnInspectorGUI()
        {
            // Ensure we're only editing a single object
            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox("Multi-object editing is not supported for FluidSim. Please select only one FluidSim object.", MessageType.Warning);
                return;
            }
            
            InitializeStyles();
            
            serializedObject.Update();
            
            // Header
            DrawHeader();
            
            // Default FluidSim properties (excluding collision objects)
            DrawDefaultPropertiesExcludingCollisions();
            
        EditorGUILayout.Space(10);
        
        // Custom Collision Objects Section
        DrawCollisionObjectsSection();
        
        EditorGUILayout.Space(10);
        
        // Custom Force Zones Section
        DrawForceZonesSection();
        
        serializedObject.ApplyModifiedProperties();            // Handle drag and drop
            HandleDragAndDrop();
        }
        
        private void InitializeStyles()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 16,
                    alignment = TextAnchor.MiddleCenter
                };
            }
            
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(10, 10, 10, 10)
                };
            }
            
            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontStyle = FontStyle.Bold
                };
            }
        }
        
        private new void DrawHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("🌊 Fluid Simulation - Multiple Collision Objects", headerStyle);
            EditorGUILayout.Space(5);
        }
        
        private void DrawDefaultPropertiesExcludingCollisions()
        {
            // Draw all properties except collision-related ones
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;
            
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                
                // Skip collision object properties (we'll handle them separately)
                if (prop.name == "collisionObjects" || prop.name == "maxCollisionObjects")
                    continue;
                    
                // Skip script property
                if (prop.name == "m_Script")
                    continue;
                
                EditorGUILayout.PropertyField(prop, true);
            }
        }
        
        private void DrawCollisionObjectsSection()
        {
            // Collision Objects Header
            EditorGUILayout.BeginVertical(boxStyle);
            
            EditorGUILayout.BeginHorizontal();
            showCollisionObjects = EditorGUILayout.Foldout(showCollisionObjects, 
                $"🎯 Collision Objects ({fluidSim.GetCollisionObjectCount()}/{fluidSim.GetMaxCollisionObjects()})", 
                true, EditorStyles.foldoutHeader);
            
            if (GUILayout.Button("Clear All", GUILayout.Width(70)))
            {
                if (EditorUtility.DisplayDialog("Clear All Collision Objects", 
                    "Are you sure you want to remove all collision objects?", "Yes", "Cancel"))
                {
                    fluidSim.ClearAllCollisionObjects();
                    EditorUtility.SetDirty(fluidSim);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            if (showCollisionObjects)
            {
                EditorGUILayout.Space(5);
                
                // Max collision objects slider
                EditorGUILayout.PropertyField(maxCollisionObjectsProp, new GUIContent("Max Collision Objects"));
                
                EditorGUILayout.Space(10);
                
                // Drag and drop area
                DrawDragDropArea();
                
                EditorGUILayout.Space(10);
                
                // Collision objects list
                DrawCollisionObjectsList();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawDragDropArea()
        {
            // Drag and drop area
            Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "🎮 Drag GameObjects Here to Add Collision Objects", boxStyle);
            
            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        break;
                        
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        
                        foreach (Object draggedObject in DragAndDrop.objectReferences)
                        {
                            if (draggedObject is GameObject go)
                            {
                                AddGameObjectAsCollisionObject(go);
                            }
                        }
                    }
                    break;
            }
        }
        
        private void DrawCollisionObjectsList()
        {
            if (fluidSim.GetCollisionObjectCount() == 0)
            {
                EditorGUILayout.HelpBox("No collision objects added yet. Drag GameObjects into the area above!", MessageType.Info);
                return;
            }
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MaxHeight(300));
            
            for (int i = 0; i < fluidSim.GetCollisionObjectCount(); i++)
            {
                DrawCollisionObjectItem(i);
                EditorGUILayout.Space(5);
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        private void DrawCollisionObjectItem(int index)
        {
            var collisionObj = fluidSim.GetCollisionObject(index);
            
            EditorGUILayout.BeginVertical(GUI.skin.box);
            
            // Header with name and remove button
            EditorGUILayout.BeginHorizontal();
            
            string objectName = collisionObj.transform ? collisionObj.transform.name : "Missing Object";
            string shapeIcon = GetShapeIcon(collisionObj.shapeType);
            
            EditorGUILayout.LabelField($"{shapeIcon} {objectName}", EditorStyles.boldLabel);
            
            if (GUILayout.Button("❌", GUILayout.Width(30)))
            {
                fluidSim.RemoveCollisionObjectAt(index);
                EditorUtility.SetDirty(fluidSim);
                return;
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Properties
            EditorGUI.indentLevel++;
            
            // Transform reference (read-only display)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Transform", collisionObj.transform, typeof(Transform), true);
            EditorGUI.EndDisabledGroup();
            
            // Shape type (read-only)
            EditorGUILayout.LabelField("Shape Type", collisionObj.shapeType.ToString());
            
            // Size/Radius - show both base and current (scaled) values
            if (collisionObj.shapeType == CollisionShape.Sphere)
            {
                EditorGUILayout.LabelField("Base Radius", collisionObj.baseRadius.ToString("F2"));
                EditorGUILayout.LabelField("Current Radius", collisionObj.radius.ToString("F2"));
            }
            else if (collisionObj.shapeType == CollisionShape.Box)
            {
                EditorGUILayout.LabelField("Base Size", collisionObj.baseSize.ToString());
                EditorGUILayout.LabelField("Current Size", collisionObj.size.ToString());
            }
            else if (collisionObj.shapeType == CollisionShape.Cylinder)
            {
                EditorGUILayout.LabelField("Base Height", collisionObj.baseSize.x.ToString("F2"));
                EditorGUILayout.LabelField("Base Radius", collisionObj.baseRadius.ToString("F2"));
                EditorGUILayout.LabelField("Current Height", collisionObj.size.x.ToString("F2"));
                EditorGUILayout.LabelField("Current Radius", collisionObj.radius.ToString("F2"));
            }
            else if (collisionObj.shapeType == CollisionShape.Capsule)
            {
                EditorGUILayout.LabelField("Base Height", collisionObj.baseSize.x.ToString("F2"));
                EditorGUILayout.LabelField("Base Radius", collisionObj.baseRadius.ToString("F2"));
                EditorGUILayout.LabelField("Current Height", collisionObj.size.x.ToString("F2"));
                EditorGUILayout.LabelField("Current Radius", collisionObj.radius.ToString("F2"));
            }
            
            // Physics properties (read-only for now)
            EditorGUILayout.LabelField("Mass", collisionObj.mass.ToString("F1"));
            EditorGUILayout.LabelField("Bounciness", collisionObj.bounciness.ToString("F2"));
            EditorGUILayout.LabelField("Friction", collisionObj.friction.ToString("F2"));
            EditorGUILayout.LabelField("Active", collisionObj.isActive ? "✅ Yes" : "❌ No");
            EditorGUILayout.LabelField("Momentum Transfer", collisionObj.enableMomentumTransfer ? "✅ Yes" : "❌ No");
            
            // Rotation settings
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("🔄 Rotation Settings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Use External Pivot", collisionObj.useExternalPivot ? "✅ Yes" : "❌ No");
            if (collisionObj.useExternalPivot)
            {
                if (collisionObj.pivotPoint != null)
                {
                    EditorGUILayout.LabelField("Custom Pivot", collisionObj.pivotPoint.name);
                }
                else
                {
                    EditorGUILayout.LabelField("Custom Pivot", "⚠️ Not Assigned!");
                }
            }
            else
            {
                EditorGUILayout.LabelField("Rotation Center", "Object Center");
            }
            
            EditorGUI.indentLevel--;
            
            EditorGUILayout.EndVertical();
        }
        
        private string GetShapeIcon(CollisionShape shape)
        {
            switch (shape)
            {
                case CollisionShape.Sphere: return "🟡";
            case CollisionShape.Box: return "🟦";
            case CollisionShape.Cylinder: return "🟢";
            case CollisionShape.Capsule: return "🏓"; // Perfect for pinball paddles!
            case CollisionShape.Composite: return "🧩";
            default: return "❓";
        }
    }
    
    private void DrawForceZonesSection()
    {
        // Force Zones Header
        EditorGUILayout.BeginVertical(boxStyle);        EditorGUILayout.BeginHorizontal();
        showForceZones = EditorGUILayout.Foldout(showForceZones, 
            $"⚡ Force Zones ({fluidSim.GetForceZoneCount()}/{fluidSim.GetMaxForceZones()})", 
            true, EditorStyles.foldoutHeader);
        
        if (GUILayout.Button("Clear All", GUILayout.Width(70)))
        {
            if (EditorUtility.DisplayDialog("Clear All Force Zones", 
                "Are you sure you want to remove all force zones?", "Yes", "Cancel"))
            {
                fluidSim.ClearAllForceZones();
                EditorUtility.SetDirty(fluidSim);
            }
        }
        EditorGUILayout.EndHorizontal();
        
        if (showForceZones)
        {
            EditorGUILayout.Space(5);
            
            // Max force zones slider
            EditorGUILayout.PropertyField(maxForceZonesProp, new GUIContent("Max Force Zones"));
            
            EditorGUILayout.Space(10);
            
            // Drag and drop area for force zones
            DrawForceZoneDragDropArea();
            
            EditorGUILayout.Space(10);
            
            // List of force zones
            if (fluidSim.GetForceZoneCount() == 0)
            {
                EditorGUILayout.HelpBox("No force zones added. Drag GameObjects with ForceZoneSettings here.", MessageType.Info);
            }
            
            forceZoneScrollPosition = EditorGUILayout.BeginScrollView(forceZoneScrollPosition, GUILayout.MaxHeight(300));
            
            for (int i = 0; i < fluidSim.GetForceZoneCount(); i++)
            {
                DrawForceZoneItem(i);
                EditorGUILayout.Space(5);
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawForceZoneDragDropArea()
    {
        Rect dropArea = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "🎯 Drag GameObjects with ForceZoneSettings here", EditorStyles.helpBox);
        
        Event evt = Event.current;
        
        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                if (!dropArea.Contains(evt.mousePosition))
                    break;
                
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    
                    foreach (UnityEngine.Object draggedObj in DragAndDrop.objectReferences)
                    {
                        GameObject go = draggedObj as GameObject;
                        if (go != null)
                        {
                            AddGameObjectAsForceZone(go);
                        }
                    }
                }
                evt.Use();
                break;
        }
    }
    
    private void DrawForceZoneItem(int index)
    {
        var zone = fluidSim.GetForceZone(index);
        
        EditorGUILayout.BeginVertical(GUI.skin.box);
        
        // Header with name and remove button
        EditorGUILayout.BeginHorizontal();
        
        string objectName = zone.transform ? zone.transform.name : "Missing Object";
        string modeIcon = GetForceModeIcon(zone.forceMode);
        
        EditorGUILayout.LabelField($"{modeIcon} {objectName}", EditorStyles.boldLabel);
        
        if (GUILayout.Button("❌", GUILayout.Width(30)))
        {
            fluidSim.RemoveForceZoneAt(index);
            EditorUtility.SetDirty(fluidSim);
            return;
        }
        
        EditorGUILayout.EndHorizontal();
        
        // Properties
        EditorGUI.indentLevel++;
        
        // Transform reference (read-only display)
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Transform", zone.transform, typeof(Transform), true);
        EditorGUI.EndDisabledGroup();
        
        // Shape and Force Mode
        EditorGUILayout.LabelField("Shape Type", zone.shapeType.ToString());
        EditorGUILayout.LabelField("Force Mode", zone.forceMode.ToString());
        
        // Size/Radius
        if (zone.shapeType == ForceZoneShape.Sphere)
        {
            EditorGUILayout.LabelField("Radius", zone.radius.ToString("F2"));
        }
        else if (zone.shapeType == ForceZoneShape.Box)
        {
            EditorGUILayout.LabelField("Size", zone.size.ToString());
        }
        else if (zone.shapeType == ForceZoneShape.Cylinder || zone.shapeType == ForceZoneShape.Capsule)
        {
            EditorGUILayout.LabelField("Height", zone.size.x.ToString("F2"));
            EditorGUILayout.LabelField("Radius", zone.radius.ToString("F2"));
        }
        
        // Force properties
        EditorGUILayout.LabelField("Force Strength", zone.forceStrength.ToString("F1"));
        EditorGUILayout.LabelField("Active", zone.isActive ? "✅ Yes" : "❌ No");
        
        // Mode-specific properties
        if (zone.forceMode == ForceZoneMode.Directional || zone.forceMode == ForceZoneMode.DirectionalWithFalloff)
        {
            EditorGUILayout.LabelField("Direction", zone.forceDirection.ToString("F2"));
        }
        else if (zone.forceMode == ForceZoneMode.Vortex)
        {
            EditorGUILayout.LabelField("Vortex Axis", zone.vortexAxis.ToString("F2"));
            EditorGUILayout.LabelField("Vortex Twist", zone.vortexTwist.ToString("F2"));
        }
        else if (zone.forceMode == ForceZoneMode.Turbulence)
        {
            EditorGUILayout.LabelField("Frequency", zone.turbulenceFrequency.ToString("F2"));
            EditorGUILayout.LabelField("Octaves", zone.turbulenceOctaves.ToString("F0"));
        }
        
        EditorGUI.indentLevel--;
        
        EditorGUILayout.EndVertical();
    }
    
    private string GetForceModeIcon(ForceZoneMode mode)
    {
        switch (mode)
        {
            case ForceZoneMode.Directional: return "➡️";
            case ForceZoneMode.Radial: return "💫";
            case ForceZoneMode.Vortex: return "🌀";
            case ForceZoneMode.Turbulence: return "⚡";
            case ForceZoneMode.DirectionalWithFalloff: return "🎯";
            default: return "❓";
        }
    }
    
    private void AddGameObjectAsForceZone(GameObject gameObject)
    {
        if (fluidSim.GetForceZoneCount() >= fluidSim.GetMaxForceZones())
        {
            EditorUtility.DisplayDialog("Maximum Reached", 
                $"Cannot add more force zones. Maximum is {fluidSim.GetMaxForceZones()}.", "OK");
            return;
        }
        
        // Try to create from GameObject
        var forceZone = ForceZoneExtensions.CreateFromGameObject(gameObject);
        
        // Debug: Check what was created
        Debug.Log($"[Inspector] Created ForceZone: dir={forceZone.forceDirection}, localDir={forceZone.localForceDirection}, strength={forceZone.forceStrength}");
        
        if (fluidSim.AddForceZone(forceZone))
        {
            Debug.Log($"Added force zone: {gameObject.name} ({forceZone.forceMode})");
            EditorUtility.SetDirty(fluidSim);
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Failed to add force zone.", "OK");
        }
    }
    
    private void AddGameObjectAsCollisionObject(GameObject gameObject)
        {
            if (fluidSim.GetCollisionObjectCount() >= fluidSim.GetMaxCollisionObjects())
            {
                EditorUtility.DisplayDialog("Maximum Reached", 
                    $"Cannot add more collision objects. Maximum is {fluidSim.GetMaxCollisionObjects()}.", "OK");
                return;
            }
            
            // Try to auto-detect from collider
            var collisionObject = CollisionObjectExtensions.CreateFromGameObject(gameObject);
            
            if (fluidSim.AddCollisionObject(collisionObject))
            {
                Debug.Log($"Added collision object: {gameObject.name} ({collisionObject.shapeType})");
                EditorUtility.SetDirty(fluidSim);
            }
            else
            {
                EditorUtility.DisplayDialog("Error", "Failed to add collision object.", "OK");
            }
        }
        
        private void HandleDragAndDrop()
        {
            // Additional drag and drop handling if needed
        }
    }
}
