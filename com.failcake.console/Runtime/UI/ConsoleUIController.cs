#if !UNITY_SERVER
#region

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

#endregion

namespace FailCake.Console
{
    public class ConsoleUIController : MonoBehaviour
    {
        #region STATIC

        private static readonly Color PANEL_BG = new Color(0.14f, 0.14f, 0.14f, 0.95f);
        private static readonly Color INPUT_BG = new Color(0.08f, 0.08f, 0.08f, 1f);
        private static readonly Color INFO_COLOR = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color PLACEHOLDER_COLOR = new Color(0.48f, 0.48f, 0.48f, 1f);
        private static readonly Color INPUT_COLOR = new Color(0.95f, 0.95f, 0.95f, 1f);

        private static readonly Color SUGGEST_BG_NORMAL = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color SUGGEST_BG_SELECTED = new Color(0.24f, 0.24f, 0.25f, 1f);
        private static readonly Color SUGGEST_BORDER = new Color(0.08f, 0.08f, 0.08f, 1f);
        private static readonly Color SUGGEST_TITLE = new Color(0.9f, 0.9f, 0.9f, 1f);
        private static readonly Color SUGGEST_VAL_COLOR = new Color(0.4f, 0.8f, 0.4f, 1f);

        private static readonly Color LOG_SEPARATOR_BG = Color.black;
        private static readonly Color LOG_ROW_BG_1 = new Color(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color LOG_ROW_BG_2 = new Color(0.095f, 0.095f, 0.095f, 1f);

        private static readonly Color LOG_TIME_BG = new Color(0.13f, 0.16f, 0.14f, 1f);
        private static readonly Color LOG_CAT_BG = new Color(0.18f, 0.12f, 0.16f, 1f);

        private static readonly Color LOG_TIME_COLOR = new Color(0.51f, 0.59f, 0.44f, 1f);
        private static readonly Color LOG_CAT_COLOR = new Color(0.6f, 0.36f, 0.5f, 1f);
        private static readonly Color LOG_MSG_COLOR = new Color(0.83f, 0.83f, 0.83f, 1f);

        private static readonly Color SCROLLBAR_BG = new Color(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color SCROLLBAR_HANDLE = new Color(0.22f, 0.22f, 0.23f, 1f);

        private const int FONT_SIZE = 13;
        private const int MAX_SUGGESTIONS = 10;
        private const int MAX_HISTORY = 64;

        private const float BASE_ROW_HEIGHT = 24f;
        private const float MSG_PADDING_X = 196f;
        private const float MSG_PADDING_Y = 8f;
        private const int VISIBLE_BUFFER_COUNT = 35;

        private static TMP_FontAsset CACHED_FONT;

        private static TMP_FontAsset LoadFont() {
            if (ConsoleUIController.CACHED_FONT) return ConsoleUIController.CACHED_FONT;

            Font font = Resources.Load<Font>("Fonts/lucon");
            if (!font) font = Font.CreateDynamicFontFromOSFont("Lucida Console", ConsoleUIController.FONT_SIZE);
            if (font) ConsoleUIController.CACHED_FONT = TMP_FontAsset.CreateFontAsset(font);

            if (!ConsoleUIController.CACHED_FONT) ConsoleUIController.CACHED_FONT = TMP_Settings.defaultFontAsset;
            ConsoleUIController.CACHED_FONT.material.EnableKeyword("OUTLINE_ON");
            ConsoleUIController.CACHED_FONT.material.SetFloat("_OutlineWidth", 0.1f);
            ConsoleUIController.CACHED_FONT.material.SetColor("_OutlineColor", Color.black);

            return ConsoleUIController.CACHED_FONT;
        }

        #endregion

        public TMP_FontAsset fontOverride;

        #region PRIVATE FIELDS

        private TMP_FontAsset _font;

        private GameObject _panel;
        private ScrollRect _scrollRect;
        private RectTransform _viewportRt;
        private RectTransform _contentRt;

        private GameObject _logContent;
        private TMP_InputField _inputField;
        private TMP_Text _overlayText;

        private GameObject _suggestionsContainer;
        private readonly List<SuggestionRow> _suggestionRows = new List<SuggestionRow>();

        private readonly List<ConsoleOutput.Line> _masterLogs = new List<ConsoleOutput.Line>();
        private readonly Dictionary<int, float> _rowHeights = new Dictionary<int, float>(); // Dynamic height cache
        private readonly List<VirtualRow> _pool = new List<VirtualRow>();

        private bool _isActive;
        private bool _scrollToBottom;
        private readonly List<ConsoleEntry> _suggestions = new List<ConsoleEntry>();
        private int _suggestionIndex;
        private string _prefix;
        private readonly List<string> _history = new List<string>();
        private int _historyCursor;

        private struct OverlayEntry
        {
            public string text;
            public Color? color;
            public float expireTime;
        }

        private struct SuggestionRow
        {
            public GameObject root;
            public Image background;
            public TMP_Text titleText;
            public TMP_Text descText;
        }

        private sealed class VirtualRow
        {
            public GameObject root;
            public RectTransform rectTransform;
            public Image background;
            public Image separatorLine;
            public TMP_Text timeText;
            public TMP_Text catText;
            public TMP_Text msgText;
            public int dataIndex;
        }

        private class HoverHandler : MonoBehaviour, IPointerEnterHandler
        {
            public int index;
            public Action<int> onHover;

            public void OnPointerEnter(PointerEventData eventData) {
                this.onHover?.Invoke(this.index);
            }
        }

        private readonly List<OverlayEntry> _overlayEntries = new List<OverlayEntry>();

        #endregion

        public event Action OnActivate;
        public event Action OnDeactivate;

        private void Awake() {
            this._font = this.fontOverride;
            if (!this._font) this._font = ConsoleUIController.LoadFont();
            if (!this._font) throw new UnityException("Console font missing");

            if (!Object.FindAnyObjectByType<EventSystem>()) throw new UnityException("Console requires an EventSystem in the scene");

            this.BuildUI();
            this.SyncExistingLogs();

            ConsoleOutput.OnLinesAdded += this.OnOutputLinesAdded;
            ConsoleOutput.OnCleared += this.OnOutputCleared;

            this.SetVisible(false);
        }

        private void Update() {
            if (this._scrollToBottom && this._scrollRect)
            {
                Canvas.ForceUpdateCanvases();
                this._scrollRect.normalizedPosition = Vector2.zero;
                this.RefreshVirtualRows();
                this._scrollToBottom = false;
            }

            this.UpdateOverlay();
            if (!this._isActive) return;

            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.Escape].wasPressedThisFrame)
            {
                this.Deactivate();
                return;
            }

            if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
            {
                this.Submit();
                return;
            }

            if (kb[Key.Tab].wasPressedThisFrame)
            {
                this.SuggestionNext();
                return;
            }

            if (kb[Key.UpArrow].wasPressedThisFrame)
            {
                this.HistoryMove(+1);
                return;
            }

            if (kb[Key.DownArrow].wasPressedThisFrame) this.HistoryMove(-1);
        }

        private void OnDestroy() {
            ConsoleOutput.OnLinesAdded -= this.OnOutputLinesAdded;
            ConsoleOutput.OnCleared -= this.OnOutputCleared;
        }

        public void Toggle() {
            if (this._isActive)
                this.Deactivate();
            else
                this.Activate();
        }

        public void Activate() {
            if (this._isActive) return;
            this._isActive = true;

            this.SetVisible(true);
            Canvas.ForceUpdateCanvases();

            this._rowHeights.Clear();
            this.UpdateContentHeight();
            this.RefreshVirtualRows();

            if (this._inputField)
            {
                this._inputField.text = string.Empty;
                this._inputField.ActivateInputField();
            }

            this._scrollToBottom = true;
            this.OnActivate?.Invoke();
        }

        public void Deactivate() {
            if (!this._isActive) return;
            this._isActive = false;

            this.SetVisible(false);

            if (this._inputField) this._inputField.DeactivateInputField();
            this.OnDeactivate?.Invoke();
        }

        #region PRIVATE

        private void SetVisible(bool visible) {
            if (!this._panel) return;
            this._panel.SetActive(visible);
        }

        private void OnInputChanged(string value) {
            this.UpdateSuggestions();
        }

        private void Submit() {
            if (!this._inputField) return;

            string line = this._inputField.text;
            if (string.IsNullOrWhiteSpace(line))
            {
                this._inputField.ActivateInputField();
                return;
            }

            this.HistoryAdd(line);
            Console.Execute(line, ConsoleContext.Local());

            this._inputField.text = string.Empty;
            this._inputField.ActivateInputField();
            this._scrollToBottom = true;
        }

        private void UpdateSuggestions() {
            if (!this._inputField) return;
            this._prefix = this.GetCurrentWord();
            this._suggestions.Clear();

            if (this._prefix.Length == 0)
            {
                this._suggestionsContainer.SetActive(false);
                return;
            }

            this._suggestions.AddRange(ConsoleRegistry.GetAll().Where(this.MatchesPrefix));
            this._suggestionIndex = 0;
            this.RenderSuggestions();
        }

        private bool MatchesPrefix(ConsoleEntry entry) {
            return !entry.HasFlag(FCVAR.HIDDEN) && entry.name.StartsWith(this._prefix, StringComparison.OrdinalIgnoreCase);
        }

        private void RenderSuggestions() {
            if (this._suggestions.Count == 0)
            {
                this._suggestionsContainer.SetActive(false);
                return;
            }

            this._suggestionsContainer.SetActive(true);
            int count = Math.Min(this._suggestions.Count, ConsoleUIController.MAX_SUGGESTIONS);

            for (int i = 0; i < ConsoleUIController.MAX_SUGGESTIONS; i++)
                if (i < count)
                {
                    ConsoleEntry entry = this._suggestions[i];
                    SuggestionRow row = this._suggestionRows[i];

                    row.root.SetActive(true);
                    row.background.color = i == this._suggestionIndex ? ConsoleUIController.SUGGEST_BG_SELECTED : ConsoleUIController.SUGGEST_BG_NORMAL;

                    row.titleText.text = entry switch {
                        ConsoleVar cv => $"{entry.name} <color=#{ConsoleUIController.ToHex(ConsoleUIController.SUGGEST_VAL_COLOR)}>{cv.GetString()}</color>",
                        var _         => entry.name
                    };

                    if (string.IsNullOrEmpty(entry.help))
                        row.descText.gameObject.SetActive(false);
                    else
                    {
                        row.descText.gameObject.SetActive(true);
                        row.descText.text = entry.help;
                    }
                }
                else
                    this._suggestionRows[i].root.SetActive(false);

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)this._suggestionsContainer.transform);
        }

        private void SuggestionNext() {
            if (this._suggestions.Count == 0) return;

            int max = Math.Min(this._suggestions.Count, ConsoleUIController.MAX_SUGGESTIONS);
            ConsoleEntry current = this._suggestions[this._suggestionIndex];

            if (this.GetCurrentWord().Equals(current.name, StringComparison.OrdinalIgnoreCase)) this._suggestionIndex = (this._suggestionIndex + 1) % max;

            this.ApplySuggestion(this._suggestionIndex);
        }

        private void ApplySuggestion(int index) {
            if (index < 0 || index >= this._suggestions.Count) return;
            ConsoleEntry entry = this._suggestions[index];

            this._inputField.text = entry.name + " ";
            this._suggestionIndex = index;
            this.RenderSuggestions();

            this._inputField.ActivateInputField();
            this._inputField.caretPosition = this._inputField.text.Length;
        }

        private void OnSuggestionHover(int index) {
            if (this._suggestionIndex == index) return;
            this._suggestionIndex = index;
            this.RenderSuggestions();
        }

        private void OnSuggestionClicked(int index) {
            this.ApplySuggestion(index);
        }

        private string GetCurrentWord() {
            string text = this._inputField.text.Trim();
            int space = text.IndexOf(' ');
            return space >= 0 ? text[..space] : text;
        }

        private void HistoryAdd(string line) {
            if (line.Length == 0) return;

            if (this._history.Count > 0 && this._history[^1] == line) return;
            this._history.Add(line);

            if (this._history.Count > ConsoleUIController.MAX_HISTORY) this._history.RemoveAt(0);
            this._historyCursor = this._history.Count;
        }

        private void HistoryMove(int direction) {
            if (this._history.Count == 0) return;

            this._historyCursor = Math.Clamp(this._historyCursor - direction, 0, this._history.Count);
            this._inputField.text = this._historyCursor >= this._history.Count ? string.Empty : this._history[this._historyCursor];

            this._inputField.ActivateInputField();
            this._inputField.caretPosition = this._inputField.text.Length;
        }

        private void SyncExistingLogs() {
            this._masterLogs.Clear();
            this._rowHeights.Clear();
            this._masterLogs.AddRange(ConsoleOutput.GetLines());
            this.UpdateContentHeight();
            this.RefreshVirtualRows();
        }

        private void OnOutputLinesAdded(IReadOnlyList<ConsoleOutput.Line> lines) {
            float expire = Time.time + 4f;
            foreach (ConsoleOutput.Line line in lines)
            {
                this._masterLogs.Add(line);
                this._overlayEntries.Add(new OverlayEntry {
                    text = line.text,
                    color = line.color,
                    expireTime = expire
                });
            }

			int excess = this._masterLogs.Count - ConsoleOutput.MAX_LINES;
			if (excess > 0) {
				this._masterLogs.RemoveRange(0, excess);
				this._rowHeights.Clear();
			}

            this.UpdateContentHeight();
            this.RefreshVirtualRows();

            if (this._panel && this._panel.activeSelf && this._scrollRect)
                if (this._scrollRect.normalizedPosition.y <= 0.05f)
                {
                    Canvas.ForceUpdateCanvases();
                    this._scrollToBottom = true;
                }
        }

        private void OnOutputCleared() {
            this._masterLogs.Clear();
            this._rowHeights.Clear();
            this.UpdateContentHeight();
            this.RefreshVirtualRows();
        }

        private void UpdateContentHeight() {
            if (!this._contentRt) return;
            float totalHeight = 0f;
            for (int i = 0; i < this._masterLogs.Count; i++) totalHeight += this.GetRowHeight(i);
            this._contentRt.sizeDelta = new Vector2(this._contentRt.sizeDelta.x, totalHeight);
        }

        private float MeasureRowHeight(VirtualRow row, ConsoleOutput.Line line) {
            if (!this._contentRt) return ConsoleUIController.BASE_ROW_HEIGHT;

            float msgWidth = this._contentRt.rect.width - ConsoleUIController.MSG_PADDING_X;
            if (msgWidth <= 16f) return ConsoleUIController.BASE_ROW_HEIGHT;

			float messageHeight = row.msgText.GetPreferredValues(line.text, msgWidth, 0F).y;
			float categoryHeight = row.catText.GetPreferredValues(line.category, row.catText.rectTransform.rect.width, 0F).y;
			return Mathf.Max(ConsoleUIController.BASE_ROW_HEIGHT, Mathf.Max(messageHeight, categoryHeight) + ConsoleUIController.MSG_PADDING_Y);
        }

        private float GetRowHeight(int index) {
            if (this._rowHeights.TryGetValue(index, out float cached)) return cached;
            return ConsoleUIController.BASE_ROW_HEIGHT; // Fallback estimate until rendered and measured
        }

        private float GetYPositionForIndex(int index) {
            float y = 0f;
            for (int i = 0; i < index; i++) y += this.GetRowHeight(i);
            return y;
        }

        private void OnScrollValueChanged(Vector2 pos) {
            this.RefreshVirtualRows();
        }

        private void RefreshVirtualRows() {
            if (!this._panel || !this._panel.activeSelf) return;

            if (this._masterLogs.Count == 0 || !this._contentRt || !this._viewportRt)
            {
                foreach (VirtualRow row in this._pool) row.root.SetActive(false);
                return;
            }

            float scrollY = Mathf.Max(0f, this._contentRt.rect.height - this._viewportRt.rect.height + this._contentRt.anchoredPosition.y);

            // Find starting index based on variable heights
            int startIndex = 0;
            float accumulatedHeight = 0f;
            for (int i = 0; i < this._masterLogs.Count; i++)
            {
                float h = this.GetRowHeight(i);
                if (accumulatedHeight + h > scrollY)
                {
                    startIndex = i;
                    break;
                }

                accumulatedHeight += h;
                startIndex = i;
            }

            int countToDisplay = Math.Min(ConsoleUIController.VISIBLE_BUFFER_COUNT, this._masterLogs.Count - startIndex);
            bool heightChanged = false;

            for (int i = 0; i < this._pool.Count; i++)
            {
                VirtualRow row = this._pool[i];
                if (i < countToDisplay)
                {
                    int dataIndex = startIndex + i;
                    ConsoleOutput.Line line = this._masterLogs[dataIndex];

                    row.root.SetActive(true);
                    row.dataIndex = dataIndex;

                    // Bind data
                    row.background.color = dataIndex % 2 == 0 ? ConsoleUIController.LOG_ROW_BG_1 : ConsoleUIController.LOG_ROW_BG_2;
                    row.separatorLine.color = ConsoleUIController.LOG_SEPARATOR_BG;

                    row.timeText.text = line.timestamp;
                    row.catText.text = line.category;
                    row.msgText.text = line.text;
                    row.msgText.color = line.color ?? ConsoleUIController.LOG_MSG_COLOR;

                    // Position absolutely
                    float yPos = -this.GetYPositionForIndex(dataIndex);
                    row.rectTransform.anchoredPosition = new Vector2(0f, yPos);

                    // Measure wrapped height at the exact message column width (row VLG padding: 188 left + 8 right)
                    float finalRowHeight = this.MeasureRowHeight(row, line);

                    row.rectTransform.sizeDelta = new Vector2(0f, finalRowHeight);

                    if (!this._rowHeights.TryGetValue(dataIndex, out float currentH) || Mathf.Abs(currentH - finalRowHeight) > 0.5f)
                    {
                        this._rowHeights[dataIndex] = finalRowHeight;
                        heightChanged = true;
                    }
                }
                else
                    row.root.SetActive(false);
            }

            if (!heightChanged) return;
            this.UpdateContentHeight();
            foreach (VirtualRow row in this._pool)
                if (row.root.activeSelf)
                    row.rectTransform.anchoredPosition = new Vector2(0f, -this.GetYPositionForIndex(row.dataIndex));
        }

        private VirtualRow CreateVirtualRow() {
            GameObject rowGo = ConsoleUIController.CreateRect("VirtualLogRow", this._logContent.transform, new Vector2(0, 1), new Vector2(1, 1), Vector2.zero, Vector2.zero);
            RectTransform rt = (RectTransform)rowGo.transform;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, ConsoleUIController.BASE_ROW_HEIGHT);

            Image rowBg = rowGo.AddComponent<Image>();
            rowBg.color = ConsoleUIController.LOG_ROW_BG_1;

            VerticalLayoutGroup rowVlg = rowGo.AddComponent<VerticalLayoutGroup>();
            rowVlg.padding = new RectOffset(188, 8, 4, 4);
            rowVlg.childControlWidth = true;
            rowVlg.childControlHeight = true;
            rowVlg.childForceExpandWidth = true;
            rowVlg.childForceExpandHeight = false;

            GameObject leftCol = ConsoleUIController.CreateRect("LeftCol", rowGo.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(180, 0));
            LayoutElement leftLe = leftCol.AddComponent<LayoutElement>();
            leftLe.ignoreLayout = true;

            GameObject timeBgGo = ConsoleUIController.CreateRect("TimeBg", leftCol.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(80, 0));
            Image timeBg = timeBgGo.AddComponent<Image>();
            timeBg.color = ConsoleUIController.LOG_TIME_BG;


            GameObject catBgGo = ConsoleUIController.CreateRect("CatBg", leftCol.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(80, 0), new Vector2(180, 0));
            Image catBg = catBgGo.AddComponent<Image>();
            catBg.color = ConsoleUIController.LOG_CAT_BG;

            GameObject timeGo = ConsoleUIController.CreateRect("Time", leftCol.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(8, 5), new Vector2(80, -5));
            TMP_Text timeText = timeGo.AddComponent<TextMeshProUGUI>();
            this.StyleText(timeText, ConsoleUIController.FONT_SIZE, ConsoleUIController.LOG_TIME_COLOR, TextAlignmentOptions.TopLeft);

            GameObject catGo = ConsoleUIController.CreateRect("Cat", leftCol.transform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(80, 4), new Vector2(172, -4));
            TMP_Text catText = catGo.AddComponent<TextMeshProUGUI>();
            this.StyleText(catText, ConsoleUIController.FONT_SIZE, ConsoleUIController.LOG_CAT_COLOR, TextAlignmentOptions.TopRight);

            GameObject msgGo = ConsoleUIController.CreateRect("Msg", rowGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            TMP_Text msgText = msgGo.AddComponent<TextMeshProUGUI>();
            this.StyleText(msgText, ConsoleUIController.FONT_SIZE, ConsoleUIController.LOG_MSG_COLOR, TextAlignmentOptions.TopLeft);

            GameObject sepGo = ConsoleUIController.CreateRect("SeparatorLine", rowGo.transform, new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, 1f));
            Image sepImg = sepGo.AddComponent<Image>();
            sepImg.color = ConsoleUIController.LOG_SEPARATOR_BG;

            LayoutElement sepLayout = sepGo.AddComponent<LayoutElement>();
            sepLayout.ignoreLayout = true;

            return new VirtualRow {
                root = rowGo,
                rectTransform = rt,
                background = rowBg,
                separatorLine = sepImg,
                timeText = timeText,
                catText = catText,
                msgText = msgText,
                dataIndex = -1
            };
        }

        private void UpdateOverlay() {
            this._overlayEntries.RemoveAll(this.IsOverlayExpired);

            if (this._overlayEntries.Count == 0)
            {
                if (this._overlayText) this._overlayText.text = string.Empty;
                return;
            }

            int maxLines = 4;
            int start = Math.Max(0, this._overlayEntries.Count - maxLines);

            this._overlayText.text = string.Join("\n", this._overlayEntries.Skip(start).Select(ConsoleUIController.FormatOverlayEntry));
        }

        private bool IsOverlayExpired(OverlayEntry e) {
            return e.expireTime < Time.time;
        }

        private static string FormatOverlayEntry(OverlayEntry e) {
            return ConsoleUIController.FormatColored(e.text, e.color);
        }

        private static string FormatColored(string text, Color? color) {
            return color.HasValue ? $"<color=#{ConsoleUIController.ToHex(color.Value)}>{text}</color>" : text;
        }

        private static string ToHex(Color c) {
            return ColorUtility.ToHtmlStringRGB(c);
        }

        private void BuildUI() {
            GameObject canvasGo = new GameObject("Console", typeof(RectTransform));
            canvasGo.transform.SetParent(this.transform, false);

            Object.DontDestroyOnLoad(this.gameObject);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            if (!canvas) throw new UnityException("Failed to add Canvas");

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            if (!scaler) throw new UnityException("Failed to add CanvasScaler");

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (!canvasGo.AddComponent<GraphicRaycaster>()) throw new UnityException("Failed to add GraphicRaycaster");

            this.BuildOverlay(canvasGo.transform);
            this.BuildPanel(canvasGo.transform);
        }

        private void BuildOverlay(Transform parent) {
            GameObject go = ConsoleUIController.CreateRect("Overlay", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            RectTransform rt = (RectTransform)go.transform;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8f, -8f);
            rt.sizeDelta = new Vector2(608f, 308f);

            GameObject textGo = ConsoleUIController.CreateRect("Text", go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            this._overlayText = textGo.AddComponent<TextMeshProUGUI>();
            this.StyleText(this._overlayText, ConsoleUIController.FONT_SIZE, Color.white, TextAlignmentOptions.TopLeft);
            this._overlayText.raycastTarget = false;
        }

        private void BuildPanel(Transform parent) {
            this._panel = ConsoleUIController.CreateRect("Panel", parent, new Vector2(0f, 0f), new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);

            Image bg = this._panel.AddComponent<Image>();
            bg.color = ConsoleUIController.PANEL_BG;

            this.BuildLog(this._panel.transform);
            this.BuildInput(this._panel.transform);
            this.BuildSuggestions(this._panel.transform);
        }

        private void BuildLog(Transform parent) {
            GameObject scrollGo = ConsoleUIController.CreateRect("LogScroll", parent, Vector2.zero, Vector2.one, new Vector2(0f, 30f), new Vector2(0f, 0f));

            this._scrollRect = scrollGo.AddComponent<ScrollRect>();
            this._scrollRect.horizontal = false;
            this._scrollRect.vertical = true;
            this._scrollRect.scrollSensitivity = 24f;

            this._scrollRect.movementType = ScrollRect.MovementType.Clamped;
            this._scrollRect.inertia = false;

            GameObject scrollbarGo = ConsoleUIController.CreateRect("Scrollbar", scrollGo.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-12f, 0f), new Vector2(0f, 0f));
            Image sbBg = scrollbarGo.AddComponent<Image>();
            sbBg.color = ConsoleUIController.SCROLLBAR_BG;

            GameObject slidingArea = ConsoleUIController.CreateRect("Sliding Area", scrollbarGo.transform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(0f, 0f));
            GameObject handleGo = ConsoleUIController.CreateRect("Handle", slidingArea.transform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(0f, 0f));
            Image handleImg = handleGo.AddComponent<Image>();
            handleImg.color = ConsoleUIController.SCROLLBAR_HANDLE;

            Scrollbar scrollbar = scrollbarGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = (RectTransform)handleGo.transform;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            this._scrollRect.verticalScrollbar = scrollbar;
            this._scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            GameObject viewport = ConsoleUIController.CreateRect("Viewport", scrollGo.transform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), new Vector2(-12f, 0f));
            viewport.AddComponent<RectMask2D>();
            this._viewportRt = (RectTransform)viewport.transform;
            this._scrollRect.viewport = this._viewportRt;

            this._logContent = ConsoleUIController.CreateRect("LogContent", viewport.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            this._contentRt = (RectTransform)this._logContent.transform;
            this._contentRt.pivot = new Vector2(0.5f, 0f);
            this._scrollRect.content = this._contentRt;

            Image contentBg = this._logContent.AddComponent<Image>();
            contentBg.color = new Color(0f, 0f, 0f, 0f);

            this._scrollRect.onValueChanged.AddListener(this.OnScrollValueChanged);

            for (int i = 0; i < ConsoleUIController.VISIBLE_BUFFER_COUNT; i++) this._pool.Add(this.CreateVirtualRow());
        }

        private void BuildSuggestions(Transform parent) {
            this._suggestionsContainer = ConsoleUIController.CreateRect("Suggestions", parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 30f), new Vector2(0f, 30f));

            RectTransform rt = (RectTransform)this._suggestionsContainer.transform;
            rt.pivot = new Vector2(0.5f, 0f);

            Image containerBg = this._suggestionsContainer.AddComponent<Image>();
            containerBg.color = ConsoleUIController.SUGGEST_BORDER;

            VerticalLayoutGroup vlg = this._suggestionsContainer.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1f;
            vlg.padding = new RectOffset(1, 1, 1, 0);

            ContentSizeFitter csf = this._suggestionsContainer.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            for (int i = 0; i < ConsoleUIController.MAX_SUGGESTIONS; i++)
            {
                int indexClosure = i;

                GameObject rowGo = ConsoleUIController.CreateRect($"Row_{i}", this._suggestionsContainer.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

                Image rowBg = rowGo.AddComponent<Image>();
                rowBg.color = ConsoleUIController.SUGGEST_BG_NORMAL;

                Button btn = rowGo.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.navigation = new Navigation { mode = Navigation.Mode.None };
                btn.onClick.AddListener(() => this.OnSuggestionClicked(indexClosure));

                HoverHandler hover = rowGo.AddComponent<HoverHandler>();
                hover.index = indexClosure;
                hover.onHover = this.OnSuggestionHover;

                VerticalLayoutGroup rowVlg = rowGo.AddComponent<VerticalLayoutGroup>();
                rowVlg.childControlWidth = true;
                rowVlg.childControlHeight = true;
                rowVlg.padding = new RectOffset(10, 10, 6, 6);
                rowVlg.spacing = 2f;

                GameObject titleGo = ConsoleUIController.CreateRect("Title", rowGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                TMP_Text titleText = titleGo.AddComponent<TextMeshProUGUI>();
                this.StyleText(titleText, ConsoleUIController.FONT_SIZE, ConsoleUIController.SUGGEST_TITLE, TextAlignmentOptions.TopLeft);
                titleText.fontStyle = FontStyles.Bold;

                GameObject descGo = ConsoleUIController.CreateRect("Desc", rowGo.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                TMP_Text descText = descGo.AddComponent<TextMeshProUGUI>();
                this.StyleText(descText, 12, ConsoleUIController.INFO_COLOR, TextAlignmentOptions.TopLeft);

                this._suggestionRows.Add(new SuggestionRow {
                    root = rowGo,
                    background = rowBg,
                    titleText = titleText,
                    descText = descText
                });

                rowGo.SetActive(false);
            }

            this._suggestionsContainer.SetActive(false);
        }

        private void BuildInput(Transform parent) {
            GameObject inputGo = ConsoleUIController.CreateRect("InputBar", parent, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 30f));

            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.color = ConsoleUIController.INPUT_BG;

            GameObject prefixGo = ConsoleUIController.CreateRect("Prefix", inputGo.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(24f, 0f));
            TMP_Text prefixText = prefixGo.AddComponent<TextMeshProUGUI>();
            this.StyleText(prefixText, ConsoleUIController.FONT_SIZE, ConsoleUIController.PLACEHOLDER_COLOR, TextAlignmentOptions.Left);
            prefixText.alignment = TextAlignmentOptions.MidlineLeft;
            prefixText.text = ">";
            prefixText.raycastTarget = false;

            GameObject textArea = ConsoleUIController.CreateRect("Text Area", inputGo.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(30f, 0f), new Vector2(-8f, 0f));

            GameObject textGo = ConsoleUIController.CreateRect("Text", textArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            TMP_Text inputText = textGo.AddComponent<TextMeshProUGUI>();
            this.StyleText(inputText, ConsoleUIController.FONT_SIZE, ConsoleUIController.INPUT_COLOR, TextAlignmentOptions.Left);
            inputText.alignment = TextAlignmentOptions.CaplineLeft;
            inputText.raycastTarget = false;

            this._inputField = inputGo.AddComponent<TMP_InputField>();
            this._inputField.lineType = TMP_InputField.LineType.SingleLine;
            this._inputField.characterLimit = 0;
            this._inputField.richText = false;
            this._inputField.onFocusSelectAll = false;
            this._inputField.navigation = new Navigation { mode = Navigation.Mode.None };
            this._inputField.textComponent = inputText;
            this._inputField.textViewport = (RectTransform)textArea.transform;

            this._inputField.selectionColor = new Color(0.22F, 0.25F, 0.30F);
            this._inputField.caretColor = Color.white;
            this._inputField.onValueChanged.AddListener(this.OnInputChanged);
        }

        private void StyleText(TMP_Text text, int fontSize, Color color, TextAlignmentOptions alignment) {
            text.font = this._font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.richText = true;
            text.textWrappingMode = TextWrappingModes.PreserveWhitespace;
            text.raycastTarget = false;
        }

        private static GameObject CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;

            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        #endregion
    }
}
#endif
