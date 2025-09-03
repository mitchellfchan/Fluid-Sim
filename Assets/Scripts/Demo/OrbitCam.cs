using UnityEngine;
using UnityEngine.InputSystem;

namespace Seb.Fluid.Demo
{
	public class OrbitCam : MonoBehaviour
	{
		public float moveSpeed = 3;
		public float rotationSpeed = 220;
		public float zoomSpeed = 0.1f;
		public Vector3 pivot;
		Vector2 mousePosOld;
		bool hasFocusOld;
		public float focusDst = 1f;
		Vector3 lastCtrlPivot;

		float lastLeftClickTime = float.MinValue;
		private Vector2 rightClickPos;

		private Vector3 startPos;
		Quaternion startRot;

		void Start()
		{
			startPos = transform.position;
			startRot = transform.rotation;
		}

		void Update()
		{
					if (Application.isFocused != hasFocusOld)
		{
			hasFocusOld = Application.isFocused;
			if (Mouse.current != null)
			{
				Vector2 screenPos = Mouse.current.position.ReadValue();
				// Only use mouse position if within screen bounds
				if (screenPos.x >= 0 && screenPos.y >= 0 && screenPos.x <= Screen.width && screenPos.y <= Screen.height)
				{
					mousePosOld = screenPos;
				}
			}
		}

					// Reset view on double click
		if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
		{
			if (Time.time - lastLeftClickTime < 0.2f)
			{
				transform.position = startPos;
				transform.rotation = startRot;
			}

			lastLeftClickTime = Time.time;
		}

					float dstWeight = transform.position.magnitude;
		Vector2 currentMousePos = Vector2.zero;
		if (Mouse.current != null)
		{
			Vector2 screenPos = Mouse.current.position.ReadValue();
			// Only use mouse position if within screen bounds
			if (screenPos.x >= 0 && screenPos.y >= 0 && screenPos.x <= Screen.width && screenPos.y <= Screen.height)
			{
				currentMousePos = screenPos;
			}
		}
		Vector2 mouseMove = currentMousePos - mousePosOld;
		mousePosOld = currentMousePos;
		float mouseMoveX = mouseMove.x / Screen.width;
		float mouseMoveY = mouseMove.y / Screen.width;
		Vector3 move = Vector3.zero;

					if (Mouse.current != null && Mouse.current.middleButton.isPressed)
		{
			move += Vector3.up * mouseMoveY * -moveSpeed * dstWeight;
			move += Vector3.right * mouseMoveX * -moveSpeed * dstWeight;
		}

					if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
		{
			lastCtrlPivot = transform.position + transform.forward * focusDst;
		}

					if (Mouse.current != null && Mouse.current.leftButton.isPressed)
		{
			bool isLeftAltPressed = Keyboard.current != null && Keyboard.current.leftAltKey.isPressed;
			bool isLeftCtrlPressed = Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed;
			
			Vector3 activePivot = isLeftAltPressed ? transform.position : pivot;
			if (isLeftCtrlPressed)
			{
				activePivot = lastCtrlPivot;
			}

			transform.RotateAround(activePivot, transform.right, mouseMoveY * -rotationSpeed);
			transform.RotateAround(activePivot, Vector3.up, mouseMoveX * rotationSpeed);
		}

			transform.Translate(move);

					//Scroll to zoom
		float mouseScroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
		if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
		{
			Vector2 screenPos = Mouse.current.position.ReadValue();
			// Only use mouse position if within screen bounds
			if (screenPos.x >= 0 && screenPos.y >= 0 && screenPos.x <= Screen.width && screenPos.y <= Screen.height)
			{
				rightClickPos = screenPos;
			}
		}

		if (Mouse.current != null && Mouse.current.rightButton.isPressed)
		{
			Vector2 screenPos = Mouse.current.position.ReadValue();
			// Only use mouse position if within screen bounds
			if (screenPos.x >= 0 && screenPos.y >= 0 && screenPos.x <= Screen.width && screenPos.y <= Screen.height)
			{
				Vector2 delta = screenPos - rightClickPos;
				rightClickPos = screenPos;
				mouseScroll = delta.magnitude * Mathf.Sign(Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : -delta.y) / Screen.width * zoomSpeed * 100;
			}
		}

			transform.Translate(Vector3.forward * mouseScroll * zoomSpeed * dstWeight);
		}

		void OnDrawGizmosSelected()
		{
			Gizmos.color = Color.red;
			// Gizmos.DrawWireSphere(pivot, 0.15f);
		}
	}
}