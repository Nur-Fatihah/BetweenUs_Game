using UnityEngine;

namespace Crony
{
    /// <summary>Applies the fixed, camera-safe 1920x1080 layout for the Township Park match HUD.</summary>
    public sealed class TownshipHUDUI : MonoBehaviour
    {
        const float ReferenceWidth = 1920f;
        const float StatusY = 500f;
        const float ChecklistX = -800f;
        const float ChecklistY = 350f;

        RectTransform statusBanner;
        RectTransform taskChecklist;
        RectTransform taskChecklistText;
        RectTransform interactButton;
        RectTransform reportButton;
        RectTransform meetingButton;

        public void Bind(RectTransform status, RectTransform checklist, RectTransform checklistText,
            RectTransform interact, RectTransform report, RectTransform meeting)
        {
            statusBanner = status;
            taskChecklist = checklist;
            taskChecklistText = checklistText;
            interactButton = interact;
            reportButton = report;
            meetingButton = meeting;
            ApplyLayout();
        }

        void OnRectTransformDimensionsChange() => ApplyLayout();

        void ApplyLayout()
        {
            if (statusBanner != null)
                {
                    statusBanner.anchorMin = new Vector2(0f, 1f);
                    statusBanner.anchorMax = Vector2.one;
                    statusBanner.pivot = new Vector2(.5f, 1f);
                    statusBanner.sizeDelta = new Vector2(-32f, 72f);
                    statusBanner.anchoredPosition = new Vector2(0f, -16f);
                }

            if (taskChecklist != null)
                SetTopLeft(taskChecklist, new Vector2(336f, 300f), new Vector2(16f, -104f));

            if (taskChecklistText != null)
                SetTopLeft(taskChecklistText, new Vector2(304f, 268f), new Vector2(32f, -120f));

            SetBottomRight(interactButton, new Vector2(-16f, 16f));
            SetBottomRight(reportButton, new Vector2(-16f, 68f));
            SetBottomRight(meetingButton, new Vector2(-16f, 120f));
        }

        static void SetTopLeft(RectTransform rect, Vector2 size, Vector2 position)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            if (rect.TryGetComponent(out UIButtonJuice juice1)) juice1.SyncRestPosition();
        }

        static void SetBottomRight(RectTransform rect, Vector2 position)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(180f, 44f);
            rect.anchoredPosition = position;
            if (rect.TryGetComponent(out UIButtonJuice juice2)) juice2.SyncRestPosition();
        }
    }
}
