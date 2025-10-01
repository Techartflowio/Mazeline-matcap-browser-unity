/*
 * ============================================================================
 * Matcap Library Window - Unity Editor Extension (Refactored)
 * ============================================================================
 * 
 * [개요]
 * Unity 에디터에서 MatCap 텍스처를 검색, 미리보기, 다운로드할 수 있는
 * 전문 브라우저 윈도우입니다.
 * 
 * [주요 기능]
 * - GitHub 레포지토리에서 600개 이상의 MatCap 텍스처 자동 로드
 * - 실시간 검색 및 필터링
 * - 그리드/리스트 뷰 모드 전환
 * - 1024px 고정 해상도 다운로드
 * - 스마트 캐싱 시스템 (7일 유효기간)
 * - 자동 Material 생성
 * 
 * [리팩토링 개선사항]
 * - SOLID 원칙 적용
 * - 서비스 레이어 분리 (CacheService, GitHubService, DownloadService)
 * - 데이터 모델 분리 (MatcapItem, CacheEntry, MatcapCacheIndex)
 * - UI 로직과 비즈니스 로직 분리
 * 
 * ============================================================================
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ML.Editor.MatcapBrowser.Core;
using ML.Editor.MatcapBrowser.Services;
using ML.Editor.MatcapBrowser.Utilities;

namespace ML.Editor.MatcapBrowser.UI
{
    /// <summary>
    /// MatCap 라이브러리 브라우저 에디터 윈도우
    /// </summary>
    public class MatcapLibraryWindow : EditorWindow
    {
        #region Services
        
        private CacheService _cacheService;
        private GitHubService _githubService;
        private DownloadService _downloadService;
        
        #endregion
        
        #region UI State
        
        private Vector2 _scrollPosition;
        private List<MatcapItem> _matcapItems = new List<MatcapItem>();
        private List<MatcapItem> _filteredItems = new List<MatcapItem>();
        private Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();
        
        private bool _isLoading = false;
        private string _statusMessage = "";
        private string _searchFilter = "";
        private int _loadedPreviewCount = 0;
        
        private ViewMode _currentViewMode = ViewMode.Grid;
        private SortMode _currentSortMode = SortMode.Name;
        private bool _sortAscending = true;
        private bool _showFilters = false;
        
        private MatcapItem _selectedItem = null;
        private MatcapItem _hoveredItem = null;
        
        private float _thumbnailSize = 100f;
        private int _itemsPerRow = 4;
        
        #endregion
        
        #region Constants
        
        private const float HeaderHeight = 60f;
        private const float ToolbarHeight = 35f;
        private const float SearchBarHeight = 25f;
        private const float StatusBarHeight = 22f;
        private const float Spacing = 8f;
        private const float BorderWidth = 1f;
        private const int FixedResolution = 1024;
        
        private static readonly Color HeaderColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color ToolbarColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color BorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.9f, 0.8f);
        private static readonly Color HoverColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        
        #endregion
        
        #region Enums
        
        private enum ViewMode { Grid, List }
        private enum SortMode { Name, Size, DateAdded, Downloaded }
        
        #endregion
        
        #region Coroutine Management
        
        private EditorCoroutine _loadingCoroutine;
        
        #endregion
        
        #region Unity Menu & Lifecycle
        
        [MenuItem("Window/Matcap Library")]
        public static void ShowWindow()
        {
            var window = GetWindow<MatcapLibraryWindow>("Matcap Library");
            window.minSize = new Vector2(500, 400);
        }
        
        private void OnEnable()
        {
            InitializeServices();
            LoadMatcapList();
        }
        
        private void OnDisable()
        {
            Cleanup();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeServices()
        {
            _cacheService = new CacheService();
            _cacheService.Initialize();
            
            _githubService = new GitHubService();
            
            _downloadService = new DownloadService();
            _downloadService.DownloadPath = "Assets/Matcaps";
        }
        
        private void Cleanup()
        {
            if (_loadingCoroutine != null)
            {
                EditorCoroutine.Stop(_loadingCoroutine);
                _loadingCoroutine = null;
            }
            
            foreach (var texture in _previewCache.Values)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
            }
            _previewCache.Clear();
        }
        
        #endregion
        
        #region GUI Drawing
        
        private void OnGUI()
        {
            Rect headerRect = new Rect(0, 0, position.width, HeaderHeight);
            Rect toolbarRect = new Rect(0, HeaderHeight, position.width, ToolbarHeight);
            Rect searchRect = new Rect(0, HeaderHeight + ToolbarHeight, position.width, SearchBarHeight + Spacing);
            Rect contentRect = new Rect(0, HeaderHeight + ToolbarHeight + SearchBarHeight + Spacing, 
                                       position.width, position.height - HeaderHeight - ToolbarHeight - SearchBarHeight - StatusBarHeight - Spacing * 2);
            Rect statusRect = new Rect(0, position.height - StatusBarHeight, position.width, StatusBarHeight);
            
            DrawHeader(headerRect);
            DrawToolbar(toolbarRect);
            DrawSearchBar(searchRect);
            
            GUILayout.BeginArea(contentRect);
            {
                if (_isLoading)
                {
                    DrawLoadingMessage();
                }
                else if (_filteredItems.Count == 0 && _matcapItems.Count == 0)
                {
                    DrawEmptyMessage();
                }
                else
                {
                    if (_currentViewMode == ViewMode.Grid)
                        DrawMatcapGrid();
                    else
                        DrawMatcapList();
                }
            }
            GUILayout.EndArea();
            
            DrawStatusBar(statusRect);
            HandleKeyboardShortcuts();
        }
        
        #endregion
        
        #region UI Components - Header
        
        private void DrawHeader(Rect rect)
        {
            EditorGUI.DrawRect(rect, HeaderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - BorderWidth, rect.width, BorderWidth), BorderColor);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(Spacing);
                    
                    GUILayout.BeginVertical();
                    {
                        GUILayout.Space(8);
                        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
                        titleStyle.fontSize = 16;
                        titleStyle.normal.textColor = Color.white;
                        GUILayout.Label("Matcap Library", titleStyle);
                        
                        GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel);
                        subtitleStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                        GUILayout.Label("Professional matcap browser and manager", subtitleStyle);
                    }
                    GUILayout.EndVertical();
                    
                    GUILayout.FlexibleSpace();
                    
                    GUILayout.BeginVertical();
                    {
                        GUILayout.Space(12);
                        GUILayout.BeginHorizontal();
                        {
                            // Connection status
                            Color statusColor = _isLoading ? Color.yellow : 
                                               (_matcapItems.Count > 0 ? Color.green : Color.red);
                            GUI.color = statusColor;
                            GUILayout.Label("●", GUILayout.Width(12));
                            GUI.color = Color.white;
                            
                            // Settings button
                            GUIStyle buttonStyle = new GUIStyle(EditorStyles.miniButton);
                            buttonStyle.normal.textColor = Color.white;
                            if (GUILayout.Button("Settings", buttonStyle, GUILayout.Width(60), GUILayout.Height(20)))
                            {
                                MatcapLibrarySettings.ShowWindow(this);
                            }
                            
                            if (GUILayout.Button("Help", buttonStyle, GUILayout.Width(40), GUILayout.Height(20)))
                            {
                                ShowHelp();
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                    
                    GUILayout.Space(Spacing);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region UI Components - Toolbar
        
        private void DrawToolbar(Rect rect)
        {
            EditorGUI.DrawRect(rect, ToolbarColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - BorderWidth, rect.width, BorderWidth), BorderColor);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(Spacing);
                    
                    // View mode buttons
                    GUIStyle toggleStyle = new GUIStyle(EditorStyles.miniButton);
                    toggleStyle.normal.textColor = Color.white;
                    
                    GUI.color = _currentViewMode == ViewMode.Grid ? SelectedColor : Color.white;
                    if (GUILayout.Button("Grid", toggleStyle, GUILayout.Width(45), GUILayout.Height(25)))
                    {
                        _currentViewMode = ViewMode.Grid;
                    }
                    
                    GUI.color = _currentViewMode == ViewMode.List ? SelectedColor : Color.white;
                    if (GUILayout.Button("List", toggleStyle, GUILayout.Width(45), GUILayout.Height(25)))
                    {
                        _currentViewMode = ViewMode.List;
                    }
                    GUI.color = Color.white;
                    
                    GUILayout.Space(Spacing);
                    
                    // Sort options
                    GUILayout.Label("Sort:", EditorStyles.miniLabel, GUILayout.Width(30));
                    SortMode newSortMode = (SortMode)EditorGUILayout.EnumPopup(_currentSortMode, GUILayout.Width(80));
                    if (newSortMode != _currentSortMode)
                    {
                        _currentSortMode = newSortMode;
                        SortMatcaps();
                    }
                    
                    string sortIcon = _sortAscending ? "↑" : "↓";
                    if (GUILayout.Button(sortIcon, toggleStyle, GUILayout.Width(20), GUILayout.Height(25)))
                    {
                        _sortAscending = !_sortAscending;
                        SortMatcaps();
                    }
                    
                    GUILayout.FlexibleSpace();
                    
                    // Quick actions
                    if (GUILayout.Button("Refresh", toggleStyle, GUILayout.Width(55), GUILayout.Height(25)))
                    {
                        LoadMatcapList();
                    }
                    
                    if (GUILayout.Button("Test", toggleStyle, GUILayout.Width(40), GUILayout.Height(25)))
                    {
                        TestGitHubConnection();
                    }
                    
                    if (GUILayout.Button("Cache", toggleStyle, GUILayout.Width(50), GUILayout.Height(25)))
                    {
                        ShowCacheInfo();
                    }
                    
                    if (GUILayout.Button("Download All", toggleStyle, GUILayout.Width(85), GUILayout.Height(25)))
                    {
                        DownloadAllMatcaps();
                    }
                    
                    GUILayout.Space(Spacing);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region UI Components - Search Bar
        
        private void DrawSearchBar(Rect rect)
        {
            rect.y += Spacing / 2;
            rect.height = SearchBarHeight;
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(Spacing);
                    
                    GUILayout.Label("Search:", GUILayout.Width(50));
                    
                    GUIStyle searchStyle = new GUIStyle(EditorStyles.textField);
                    searchStyle.margin = new RectOffset(2, 2, 2, 2);
                    
                    string newSearch = GUILayout.TextField(_searchFilter, searchStyle);
                    if (newSearch != _searchFilter)
                    {
                        _searchFilter = newSearch;
                        FilterMatcaps();
                    }
                    
                    if (!string.IsNullOrEmpty(_searchFilter))
                    {
                        if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(40)))
                        {
                            _searchFilter = "";
                            FilterMatcaps();
                        }
                    }
                    
                    GUILayout.Space(Spacing);
                    
                    // Thumbnail size slider
                    GUILayout.Label("Size:", EditorStyles.miniLabel, GUILayout.Width(30));
                    float newSize = GUILayout.HorizontalSlider(_thumbnailSize, 60, 200, GUILayout.Width(80));
                    if (newSize != _thumbnailSize)
                    {
                        _thumbnailSize = newSize;
                    }
                    
                    GUILayout.Space(Spacing);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region UI Components - Content
        
        private void DrawMatcapGrid()
        {
            List<MatcapItem> itemsToDisplay = GetSortedAndFilteredItems();
            
            float contentWidth = position.width - 40;
            float itemWidth = _thumbnailSize + Spacing;
            _itemsPerRow = Mathf.Max(1, Mathf.FloorToInt(contentWidth / itemWidth));
            
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            {
                for (int i = 0; i < itemsToDisplay.Count; i += _itemsPerRow)
                {
                    GUILayout.BeginHorizontal();
                    {
                        for (int j = 0; j < _itemsPerRow && i + j < itemsToDisplay.Count; j++)
                        {
                            DrawMatcapGridItem(itemsToDisplay[i + j]);
                        }
                        GUILayout.FlexibleSpace();
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(Spacing);
                }
                
                GUILayout.Space(20);
            }
            GUILayout.EndScrollView();
        }
        
        private void DrawMatcapGridItem(MatcapItem item)
        {
            float itemSize = _thumbnailSize;
            Rect itemRect = GUILayoutUtility.GetRect(itemSize, itemSize + 30);
            
            bool isHovered = itemRect.Contains(Event.current.mousePosition);
            bool isSelected = _selectedItem == item;
            
            if (isHovered) _hoveredItem = item;
            
            Color bgColor = isSelected ? SelectedColor : (isHovered ? HoverColor : Color.clear);
            if (bgColor != Color.clear)
            {
                EditorGUI.DrawRect(itemRect, bgColor);
            }
            
            // Preview
            Rect previewRect = new Rect(itemRect.x + 4, itemRect.y + 4, itemSize - 8, itemSize - 8);
            
            if (item.preview != null)
            {
                GUI.DrawTexture(previewRect, item.preview, ScaleMode.ScaleToFit);
                
                if (item.isDownloaded)
                {
                    Rect checkRect = new Rect(previewRect.x + previewRect.width - 20, previewRect.y + 4, 16, 16);
                    GUI.color = Color.green;
                    GUI.Label(checkRect, "✓");
                    GUI.color = Color.white;
                }
                else if (item.isDownloading)
                {
                    DrawLoadingOverlay(previewRect);
                }
            }
            else
            {
                DrawPreviewPlaceholder(previewRect);
            }
            
            // Name
            DrawItemName(item, itemRect, itemSize);
            
            // Handle events
            HandleItemEvents(item, itemRect);
        }
        
        private void DrawLoadingOverlay(Rect rect)
        {
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = Color.white;
            
            GUIStyle loadingStyle = new GUIStyle(EditorStyles.boldLabel);
            loadingStyle.alignment = TextAnchor.MiddleCenter;
            loadingStyle.normal.textColor = Color.white;
            GUI.Label(rect, "⟳", loadingStyle);
        }
        
        private void DrawPreviewPlaceholder(Rect rect)
        {
            GUI.color = new Color(0.3f, 0.3f, 0.3f, 1f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = Color.white;
            
            GUIStyle placeholderStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            placeholderStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(rect, "Loading...", placeholderStyle);
        }
        
        private void DrawItemName(MatcapItem item, Rect itemRect, float itemSize)
        {
            Rect nameRect = new Rect(itemRect.x + 2, itemRect.y + itemSize - 6, itemSize - 4, 20);
            
            string displayName = item.name;
            int maxChars = Mathf.FloorToInt(itemSize / 7);
            if (displayName.Length > maxChars)
            {
                displayName = displayName.Substring(0, maxChars - 3) + "...";
            }
            
            GUIStyle nameStyle = new GUIStyle(EditorStyles.miniLabel);
            nameStyle.alignment = TextAnchor.MiddleCenter;
            nameStyle.fontSize = 9;
            
            if (item.isDownloaded)
            {
                nameStyle.normal.textColor = new Color(0.6f, 1f, 0.6f, 1f);
            }
            
            GUI.Label(nameRect, displayName, nameStyle);
        }
        
        private void HandleItemEvents(MatcapItem item, Rect itemRect)
        {
            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0) // Left click
                {
                    _selectedItem = item;
                    if (Event.current.clickCount == 2) // Double click
                    {
                        if (!item.isDownloading)
                        {
                            DownloadMatcap(item);
                        }
                    }
                    Event.current.Use();
                }
                else if (Event.current.button == 1) // Right click
                {
                    ShowContextMenu(item);
                    Event.current.Use();
                }
            }
        }
        
        private void DrawMatcapList()
        {
            List<MatcapItem> itemsToDisplay = GetSortedAndFilteredItems();
            
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            {
                // Header
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    GUILayout.Label("Preview", EditorStyles.miniLabel, GUILayout.Width(60));
                    GUILayout.Label("Name", EditorStyles.miniLabel, GUILayout.Width(200));
                    GUILayout.Label("Status", EditorStyles.miniLabel, GUILayout.Width(80));
                    GUILayout.Label("Actions", EditorStyles.miniLabel);
                }
                GUILayout.EndHorizontal();
                
                // Items
                for (int i = 0; i < itemsToDisplay.Count; i++)
                {
                    DrawMatcapListItem(itemsToDisplay[i], i);
                }
            }
            GUILayout.EndScrollView();
        }
        
        private void DrawMatcapListItem(MatcapItem item, int index)
        {
            bool isSelected = _selectedItem == item;
            
            GUIStyle rowStyle = new GUIStyle();
            if (index % 2 == 0)
            {
                rowStyle.normal.background = EditorGUIUtility.whiteTexture;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.1f);
            }
            
            if (isSelected)
            {
                GUI.color = SelectedColor;
            }
            
            GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(30));
            {
                GUI.color = Color.white;
                
                // Preview
                if (item.preview != null)
                {
                    GUILayout.Label(item.preview, GUILayout.Width(30), GUILayout.Height(30));
                }
                else
                {
                    GUILayout.Box("", GUILayout.Width(30), GUILayout.Height(30));
                }
                
                GUILayout.Label(item.name, EditorStyles.label, GUILayout.Width(200));
                
                // Status
                string status = item.isDownloading ? "Downloading..." : 
                               item.isDownloaded ? "Downloaded" : "Available";
                Color statusColor = item.isDownloading ? Color.yellow :
                                   item.isDownloaded ? Color.green : Color.white;
                
                GUI.color = statusColor;
                GUILayout.Label(status, EditorStyles.miniLabel, GUILayout.Width(80));
                GUI.color = Color.white;
                
                // Actions
                GUILayout.FlexibleSpace();
                
                if (!item.isDownloading)
                {
                    if (GUILayout.Button(item.isDownloaded ? "Re-download" : "Download", 
                                       EditorStyles.miniButton, GUILayout.Width(80)))
                    {
                        DownloadMatcap(item);
                    }
                }
                else
                {
                    GUILayout.Label("⟳", GUILayout.Width(80));
                }
            }
            GUILayout.EndHorizontal();
            
            if (Event.current.type == EventType.MouseDown && 
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                _selectedItem = item;
                Event.current.Use();
            }
        }
        
        private void DrawLoadingMessage()
        {
            GUILayout.FlexibleSpace();
            
            string[] spinChars = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            int spinIndex = (int)(EditorApplication.timeSinceStartup * 10) % spinChars.Length;
            
            GUIStyle loadingStyle = new GUIStyle(EditorStyles.boldLabel);
            loadingStyle.fontSize = 18;
            loadingStyle.alignment = TextAnchor.MiddleCenter;
            
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{spinChars[spinIndex]} Loading matcaps...", loadingStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            GUIStyle progressStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            GUILayout.Label($"{_loadedPreviewCount} of {_matcapItems.Count} previews loaded", progressStyle);
            
            GUILayout.Space(20);
            
            Rect progressRect = GUILayoutUtility.GetRect(300, 20);
            progressRect.x = (position.width - 300) / 2;
            
            float progress = _matcapItems.Count > 0 ? (float)_loadedPreviewCount / _matcapItems.Count : 0;
            EditorGUI.ProgressBar(progressRect, progress, $"{(progress * 100):F0}%");
            
            GUILayout.FlexibleSpace();
            Repaint();
        }
        
        private void DrawEmptyMessage()
        {
            GUILayout.FlexibleSpace();
            
            GUIStyle iconStyle = new GUIStyle(EditorStyles.boldLabel);
            iconStyle.fontSize = 48;
            iconStyle.alignment = TextAnchor.MiddleCenter;
            iconStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("📦", iconStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            GUIStyle messageStyle = new GUIStyle(EditorStyles.boldLabel);
            messageStyle.fontSize = 16;
            messageStyle.alignment = TextAnchor.MiddleCenter;
            messageStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("No matcaps found", messageStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            
            GUIStyle subMessageStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            subMessageStyle.fontSize = 12;
            
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Click the Refresh button in the toolbar to load matcaps", subMessageStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.FlexibleSpace();
        }
        
        #endregion
        
        #region UI Components - Status Bar
        
        private void DrawStatusBar(Rect rect)
        {
            EditorGUI.DrawRect(rect, ToolbarColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, BorderWidth), BorderColor);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(Spacing);
                    
                    List<MatcapItem> displayItems = GetSortedAndFilteredItems();
                    int downloadedCount = _matcapItems.Count(m => m.isDownloaded);
                    int downloadingCount = _matcapItems.Count(m => m.isDownloading);
                    
                    GUIStyle statsStyle = new GUIStyle(EditorStyles.miniLabel);
                    statsStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                    
                    GUILayout.Label($"Total: {_matcapItems.Count}", statsStyle);
                    GUILayout.Label("•", statsStyle, GUILayout.Width(10));
                    GUILayout.Label($"Showing: {displayItems.Count}", statsStyle);
                    GUILayout.Label("•", statsStyle, GUILayout.Width(10));
                    GUILayout.Label($"Downloaded: {downloadedCount}", statsStyle);
                    
                    if (downloadingCount > 0)
                    {
                        GUILayout.Label("•", statsStyle, GUILayout.Width(10));
                        GUI.color = Color.yellow;
                        GUILayout.Label($"Downloading: {downloadingCount}", statsStyle);
                        GUI.color = Color.white;
                    }
                    
                    GUILayout.FlexibleSpace();
                    
                    if (!string.IsNullOrEmpty(_statusMessage))
                    {
                        GUIStyle messageStyle = new GUIStyle(EditorStyles.miniLabel);
                        messageStyle.normal.textColor = Color.white;
                        GUILayout.Label(_statusMessage, messageStyle);
                    }
                    
                    if (_selectedItem != null)
                    {
                        GUILayout.Label("•", statsStyle, GUILayout.Width(10));
                        GUILayout.Label($"Selected: {_selectedItem.name}", statsStyle);
                    }
                    
                    GUILayout.Space(Spacing);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region MatCap Loading
        
        public void LoadMatcapList()
        {
            if (_loadingCoroutine != null)
            {
                EditorCoroutine.Stop(_loadingCoroutine);
            }
            
            _isLoading = true;
            _loadedPreviewCount = 0;
            _statusMessage = "Loading matcap list...";
            _matcapItems.Clear();
            _filteredItems.Clear();
            _previewCache.Clear();
            
            _loadingCoroutine = EditorCoroutine.Start(LoadMatcapsCoroutine());
        }
        
        private IEnumerator LoadMatcapsCoroutine()
        {
            bool loaded = false;
            List<string> fileNames = new List<string>();
            
            // Try GitHub API first
            yield return _githubService.FetchMatcapListFromAPI(
                onSuccess: files => {
                    fileNames = files;
                    loaded = true;
                },
                onError: error => Debug.LogWarning($"API failed: {error}")
            );
            
            // Fallback to page scraping
            if (!loaded)
            {
                yield return _githubService.FetchMatcapListFromPage(
                    onSuccess: files => {
                        fileNames = files;
                        loaded = true;
                    },
                    onError: error => Debug.LogError($"Page scraping failed: {error}")
                );
            }
            
            if (!loaded)
            {
                ShowNetworkError();
                _isLoading = false;
                yield break;
            }
            
            // Create MatcapItems
            foreach (string fileName in fileNames)
            {
                MatcapItem item = new MatcapItem(fileName);
                item.isDownloaded = _downloadService.IsDownloaded(fileName);
                _matcapItems.Add(item);
            }
            
            FilterMatcaps();
            
            // Load previews
            foreach (var item in _matcapItems)
            {
                EditorCoroutine.Start(LoadPreviewCoroutine(item));
            }
            
            _statusMessage = $"Loading {_matcapItems.Count} matcaps...";
            
            while (_loadedPreviewCount < _matcapItems.Count)
            {
                yield return null;
            }
            
            _isLoading = false;
            _statusMessage = $"Loaded {_matcapItems.Count} matcaps";
        }
        
        private IEnumerator LoadPreviewCoroutine(MatcapItem item)
        {
            // Try cache first
            Texture2D cachedTexture = _cacheService.LoadFromCache(item.fileName);
            if (cachedTexture != null)
            {
                item.preview = cachedTexture;
                _previewCache[item.fileName] = item.preview;
                _loadedPreviewCount++;
                Repaint();
                yield break;
            }
            
            // Download from GitHub
            yield return _githubService.DownloadPreview(item.fileName,
                onSuccess: texture => {
                    item.preview = texture;
                    _previewCache[item.fileName] = texture;
                    _cacheService.SaveToCache(item.fileName, texture);
                },
                onError: error => {
                    Debug.LogWarning($"Failed to load preview for {item.name}: {error}");
                }
            );
            
            _loadedPreviewCount++;
            Repaint();
        }
        
        private void ShowNetworkError()
        {
            _statusMessage = "Network error: Unable to connect to GitHub";
            
            if (EditorUtility.DisplayDialog("Network Error", 
                "Failed to load matcap library from GitHub.\n\n" +
                "Please check:\n" +
                "• Internet connection\n" +
                "• Firewall settings\n" +
                "• GitHub accessibility\n\n" +
                "Would you like to retry?", 
                "Retry", "Cancel"))
            {
                LoadMatcapList();
            }
        }
        
        #endregion
        
        #region MatCap Download
        
        private void DownloadMatcap(MatcapItem item)
        {
            EditorCoroutine.Start(DownloadMatcapCoroutine(item));
        }
        
        private IEnumerator DownloadMatcapCoroutine(MatcapItem item)
        {
            item.isDownloading = true;
            _statusMessage = $"Downloading {item.name}...";
            Repaint();
            
            yield return _githubService.DownloadMatcap(item.fileName, FixedResolution,
                onSuccess: texture => {
                    if (_downloadService.SaveTexture(texture, item.fileName))
                    {
                        item.isDownloaded = true;
                        _statusMessage = $"Downloaded {item.name} (1024px)";
                    }
                    else
                    {
                        _statusMessage = $"Failed to save {item.name}";
                    }
                },
                onError: error => {
                    _statusMessage = $"Failed to download {item.name}: {error}";
                    Debug.LogError($"Download error: {error}");
                }
            );
            
            item.isDownloading = false;
            Repaint();
        }
        
        public void DownloadAllMatcaps()
        {
            EditorCoroutine.Start(DownloadAllMatcapsCoroutine());
        }
        
        private IEnumerator DownloadAllMatcapsCoroutine()
        {
            List<MatcapItem> itemsToDownload = _matcapItems.Where(item => !item.isDownloaded).ToList();
            
            for (int i = 0; i < itemsToDownload.Count; i++)
            {
                var item = itemsToDownload[i];
                _statusMessage = $"Downloading {i + 1}/{itemsToDownload.Count}: {item.name}";
                yield return DownloadMatcapCoroutine(item);
            }
            
            _statusMessage = $"Downloaded all matcaps";
        }
        
        #endregion
        
        #region Filtering & Sorting
        
        private void FilterMatcaps()
        {
            if (string.IsNullOrEmpty(_searchFilter))
            {
                _filteredItems = new List<MatcapItem>(_matcapItems);
            }
            else
            {
                _filteredItems = _matcapItems.Where(item => 
                    item.name.ToLower().Contains(_searchFilter.ToLower())).ToList();
            }
            Repaint();
        }
        
        private void SortMatcaps()
        {
            Repaint();
        }
        
        private List<MatcapItem> GetSortedAndFilteredItems()
        {
            List<MatcapItem> items = string.IsNullOrEmpty(_searchFilter) ? 
                new List<MatcapItem>(_matcapItems) : 
                new List<MatcapItem>(_filteredItems);
            
            switch (_currentSortMode)
            {
                case SortMode.Name:
                    items.Sort((a, b) => _sortAscending ? 
                        string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase) :
                        string.Compare(b.name, a.name, StringComparison.OrdinalIgnoreCase));
                    break;
                    
                case SortMode.Downloaded:
                    items.Sort((a, b) => _sortAscending ?
                        a.isDownloaded.CompareTo(b.isDownloaded) :
                        b.isDownloaded.CompareTo(a.isDownloaded));
                    break;
                    
                case SortMode.Size:
                    items.Sort((a, b) => _sortAscending ?
                        a.fileName.Length.CompareTo(b.fileName.Length) :
                        b.fileName.Length.CompareTo(a.fileName.Length));
                    break;
            }
            
            return items;
        }
        
        #endregion
        
        #region Context Menu & Actions
        
        private void ShowContextMenu(MatcapItem item)
        {
            GenericMenu menu = new GenericMenu();
            
            if (!item.isDownloading)
            {
                menu.AddItem(new GUIContent("Download"), false, () => DownloadMatcap(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Download"));
            }
            
            if (item.isDownloaded)
            {
                menu.AddItem(new GUIContent("Re-download"), false, () => DownloadMatcap(item));
                menu.AddItem(new GUIContent("Show in Project"), false, () => ShowInProject(item));
                menu.AddItem(new GUIContent("Create Material"), false, () => CreateMaterialForItem(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Show in Project"));
                menu.AddDisabledItem(new GUIContent("Create Material"));
            }
            
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy Name"), false, () => EditorGUIUtility.systemCopyBuffer = item.name);
            
            menu.ShowAsContext();
        }
        
        private void ShowInProject(MatcapItem item)
        {
            string path = System.IO.Path.Combine(_downloadService.DownloadPath, $"Matcap_{item.fileName}");
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }
        
        private void CreateMaterialForItem(MatcapItem item)
        {
            Material material = _downloadService.CreateMaterial(item.fileName, item.name);
            if (material != null)
            {
                Selection.activeObject = material;
                EditorGUIUtility.PingObject(material);
                _statusMessage = $"Created material for {item.name}";
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        public void TestGitHubConnection()
        {
            EditorCoroutine.Start(_githubService.TestConnection(result => {
                Debug.Log(result);
                _statusMessage = "Connection test completed. Check Console.";
            }));
        }
        
        public void ShowCacheInfo()
        {
            var stats = _cacheService.GetStatistics();
            
            string info = $@"Matcap Cache Information

Cache Location: {CacheService.CacheDirectory}
Total Entries: {stats.TotalEntries}
Valid Entries: {stats.ValidEntries}
Expired Entries: {stats.ExpiredEntries}
Total Size: {FormatHelper.FormatFileSize(stats.TotalSize)}
Valid Size: {FormatHelper.FormatFileSize(stats.ValidSize)}
Last Updated: {(stats.LastUpdate > 0 ? DateTimeOffset.FromUnixTimeSeconds(stats.LastUpdate).ToString("yyyy-MM-dd HH:mm:ss") : "Never")}
Cache Expiry: 7 days

The cache automatically stores preview images to improve loading speed on subsequent uses.";
            
            EditorUtility.DisplayDialog("Cache Information", info, "OK");
        }
        
        public void ClearCache()
        {
            try
            {
                _cacheService.ClearAllCache();
                
                foreach (var texture in _previewCache.Values)
                {
                    if (texture != null)
                    {
                        DestroyImmediate(texture);
                    }
                }
                _previewCache.Clear();
                
                foreach (var item in _matcapItems)
                {
                    item.preview = null;
                }
                
                _statusMessage = "Cache cleared successfully";
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to clear cache: {e.Message}");
                _statusMessage = "Failed to clear cache";
            }
        }
        
        public void ShowHelp()
        {
            string helpText = @"Matcap Library Help

OVERVIEW
Professional matcap browser and downloader for Unity projects.

TOOLBAR ACTIONS
Grid View - Show matcaps in a grid layout
List View - Show matcaps in a detailed list
Refresh - Reload matcap list from GitHub
Test - Verify GitHub connectivity
Cache - Show cache information and stats
Download All - Download all available matcaps

INTERACTIONS
• Left Click - Select matcap
• Double Click - Download matcap
• Right Click - Show context menu
• Search - Filter matcaps by name

SETTINGS
• Download Path - Where to save matcaps
• Resolution - Image quality (1024px fixed)
• Thumbnail Size - Preview size in grid view
• Cache Management - View and clear cached previews

KEYBOARD SHORTCUTS
• F5 - Refresh matcap list
• Esc - Clear selection/close panels
• Tab - Switch view mode

Source: github.com/nidorx/matcaps";

            EditorUtility.DisplayDialog("Matcap Library Help", helpText, "OK");
        }
        
        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.F5:
                        LoadMatcapList();
                        e.Use();
                        break;
                        
                    case KeyCode.Escape:
                        _selectedItem = null;
                        _showFilters = false;
                        e.Use();
                        break;
                        
                    case KeyCode.Tab:
                        _currentViewMode = _currentViewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;
                        e.Use();
                        break;
                }
            }
        }
        
        #endregion
        
        #region Public API for Settings Window
        
        public CacheService GetCacheService() => _cacheService;
        public DownloadService GetDownloadService() => _downloadService;
        
        #endregion
    }
}

