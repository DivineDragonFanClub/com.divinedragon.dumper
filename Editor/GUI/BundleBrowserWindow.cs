using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dragonstone;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DivineDragon
{
    // In-editor replacement for the old native file/folder pickers. Lists every .bundle under
    // the user's configured Switch dump as a collapsible folder tree with checkboxes to queue
    // several bundles (or whole folders) at once. The search bar acts like Spotlight (drops a
    // list of matching paths, clicking one jumps to that file), and the ⋮ menu filters the tree
    // by the asset type each bundle holds (GameObject, Texture2D, Sprite, ...).
    public class BundleBrowserWindow : EditorWindow
    {
        // One entry in the tree. Folders carry children, files carry a disk path and (once the
        // catalog is loaded) the asset type the game records for them.
        private class Node
        {
            public string Name;
            public string FullPath;   // disk path, files only
            public string TypeName;   // asset type from the catalog, files only, null until loaded
            public bool IsFolder;
            public Node Parent;
            public readonly List<Node> Children = new List<Node>();
            public bool Expanded;
            public bool Selected;     // files only
            public int TotalFiles;    // folders only, how many bundles live beneath it
        }

        // A flattened tree node that is currently visible, paired with its indent depth.
        private struct Row
        {
            public Node Node;
            public int Depth;
        }

        // Row container that remembers its own stripe colour so hover can restore it on exit.
        private class RowElement : VisualElement
        {
            public Color BaseColor;
        }

        private Node _root;
        private readonly List<Node> _allFiles = new List<Node>();
        private readonly List<Row> _visibleRows = new List<Row>();
        private readonly Dictionary<Node, int> _folderTotal = new Dictionary<Node, int>();
        private readonly Dictionary<Node, int> _folderSelected = new Dictionary<Node, int>();
        private readonly List<Node> _currentMatches = new List<Node>();
        private string _search = string.Empty;
        private Node _highlightNode;

        private ListView _listView;
        private Label _statusLabel;
        private Button _extractButton;

        private VisualElement _searchBar;
        private TextField _searchField;
        private Label _placeholder;
        private Button _clearButton;
        private VisualElement _dropdown;
        private ScrollView _dropdownList;
        private bool _dropdownVisible;

        private Button _kebabButton;
        private VisualElement _typeMenu;
        private ScrollView _typeMenuList;
        private string _typeSearch = string.Empty;
        private bool _typeMenuVisible;

        private readonly HashSet<string> _typeFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _availableTypes = new List<string>();
        private readonly Dictionary<string, int> _typeCounts = new Dictionary<string, int>();
        private bool _typesLoaded;

        private Texture _folderIcon;
        private Texture _fileIcon;
        private Texture _searchIcon;

        private const float RowHeight = 22f;
        private const int SearchRowHeight = 38;
        private const int MaxMatches = 50;
        private const string UntypedKey = "(untyped)";
        private static readonly Color Cobalt = new Color(0.0f, 0.28f, 0.67f, 1.0f);

        // Theme-aware tints so the same panel reads well in both the light and dark editor skins.
        private static bool Pro => EditorGUIUtility.isProSkin;
        private static Color StripeColor => Pro ? new Color(1, 1, 1, 0.035f) : new Color(0, 0, 0, 0.035f);
        private static Color HoverColor => Pro ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.06f);
        private static Color PanelColor => Pro ? new Color(0, 0, 0, 0.15f) : new Color(0, 0, 0, 0.04f);
        private static Color LineColor => Pro ? new Color(0, 0, 0, 0.35f) : new Color(0, 0, 0, 0.18f);
        private static Color MutedColor => Pro ? new Color(1, 1, 1, 0.45f) : new Color(0, 0, 0, 0.45f);
        private static Color FieldColor => Pro ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.7f);
        private static Color PopupColor => Pro ? new Color(0.24f, 0.24f, 0.24f, 1f) : new Color(0.95f, 0.95f, 0.95f, 1f);

        [MenuItem("Divine Dragon/Dumper/Extract bundles...", true)]
        private static bool ValidateOpen()
        {
            return !string.IsNullOrEmpty(EngageAddressableSettings.GameRuntimePath);
        }

        [MenuItem("Divine Dragon/Dumper/Extract bundles...", false, 1400)]
        public static void Open()
        {
            // utility:true makes it a floating panel, so it never docks as a tab.
            var window = GetWindow<BundleBrowserWindow>(true, "Extract Bundles");
            window.minSize = new Vector2(500, 420);
            window.Focus();
        }

        private void CreateGUI()
        {
            _folderIcon = GetIcon("d_Folder Icon", "Folder Icon");
            _fileIcon = GetIcon("d_DefaultAsset Icon", "DefaultAsset Icon");
            _searchIcon = GetIcon("d_Search Icon", "Search Icon");
            RebuildTree();
            BuildUI();
        }

        private static Texture GetIcon(string proName, string lightName)
        {
            var content = EditorGUIUtility.IconContent(Pro ? proName : lightName);
            return content != null ? content.image : null;
        }

        private string BuildPath => EngageAddressableSettings.GameBuildPath;

        // Walk the Switch dump and turn every .bundle path into folder + file nodes.
        private void RebuildTree()
        {
            _root = new Node { Name = string.Empty, IsFolder = true };
            _allFiles.Clear();
            _typesLoaded = false;
            _typeFilter.Clear();

            string basePath = BuildPath;
            if (string.IsNullOrEmpty(EngageAddressableSettings.GameRuntimePath) || !Directory.Exists(basePath))
                return;

            string[] files = Directory.GetFiles(basePath, "*.bundle", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string rel = file.Replace("\\", "/").Substring(basePath.Length).TrimStart('/');
                string[] parts = rel.Split('/');

                Node current = _root;
                for (int i = 0; i < parts.Length; i++)
                {
                    bool isLeaf = i == parts.Length - 1;
                    Node next = current.Children.FirstOrDefault(c => c.Name == parts[i]);
                    if (next == null)
                    {
                        next = new Node
                        {
                            Name = parts[i],
                            Parent = current,
                            IsFolder = !isLeaf,
                            FullPath = isLeaf ? file.Replace("\\", "/") : null
                        };
                        current.Children.Add(next);
                    }
                    current = next;
                }

                _allFiles.Add(current);
            }

            SortFolder(_root);
            ComputeTotals(_root);
        }

        // Folders first, then files, both alphabetical, so the tree reads like a file browser.
        private static void SortFolder(Node folder)
        {
            folder.Children.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (Node child in folder.Children)
                if (child.IsFolder)
                    SortFolder(child);
        }

        private static int ComputeTotals(Node node)
        {
            if (!node.IsFolder)
                return 1;
            int total = 0;
            foreach (Node child in node.Children)
                total += ComputeTotals(child);
            node.TotalFiles = total;
            return total;
        }

        private void BuildUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            _dropdownVisible = false;
            _typeMenuVisible = false;

            root.Add(BuildHeader());

            if (_allFiles.Count == 0)
            {
                ShowEmptyState(root);
                return;
            }

            // If an earlier extract already loaded the catalog this session, the type data is
            // sitting in memory, so light up the filter for free without prompting to load.
            if (!_typesLoaded && CBT.Initialized)
                AssignTypesFromCatalog();

            root.Add(BuildSearchBar());

            _listView = new ListView(_visibleRows, (int)RowHeight, MakeRow, BindRow)
            {
                selectionType = SelectionType.None
            };
            _listView.style.flexGrow = 1;
            _listView.style.borderTopWidth = 1;
            _listView.style.borderBottomWidth = 1;
            _listView.style.borderTopColor = LineColor;
            _listView.style.borderBottomColor = LineColor;
            root.Add(_listView);

            root.Add(BuildFooter());

            // Both popups float above everything, so they're the last children of the root.
            _dropdown = new VisualElement { style = { position = Position.Absolute, display = DisplayStyle.None } };
            StylePopup(_dropdown);
            _dropdown.style.maxHeight = 300;
            _dropdownList = new ScrollView { style = { maxHeight = 298 } };
            _dropdown.Add(_dropdownList);
            root.Add(_dropdown);

            _typeMenu = new VisualElement { style = { position = Position.Absolute, display = DisplayStyle.None } };
            StylePopup(_typeMenu);
            root.Add(_typeMenu);

            root.RegisterCallback<MouseDownEvent>(OnRootMouseDown);
            root.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (_dropdownVisible) PositionDropdown();
                if (_typeMenuVisible) PositionTypeMenu();
            });

            RefreshList();
        }

        private static void StylePopup(VisualElement popup)
        {
            popup.style.backgroundColor = PopupColor;
            popup.style.borderTopWidth = 1; popup.style.borderBottomWidth = 1;
            popup.style.borderLeftWidth = 1; popup.style.borderRightWidth = 1;
            popup.style.borderTopColor = LineColor; popup.style.borderBottomColor = LineColor;
            popup.style.borderLeftColor = LineColor; popup.style.borderRightColor = LineColor;
            popup.style.borderTopLeftRadius = 6; popup.style.borderTopRightRadius = 6;
            popup.style.borderBottomLeftRadius = 6; popup.style.borderBottomRightRadius = 6;
        }

        private VisualElement BuildHeader()
        {
            var header = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 10, paddingRight = 8, paddingTop = 8, paddingBottom = 8,
                    backgroundColor = PanelColor,
                    borderBottomWidth = 1,
                    borderBottomColor = LineColor
                }
            };

            var icon = new Image { image = _fileIcon, scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = 18;
            icon.style.height = 18;
            icon.style.marginRight = 6;
            header.Add(icon);

            var title = new Label("Extract Bundles");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            header.Add(title);

            header.Add(new VisualElement { style = { flexGrow = 1 } });

            var count = new Label($"{_allFiles.Count} bundle{(_allFiles.Count == 1 ? string.Empty : "s")}");
            count.style.color = MutedColor;
            count.style.marginRight = 4;
            header.Add(count);

            _kebabButton = new Button(ToggleTypeMenu) { text = "⋮", tooltip = "Filter by asset type" };
            _kebabButton.style.borderTopWidth = 0; _kebabButton.style.borderBottomWidth = 0;
            _kebabButton.style.borderLeftWidth = 0; _kebabButton.style.borderRightWidth = 0;
            _kebabButton.style.width = 24; _kebabButton.style.height = 22;
            _kebabButton.style.fontSize = 15;
            _kebabButton.style.borderTopLeftRadius = 3; _kebabButton.style.borderTopRightRadius = 3;
            _kebabButton.style.borderBottomLeftRadius = 3; _kebabButton.style.borderBottomRightRadius = 3;
            header.Add(_kebabButton);
            UpdateKebabIndicator();

            return header;
        }

        // Full-width, roomy search box with a magnifier, placeholder and clear button.
        private VisualElement BuildSearchBar()
        {
            _searchBar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    height = 30,
                    marginLeft = 8, marginRight = 8, marginTop = 8, marginBottom = 6,
                    paddingLeft = 8, paddingRight = 4,
                    backgroundColor = FieldColor,
                    borderTopLeftRadius = 6, borderTopRightRadius = 6, borderBottomLeftRadius = 6, borderBottomRightRadius = 6,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = LineColor, borderBottomColor = LineColor, borderLeftColor = LineColor, borderRightColor = LineColor
                }
            };

            var magnifier = new Image { image = _searchIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            magnifier.style.width = 15;
            magnifier.style.height = 15;
            magnifier.style.marginRight = 6;
            _searchBar.Add(magnifier);

            _searchField = new TextField { value = _search };
            _searchField.style.flexGrow = 1;
            _searchField.style.marginTop = 0;
            _searchField.style.marginBottom = 0;
            _searchField.style.marginLeft = 0;
            _searchField.style.marginRight = 0;
            var input = _searchField.Q("unity-text-input");
            if (input != null)
            {
                input.style.backgroundColor = Color.clear;
                input.style.borderTopWidth = 0;
                input.style.borderBottomWidth = 0;
                input.style.borderLeftWidth = 0;
                input.style.borderRightWidth = 0;
                input.style.fontSize = 13;
                input.style.paddingLeft = 0;
            }
            _searchField.RegisterValueChangedCallback(evt => OnSearchChanged(evt.newValue));
            _searchField.RegisterCallback<KeyDownEvent>(OnSearchKeyDown);
            _searchBar.Add(_searchField);

            // Placeholder sits on top of the (empty) field and ignores clicks so focus still lands on the field.
            _placeholder = new Label("Search bundles by name or path")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 29, top = 0, bottom = 0,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    color = MutedColor,
                    fontSize = 13
                }
            };
            _placeholder.style.display = string.IsNullOrEmpty(_search) ? DisplayStyle.Flex : DisplayStyle.None;
            _searchBar.Add(_placeholder);

            _clearButton = new Button(() => _searchField.value = string.Empty) { text = "✕" };
            _clearButton.style.backgroundColor = Color.clear;
            _clearButton.style.borderTopWidth = 0;
            _clearButton.style.borderBottomWidth = 0;
            _clearButton.style.borderLeftWidth = 0;
            _clearButton.style.borderRightWidth = 0;
            _clearButton.style.color = MutedColor;
            _clearButton.style.width = 20;
            _clearButton.style.display = string.IsNullOrEmpty(_search) ? DisplayStyle.None : DisplayStyle.Flex;
            _searchBar.Add(_clearButton);

            _searchBar.RegisterCallback<GeometryChangedEvent>(_ => { if (_dropdownVisible) PositionDropdown(); });

            return _searchBar;
        }

        private VisualElement BuildFooter()
        {
            var footer = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 8, paddingRight = 8, paddingTop = 6, paddingBottom = 6,
                    backgroundColor = PanelColor
                }
            };

            _statusLabel = new Label { style = { flexGrow = 1 } };
            footer.Add(_statusLabel);

            footer.Add(GhostButton("Expand all", () => SetAllExpanded(true)));
            footer.Add(GhostButton("Collapse all", () => SetAllExpanded(false)));
            footer.Add(GhostButton("Clear", ClearSelection));
            footer.Add(GhostButton("Refresh", () => { RebuildTree(); BuildUI(); }));

            _extractButton = new Button(ExtractSelected) { text = "Extract" };
            _extractButton.style.marginLeft = 8;
            _extractButton.style.paddingLeft = 14;
            _extractButton.style.paddingRight = 14;
            _extractButton.style.height = 24;
            _extractButton.style.color = Color.white;
            _extractButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            _extractButton.style.backgroundColor = Cobalt;
            _extractButton.style.borderTopLeftRadius = 3;
            _extractButton.style.borderTopRightRadius = 3;
            _extractButton.style.borderBottomLeftRadius = 3;
            _extractButton.style.borderBottomRightRadius = 3;
            footer.Add(_extractButton);

            return footer;
        }

        private static Button GhostButton(string text, Action action)
        {
            var b = new Button(action) { text = text };
            b.style.marginLeft = 3;
            b.style.backgroundColor = Color.clear;
            b.style.borderTopWidth = 0;
            b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0;
            b.style.borderRightWidth = 0;
            b.style.color = MutedColor;
            b.RegisterCallback<MouseEnterEvent>(_ => b.style.color = Pro ? Color.white : Color.black);
            b.RegisterCallback<MouseLeaveEvent>(_ => b.style.color = MutedColor);
            return b;
        }

        private void ShowEmptyState(VisualElement root)
        {
            var box = new VisualElement { style = { flexGrow = 1, justifyContent = Justify.Center, alignItems = Align.Center } };
            bool configured = !string.IsNullOrEmpty(EngageAddressableSettings.GameRuntimePath);
            var label = new Label(configured
                ? $"No .bundle files found under:\n{BuildPath}"
                : "The path to your game dump's settings.json is not set.\nConfigure it in Divine Dragon settings first.");
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.color = MutedColor;
            label.style.marginBottom = 10;
            box.Add(label);
            if (!configured)
                box.Add(new Button(() => SettingsService.OpenProjectSettings("Project/Divine Dragon")) { text = "Open Settings" });
            else
                box.Add(new Button(() => { RebuildTree(); BuildUI(); }) { text = "Refresh" });
            root.Add(box);
        }

        // --- Search / Spotlight dropdown ---------------------------------------------------

        private void OnSearchChanged(string text)
        {
            _search = text ?? string.Empty;
            bool empty = string.IsNullOrEmpty(_search);
            if (_placeholder != null)
                _placeholder.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            if (_clearButton != null)
                _clearButton.style.display = empty ? DisplayStyle.None : DisplayStyle.Flex;

            // Filter the main list as you type. The Spotlight dropdown path below is kept in this
            // file but intentionally not wired up right now.
            RefreshList();
        }

        private void OnSearchKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                _searchField.value = string.Empty;
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                if (_currentMatches.Count > 0)
                    JumpToFile(_currentMatches[0]);
                evt.StopPropagation();
            }
        }

        private void UpdateDropdown()
        {
            _currentMatches.Clear();
            _currentMatches.AddRange(_allFiles
                .Where(Matches)
                .OrderBy(RelativePath, StringComparer.OrdinalIgnoreCase)
                .Take(MaxMatches));

            int totalMatches = _allFiles.Count(Matches);

            _dropdownList.Clear();
            if (_currentMatches.Count == 0)
            {
                _dropdownList.Add(InfoRow("No matches found"));
            }
            else
            {
                foreach (Node file in _currentMatches)
                    _dropdownList.Add(BuildDropdownItem(file));
                if (totalMatches > _currentMatches.Count)
                    _dropdownList.Add(InfoRow($"{totalMatches - _currentMatches.Count} more… refine your search"));
            }

            ShowDropdown();
        }

        private Label InfoRow(string text)
        {
            var label = new Label(text);
            label.style.color = MutedColor;
            label.style.paddingLeft = 12;
            label.style.paddingTop = 8;
            label.style.paddingBottom = 8;
            label.style.unityFontStyleAndWeight = FontStyle.Italic;
            return label;
        }

        private VisualElement BuildDropdownItem(Node file)
        {
            var item = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    paddingLeft = 10, paddingRight = 10, paddingTop = 5, paddingBottom = 5
                }
            };

            var icon = new Image { image = _fileIcon, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            icon.style.width = 15;
            icon.style.height = 15;
            icon.style.marginRight = 8;
            item.Add(icon);

            string rel = RelativePath(file);
            int slash = rel.LastIndexOf('/');
            string dir = slash >= 0 ? rel.Substring(0, slash + 1) : string.Empty;
            string name = slash >= 0 ? rel.Substring(slash + 1) : rel;

            var text = new VisualElement { style = { flexGrow = 1, flexDirection = FlexDirection.Column } };
            text.Add(HighlightedText(name, true, null, 12));
            if (!string.IsNullOrEmpty(dir))
                text.Add(HighlightedText(dir, false, MutedColor, 10));
            item.Add(text);

            item.RegisterCallback<MouseEnterEvent>(_ => item.style.backgroundColor = HoverColor);
            item.RegisterCallback<MouseLeaveEvent>(_ => item.style.backgroundColor = Color.clear);
            // MouseDown (not click) so the jump fires before the field loses focus.
            item.RegisterCallback<MouseDownEvent>(evt => { JumpToFile(file); evt.StopPropagation(); });

            return item;
        }

        // Split text into inline segments so the part matching the search query can be marked.
        // (A UI Toolkit Label is single-styled, so highlighting means several little Labels in a row.)
        private VisualElement HighlightedText(string text, bool bold, Color? baseColor, int fontSize)
        {
            var container = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            AppendHighlighted(container, text, bold, baseColor, fontSize);
            return container;
        }

        // Single-line highlighted text (no wrap, clips), for list rows where the height is fixed.
        private VisualElement HighlightedLine(string text, bool bold, Color? baseColor, int fontSize)
        {
            var line = new VisualElement { style = { flexDirection = FlexDirection.Row, overflow = Overflow.Hidden } };
            AppendHighlighted(line, text, bold, baseColor, fontSize);
            return line;
        }

        // Fill a container with inline segments, marking the parts that match the search query.
        // With no query it's just one plain segment.
        private void AppendHighlighted(VisualElement container, string text, bool bold, Color? baseColor, int fontSize)
        {
            string query = _search;
            if (string.IsNullOrEmpty(query))
            {
                container.Add(Segment(text, bold, baseColor, fontSize, false));
                return;
            }

            int start = 0;
            while (start < text.Length)
            {
                int found = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    container.Add(Segment(text.Substring(start), bold, baseColor, fontSize, false));
                    break;
                }
                if (found > start)
                    container.Add(Segment(text.Substring(start, found - start), bold, baseColor, fontSize, false));
                container.Add(Segment(text.Substring(found, query.Length), bold, baseColor, fontSize, true));
                start = found + query.Length;
            }
        }

        private Label Segment(string s, bool bold, Color? baseColor, int fontSize, bool highlight)
        {
            var label = new Label(s);
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = bold || highlight ? FontStyle.Bold : FontStyle.Normal;
            label.style.marginTop = 0; label.style.marginBottom = 0;
            label.style.marginLeft = 0; label.style.marginRight = 0;
            label.style.paddingTop = 0; label.style.paddingBottom = 0;
            label.style.paddingLeft = 0; label.style.paddingRight = 0;
            if (highlight)
            {
                label.style.color = Color.white;
                label.style.backgroundColor = new Color(Cobalt.r, Cobalt.g, Cobalt.b, 0.85f);
                label.style.borderTopLeftRadius = 2; label.style.borderTopRightRadius = 2;
                label.style.borderBottomLeftRadius = 2; label.style.borderBottomRightRadius = 2;
            }
            else if (baseColor.HasValue)
            {
                label.style.color = baseColor.Value;
            }
            return label;
        }

        private void ShowDropdown()
        {
            HideTypeMenu();
            _dropdown.style.display = DisplayStyle.Flex;
            _dropdownVisible = true;
            PositionDropdown();
        }

        private void HideDropdown()
        {
            if (_dropdown != null)
                _dropdown.style.display = DisplayStyle.None;
            _dropdownVisible = false;
        }

        // Anchor the popup right under the search bar, matched to its width.
        private void PositionDropdown()
        {
            if (_searchBar == null || _dropdown == null)
                return;
            Rect wb = _searchBar.worldBound;
            if (float.IsNaN(wb.x) || wb.width <= 0)
                return;
            Vector2 topLeft = rootVisualElement.WorldToLocal(new Vector2(wb.xMin, wb.yMax + 2f));
            _dropdown.style.left = topLeft.x;
            _dropdown.style.top = topLeft.y;
            _dropdown.style.width = wb.width;
        }

        // --- Type filter (⋮ menu) ----------------------------------------------------------

        private bool FilterActive => _typeFilter.Count > 0;

        private bool IsFileVisible(Node file)
        {
            return !FilterActive || _typeFilter.Contains(file.TypeName ?? UntypedKey);
        }

        private void ToggleTypeMenu()
        {
            if (_typeMenuVisible)
            {
                HideTypeMenu();
                return;
            }
            HideDropdown();
            _typeSearch = string.Empty;
            EnsureTypesLoaded();
            PopulateTypeMenu();
            ShowTypeMenu();
        }

        private void PopulateTypeMenu()
        {
            _typeMenu.Clear();

            var title = new Label("Filter by asset type");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.paddingLeft = 10; title.style.paddingRight = 10;
            title.style.paddingTop = 8; title.style.paddingBottom = 6;
            _typeMenu.Add(title);

            if (!_typesLoaded)
            {
                var info = new Label("Couldn't load the game catalog. Check the console for details.");
                info.style.whiteSpace = WhiteSpace.Normal;
                info.style.color = MutedColor;
                info.style.paddingLeft = 10; info.style.paddingRight = 10; info.style.paddingBottom = 8;
                _typeMenu.Add(info);
                return;
            }

            var typeSearch = new TextField { value = _typeSearch, tooltip = "Filter the type list" };
            typeSearch.style.marginLeft = 8; typeSearch.style.marginRight = 8; typeSearch.style.marginBottom = 4;
            var typeInput = typeSearch.Q("unity-text-input");
            if (typeInput != null) typeInput.style.fontSize = 12;
            typeSearch.RegisterValueChangedCallback(evt => { _typeSearch = evt.newValue ?? string.Empty; RefreshTypeList(); });
            _typeMenu.Add(typeSearch);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 8, paddingBottom = 4 } };
            actions.Add(GhostButton("Select all", () =>
            {
                _typeFilter.Clear();
                _typeFilter.UnionWith(_availableTypes);
                AfterFilterChanged();
            }));
            actions.Add(GhostButton("Clear filter", () =>
            {
                _typeFilter.Clear();
                AfterFilterChanged();
            }));
            _typeMenu.Add(actions);

            _typeMenuList = new ScrollView { style = { maxHeight = 300 } };
            _typeMenu.Add(_typeMenuList);
            RefreshTypeList();
        }

        // Rebuild just the type checkbox rows, honouring the little in-menu search. Kept separate
        // from PopulateTypeMenu so typing in that box doesn't recreate (and unfocus) it.
        private void RefreshTypeList()
        {
            if (_typeMenuList == null)
                return;
            _typeMenuList.Clear();

            List<string> shown = string.IsNullOrEmpty(_typeSearch)
                ? _availableTypes
                : _availableTypes.Where(t => DisplayType(t).IndexOf(_typeSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (shown.Count == 0)
            {
                _typeMenuList.Add(InfoRow("No types match"));
                return;
            }

            foreach (string type in shown)
            {
                string captured = type;
                var toggle = new Toggle { text = DisplayType(type) };
                toggle.SetValueWithoutNotify(_typeFilter.Contains(type));
                toggle.style.paddingLeft = 10; toggle.style.paddingRight = 10;
                toggle.style.paddingTop = 2; toggle.style.paddingBottom = 2;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _typeFilter.Add(captured);
                    else _typeFilter.Remove(captured);
                    UpdateKebabIndicator();
                    RefreshList();
                });
                _typeMenuList.Add(toggle);
            }
        }

        // Re-tick the menu toggles and repaint the tree after a bulk filter change.
        private void AfterFilterChanged()
        {
            UpdateKebabIndicator();
            RefreshList();
            RefreshTypeList();
            PositionTypeMenu();
        }

        private static string DisplayType(string type)
        {
            return type == UntypedKey ? "Other (untyped)" : type;
        }

        private void ShowTypeMenu()
        {
            _typeMenu.style.display = DisplayStyle.Flex;
            _typeMenuVisible = true;
            PositionTypeMenu();
        }

        private void HideTypeMenu()
        {
            if (_typeMenu != null)
                _typeMenu.style.display = DisplayStyle.None;
            _typeMenuVisible = false;
        }

        // Anchor the menu under the ⋮ button, right edge aligned with it.
        private void PositionTypeMenu()
        {
            if (_kebabButton == null || _typeMenu == null)
                return;
            Rect wb = _kebabButton.worldBound;
            if (float.IsNaN(wb.x))
                return;
            const float width = 250f;
            Vector2 topRight = rootVisualElement.WorldToLocal(new Vector2(wb.xMax, wb.yMax + 2f));
            _typeMenu.style.width = width;
            _typeMenu.style.left = Mathf.Max(4f, topRight.x - width);
            _typeMenu.style.top = topRight.y;
        }

        private bool EnsureTypesLoaded()
        {
            if (_typesLoaded)
                return true;

            // Only the very first catalog load costs anything. Once CBT is initialized the map is
            // already in memory, so don't flash a progress bar for a no-op.
            bool needsLoad = !CBT.Initialized;
            if (needsLoad)
                EditorUtility.DisplayProgressBar("Dumper", "Loading game catalog...", 0.35f);
            try
            {
                if (!Dumper.Initialize())
                {
                    EditorUtility.DisplayDialog("Filter by type",
                        "Couldn't load the game catalog. Check the console for details.", "OK");
                    return false;
                }

                AssignTypesFromCatalog();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to load asset types: {ex}");
                EditorUtility.DisplayDialog("Filter by type", $"Failed to load asset types:\n{ex.Message}", "OK");
                return false;
            }
            finally
            {
                if (needsLoad)
                    EditorUtility.ClearProgressBar();
            }
        }

        // Tag every file node with its catalog type. Cheap once CBT is initialized (in-memory lookup).
        private void AssignTypesFromCatalog()
        {
            foreach (Node file in _allFiles)
                file.TypeName = CBT.GetTypeForBundlePath(file.FullPath);
            BuildTypeCounts();
            _typesLoaded = true;
        }

        private void BuildTypeCounts()
        {
            _typeCounts.Clear();
            foreach (Node file in _allFiles)
            {
                string key = file.TypeName ?? UntypedKey;
                _typeCounts[key] = (_typeCounts.TryGetValue(key, out int c) ? c : 0) + 1;
            }

            _availableTypes.Clear();
            _availableTypes.AddRange(_typeCounts.Keys
                .Where(k => k != UntypedKey)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            if (_typeCounts.ContainsKey(UntypedKey))
                _availableTypes.Add(UntypedKey);
        }

        private void UpdateKebabIndicator()
        {
            if (_kebabButton == null)
                return;
            bool active = FilterActive;
            _kebabButton.text = active ? $"⋮ {_typeFilter.Count}" : "⋮";
            _kebabButton.style.color = active ? (StyleColor)Color.white : (StyleColor)MutedColor;
            _kebabButton.style.backgroundColor = active ? (StyleColor)Cobalt : (StyleColor)Color.clear;
        }

        // --- Popup dismissal ---------------------------------------------------------------

        private void OnRootMouseDown(MouseDownEvent evt)
        {
            var target = evt.target as VisualElement;
            if (_dropdownVisible && !IsWithin(target, _dropdown) && !IsWithin(target, _searchBar))
                HideDropdown();
            if (_typeMenuVisible && !IsWithin(target, _typeMenu) && !IsWithin(target, _kebabButton))
                HideTypeMenu();
        }

        private static bool IsWithin(VisualElement element, VisualElement ancestor)
        {
            for (VisualElement e = element; e != null; e = e.parent)
                if (e == ancestor)
                    return true;
            return false;
        }

        // Expand the file's folders, close the search, then scroll to and flash the row.
        private void JumpToFile(Node file)
        {
            for (Node p = file.Parent; p != null && p != _root; p = p.Parent)
                p.Expanded = true;

            _highlightNode = file;
            _searchField.SetValueWithoutNotify(string.Empty);
            _search = string.Empty;
            if (_placeholder != null) _placeholder.style.display = DisplayStyle.Flex;
            if (_clearButton != null) _clearButton.style.display = DisplayStyle.None;
            HideDropdown();

            RefreshList();

            int index = _visibleRows.FindIndex(r => r.Node == file);
            if (index >= 0)
                rootVisualElement.schedule.Execute(() => ScrollToIndex(index)).ExecuteLater(16);

            // Let the highlight linger for a moment, then fade it back to normal.
            rootVisualElement.schedule.Execute(() =>
            {
                if (_highlightNode == file)
                {
                    _highlightNode = null;
                    RefreshList();
                }
            }).ExecuteLater(1600);
        }

        private void ScrollToIndex(int index)
        {
            var scroll = _listView?.Q<ScrollView>();
            if (scroll == null)
                return;
            float viewport = _listView.resolvedStyle.height;
            float target = index * RowHeight - viewport / 2f + RowHeight / 2f;
            scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, Mathf.Max(0f, target));
        }

        // --- Tree rows ---------------------------------------------------------------------

        // Reusable row: [arrow] [icon] [checkbox] [name] [count badge]. bindRow fills it in per node.
        private VisualElement MakeRow()
        {
            var row = new RowElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            row.style.paddingRight = 8;

            var arrow = new Label { name = "arrow" };
            arrow.style.width = 14;
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.color = MutedColor;
            arrow.RegisterCallback<MouseDownEvent>(_ => ToggleExpand(row.userData as Node));
            row.Add(arrow);

            var icon = new Image { name = "icon", scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = 16;
            icon.style.height = 16;
            icon.style.marginRight = 4;
            row.Add(icon);

            var toggle = new Toggle { name = "toggle" };
            toggle.style.marginRight = 2;
            toggle.RegisterValueChangedCallback(evt => OnToggleChanged(row.userData as Node, evt.newValue));
            row.Add(toggle);

            // A container (not a plain Label) so the search match can be highlighted as segments,
            // and can stack into the two-line filename + path entry while searching.
            var label = new VisualElement { name = "label", style = { flexDirection = FlexDirection.Column, justifyContent = Justify.Center, flexShrink = 1, overflow = Overflow.Hidden } };
            label.RegisterCallback<MouseDownEvent>(_ =>
            {
                var node = row.userData as Node;
                if (node != null && node.IsFolder)
                    ToggleExpand(node);
            });
            row.Add(label);

            var typeTag = new Label { name = "type" };
            typeTag.style.color = MutedColor;
            typeTag.style.fontSize = 10;
            typeTag.style.marginLeft = 6;
            row.Add(typeTag);

            row.Add(new VisualElement { name = "spacer", style = { flexGrow = 1 } });

            var badge = new Label { name = "badge" };
            badge.style.fontSize = 10;
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.borderTopLeftRadius = 8;
            badge.style.borderTopRightRadius = 8;
            badge.style.borderBottomLeftRadius = 8;
            badge.style.borderBottomRightRadius = 8;
            badge.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(badge);

            row.RegisterCallback<MouseEnterEvent>(_ => row.style.backgroundColor = HoverColor);
            row.RegisterCallback<MouseLeaveEvent>(_ => row.style.backgroundColor = row.BaseColor);

            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            Row row = _visibleRows[index];
            Node node = row.Node;
            var rowElement = (RowElement)element;
            rowElement.userData = node;
            rowElement.style.paddingLeft = 6 + row.Depth * 14;

            if (node == _highlightNode)
                rowElement.BaseColor = new Color(Cobalt.r, Cobalt.g, Cobalt.b, 0.35f);
            else
                rowElement.BaseColor = index % 2 == 1 ? StripeColor : Color.clear;
            rowElement.style.backgroundColor = rowElement.BaseColor;

            var arrow = element.Q<Label>("arrow");
            var icon = element.Q<Image>("icon");
            var toggle = element.Q<Toggle>("toggle");
            var label = element.Q<VisualElement>("label");
            var typeTag = element.Q<Label>("type");
            var badge = element.Q<Label>("badge");

            arrow.text = node.IsFolder ? ((node.Expanded || FilterActive) ? "▾" : "▸") : string.Empty;
            arrow.visible = node.IsFolder;
            icon.image = node.IsFolder ? _folderIcon : _fileIcon;

            // Tree rows show the plain name. Search rows use the two-line entry (bold filename on
            // top, muted path underneath), both with the matched substring highlighted.
            label.Clear();
            if (SearchActive && !node.IsFolder)
            {
                string rel = RelativePath(node);
                int slash = rel.LastIndexOf('/');
                string dir = slash >= 0 ? rel.Substring(0, slash + 1) : string.Empty;
                string name = slash >= 0 ? rel.Substring(slash + 1) : rel;
                label.Add(HighlightedLine(name, true, null, 12));
                if (!string.IsNullOrEmpty(dir))
                    label.Add(HighlightedLine(dir, false, MutedColor, 10));
            }
            else
            {
                label.Add(HighlightedLine(node.Name, false, null, 12));
            }

            if (node.IsFolder)
            {
                int total = _folderTotal.TryGetValue(node, out int t) ? t : 0;
                int sel = _folderSelected.TryGetValue(node, out int s) ? s : 0;
                toggle.SetValueWithoutNotify(sel == total && total > 0);

                typeTag.text = string.Empty;
                badge.style.display = DisplayStyle.Flex;
                if (sel > 0)
                {
                    badge.text = $"{sel} / {total}";
                    badge.style.color = Color.white;
                    badge.style.backgroundColor = Cobalt;
                }
                else
                {
                    badge.text = total.ToString();
                    badge.style.color = MutedColor;
                    badge.style.backgroundColor = Pro ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.08f);
                }
            }
            else
            {
                toggle.SetValueWithoutNotify(node.Selected);
                typeTag.text = _typesLoaded ? (node.TypeName ?? string.Empty) : string.Empty;
                badge.style.display = DisplayStyle.None;
                badge.text = string.Empty;
            }
        }

        private void OnToggleChanged(Node node, bool value)
        {
            if (node == null)
                return;
            if (node.IsFolder)
                SetSelectedRecursive(node, value);
            else
                node.Selected = value;
            RefreshList();
        }

        // Checking a folder only touches the bundles you can currently see (respects the type filter).
        private void SetSelectedRecursive(Node folder, bool value)
        {
            foreach (Node child in folder.Children)
            {
                if (child.IsFolder)
                    SetSelectedRecursive(child, value);
                else if (IsFileVisible(child))
                    child.Selected = value;
            }
        }

        private void ToggleExpand(Node node)
        {
            if (node == null || !node.IsFolder || FilterActive)
                return;
            node.Expanded = !node.Expanded;
            RefreshList();
        }

        private void SetAllExpanded(bool expanded)
        {
            SetExpandedRecursive(_root, expanded);
            RefreshList();
        }

        private static void SetExpandedRecursive(Node node, bool expanded)
        {
            foreach (Node child in node.Children)
            {
                if (!child.IsFolder)
                    continue;
                child.Expanded = expanded;
                SetExpandedRecursive(child, expanded);
            }
        }

        private void ClearSelection()
        {
            foreach (Node file in _allFiles)
                file.Selected = false;
            RefreshList();
        }

        // Recompute counts, rebuild the flattened visible list, repaint.
        private void RefreshList()
        {
            _folderTotal.Clear();
            _folderSelected.Clear();
            CountFolder(_root);

            _visibleRows.Clear();
            if (SearchActive)
                BuildFlatSearchRows();
            else
                BuildTreeRows(_root, 0);

            if (_listView != null)
            {
                // Taller rows while searching so the two-line filename + path entry fits.
                int rowHeight = SearchActive ? SearchRowHeight : (int)RowHeight;
                if (_listView.itemHeight != rowHeight)
                    _listView.itemHeight = rowHeight;
                _listView.itemsSource = _visibleRows;
                _listView.Refresh();
            }

            int total = _allFiles.Count(f => f.Selected);
            if (_statusLabel != null)
            {
                _statusLabel.text = total > 0
                    ? $"{total} bundle{(total == 1 ? string.Empty : "s")} selected"
                    : "Nothing selected";
                _statusLabel.style.color = total > 0 ? (StyleColor)Color.white : (StyleColor)MutedColor;
                _statusLabel.style.unityFontStyleAndWeight = total > 0 ? FontStyle.Bold : FontStyle.Normal;
            }
            if (_extractButton != null)
            {
                _extractButton.text = total > 0 ? $"Extract  ({total})" : "Extract";
                _extractButton.SetEnabled(total > 0);
            }
        }

        // Per-folder visible + selected counts, both aware of the active type filter.
        private (int total, int selected) CountFolder(Node node)
        {
            if (!node.IsFolder)
            {
                bool visible = IsFileVisible(node);
                return (visible ? 1 : 0, visible && node.Selected ? 1 : 0);
            }

            int total = 0, selected = 0;
            foreach (Node child in node.Children)
            {
                var counts = CountFolder(child);
                total += counts.total;
                selected += counts.selected;
            }
            _folderTotal[node] = total;
            _folderSelected[node] = selected;
            return (total, selected);
        }

        // Emit folder rows and recurse. With a filter on, force branches open and prune folders
        // that hold nothing matching.
        private bool BuildTreeRows(Node folder, int depth)
        {
            bool emitted = false;
            foreach (Node child in folder.Children)
            {
                if (child.IsFolder)
                {
                    int before = _visibleRows.Count;
                    _visibleRows.Add(new Row { Node = child, Depth = depth });
                    bool sub = (FilterActive || child.Expanded) && BuildTreeRows(child, depth + 1);
                    if (FilterActive && !sub)
                        _visibleRows.RemoveRange(before, _visibleRows.Count - before);
                    else
                        emitted = true;
                }
                else if (IsFileVisible(child))
                {
                    _visibleRows.Add(new Row { Node = child, Depth = depth });
                    emitted = true;
                }
            }
            return emitted;
        }

        private bool SearchActive => !string.IsNullOrEmpty(_search);

        // While searching we drop the folder hierarchy and just list the matching bundles flat,
        // each showing its relative path so they stay easy to tell apart. Respects the type filter.
        private void BuildFlatSearchRows()
        {
            foreach (Node file in _allFiles
                .Where(f => Matches(f) && IsFileVisible(f))
                .OrderBy(RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                _visibleRows.Add(new Row { Node = file, Depth = 0 });
            }
        }

        private bool Matches(Node file)
        {
            return file.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || RelativePath(file).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string RelativePath(Node file)
        {
            string basePath = BuildPath;
            return file.FullPath != null && file.FullPath.Length > basePath.Length
                ? file.FullPath.Substring(basePath.Length).TrimStart('/')
                : file.Name;
        }

        private void ExtractSelected()
        {
            string[] paths = _allFiles.Where(f => f.Selected).Select(f => f.FullPath).ToArray();
            if (paths.Length == 0)
            {
                EditorUtility.DisplayDialog("Extract Bundles", "Select at least one bundle to extract.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Extract Bundles",
                    $"Extract {paths.Length} bundle{(paths.Length == 1 ? string.Empty : "s")} (dependencies included)?",
                    "Extract", "Cancel"))
                return;

            EditorUtility.DisplayProgressBar("Dumper", "Starting AssetRipper...", 0.1f);
            try
            {
                if (!Dumper.Initialize())
                {
                    EditorUtility.DisplayDialog("Error", "Dumper failed to initialize. Check the console for details.", "OK");
                    return;
                }

                bool success = Dumper.ExtractAssets(paths);
                EditorUtility.DisplayDialog(success ? "Success" : "Error",
                    success ? "Assets extracted successfully." : "Failed to extract assets. Check the console for details.",
                    "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error running AssetRipper: {ex}");
                EditorUtility.DisplayDialog("Error", $"Error running AssetRipper:\n{ex.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
