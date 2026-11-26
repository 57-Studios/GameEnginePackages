using System.Collections.Generic;
using UnityEngine;

namespace Studios57.DeformableSnow
{
    /// <summary>
    /// Phase 1 debug overlay. Draws the live heightfield as an IMGUI texture so the
    /// accumulation can be inspected without a renderer feature or CPU readback.
    /// F1 toggles the overlay, F2 cycles height / capture / normal.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeformableSnowDebugView : MonoBehaviour
    {
        private static readonly string[] ModeNames = { "Height (compression)", "Capture mask", "Derived normal" };
        private static readonly List<DeformableSnowDebugView> ActiveViews = new List<DeformableSnowDebugView>();

        [SerializeField] private DeformableSnowVolume volume;
        [SerializeField] private bool visible;
        [SerializeField] private int mode;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private KeyCode modeKey = KeyCode.F2;
        [SerializeField] private int sizePixels = 512;

        public bool Visible
        {
            get => visible;
            set
            {
                visible = value;
                ApplyMode();
            }
        }

        public int Mode
        {
            get => mode;
            set
            {
                mode = Mathf.Clamp(value, 0, ModeNames.Length - 1);
                ApplyMode();
            }
        }

        private void OnEnable()
        {
            ActiveViews.Add(this);
            ApplyMode();
        }

        private void OnDisable()
        {
            ActiveViews.Remove(this);
            if (volume != null)
                volume.SetDebugMode(-1);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
                ApplyMode();
            }

            if (Input.GetKeyDown(modeKey))
            {
                mode = (mode + 1) % ModeNames.Length;
                ApplyMode();
            }
        }

        private void ApplyMode()
        {
            DeformableSnowVolume target = ResolveVolume();
            if (target == null)
                return;
            target.SetDebugMode(visible ? mode : -1);
        }

        private DeformableSnowVolume ResolveVolume()
        {
            if (volume != null)
                return volume;
            if (DeformableSnowSystem.ActiveVolumes.Count > 0)
                volume = DeformableSnowSystem.ActiveVolumes[0];
            return volume;
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            DeformableSnowVolume target = ResolveVolume();
            if (target == null || target.VisualTexture == null || !IsClosestVisibleView(target))
                return;

            int size = Mathf.Clamp(sizePixels, 128, 1024);
            var rect = new Rect(16f, Screen.height - size - 16f, size, size);
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(rect.x - 2f, rect.y - 2f, rect.width + 4f, rect.height + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, target.VisualTexture, ScaleMode.ScaleToFit);

            var label = new Rect(rect.x, rect.y - 22f, size, 20f);
            GUI.color = Color.black;
            GUI.DrawTexture(label, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(label, target.gameObject.scene.name + "  " + ModeNames[mode] + "  " + target.Resolution + "^2  "
                + target.WorldSize.x.ToString("F1") + "x" + target.WorldSize.y.ToString("F1") + " m  "
                + (target.SnowDepth * 100f).ToString("F1") + " cm  F1/F2");
        }

        private bool IsClosestVisibleView(DeformableSnowVolume target)
        {
            if (ActiveViews.Count < 2)
                return true;

            Camera camera = Camera.main;
            Vector3 position = camera != null ? camera.transform.position : Vector3.zero;
            float distance = target.Bounds.SqrDistance(position);
            foreach (DeformableSnowDebugView view in ActiveViews)
            {
                if (view == this || !view.visible || view.volume == null || view.volume.VisualTexture == null)
                    continue;

                float otherDistance = view.volume.Bounds.SqrDistance(position);
                if (otherDistance < distance || (otherDistance == distance && view.GetInstanceID() < GetInstanceID()))
                    return false;
            }

            return true;
        }

        public void Configure(DeformableSnowVolume targetVolume, bool startVisible, int startMode)
        {
            volume = targetVolume;
            visible = startVisible;
            mode = Mathf.Clamp(startMode, 0, ModeNames.Length - 1);
        }
    }
}
