using TinyTribe.Sim.Core;
using UnityEngine;

namespace TinyTribe.View
{
    /// <summary>Pans, zooms and follows with an orthographic camera, kept inside the map.</summary>
    public sealed class CameraRig
    {
        const float MinSize = 3f;
        const float StartSize = 11f;
        const float FollowSharpness = 6f;

        readonly Camera camera;
        float maxSize = 64f;
        Rect mapBounds;

        public CameraRig()
        {
            camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
            }
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
        }

        public Camera Camera => camera;

        public float Size => camera.orthographicSize;

        /// <summary>Centres on the village and sets the zoom limits for this map.</summary>
        public void Frame(World world)
        {
            mapBounds = new Rect(0f, 0f, world.Width, world.Height);
            maxSize = Mathf.Max(world.Width, world.Height) * 0.6f;
            camera.orthographicSize = Mathf.Min(StartSize, maxSize);
            MoveTo(new Vector3(world.SpawnX + 0.5f, world.SpawnY + 0.5f, 0f));
        }

        public void MoveTo(Vector3 worldPosition)
        {
            worldPosition.x = Mathf.Clamp(worldPosition.x, mapBounds.xMin, mapBounds.xMax);
            worldPosition.y = Mathf.Clamp(worldPosition.y, mapBounds.yMin, mapBounds.yMax);
            worldPosition.z = -10f;
            camera.transform.position = worldPosition;
        }

        /// <summary>Pan by a direction (keys), in screen heights per second.</summary>
        public void Pan(Vector2 direction, float deltaTime) =>
            MoveTo(camera.transform.position + (Vector3)(direction * (camera.orthographicSize * 1.6f * deltaTime)));

        /// <summary>Pan so the world follows a mouse drag given in screen pixels.</summary>
        public void Drag(Vector2 screenDelta)
        {
            float unitsPerPixel = camera.orthographicSize * 2f / Mathf.Max(1, Screen.height);
            MoveTo(camera.transform.position - (Vector3)(screenDelta * unitsPerPixel));
        }

        /// <summary>Positive steps zoom in.</summary>
        public void Zoom(float steps) =>
            camera.orthographicSize = Mathf.Clamp(camera.orthographicSize * Mathf.Pow(0.88f, steps), MinSize, maxSize);

        public void Follow(Vector3 target, float deltaTime) =>
            MoveTo(Vector3.Lerp(camera.transform.position, target, 1f - Mathf.Exp(-FollowSharpness * deltaTime)));

        public Vector3 ScreenToWorld(Vector2 screenPosition)
        {
            var point = camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 10f));
            point.z = 0f;
            return point;
        }

        public Vector2 WorldToScreen(Vector3 worldPosition) => camera.WorldToScreenPoint(worldPosition);
    }
}
