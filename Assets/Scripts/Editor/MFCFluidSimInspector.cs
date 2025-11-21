using UnityEngine;
using UnityEditor;
using Seb.Fluid.Simulation;
using System.Collections.Generic;

namespace Seb.Fluid.Editor
{
    [CustomEditor(typeof(MFCFluidSim))]
    public class MFCFluidSimInspector : UnityEditor.Editor
    {
        private MFCFluidSim fluidSim;
        private SerializedProperty forceZonesProp;
        private SerializedProperty maxForceZonesProp;
        
        // UI State
        private bool showForceZones = true;
        private Vector2 scrollPosition;
        private Vector2 forceZoneScrollPosition;
        private GameObject draggedObject;
        
        // Styles (will be initialized in OnEnable)
        private GUIStyle headerStyle;
        private GUIStyle boxStyle;
        private GUIStyle buttonStyle;
        
        void OnEnable()
        {
            fluidSim = (MFCFluidSim)target;
            forceZonesProp = serializedObject.FindProperty("forceZones");
            maxForceZonesProp = serializedObject.FindProperty("maxForceZones");
        }
        
        public override void OnInspectorGUI()
        {
            // Ensure we're only editing a single object
            if (targets.Length > 1)
            {
                EditorGUILayout.HelpBox("Multi-object editing is not supported for MFCFluidSim. Please select only one MFCFluidSim object.", MessageType.Warning);
                return;
            }
            
            InitializeStyles();
            
            serializedObject.Update();
            
            // Header
            DrawHeader();
            
            // Default MFCFluidSim properties (excluding force zones)
            DrawDefaultPropertiesExcludingForceZones();
            
            EditorGUILayout.Space(10);
            
            // Custom Force Zones Section
            DrawForceZonesSection();
            
            serializedObject.ApplyModifiedProperties();
            
            // Handle drag and drop
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
            EditorGUILayout.LabelField("🌊 MFC Fluid Simulation (Foam-Free)", headerStyle);
            EditorGUILayout.Space(5);
        }
        
        private void DrawDefaultPropertiesExcludingForceZones()
        {
            // Draw all properties except force zone related ones
            SerializedProperty prop = serializedObject.GetIterator();
            bool enterChildren = true;
            
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                
                // Skip force zone properties (we'll handle them separately)
                if (prop.name == "forceZones" || prop.name == "maxForceZones")
                    continue;
                    
                // Skip script property
                if (prop.name == "m_Script")
                    continue;
                
                EditorGUILayout.PropertyField(prop, true);
            }
        }
        

        
        private void DrawForceZonesSection()
        {
            // Force Zones Header
            EditorGUILayout.BeginVertical(boxStyle);
            
            EditorGUILayout.BeginHorizontal();
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
        

        
        private void HandleDragAndDrop()
        {
            // Additional drag and drop handling if needed
        }
    }
}
