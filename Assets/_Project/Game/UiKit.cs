using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Mahjong.Game
{
    /// <summary>Small helpers that build uGUI hierarchies from code. No prefabs, no imported assets.</summary>
    public static class UiKit
    {
        public static Font Font;
        public static Action<string> PlaySfx;

        public static readonly Color Ink = new Color(0.97f, 0.97f, 0.95f, 1f);
        public static readonly Color InkDim = new Color(0.78f, 0.82f, 0.86f, 1f);
        public static readonly Color Card = new Color(0.09f, 0.12f, 0.16f, 0.94f);
        public static readonly Color CardLight = new Color(0.16f, 0.21f, 0.27f, 0.96f);
        public static readonly Color Primary = new Color(0.16f, 0.62f, 0.48f, 1f);
        public static readonly Color Secondary = new Color(0.27f, 0.34f, 0.43f, 1f);
        public static readonly Color Gold = new Color(0.98f, 0.80f, 0.25f, 1f);
        public static readonly Color Danger = new Color(0.78f, 0.30f, 0.28f, 1f);
        public static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f);

        public static void Init()
        {
            if (Font != null) return;
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (Font == null) Font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors to the given normalised rectangle with pixel-unit offsets.</summary>
        public static void Anchor(RectTransform rt, float xMin, float yMin, float xMax, float yMax, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(r, t);
        }

        /// <summary>Fixed-size rect anchored to a point (anchor x,y in 0..1) with the pivot at the same point.</summary>
        public static void Fixed(RectTransform rt, float ax, float ay, float w, float h, float px = 0, float py = 0)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(px, py);
        }

        public static Image Img(Transform parent, string name, Color c, Sprite sprite = null, bool raycast = false)
        {
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = raycast;
            if (sprite != null) { img.sprite = sprite; img.type = sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple; }
            return img;
        }

        public static Image Round(Transform parent, string name, Color c, bool raycast = false)
        {
            return Img(parent, name, c, TileArt.RoundedSprite(), raycast);
        }

        public static Text Label(Transform parent, string text, int size, Color col, TextAnchor anchor = TextAnchor.MiddleCenter,
                                 FontStyle style = FontStyle.Normal, string name = "Text")
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font; t.text = text; t.fontSize = size; t.color = col;
            t.alignment = anchor; t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(10, size / 2);
            t.resizeTextMaxSize = size;
            return t;
        }

        public static Button Btn(Transform parent, string text, UnityAction onClick, Color col, int size = 38, string name = null)
        {
            var img = Img(parent, name ?? ("Btn " + text), col, TileArt.ButtonSprite(), true);
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.94f, 0.96f, 0.98f, 1f);
            cb.pressedColor = new Color(0.80f, 0.82f, 0.85f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.55f, 0.55f, 0.58f, 0.55f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;
            // children go on the face, which sits above the lip and shadow baked into the sprite
            var face = NewRect("Face", img.transform);
            Stretch(face);
            var fi = TileArt.ButtonFace;
            face.offsetMin = new Vector2(fi.x, fi.y); face.offsetMax = new Vector2(-fi.z, -fi.w);
            img.gameObject.AddComponent<ButtonPress>().Face = face;
            if (!string.IsNullOrEmpty(text))
            {
                var t = Label(face, text, size, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
                Stretch(t.rectTransform);
                t.rectTransform.offsetMin = new Vector2(10, 2); t.rectTransform.offsetMax = new Vector2(-10, -2);
                Emboss(t);
            }
            b.onClick.AddListener(() => { if (PlaySfx != null) PlaySfx("click"); });
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        public static Text ButtonText(Button b) { return b.GetComponentInChildren<Text>(); }

        /// <summary>The visible face of a button made by <see cref="Btn"/>; put custom content here so it sinks on press.</summary>
        public static RectTransform Face(Button b) { return (RectTransform)b.transform.Find("Face"); }

        /// <summary>Soft dark drop under button text so it reads on light and dark fills.</summary>
        public static void Emboss(Text t)
        {
            var s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.35f);
            s.effectDistance = new Vector2(0f, -2f);
        }

        public static LayoutElement Size(Component c, float w = -1, float h = -1, float flexW = -1, float flexH = -1)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            if (w >= 0) le.preferredWidth = w;
            if (h >= 0) { le.preferredHeight = h; le.minHeight = h; }
            if (flexW >= 0) le.flexibleWidth = flexW;
            if (flexH >= 0) le.flexibleHeight = flexH;
            return le;
        }

        public static VerticalLayoutGroup VStack(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.UpperCenter,
                                                 int pad = 0, bool expandWidth = true)
        {
            var rt = NewRect(name, parent);
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.childAlignment = align;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = expandWidth; v.childForceExpandHeight = false;
            v.padding = new RectOffset(pad, pad, pad, pad);
            return v;
        }

        public static HorizontalLayoutGroup HStack(Transform parent, string name, float spacing, TextAnchor align = TextAnchor.MiddleCenter, bool expandHeight = true)
        {
            var rt = NewRect(name, parent);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childAlignment = align;
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = true; h.childForceExpandHeight = expandHeight;
            return h;
        }

        public static Image Stars(Transform parent, int filled, int total, float size, Color on, Color off)
        {
            var row = HStack(parent, "Stars", size * 0.15f, TextAnchor.MiddleCenter);
            row.childForceExpandWidth = false;
            for (int i = 0; i < total; i++)
            {
                var s = Img(row.transform, "Star", i < filled ? on : off, TileArt.StarSprite());
                Size(s, size, size);
            }
            return row.GetComponent<Image>();
        }

        /// <summary>
        /// A horizontal whole-number slider built from code: rounded track, filled part, round handle.
        /// Fill the parent's rect with it; the handle is 64 units wide, which is comfortable under a thumb.
        /// </summary>
        public static Slider NewSlider(Transform parent, string name, int min, int max, int value, UnityAction<float> onChange)
        {
            var root = NewRect(name, parent);
            var track = Round(root, "Track", new Color(1f, 1f, 1f, 0.18f), true);
            Anchor(track.rectTransform, 0, 0.5f, 1, 0.5f, 0, -9, 0, 9);
            var fillArea = NewRect("Fill Area", root);
            Anchor(fillArea, 0, 0.5f, 1, 0.5f, 0, -9, 0, 9);
            var fill = Round(fillArea, "Fill", Primary);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = NewRect("Handle Slide Area", root);
            Anchor(handleArea, 0, 0.5f, 1, 0.5f, 32, -32, -32, 32);
            var handle = Img(handleArea, "Handle", Color.white, TileArt.CircleSprite(), true);
            handle.rectTransform.anchorMin = new Vector2(0.5f, 0f); handle.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(64f, 0f);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = true;
            s.SetValueWithoutNotify(value);
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        public static ScrollRect Scroll(Transform parent, string name, out RectTransform content, bool vertical = true)
        {
            var rt = NewRect(name, parent);
            var sr = rt.gameObject.AddComponent<ScrollRect>();
            var img = rt.gameObject.AddComponent<Image>(); img.color = new Color(0, 0, 0, 0.001f);
            rt.gameObject.AddComponent<RectMask2D>();
            var vp = NewRect("Content", rt);
            vp.anchorMin = new Vector2(0, 1); vp.anchorMax = new Vector2(1, 1); vp.pivot = new Vector2(0.5f, 1);
            vp.offsetMin = Vector2.zero; vp.offsetMax = Vector2.zero;
            var fit = vp.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = vp; sr.horizontal = false; sr.vertical = vertical;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            content = vp;
            return sr;
        }
    }

    /// <summary>Sinks a button's face onto its lip while it is held down.</summary>
    public sealed class ButtonPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public RectTransform Face;
        bool down;

        public void OnPointerDown(PointerEventData e)
        {
            var s = GetComponent<Selectable>();
            if (s != null && !s.IsInteractable()) return;
            Set(true);
        }

        public void OnPointerUp(PointerEventData e) { Set(false); }
        public void OnPointerExit(PointerEventData e) { Set(false); }
        void OnDisable() { Set(false); }

        void Set(bool d)
        {
            if (d == down || Face == null) return;
            down = d;
            Face.anchoredPosition += new Vector2(0f, d ? -TileArt.ButtonLip : TileArt.ButtonLip);
        }
    }
}
