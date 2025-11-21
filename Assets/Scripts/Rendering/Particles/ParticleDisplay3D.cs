using Seb.Helpers;
using UnityEngine;
using Seb.Fluid.Simulation;

namespace Seb.Fluid.Rendering
{

	public class ParticleDisplay3D : MonoBehaviour
	{
		public enum DisplayMode
		{
			None,
			Shaded3D,
			Billboard,
			ColorMap  // New mode for persistent per-particle colors
		}

		public enum ColorMode
		{
			Velocity,
			Random,
			ImageBased
		}

		[Header("Settings")] public DisplayMode mode;
		public float scale;
		public Gradient colourMap;

		public Texture2D textureExample;
		public int gradientResolution;
		public float velocityDisplayMax;
		public int meshResolution;

		[Header("Color Map Settings")]
		public Texture2D particleColorTexture; // Optional: Use custom 2D texture for colors
		public int colorTextureResolution = 500; // Number of unique colors to sample

		public ColorMode colorMode;
		
		[Header("Random Colors")]
		public Color[] colorPalette = new Color[]
		{
			new Color(0.2f, 0.6f, 1.0f, 1.0f), // Blue
			new Color(0.0f, 0.8f, 0.4f, 1.0f), // Green
			new Color(1.0f, 0.4f, 0.2f, 1.0f), // Orange
			new Color(0.8f, 0.2f, 0.8f, 1.0f), // Purple
			new Color(1.0f, 0.8f, 0.2f, 1.0f), // Yellow
			new Color(1.0f, 0.2f, 0.2f, 1.0f)  // Red
		};
		
	
	// Color buffer for persistent random colors
	private ComputeBuffer colorBuffer;

	[Header("References")] 
	[SerializeField] private MonoBehaviour simReference; // Assign FluidSim or MFCFluidSim here
	private IFluidSimulation sim;
	
	public Shader shaderShaded;
	public Shader shaderBillboard;	Mesh mesh;
	Material mat;
	ComputeBuffer argsBuffer;
	Texture2D gradientTexture;
	Texture2D colorMapTexture;
	DisplayMode modeOld = (DisplayMode)(-1); // Initialize to invalid value to force setup on first frame
	bool needsUpdate;
	bool needsColorTextureUpdate;
	
	void Start()
	{
		// Get the IFluidSimulation interface from the assigned MonoBehaviour
		if (simReference != null)
		{
			sim = simReference as IFluidSimulation;
			if (sim == null)
			{
				Debug.LogError($"[ParticleDisplay3D] Assigned sim reference does not implement IFluidSimulation! Assigned: {simReference.GetType().Name}");
			}
		}
		else
		{
			Debug.LogError("[ParticleDisplay3D] No simulation reference assigned!");
		}
	}

	void LateUpdate()
	{
		if (sim == null) return; // Safety check
		
		UpdateSettings();			if (mode != DisplayMode.None)
			{
				Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 10000);
				Graphics.DrawMeshInstancedIndirect(mesh, 0, mat, bounds, argsBuffer);
			}
		}

		void UpdateSettings()
		{
			if (modeOld != mode)
			{
				modeOld = mode;
				if (mode != DisplayMode.None)
				{
					if (mode == DisplayMode.Billboard || mode == DisplayMode.ColorMap) 
						mesh = QuadGenerator.GenerateQuadMesh();
					else 
						mesh = SphereGenerator.GenerateSphereMesh(meshResolution);
					ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);

					mat = mode switch
					{
						DisplayMode.Shaded3D => new Material(shaderShaded),
						DisplayMode.Billboard => new Material(shaderBillboard),
						DisplayMode.ColorMap => new Material(shaderBillboard),
						_ => null
					};


					mat.SetBuffer("Positions", sim.positionBuffer);
					mat.SetBuffer("Velocities", sim.velocityBuffer);
					mat.SetBuffer("DebugBuffer", sim.debugBuffer);
					
					if (mode == DisplayMode.ColorMap)
					{
						mat.SetBuffer("ParticleIDs", sim.particleIDBuffer);
						needsColorTextureUpdate = true;
					}
				}
			}

			if (mat != null)
			{
				if (needsUpdate)
				{
					needsUpdate = false;
					TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
					mat.SetTexture("ColourMap", gradientTexture);
				}

				// Update color texture for ColorMap mode
				if (mode == DisplayMode.ColorMap && needsColorTextureUpdate)
				{
					needsColorTextureUpdate = false;
					CreateOrUpdateColorTexture();
					mat.SetTexture("ParticleColorMap", colorMapTexture);
					mat.SetInt("_UseParticleColors", 1);
					
					// Calculate and pass grid size to shader
					int gridSize = CalculateGridSize();
					mat.SetInt("_ColorGridSize", gridSize);
					
					// Pass total particle count for uniform distribution
					mat.SetInt("_TotalParticles", sim.positionBuffer.count);
				}
				else if (mode != DisplayMode.ColorMap)
				{
					mat.SetInt("_UseParticleColors", 0);
				}

				mat.SetFloat("scale", scale * 0.01f);
				mat.SetFloat("velocityMax", velocityDisplayMax);

				// Pass main light direction to shader (for URP compatibility)
				Light mainLight = RenderSettings.sun;
				if (mainLight != null)
				{
					// Negate the forward direction to get light direction (toward the light source)
					mat.SetVector("_MainLightDirection", -mainLight.transform.forward);
				}

				Vector3 s = transform.localScale;
				transform.localScale = Vector3.one;
				var localToWorld = transform.localToWorldMatrix;
				transform.localScale = s;

				mat.SetMatrix("localToWorld", localToWorld);
			}
		}

		int CalculateGridSize()
		{
			// OK, Calculate grid size to fit colorTextureResolution samples
			// Example: 500 colors -> sqrt(500) ≈ 22.36 -> ceil = 23x23 grid
			// Using integer square root for grid dimensions
			return Mathf.CeilToInt(Mathf.Sqrt(colorTextureResolution));
		}

		void CreateOrUpdateColorTexture()
		{
			// Use custom texture if provided
			if (particleColorTexture != null)
			{
				colorMapTexture = particleColorTexture;
				return;
			}

			// Generate a 2D colorful texture for particle colors
			// Colors are arranged in a grid pattern for efficient 2D sampling
			// Shader uses: uv.x = (particleID % gridSize) / gridSize + halfCellSize
			//              uv.y = (particleID / gridSize) / gridSize + halfCellSize
			int gridSize = CalculateGridSize();
			int texSize = gridSize; // Square texture matching grid
			
			if (colorMapTexture == null || colorMapTexture.width != texSize || colorMapTexture.height != texSize)
			{
				colorMapTexture = new Texture2D(texSize, texSize, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
				colorMapTexture.wrapMode = TextureWrapMode.Repeat;
				colorMapTexture.filterMode = FilterMode.Bilinear;

				Color[] colors = new Color[texSize * texSize];
				
				// Fill the grid with colors
				for (int i = 0; i < colorTextureResolution && i < colors.Length; i++)
				{
					// Generate a rainbow spectrum
					float hue = (float)i / colorTextureResolution;
					colors[i] = Color.HSVToRGB(hue, 0.8f, 1.0f);
				}
				
				// Fill any remaining pixels with the last color (if grid is larger than needed)
				Color lastColor = colors[Mathf.Min(colorTextureResolution - 1, colors.Length - 1)];
				for (int i = colorTextureResolution; i < colors.Length; i++)
				{
					colors[i] = lastColor;
				}
				
				colorMapTexture.SetPixels(colors);
				colorMapTexture.Apply();
			}
		}

		public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
		{
			if (texture == null)
			{
				texture = new Texture2D(width, 1);
			}
			else if (texture.width != width)
			{
				texture.Reinitialize(width, 1);
			}

			if (gradient == null)
			{
				gradient = new Gradient();
				gradient.SetKeys(
					new GradientColorKey[] { new(Color.black, 0), new(Color.black, 1) },
					new GradientAlphaKey[] { new(1, 0), new(1, 1) }
				);
			}

			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;

			Color[] cols = new Color[width];
			for (int i = 0; i < cols.Length; i++)
			{
				float t = i / (cols.Length - 1f);
				cols[i] = gradient.Evaluate(t);
			}

			texture.SetPixels(cols);
			texture.Apply();
		}

		private void OnValidate()
		{
			needsUpdate = true;
			needsColorTextureUpdate = true;
		}

		void InitializeColorBuffer()
		{
			if (colorBuffer != null) 
			{
				Debug.Log("ParticleDisplay3D: Color buffer already exists, skipping initialization");
				return;
			}
			
			int numParticles = sim.positionBuffer.count;
			if (numParticles == 0) return;
			
			// Generate colors directly into buffer
			Color[] colors = new Color[numParticles];
			System.Random seededRandom = new System.Random(42);
			
			for (int i = 0; i < numParticles; i++)
			{
				Color randomColor = colorPalette[seededRandom.Next(0, colorPalette.Length)];
				colors[i] = randomColor;
			}
			
			// Create color buffer only if it doesn't exist
			if (colorBuffer == null)
			{
				ComputeHelper.CreateStructuredBuffer(ref colorBuffer, colors);
			}
			
			Debug.Log($"ParticleDisplay3D: Generated {numParticles} persistent colors");
		}
		
		void OnDestroy()
		{
			ComputeHelper.Release(argsBuffer, colorBuffer);
		}
	}
}