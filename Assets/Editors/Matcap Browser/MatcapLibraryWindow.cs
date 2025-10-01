using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace SGE.Editor.MatcapLibrary
{
    public class MatcapLibraryWindow : EditorWindow
    {
        // GitHub URLs - Using API and raw content
        private const string GITHUB_API_BASE = "https://api.github.com/repos/nidorx/matcaps/contents/";
        private const string GITHUB_RAW_BASE = "https://raw.githubusercontent.com/nidorx/matcaps/master/";
        private const string GITHUB_PAGE_URL = "https://github.com/nidorx/matcaps/tree/master/preview";
        
        // Cache settings
        private const string CACHE_DIR_NAME = "MatcapCache";
        private const string CACHE_INDEX_FILE = "cache_index.json";
        private const int CACHE_EXPIRY_DAYS = 7; // Cache expires after 7 days
        public static string CacheDirectory => Path.Combine(Application.dataPath, "..", "Library", CACHE_DIR_NAME);
        public static string CacheIndexPath => Path.Combine(CacheDirectory, CACHE_INDEX_FILE);
        

        
        // UI Properties
        private Vector2 scrollPosition;
        private List<MatcapItem> matcapItems = new List<MatcapItem>();
        private Dictionary<string, Texture2D> previewCache = new Dictionary<string, Texture2D>();
        public string downloadPath = "Assets/Matcaps";
        private const int FIXED_RESOLUTION = 1024; // Always download at 1024px
        private bool isLoading = false;
        private string statusMessage = "";
        private int itemsPerRow = 4;
        private float thumbnailSize = 100f;
        private string searchFilter = "";
        private List<MatcapItem> filteredItems = new List<MatcapItem>();
        private int loadedPreviewCount = 0;
        
        // UI Style Constants
        private const float HEADER_HEIGHT = 60f;
        private const float TOOLBAR_HEIGHT = 35f;
        private const float SEARCH_BAR_HEIGHT = 25f;
        private const float STATUS_BAR_HEIGHT = 22f;
        private const float SPACING = 8f;
        private const float BORDER_WIDTH = 1f;
        
        // UI State
        private bool showFilters = false;
        private ViewMode currentViewMode = ViewMode.Grid;
        private SortMode currentSortMode = SortMode.Name;
        private bool sortAscending = true;
        private MatcapItem selectedItem = null;
        private MatcapItem hoveredItem = null;
        
        // Colors (Dark Theme)
        private static readonly Color HEADER_COLOR = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color TOOLBAR_COLOR = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color BORDER_COLOR = new Color(0.1f, 0.1f, 0.1f, 1f);
        private static readonly Color SELECTED_COLOR = new Color(0.3f, 0.5f, 0.9f, 0.8f);
        private static readonly Color HOVER_COLOR = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        
        // Enums
        private enum ViewMode { Grid, List }
        private enum SortMode { Name, Size, DateAdded, Downloaded }
        
        // Coroutine management
        private EditorCoroutine loadingCoroutine;
        
        // Cache management
        public CacheIndex cacheIndex;
        private bool cacheInitialized = false;
        
        [Serializable]
        private class MatcapItem
        {
            public string name;
            public string fileName;
            public Texture2D preview;
            public bool isDownloading;
            public bool isDownloaded;
        }
        
        [Serializable]
        public class CacheEntry
        {
            public string fileName;
            public string cacheFileName;
            public long cacheTime; // Unix timestamp
            public int fileSize;
            public bool isValid;
        }
        
        [Serializable]
        public class CacheIndex
        {
            public List<CacheEntry> entries = new List<CacheEntry>();
            public long lastUpdate;
            
            public CacheEntry GetEntry(string fileName)
            {
                return entries.FirstOrDefault(e => e.fileName == fileName);
            }
            
            public void AddOrUpdateEntry(string fileName, string cacheFileName, int fileSize)
            {
                var existing = GetEntry(fileName);
                if (existing != null)
                {
                    existing.cacheFileName = cacheFileName;
                    existing.cacheTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    existing.fileSize = fileSize;
                    existing.isValid = true;
                }
                else
                {
                    entries.Add(new CacheEntry
                    {
                        fileName = fileName,
                        cacheFileName = cacheFileName,
                        cacheTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        fileSize = fileSize,
                        isValid = true
                    });
                }
                lastUpdate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            
            public void RemoveEntry(string fileName)
            {
                entries.RemoveAll(e => e.fileName == fileName);
            }
            
            public void CleanExpiredEntries()
            {
                long expireTime = DateTimeOffset.UtcNow.AddDays(-CACHE_EXPIRY_DAYS).ToUnixTimeSeconds();
                entries.RemoveAll(e => e.cacheTime < expireTime);
            }
        }
        
        [MenuItem("Window/Matcap Library")]
        public static void ShowWindow()
        {
            var window = GetWindow<MatcapLibraryWindow>("Matcap Library");
            window.minSize = new Vector2(500, 400);
        }
        
        private void OnEnable()
        {
            InitializeCache();
            LoadMatcapList();
        }
        
        private void OnDisable()
        {
            // Stop any running coroutines
            if (loadingCoroutine != null)
            {
                EditorCoroutine.Stop(loadingCoroutine);
                loadingCoroutine = null;
            }
            
            // Clear preview cache to free memory
            foreach (var texture in previewCache.Values)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
            }
            previewCache.Clear();
        }
        
        private void OnGUI()
        {
            // Calculate layout areas
            Rect headerRect = new Rect(0, 0, position.width, HEADER_HEIGHT);
            Rect toolbarRect = new Rect(0, HEADER_HEIGHT, position.width, TOOLBAR_HEIGHT);
            Rect searchRect = new Rect(0, HEADER_HEIGHT + TOOLBAR_HEIGHT, position.width, SEARCH_BAR_HEIGHT + SPACING);
            Rect contentRect = new Rect(0, HEADER_HEIGHT + TOOLBAR_HEIGHT + SEARCH_BAR_HEIGHT + SPACING, 
                                       position.width, position.height - HEADER_HEIGHT - TOOLBAR_HEIGHT - SEARCH_BAR_HEIGHT - STATUS_BAR_HEIGHT - SPACING * 2);
            Rect statusRect = new Rect(0, position.height - STATUS_BAR_HEIGHT, position.width, STATUS_BAR_HEIGHT);
            
            // Draw main UI sections
            DrawProfessionalHeader(headerRect);
            DrawToolbar(toolbarRect);
            DrawAdvancedSearchBar(searchRect);
            
            // Draw content area
            GUILayout.BeginArea(contentRect);
            {
                if (isLoading)
                {
                    DrawEnhancedLoadingMessage();
                }
                else if (filteredItems.Count == 0 && matcapItems.Count == 0)
                {
                    DrawEnhancedEmptyMessage();
                }
                else
                {
                    if (currentViewMode == ViewMode.Grid)
                        DrawEnhancedMatcapGrid();
                    else
                        DrawMatcapList();
                }
            }
            GUILayout.EndArea();
            
            DrawEnhancedStatusBar(statusRect);
            
            // Handle events
            HandleEvents();
        }
        
        private void DrawProfessionalHeader(Rect rect)
        {
            // Background
            EditorGUI.DrawRect(rect, HEADER_COLOR);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - BORDER_WIDTH, rect.width, BORDER_WIDTH), BORDER_COLOR);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(SPACING);
                    
                    // Icon and title
                    GUILayout.BeginVertical();
                    {
                        GUILayout.Space(8);
                        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
                        titleStyle.fontSize = 16;
                        titleStyle.normal.textColor = Color.white;
                        GUILayout.Label("🎨 Matcap Library", titleStyle);
                        
                        GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel);
                        subtitleStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                        GUILayout.Label("Professional matcap browser and manager", subtitleStyle);
                    }
                    GUILayout.EndVertical();
                    
                    GUILayout.FlexibleSpace();
                    
                    // Header buttons
                    GUILayout.BeginVertical();
                    {
                        GUILayout.Space(12);
                        GUILayout.BeginHorizontal();
                        {
                            // Connection status indicator
                            Color statusColor = isLoading ? Color.yellow : 
                                               (matcapItems.Count > 0 ? Color.green : Color.red);
                            GUI.color = statusColor;
                            GUILayout.Label("●", GUILayout.Width(12));
                            GUI.color = Color.white;
                            
                            // Settings toggle
                            GUIStyle buttonStyle = new GUIStyle(EditorStyles.miniButton);
                            buttonStyle.normal.textColor = Color.white;
                            if (GUILayout.Button("⚙", buttonStyle, GUILayout.Width(25), GUILayout.Height(20)))
                            {
                                MatcapLibrarySettings.ShowWindow(this);
                            }
                            
                            // Help button
                            if (GUILayout.Button("?", buttonStyle, GUILayout.Width(25), GUILayout.Height(20)))
                            {
                                ShowHelp();
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                    
                    GUILayout.Space(SPACING);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        private void DrawToolbar(Rect rect)
        {
            // Background
            EditorGUI.DrawRect(rect, TOOLBAR_COLOR);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - BORDER_WIDTH, rect.width, BORDER_WIDTH), BORDER_COLOR);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(SPACING);
                    
                    // View mode buttons
                    GUIStyle toggleStyle = new GUIStyle(EditorStyles.miniButton);
                    toggleStyle.normal.textColor = Color.white;
                    
                    GUI.color = currentViewMode == ViewMode.Grid ? SELECTED_COLOR : Color.white;
                    if (GUILayout.Button("⊞", toggleStyle, GUILayout.Width(30), GUILayout.Height(25)))
                    {
                        currentViewMode = ViewMode.Grid;
                    }
                    
                    GUI.color = currentViewMode == ViewMode.List ? SELECTED_COLOR : Color.white;
                    if (GUILayout.Button("☰", toggleStyle, GUILayout.Width(30), GUILayout.Height(25)))
                    {
                        currentViewMode = ViewMode.List;
                    }
                    GUI.color = Color.white;
                    
                    GUILayout.Space(SPACING);
                    
                    // Sort options
                    GUILayout.Label("Sort:", EditorStyles.miniLabel, GUILayout.Width(30));
                    SortMode newSortMode = (SortMode)EditorGUILayout.EnumPopup(currentSortMode, GUILayout.Width(80));
                    if (newSortMode != currentSortMode)
                    {
                        currentSortMode = newSortMode;
                        SortMatcaps();
                    }
                    
                    // Sort direction
                    string sortIcon = sortAscending ? "↑" : "↓";
                    if (GUILayout.Button(sortIcon, toggleStyle, GUILayout.Width(20), GUILayout.Height(25)))
                    {
                        sortAscending = !sortAscending;
                        SortMatcaps();
                    }
                    
                    GUILayout.FlexibleSpace();
                    
                    // Quick actions
                    if (GUILayout.Button("🔄", toggleStyle, GUILayout.Width(30), GUILayout.Height(25)))
                    {
                        LoadMatcapList();
                    }
                    
                    if (GUILayout.Button("🔗", toggleStyle, GUILayout.Width(30), GUILayout.Height(25)))
                    {
                        TestGitHubConnection();
                    }
                    
                    if (GUILayout.Button("💾", toggleStyle, GUILayout.Width(30), GUILayout.Height(25)))
                    {
                        ShowCacheInfo();
                    }
                    
                    if (GUILayout.Button("⬇", toggleStyle, GUILayout.Width(30), GUILayout.Height(25)))
                    {
                        DownloadAllMatcaps();
                    }
                    
                    GUILayout.Space(SPACING);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Download Path:", GUILayout.Width(100));
            downloadPath = EditorGUILayout.TextField(downloadPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.SaveFolderPanel("Select Download Folder", downloadPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        downloadPath = "Assets" + path.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Invalid Path", "Please select a folder within the Assets directory.", "OK");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            
            // Resolution is now fixed at 1024px
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Resolution:", GUILayout.Width(100));
            EditorGUILayout.LabelField("1024px (Fixed)", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Thumbnail Size:", GUILayout.Width(100));
            thumbnailSize = EditorGUILayout.Slider(thumbnailSize, 50, 200);
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh List", GUILayout.Height(25)))
            {
                LoadMatcapList();
            }
            if (GUILayout.Button("Test Connection", GUILayout.Height(25)))
            {
                TestGitHubConnection();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Download All", GUILayout.Height(25)))
            {
                DownloadAllMatcaps();
            }
            if (GUILayout.Button("Create Material", GUILayout.Height(25)))
            {
                CreateMatcapMaterial();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }
        
        private void DrawAdvancedSearchBar(Rect rect)
        {
            rect.y += SPACING / 2;
            rect.height = SEARCH_BAR_HEIGHT;
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(SPACING);
                    
                    // Search icon and field
                    GUILayout.Label("🔍", GUILayout.Width(16));
                    
                    GUIStyle searchStyle = new GUIStyle(EditorStyles.textField);
                    searchStyle.margin = new RectOffset(2, 2, 2, 2);
                    
                    string newSearch = GUILayout.TextField(searchFilter, searchStyle);
                    if (newSearch != searchFilter)
                    {
                        searchFilter = newSearch;
                        FilterMatcaps();
                    }
                    
                    // Clear button
                    if (!string.IsNullOrEmpty(searchFilter))
                    {
                        if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(20)))
                        {
                            searchFilter = "";
                            FilterMatcaps();
                        }
                    }
                    
                    GUILayout.Space(SPACING);
                    
                    // Filters toggle
                    GUIStyle filterStyle = new GUIStyle(EditorStyles.miniButton);
                    if (showFilters)
                    {
                        filterStyle.normal.background = EditorStyles.miniButton.active.background;
                    }
                    
                    if (GUILayout.Button("Filters", filterStyle, GUILayout.Width(50)))
                    {
                        showFilters = !showFilters;
                    }
                    
                    // Thumbnail size slider
                    GUILayout.Label("Size:", EditorStyles.miniLabel, GUILayout.Width(30));
                    float newSize = GUILayout.HorizontalSlider(thumbnailSize, 60, 200, GUILayout.Width(80));
                    if (newSize != thumbnailSize)
                    {
                        thumbnailSize = newSize;
                    }
                    
                    GUILayout.Space(SPACING);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        private void DrawLoadingMessage()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Loading matcaps... ({loadedPreviewCount}/{matcapItems.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            // Progress bar
            Rect rect = GUILayoutUtility.GetRect(300, 20);
            rect.x = (position.width - 300) / 2;
            EditorGUI.ProgressBar(rect, matcapItems.Count > 0 ? (float)loadedPreviewCount / matcapItems.Count : 0, "Loading previews...");
            
            GUILayout.FlexibleSpace();
        }
        
        private void DrawEmptyMessage()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("No matcaps found. Click 'Refresh List' to load.", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }
        
        private void DrawEnhancedMatcapGrid()
        {
            List<MatcapItem> itemsToDisplay = GetSortedAndFilteredItems();
            
            // Calculate content width before starting scroll view
            float contentWidth = position.width - 40; // Account for scroll bar and padding
            float itemWidth = thumbnailSize + SPACING;
            itemsPerRow = Mathf.Max(1, Mathf.FloorToInt(contentWidth / itemWidth));
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            {
                for (int i = 0; i < itemsToDisplay.Count; i += itemsPerRow)
                {
                    GUILayout.BeginHorizontal();
                    {
                        for (int j = 0; j < itemsPerRow && i + j < itemsToDisplay.Count; j++)
                        {
                            DrawEnhancedMatcapItem(itemsToDisplay[i + j]);
                        }
                        GUILayout.FlexibleSpace(); // Fill remaining space in row
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(SPACING);
                }
                
                // Add some bottom padding
                GUILayout.Space(20);
            }
            GUILayout.EndScrollView();
        }
        
        private void DrawMatcapList()
        {
            List<MatcapItem> itemsToDisplay = GetSortedAndFilteredItems();
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            {
                // List header
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    GUILayout.Label("Preview", EditorStyles.miniLabel, GUILayout.Width(60));
                    GUILayout.Label("Name", EditorStyles.miniLabel, GUILayout.Width(200));
                    GUILayout.Label("Status", EditorStyles.miniLabel, GUILayout.Width(80));
                    GUILayout.Label("Actions", EditorStyles.miniLabel);
                }
                GUILayout.EndHorizontal();
                
                // List items
                for (int i = 0; i < itemsToDisplay.Count; i++)
                {
                    DrawMatcapListItem(itemsToDisplay[i], i);
                }
            }
            GUILayout.EndScrollView();
        }
        
        private void DrawEnhancedMatcapItem(MatcapItem item)
        {
            float itemSize = thumbnailSize;
            Rect itemRect = GUILayoutUtility.GetRect(itemSize, itemSize + 30);
            
            // Handle hover and selection
            bool isHovered = itemRect.Contains(Event.current.mousePosition);
            bool isSelected = selectedItem == item;
            
            if (isHovered)
            {
                hoveredItem = item;
            }
            
            // Background
            Color bgColor = isSelected ? SELECTED_COLOR : 
                           (isHovered ? HOVER_COLOR : Color.clear);
            
            if (bgColor != Color.clear)
            {
                EditorGUI.DrawRect(itemRect, bgColor);
            }
            
            // Preview area
            Rect previewRect = new Rect(itemRect.x + 4, itemRect.y + 4, itemSize - 8, itemSize - 8);
            
            if (item.preview != null)
            {
                // Draw preview with rounded corners effect
                GUI.DrawTexture(previewRect, item.preview, ScaleMode.ScaleToFit);
                
                // Status overlay
                if (item.isDownloaded)
                {
                    Rect checkRect = new Rect(previewRect.x + previewRect.width - 20, previewRect.y + 4, 16, 16);
                    GUI.color = Color.green;
                    GUI.Label(checkRect, "✓");
                    GUI.color = Color.white;
                }
                else if (item.isDownloading)
                {
                    Rect loadingRect = new Rect(previewRect.x, previewRect.y, previewRect.width, previewRect.height);
                    GUI.color = new Color(0, 0, 0, 0.7f);
                    GUI.DrawTexture(loadingRect, EditorGUIUtility.whiteTexture);
                    GUI.color = Color.white;
                    
                    GUIStyle loadingStyle = new GUIStyle(EditorStyles.boldLabel);
                    loadingStyle.alignment = TextAnchor.MiddleCenter;
                    loadingStyle.normal.textColor = Color.white;
                    GUI.Label(loadingRect, "⟳", loadingStyle);
                }
            }
            else
            {
                // Loading placeholder
                GUI.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                GUI.DrawTexture(previewRect, EditorGUIUtility.whiteTexture);
                GUI.color = Color.white;
                
                GUIStyle placeholderStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                placeholderStyle.alignment = TextAnchor.MiddleCenter;
                GUI.Label(previewRect, "Loading...", placeholderStyle);
            }
            
            // Name label
            Rect nameRect = new Rect(itemRect.x + 2, itemRect.y + itemSize - 6, itemSize - 4, 20);
            
            string displayName = item.name;
            int maxChars = Mathf.FloorToInt(itemSize / 7); // Approximate character width
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
            
            // Handle clicks
            if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0) // Left click
                {
                    selectedItem = item;
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
        
        private void DrawMatcapListItem(MatcapItem item, int index)
        {
            bool isSelected = selectedItem == item;
            
            GUIStyle rowStyle = new GUIStyle();
            if (index % 2 == 0)
            {
                rowStyle.normal.background = EditorGUIUtility.whiteTexture;
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.1f);
            }
            else
            {
                GUI.color = Color.white;
            }
            
            if (isSelected)
            {
                GUI.color = SELECTED_COLOR;
            }
            
            GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(30));
            {
                GUI.color = Color.white;
                
                // Preview thumbnail
                if (item.preview != null)
                {
                    GUILayout.Label(item.preview, GUILayout.Width(30), GUILayout.Height(30));
                }
                else
                {
                    GUILayout.Box("", GUILayout.Width(30), GUILayout.Height(30));
                }
                
                // Name
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
            
            // Handle selection
            if (Event.current.type == EventType.MouseDown && 
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                selectedItem = item;
                Event.current.Use();
            }
        }
        
        private void DrawEnhancedStatusBar(Rect rect)
        {
            // Background
            EditorGUI.DrawRect(rect, TOOLBAR_COLOR);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, BORDER_WIDTH), BORDER_COLOR);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(SPACING);
                    
                    // Stats
                    List<MatcapItem> displayItems = GetSortedAndFilteredItems();
                    int downloadedCount = matcapItems.Count(m => m.isDownloaded);
                    int downloadingCount = matcapItems.Count(m => m.isDownloading);
                    
                    GUIStyle statsStyle = new GUIStyle(EditorStyles.miniLabel);
                    statsStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                    
                    GUILayout.Label($"Total: {matcapItems.Count}", statsStyle);
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
                    
                    // Status message
                    if (!string.IsNullOrEmpty(statusMessage))
                    {
                        GUIStyle messageStyle = new GUIStyle(EditorStyles.miniLabel);
                        messageStyle.normal.textColor = Color.white;
                        GUILayout.Label(statusMessage, messageStyle);
                    }
                    
                    // Selected item info
                    if (selectedItem != null)
                    {
                        GUILayout.Label("•", statsStyle, GUILayout.Width(10));
                        GUILayout.Label($"Selected: {selectedItem.name}", statsStyle);
                    }
                    
                    GUILayout.Space(SPACING);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        private void DrawEnhancedLoadingMessage()
        {
            GUILayout.FlexibleSpace();
            
            // Animated loading indicator
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
            
            // Progress info
            GUIStyle progressStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            GUILayout.Label($"{loadedPreviewCount} of {matcapItems.Count} previews loaded", progressStyle);
            
            GUILayout.Space(20);
            
            // Progress bar
            Rect progressRect = GUILayoutUtility.GetRect(300, 20);
            progressRect.x = (position.width - 300) / 2;
            
            float progress = matcapItems.Count > 0 ? (float)loadedPreviewCount / matcapItems.Count : 0;
            EditorGUI.ProgressBar(progressRect, progress, $"{(progress * 100):F0}%");
            
            GUILayout.FlexibleSpace();
            
            // Force repaint for animation
            Repaint();
        }
        
        private void DrawEnhancedEmptyMessage()
        {
            GUILayout.FlexibleSpace();
            
            GUIStyle iconStyle = new GUIStyle(EditorStyles.boldLabel);
            iconStyle.fontSize = 48;
            iconStyle.alignment = TextAnchor.MiddleCenter;
            iconStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            
            GUILayout.BeginHorizontal();
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("🎨", iconStyle);
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
                GUILayout.Label("Click the refresh button (🔄) in the toolbar to load matcaps", subMessageStyle);
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
            
            GUILayout.FlexibleSpace();
        }
        
        public void LoadMatcapList()
        {
            if (loadingCoroutine != null)
            {
                EditorCoroutine.Stop(loadingCoroutine);
            }
            
            isLoading = true;
            loadedPreviewCount = 0;
            statusMessage = "Loading matcap list...";
            matcapItems.Clear();
            filteredItems.Clear();
            previewCache.Clear();
            
            // Try to load from GitHub first, fallback to predefined list
            loadingCoroutine = EditorCoroutine.Start(LoadMatcapsCoroutine());
        }
        
        private IEnumerator LoadMatcapsCoroutine()
        {
            bool useLocalList = true;
            
            // First try GitHub API to get directory contents
            yield return LoadFromGitHubAPI();
            
            if (matcapItems.Count > 0)
            {
                useLocalList = false;
                Debug.Log($"Loaded {matcapItems.Count} matcaps from GitHub API");
            }
            else
            {
                // Fallback: Try scraping from GitHub page
                yield return LoadFromGitHubPage();
                
                if (matcapItems.Count > 0)
                {
                    useLocalList = false;
                    Debug.Log($"Loaded {matcapItems.Count} matcaps from GitHub page scraping");
                }
            }
            
            // If both GitHub methods failed, show network error
            if (useLocalList)
            {
                Debug.LogError("Failed to load matcaps: Both GitHub API and page scraping failed");
                statusMessage = "❌ Network error: Unable to connect to GitHub. Please check your internet connection.";
                
                // Show helpful message to user
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
                    yield break;
                }
            }
            
            FilterMatcaps();
            
            // Load previews
            foreach (var item in matcapItems)
            {
                EditorCoroutine.Start(LoadPreviewCoroutine(item));
            }
            
            statusMessage = $"Loading {matcapItems.Count} matcaps...";
            
            // Wait for all previews to load
            while (loadedPreviewCount < matcapItems.Count)
            {
                yield return null;
            }
            
            isLoading = false;
            statusMessage = $"Loaded {matcapItems.Count} matcaps";
        }
        
        private IEnumerator LoadFromGitHubAPI()
        {
            string[] directories = { "preview", "256", "512", "1024" };
            HashSet<string> uniqueFiles = new HashSet<string>();
            
            foreach (string dir in directories)
            {
                string apiUrl = GITHUB_API_BASE + dir;
                using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
                {
                    request.timeout = 15;
                    request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary");
                    yield return request.SendWebRequest();
                    
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            string json = request.downloadHandler.text;
                            var files = ParseGitHubAPIResponse(json);
                            foreach (string fileName in files)
                            {
                                if (fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                                {
                                    uniqueFiles.Add(fileName);
                                }
                            }
                            
                            // If we got files from preview directory, that's enough
                            if (dir == "preview" && uniqueFiles.Count > 0)
                            {
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Failed to parse GitHub API response for {dir}: {e.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"GitHub API request failed for {dir}: {request.error}");
                    }
                }
                
                // Small delay between requests to avoid rate limiting
                yield return null;
            }
            
            // Create items from unique files
            foreach (string fileName in uniqueFiles)
            {
                MatcapItem item = new MatcapItem
                {
                    name = Path.GetFileNameWithoutExtension(fileName),
                    fileName = fileName,
                    isDownloaded = CheckIfDownloaded(fileName)
                };
                matcapItems.Add(item);
            }
        }
        
        private IEnumerator LoadFromGitHubPage()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(GITHUB_PAGE_URL))
            {
                request.timeout = 15;
                request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary");
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string html = request.downloadHandler.text;
                    
                    // More flexible regex patterns to catch various file naming formats
                    string[] patterns = {
                        @"([0-9A-F]{8}_[0-9A-F]{8}_[0-9A-F]{8}_[0-9A-F]{8}\.png)",
                        @"([0-9A-F]{6}_[0-9A-F]{6}_[0-9A-F]{6}_[0-9A-F]{6}\.png)",
                        @"([0-9A-Fa-f]+_[0-9A-Fa-f]+_[0-9A-Fa-f]+_[0-9A-Fa-f]+\.png)",
                        @"(\w+\.png)"
                    };
                    
                    HashSet<string> uniqueFiles = new HashSet<string>();
                    
                    foreach (string pattern in patterns)
                    {
                        MatchCollection matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                        foreach (Match match in matches)
                        {
                            string fileName = match.Groups[1].Value;
                            if (IsValidMatcapFileName(fileName))
                            {
                                uniqueFiles.Add(fileName);
                            }
                        }
                        
                        if (uniqueFiles.Count > 10) // If we found enough files with this pattern, stop
                            break;
                    }
                    
                    foreach (string fileName in uniqueFiles)
                    {
                        MatcapItem item = new MatcapItem
                        {
                            name = Path.GetFileNameWithoutExtension(fileName),
                            fileName = fileName,
                            isDownloaded = CheckIfDownloaded(fileName)
                        };
                        matcapItems.Add(item);
                    }
                }
                else
                {
                    Debug.LogWarning($"Could not fetch matcap list from GitHub page: {request.error}");
                }
            }
        }
        

        
        private IEnumerator LoadPreviewCoroutine(MatcapItem item)
        {
            // First, try to load from cache
            Texture2D cachedTexture = LoadFromCache(item.fileName);
            if (cachedTexture != null)
            {
                item.preview = cachedTexture;
                previewCache[item.fileName] = item.preview;
                loadedPreviewCount++;
                Repaint();
                yield break; // Exit early if cache hit
            }
            
            // If not in cache, download from GitHub
            string url = GITHUB_RAW_BASE + "preview/" + item.fileName;
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    item.preview = DownloadHandlerTexture.GetContent(request);
                    previewCache[item.fileName] = item.preview;
                    
                    // Save to cache for future use
                    SaveToCache(item.fileName, item.preview);
                }
                else
                {
                    Debug.LogWarning($"Failed to load preview for {item.name}: {request.error}");
                }
                
                loadedPreviewCount++;
                Repaint();
            }
        }
        
        private void DownloadMatcap(MatcapItem item)
        {
            EditorCoroutine.Start(DownloadMatcapCoroutine(item));
        }
        
        private IEnumerator DownloadMatcapCoroutine(MatcapItem item)
        {
            item.isDownloading = true;
            statusMessage = $"Downloading {item.name}...";
            Repaint();
            
            // Clean filename - remove any preview suffix that might have been added
            string cleanFileName = CleanFileName(item.fileName);
            
            // Always download at 1024px resolution
            Debug.Log($"🔍 다운로드 시작 - 고정 해상도: {FIXED_RESOLUTION}px");
            
            string url = $"{GITHUB_RAW_BASE}{FIXED_RESOLUTION}/{cleanFileName}";
            Debug.Log($"다운로드 URL: {url}");
                
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 30;
                request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary");
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        Texture2D texture = DownloadHandlerTexture.GetContent(request);
                        if (texture != null)
                        {
                            Debug.Log($"✅ 다운로드 성공: {item.name} (1024px, 텍스처: {texture.width}x{texture.height})");
                            
                            // Save with Matcap_ prefix
                            SaveTexture(texture, cleanFileName);
                            item.isDownloaded = true;
                            statusMessage = $"Downloaded {item.name} (1024px)";
                        }
                        else
                        {
                            Debug.LogError($"텍스처 변환 실패: {item.name}");
                            statusMessage = $"Failed to process {item.name}";
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"다운로드 처리 중 오류: {item.name} - {e.Message}");
                        statusMessage = $"Error processing {item.name}";
                    }
                }
                else
                {
                    Debug.LogError($"다운로드 실패: {item.name}");
                    Debug.LogError($"URL: {url}");
                    Debug.LogError($"오류: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                    statusMessage = $"Failed to download {item.name} (Code: {request.responseCode})";
                    
                    // Try alternative file naming patterns as last resort
                    yield return TryAlternativeDownload(item, cleanFileName);
                }
            }
            
            item.isDownloading = false;
            Repaint();
        }
        
        public void DownloadAllMatcaps()
        {
            EditorCoroutine.Start(DownloadAllMatcapsCoroutine());
        }
        
        private IEnumerator DownloadAllMatcapsCoroutine()
        {
            List<MatcapItem> itemsToDownload = matcapItems.Where(item => !item.isDownloaded).ToList();
            
            for (int i = 0; i < itemsToDownload.Count; i++)
            {
                var item = itemsToDownload[i];
                statusMessage = $"Downloading {i + 1}/{itemsToDownload.Count}: {item.name}";
                yield return DownloadMatcapCoroutine(item);
            }
            
            statusMessage = $"Downloaded all matcaps";
        }
        
        private void SaveTexture(Texture2D texture, string fileName)
        {
            SaveTextureWithMatcapPrefix(texture, fileName);
        }
        
        private void SaveTextureWithMatcapPrefix(Texture2D texture, string fileName)
        {
            try
            {
                // Ensure download directory exists
                if (!Directory.Exists(downloadPath))
                {
                    Directory.CreateDirectory(downloadPath);
                    Debug.Log($"Created directory: {downloadPath}");
                }
                
                // Create filename with Matcap_ prefix
                string safeFileName = $"Matcap_{fileName}";
                
                // Make sure the filename is valid for the file system
                safeFileName = Path.GetFileName(safeFileName); // Remove any path separators
                
                string fullPath = Path.Combine(downloadPath, safeFileName);
                
                // Check if file already exists
                if (File.Exists(fullPath))
                {
                    Debug.Log($"파일이 이미 존재합니다: {fullPath}");
                }
                
                byte[] bytes = texture.EncodeToPNG();
                
                if (bytes != null && bytes.Length > 0)
                {
                    File.WriteAllBytes(fullPath, bytes);
                    Debug.Log($"파일 저장 완료: {fullPath} (크기: {bytes.Length} bytes)");
                    
                    // Import the texture with proper settings
                    AssetDatabase.ImportAsset(fullPath);
                    
                    // Set texture import settings for matcap usage
                    TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
                    if (importer != null)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.wrapMode = TextureWrapMode.Clamp;
                        importer.filterMode = FilterMode.Bilinear;
                        importer.mipmapEnabled = false;
                        AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);
                    }
                }
                else
                {
                    Debug.LogError($"PNG 인코딩 실패: {fileName}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"파일 저장 중 오류 발생: {fileName} - {e.Message}");
            }
            finally
            {
                AssetDatabase.Refresh();
            }
        }
        
        private IEnumerator TryAlternativeDownload(MatcapItem item, string cleanFileName)
        {
            Debug.Log($"대체 다운로드 시도: {item.name}");
            
            // Try different filename variations at 1024px resolution only
            string[] alternativeNames = GenerateAlternativeFileNames(cleanFileName);
            
            foreach (string altName in alternativeNames)
            {
                string url = $"{GITHUB_RAW_BASE}{FIXED_RESOLUTION}/{altName}";
                Debug.Log($"대체 파일명 시도: {url}");
                
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
                {
                    request.timeout = 30;
                    request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary");
                    yield return request.SendWebRequest();
                    
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            Texture2D texture = DownloadHandlerTexture.GetContent(request);
                            if (texture != null)
                            {
                                Debug.Log($"대체 다운로드 성공: {item.name} ({altName}, 1024px)");
                                SaveTexture(texture, altName);
                                item.isDownloaded = true;
                                statusMessage = $"Downloaded {item.name} (alternative naming, 1024px)";
                                yield break; // Success, exit
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"대체 다운로드 처리 오류: {altName} - {e.Message}");
                        }
                    }
                }
                
                yield return null; // Small delay
            }
            
            Debug.LogError($"모든 대체 방법 실패: {item.name}");
            statusMessage = $"All download attempts failed for {item.name}";
        }
        
        private string[] GenerateAlternativeFileNames(string fileName)
        {
            List<string> alternatives = new List<string>();
            
            // Original cleaned name
            alternatives.Add(fileName);
            
            // Try without extension and add .png
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (nameWithoutExt != fileName)
            {
                alternatives.Add(nameWithoutExt + ".png");
            }
            
            // Try lowercase
            alternatives.Add(fileName.ToLower());
            alternatives.Add(nameWithoutExt.ToLower() + ".png");
            
            // Try with different separators
            if (fileName.Contains("_"))
            {
                alternatives.Add(fileName.Replace("_", "-"));
                alternatives.Add(nameWithoutExt.Replace("_", "-") + ".png");
            }
            
            // Remove duplicates
            return alternatives.Distinct().ToArray();
        }
        
        private string CleanFileName(string fileName)
        {
            // Remove any preview suffix or other suffixes that might cause issues
            string cleanFileName = fileName;
            if (cleanFileName.Contains("-preview"))
            {
                cleanFileName = cleanFileName.Replace("-preview", "");
            }
            
            // Also handle any other potential issues
            if (cleanFileName.EndsWith("-"))
            {
                cleanFileName = cleanFileName.TrimEnd('-');
            }
            
            return cleanFileName;
        }
        
        private bool CheckIfDownloaded(string fileName)
        {
            string cleanFileName = CleanFileName(fileName);
            string fullPath = Path.Combine(downloadPath, $"Matcap_{cleanFileName}");
            return File.Exists(fullPath);
        }
        
        private void FilterMatcaps()
        {
            if (string.IsNullOrEmpty(searchFilter))
            {
                filteredItems = new List<MatcapItem>(matcapItems);
            }
            else
            {
                filteredItems = matcapItems.Where(item => 
                    item.name.ToLower().Contains(searchFilter.ToLower())).ToList();
            }
            Repaint();
        }
        
        private List<string> ParseGitHubAPIResponse(string json)
        {
            List<string> fileNames = new List<string>();
            
            try
            {
                // Simple JSON parsing for GitHub API response
                // Looking for "name": "filename.png" patterns
                MatchCollection matches = Regex.Matches(json, @"""name""\s*:\s*""([^""]+\.png)""", RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    string fileName = match.Groups[1].Value;
                    if (IsValidMatcapFileName(fileName))
                    {
                        fileNames.Add(fileName);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse GitHub API JSON: {e.Message}");
            }
            
            return fileNames;
        }
        
        private bool IsValidMatcapFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return false;
                
            // Check if it's a reasonable matcap file name
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            
            // Skip very short names or common non-matcap files
            if (nameWithoutExt.Length < 3)
                return false;
                
            string[] skipPatterns = { "readme", "license", "example", "test", "thumb" };
            foreach (string pattern in skipPatterns)
            {
                if (nameWithoutExt.ToLower().Contains(pattern))
                    return false;
            }
            
            return true;
        }
        
        public void TestGitHubConnection()
        {
            EditorCoroutine.Start(TestConnectionCoroutine());
        }
        
        private IEnumerator TestConnectionCoroutine()
        {
            statusMessage = "Testing GitHub connection...";
            
            // Test 1: GitHub API
            Debug.Log("=== GitHub Connection Test ===");
            Debug.Log($"Testing GitHub API: {GITHUB_API_BASE}preview");
            
            using (UnityWebRequest request = UnityWebRequest.Get(GITHUB_API_BASE + "preview"))
            {
                request.timeout = 10;
                request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary-Test");
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"✅ GitHub API 연결 성공 (응답 크기: {request.downloadHandler.data.Length} bytes)");
                    Debug.Log($"응답 내용 (일부): {request.downloadHandler.text.Substring(0, Mathf.Min(200, request.downloadHandler.text.Length))}...");
                    
                    // Parse and count files
                    var files = ParseGitHubAPIResponse(request.downloadHandler.text);
                    Debug.Log($"파싱된 파일 수: {files.Count}");
                    if (files.Count > 0)
                    {
                        Debug.Log($"첫 번째 파일 예시: {files[0]}");
                    }
                }
                else
                {
                    Debug.LogError($"❌ GitHub API 연결 실패: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                }
            }
            
            yield return null;
            
            // Test 2: GitHub Page
            Debug.Log($"Testing GitHub Page: {GITHUB_PAGE_URL}");
            
            using (UnityWebRequest request = UnityWebRequest.Get(GITHUB_PAGE_URL))
            {
                request.timeout = 10;
                request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary-Test");
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"✅ GitHub 페이지 연결 성공 (HTML 크기: {request.downloadHandler.data.Length} bytes)");
                    
                    // Test regex patterns
                    string html = request.downloadHandler.text;
                    string[] patterns = {
                        @"([0-9A-F]{8}_[0-9A-F]{8}_[0-9A-F]{8}_[0-9A-F]{8}\.png)",
                        @"([0-9A-F]{6}_[0-9A-F]{6}_[0-9A-F]{6}_[0-9A-F]{6}\.png)",
                        @"([0-9A-Fa-f]+_[0-9A-Fa-f]+_[0-9A-Fa-f]+_[0-9A-Fa-f]+\.png)",
                        @"(\w+\.png)"
                    };
                    
                    for (int i = 0; i < patterns.Length; i++)
                    {
                        MatchCollection matches = Regex.Matches(html, patterns[i], RegexOptions.IgnoreCase);
                        Debug.Log($"패턴 {i + 1} 매칭 결과: {matches.Count}개");
                    }
                }
                else
                {
                    Debug.LogError($"❌ GitHub 페이지 연결 실패: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                }
            }
            
            yield return null;
            
            // Test 3: Raw file download
            string testFileName = "1B1B1B1B_999999_575757_747474.png"; // Use a common matcap for testing
            string testUrl = GITHUB_RAW_BASE + "preview/" + testFileName;
            Debug.Log($"Testing raw file download: {testUrl}");
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(testUrl))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    Debug.Log($"✅ Raw 파일 다운로드 성공 (텍스처 크기: {texture.width}x{texture.height})");
                }
                else
                {
                    Debug.LogError($"❌ Raw 파일 다운로드 실패: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                }
            }
            
            statusMessage = "Connection test completed. Check Console for results.";
            Debug.Log("=== Connection Test Complete ===");
        }
        
        // Helper Methods
        private List<MatcapItem> GetSortedAndFilteredItems()
        {
            List<MatcapItem> items = string.IsNullOrEmpty(searchFilter) ? 
                new List<MatcapItem>(matcapItems) : 
                new List<MatcapItem>(filteredItems);
            
            // Apply sorting
            switch (currentSortMode)
            {
                case SortMode.Name:
                    items.Sort((a, b) => sortAscending ? 
                        string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase) :
                        string.Compare(b.name, a.name, StringComparison.OrdinalIgnoreCase));
                    break;
                    
                case SortMode.Downloaded:
                    items.Sort((a, b) => sortAscending ?
                        a.isDownloaded.CompareTo(b.isDownloaded) :
                        b.isDownloaded.CompareTo(a.isDownloaded));
                    break;
                    
                case SortMode.Size:
                    // Sort by file size (approximate based on name length for now)
                    items.Sort((a, b) => sortAscending ?
                        a.fileName.Length.CompareTo(b.fileName.Length) :
                        b.fileName.Length.CompareTo(a.fileName.Length));
                    break;
            }
            
            return items;
        }
        
        private void SortMatcaps()
        {
            // Sorting is handled in GetSortedAndFilteredItems()
            Repaint();
        }
        
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
            menu.AddItem(new GUIContent("Copy URL"), false, () => 
                EditorGUIUtility.systemCopyBuffer = $"{GITHUB_RAW_BASE}{FIXED_RESOLUTION}/{item.fileName}");
            
            menu.ShowAsContext();
        }
        
        private void ShowInProject(MatcapItem item)
        {
            string cleanFileName = CleanFileName(item.fileName);
            string path = Path.Combine(downloadPath, $"Matcap_{cleanFileName}");
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }
        
        private void CreateMaterialForItem(MatcapItem item)
        {
            string cleanFileName = CleanFileName(item.fileName);
            string texturePath = Path.Combine(downloadPath, $"Matcap_{cleanFileName}");
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            
            if (texture == null)
            {
                Debug.LogWarning($"Texture not found: {texturePath}");
                return;
            }
            
            Shader matcapShader = Shader.Find("MatCap/Lit");
            if (matcapShader == null)
            {
                matcapShader = Shader.Find("Legacy Shaders/Diffuse");
            }
            
            if (matcapShader != null)
            {
                Material material = new Material(matcapShader);
                material.mainTexture = texture;
                
                string materialPath = Path.Combine(downloadPath, $"Mat_{item.name}.mat");
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                
                Selection.activeObject = material;
                EditorGUIUtility.PingObject(material);
                
                statusMessage = $"Created material for {item.name}";
            }
        }
        
        public void ShowHelp()
        {
            string helpText = @"Matcap Library Help

🎨 OVERVIEW
Professional matcap browser and downloader for Unity projects.

🔧 TOOLBAR ACTIONS
⊞ Grid View - Show matcaps in a grid layout
☰ List View - Show matcaps in a detailed list
🔄 Refresh - Reload matcap list from GitHub
🔗 Test Connection - Verify GitHub connectivity
💾 Cache Info - Show cache information and stats
⬇ Download All - Download all available matcaps

🖱️ INTERACTIONS
• Left Click - Select matcap
• Double Click - Download matcap
• Right Click - Show context menu
• Search - Filter matcaps by name

⚙️ SETTINGS
• Download Path - Where to save matcaps
• Resolution - Image quality (256/512/1024)
• Thumbnail Size - Preview size in grid view
• Cache Management - View and clear cached previews

💾 CACHE SYSTEM
Preview images are automatically cached in Library/MatcapCache for faster loading. Cache expires after 7 days and can be manually cleared from settings.

⌨️ KEYBOARD SHORTCUTS
• F5 - Refresh matcap list
• Esc - Clear selection/close panels
• Tab - Switch view mode

Source: github.com/nidorx/matcaps";

            EditorUtility.DisplayDialog("Matcap Library Help", helpText, "OK");
        }
        

        
        private void HandleEvents()
        {
            Event e = Event.current;
            
            // Handle keyboard shortcuts
            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.F5:
                        LoadMatcapList();
                        e.Use();
                        break;
                        
                    case KeyCode.Escape:
                        selectedItem = null;
                        showFilters = false;
                        e.Use();
                        break;
                        
                    case KeyCode.Tab:
                        currentViewMode = currentViewMode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;
                        e.Use();
                        break;
                }
            }
            

        }
        
        // Cache Management Methods
        private void InitializeCache()
        {
            if (cacheInitialized) return;
            
            try
            {
                // Create cache directory if it doesn't exist
                if (!Directory.Exists(CacheDirectory))
                {
                    Directory.CreateDirectory(CacheDirectory);
                    Debug.Log($"Created matcap cache directory: {CacheDirectory}");
                }
                
                // Load cache index
                LoadCacheIndex();
                
                // Clean expired entries
                if (cacheIndex != null)
                {
                    cacheIndex.CleanExpiredEntries();
                    SaveCacheIndex();
                }
                
                cacheInitialized = true;
                Debug.Log($"Matcap cache initialized. Cached items: {cacheIndex?.entries.Count ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize matcap cache: {e.Message}");
                cacheIndex = new CacheIndex();
                cacheInitialized = true;
            }
        }
        
        private void LoadCacheIndex()
        {
            if (File.Exists(CacheIndexPath))
            {
                try
                {
                    string json = File.ReadAllText(CacheIndexPath);
                    cacheIndex = JsonUtility.FromJson<CacheIndex>(json);
                    
                    if (cacheIndex == null)
                    {
                        cacheIndex = new CacheIndex();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load cache index: {e.Message}. Creating new index.");
                    cacheIndex = new CacheIndex();
                }
            }
            else
            {
                cacheIndex = new CacheIndex();
            }
        }
        
        public void SaveCacheIndex()
        {
            if (cacheIndex == null) return;
            
            try
            {
                string json = JsonUtility.ToJson(cacheIndex, true);
                File.WriteAllText(CacheIndexPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save cache index: {e.Message}");
            }
        }
        
        private string GetCacheFileName(string originalFileName)
        {
            // Create a safe filename for cache
            string hash = originalFileName.GetHashCode().ToString("X8");
            string extension = Path.GetExtension(originalFileName);
            return $"preview_{hash}{extension}";
        }
        
        public bool IsCacheValid(CacheEntry entry)
        {
            if (entry == null || !entry.isValid)
                return false;
                
            string cachePath = Path.Combine(CacheDirectory, entry.cacheFileName);
            if (!File.Exists(cachePath))
                return false;
                
            // Check if cache is expired
            long expireTime = DateTimeOffset.UtcNow.AddDays(-CACHE_EXPIRY_DAYS).ToUnixTimeSeconds();
            if (entry.cacheTime < expireTime)
                return false;
                
            return true;
        }
        
        private Texture2D LoadFromCache(string fileName)
        {
            var entry = cacheIndex?.GetEntry(fileName);
            if (!IsCacheValid(entry))
                return null;
                
            try
            {
                string cachePath = Path.Combine(CacheDirectory, entry.cacheFileName);
                byte[] fileData = File.ReadAllBytes(cachePath);
                
                Texture2D texture = new Texture2D(2, 2);
                if (texture.LoadImage(fileData))
                {
                    return texture;
                }
                else
                {
                    DestroyImmediate(texture);
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load cached preview for {fileName}: {e.Message}");
                return null;
            }
        }
        
        private void SaveToCache(string fileName, Texture2D texture)
        {
            if (texture == null || cacheIndex == null) return;
            
            try
            {
                string cacheFileName = GetCacheFileName(fileName);
                string cachePath = Path.Combine(CacheDirectory, cacheFileName);
                
                byte[] pngData = texture.EncodeToPNG();
                File.WriteAllBytes(cachePath, pngData);
                
                cacheIndex.AddOrUpdateEntry(fileName, cacheFileName, pngData.Length);
                SaveCacheIndex();
                
                Debug.Log($"Cached preview for {fileName} ({pngData.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to cache preview for {fileName}: {e.Message}");
            }
        }
        
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                    Directory.CreateDirectory(CacheDirectory);
                }
                
                cacheIndex = new CacheIndex();
                SaveCacheIndex();
                
                // Clear preview cache in memory
                foreach (var texture in previewCache.Values)
                {
                    if (texture != null)
                    {
                        DestroyImmediate(texture);
                    }
                }
                previewCache.Clear();
                
                // Reset preview references
                foreach (var item in matcapItems)
                {
                    item.preview = null;
                }
                
                statusMessage = "Cache cleared successfully";
                Debug.Log("Matcap cache cleared");
                
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to clear cache: {e.Message}");
                statusMessage = "Failed to clear cache";
            }
        }
        
        public void ShowCacheInfo()
        {
            if (cacheIndex == null)
            {
                EditorUtility.DisplayDialog("Cache Information", "Cache not initialized.", "OK");
                return;
            }
            
            int validEntries = cacheIndex.entries.Count(e => IsCacheValid(e));
            int expiredEntries = cacheIndex.entries.Count - validEntries;
            long totalSize = cacheIndex.entries.Sum(e => e.fileSize);
            long validSize = cacheIndex.entries.Where(e => IsCacheValid(e)).Sum(e => e.fileSize);
            
            string lastUpdate = cacheIndex.lastUpdate > 0 ? 
                DateTimeOffset.FromUnixTimeSeconds(cacheIndex.lastUpdate).ToString("yyyy-MM-dd HH:mm:ss") : 
                "Never";
            
            string info = $@"Matcap Cache Information

📁 Cache Location: {CacheDirectory}
📊 Total Entries: {cacheIndex.entries.Count}
✅ Valid Entries: {validEntries}
❌ Expired Entries: {expiredEntries}
💾 Total Size: {FormatFileSize(totalSize)}
✅ Valid Size: {FormatFileSize(validSize)}
🕒 Last Updated: {lastUpdate}
⏰ Cache Expiry: {CACHE_EXPIRY_DAYS} days

The cache automatically stores preview images to improve loading speed on subsequent uses.";
            
            EditorUtility.DisplayDialog("Cache Information", info, "OK");
        }
        
        public string FormatFileSize(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            else if (bytes < 1024 * 1024)
                return $"{bytes / 1024.0:F1} KB";
            else if (bytes < 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024.0):F1} MB";
            else
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }
        
        public void CreateMatcapMaterial()
        {
            // Create a simple matcap shader if not exists
            Shader matcapShader = Shader.Find("MatCap/Lit");
            
            if (matcapShader == null)
            {
                Debug.LogWarning("MatCap shader not found. Please import a MatCap shader first.");
                return;
            }
            
            Material material = new Material(matcapShader);
            string materialPath = Path.Combine(downloadPath, "NewMatcapMaterial.mat");
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            
            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
            
            statusMessage = "Created new Matcap material";
        }
    }
    
    // Settings Window
    public class MatcapLibrarySettings : EditorWindow
    {
        private MatcapLibraryWindow parentWindow;
        private Vector2 scrollPosition;
        
        // Settings data
        private string downloadPath;
        private int selectedResolution;
        private int[] resolutionOptions = { 256, 512, 1024 };
        
        [MenuItem("Window/Matcap Library Settings")]
        public static void ShowWindowFromMenu()
        {
            // Try to find existing MatcapLibraryWindow
            var mainWindow = FindObjectOfType<MatcapLibraryWindow>();
            if (mainWindow == null)
            {
                // Open main window first
                MatcapLibraryWindow.ShowWindow();
                mainWindow = FindObjectOfType<MatcapLibraryWindow>();
            }
            
            ShowWindow(mainWindow);
        }
        
        public static void ShowWindow(MatcapLibraryWindow parent)
        {
            var window = GetWindow<MatcapLibrarySettings>("Matcap Settings");
            window.parentWindow = parent;
            window.minSize = new Vector2(400, 500);
            window.maxSize = new Vector2(600, 800);
            
            // Load current settings from parent
            window.LoadSettingsFromParent();
            
            // Center the window
            var main = EditorGUIUtility.GetMainWindowPosition();
            var pos = window.position;
            pos.x = main.x + (main.width - pos.width) * 0.5f;
            pos.y = main.y + (main.height - pos.height) * 0.5f;
            window.position = pos;
        }
        
        private void LoadSettingsFromParent()
        {
            if (parentWindow != null)
            {
                downloadPath = parentWindow.downloadPath;
            }
        }
        
        private void ApplySettingsToParent()
        {
            if (parentWindow != null)
            {
                Debug.Log($"🔄 ApplySettingsToParent - 설정 적용");
                parentWindow.downloadPath = downloadPath;
                Debug.Log($"🔄 ApplySettingsToParent - 설정 적용 완료");
                parentWindow.Repaint();
            }
            else
            {
                Debug.LogWarning("⚠️ parentWindow가 null입니다!");
            }
        }
        
        private void OnGUI()
        {
            DrawHeader();
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            {
                DrawGeneralSettings();
                GUILayout.Space(10);
                DrawCacheSettings();
                GUILayout.Space(10);
                DrawAdvancedSettings();
                GUILayout.Space(10);
                DrawActions();
            }
            EditorGUILayout.EndScrollView();
            
            DrawFooter();
        }
        
        private void DrawHeader()
        {
            // Header background
            Rect headerRect = new Rect(0, 0, position.width, 60);
            EditorGUI.DrawRect(headerRect, new Color(0.2f, 0.2f, 0.2f, 1f));
            EditorGUI.DrawRect(new Rect(0, 59, position.width, 1), new Color(0.1f, 0.1f, 0.1f, 1f));
            
            GUILayout.BeginArea(headerRect);
            {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(15);
                    
                    GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
                    titleStyle.fontSize = 18;
                    titleStyle.normal.textColor = Color.white;
                    GUILayout.Label("⚙️ Matcap Library Settings", titleStyle);
                    
                    GUILayout.FlexibleSpace();
                }
                GUILayout.EndHorizontal();
                
                GUIStyle subtitleStyle = new GUIStyle(EditorStyles.miniLabel);
                subtitleStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(15);
                    GUILayout.Label("Configure download settings, cache management, and advanced options", subtitleStyle);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
            
            GUILayout.Space(70);
        }
        
        private void DrawGeneralSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("📁 General Settings", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                // Download path
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Download Path:", GUILayout.Width(120));
                    downloadPath = EditorGUILayout.TextField(downloadPath);
                    
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        string path = EditorUtility.SaveFolderPanel("Select Download Folder", downloadPath, "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (path.StartsWith(Application.dataPath))
                            {
                                downloadPath = "Assets" + path.Substring(Application.dataPath.Length);
                                ApplySettingsToParent();
                            }
                            else
                            {
                                EditorUtility.DisplayDialog("Invalid Path", 
                                    "Please select a folder within the Assets directory.", "OK");
                            }
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.HelpBox("Matcap files will be downloaded to this folder within your Assets directory.", MessageType.Info);
                
                GUILayout.Space(10);
                
                // Resolution (Fixed)
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Resolution:", GUILayout.Width(120));
                    EditorGUILayout.LabelField("1024px (Fixed)", EditorStyles.boldLabel);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.HelpBox("Resolution is now fixed at 1024px for consistency and optimal quality.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }
        
        private void DrawCacheSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("💾 Cache Settings", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                if (parentWindow?.cacheIndex != null)
                {
                    var cacheIndex = parentWindow.cacheIndex;
                    int cachedCount = cacheIndex.entries.Count;
                    int validCount = cacheIndex.entries.Count(e => parentWindow.IsCacheValid(e));
                    int expiredCount = cachedCount - validCount;
                    long totalSize = cacheIndex.entries.Sum(e => e.fileSize);
                    
                    // Cache statistics
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Cache Location:", GUILayout.Width(120));
                        EditorGUILayout.SelectableLabel($"Library/MatcapCache", EditorStyles.textField, GUILayout.Height(18));
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Cached Items:", GUILayout.Width(120));
                        EditorGUILayout.LabelField($"{cachedCount} total ({validCount} valid, {expiredCount} expired)");
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Cache Size:", GUILayout.Width(120));
                        EditorGUILayout.LabelField(parentWindow.FormatFileSize(totalSize));
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Cache Expiry:", GUILayout.Width(120));
                        EditorGUILayout.LabelField("7 days");
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    GUILayout.Space(10);
                    
                    // Cache actions
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("📊 Show Detailed Info"))
                        {
                            parentWindow.ShowCacheInfo();
                        }
                        
                        if (GUILayout.Button("📁 Open Cache Folder"))
                        {
                            EditorUtility.RevealInFinder(MatcapLibraryWindow.CacheDirectory);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("🗑️ Clear All Cache"))
                        {
                            if (EditorUtility.DisplayDialog("Clear Cache", 
                                "Are you sure you want to clear all cached preview images?\n\nThis will force re-download of all previews on next use.", 
                                "Yes, Clear", "Cancel"))
                            {
                                parentWindow.ClearCache();
                            }
                        }
                        
                        if (GUILayout.Button("🧹 Clean Expired"))
                        {
                            CleanExpiredCache();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("Cache not initialized. Please open the main Matcap Library window first.", MessageType.Warning);
                }
                
                EditorGUILayout.HelpBox("Cache stores preview images locally to speed up loading. Expired items are automatically cleaned on startup.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }
        
        private void DrawAdvancedSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("🔧 Advanced Settings", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                // GitHub connection info
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Source Repository:", GUILayout.Width(120));
                    EditorGUILayout.SelectableLabel("github.com/nidorx/matcaps", EditorStyles.textField, GUILayout.Height(18));
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("API Endpoint:", GUILayout.Width(120));
                    EditorGUILayout.SelectableLabel("api.github.com/repos/...", EditorStyles.textField, GUILayout.Height(18));
                }
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(10);
                
                // Advanced actions
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("🔗 Test Connection"))
                    {
                        parentWindow?.TestGitHubConnection();
                    }
                    
                    if (GUILayout.Button("🔄 Force Refresh"))
                    {
                        parentWindow?.LoadMatcapList();
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.HelpBox("Use these tools to diagnose connection issues or force update the matcap list.", MessageType.Info);
            }
            EditorGUILayout.EndVertical();
        }
        
        private void DrawActions()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("🛠️ Quick Actions", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("📁 Open Download Folder"))
                    {
                        EditorUtility.RevealInFinder(downloadPath);
                    }
                    
                    if (GUILayout.Button("🎨 Create Material"))
                    {
                        parentWindow?.CreateMatcapMaterial();
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("⬇️ Download All Matcaps"))
                    {
                        if (EditorUtility.DisplayDialog("Download All", 
                            "This will download all available matcaps. This may take a while and use significant bandwidth.\n\nContinue?", 
                            "Yes, Download", "Cancel"))
                        {
                            parentWindow?.DownloadAllMatcaps();
                        }
                    }
                    
                    if (GUILayout.Button("❓ Show Help"))
                    {
                        parentWindow?.ShowHelp();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }
        
        private void DrawFooter()
        {
            GUILayout.FlexibleSpace();
            
            // Footer
            Rect footerRect = new Rect(0, position.height - 30, position.width, 30);
            EditorGUI.DrawRect(footerRect, new Color(0.25f, 0.25f, 0.25f, 1f));
            EditorGUI.DrawRect(new Rect(0, position.height - 30, position.width, 1), new Color(0.1f, 0.1f, 0.1f, 1f));
            
            GUILayout.BeginArea(footerRect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(15);
                    GUIStyle footerStyle = new GUIStyle(EditorStyles.miniLabel);
                    footerStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f, 1f);
                    GUILayout.Label("Settings are automatically saved", footerStyle);
                    
                    GUILayout.FlexibleSpace();
                    
                    if (GUILayout.Button("Close", EditorStyles.miniButton, GUILayout.Width(50)))
                    {
                        Close();
                    }
                    
                    GUILayout.Space(15);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }
        
        private void CleanExpiredCache()
        {
            if (parentWindow?.cacheIndex != null)
            {
                int beforeCount = parentWindow.cacheIndex.entries.Count;
                parentWindow.cacheIndex.CleanExpiredEntries();
                parentWindow.SaveCacheIndex();
                int afterCount = parentWindow.cacheIndex.entries.Count;
                int removedCount = beforeCount - afterCount;
                
                EditorUtility.DisplayDialog("Cache Cleaned", 
                    $"Removed {removedCount} expired cache entries.\n\nRemaining entries: {afterCount}", "OK");
                
                Repaint();
            }
        }
        
        private void OnDestroy()
        {
            // Ensure settings are applied when window closes
            ApplySettingsToParent();
        }
    }
    
    // Editor Coroutine Helper
    public class EditorCoroutine
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        private AsyncOperation waitingAsyncOp;
        private CustomYieldInstruction waitingCustomYield;
        private bool isDone;

        private EditorCoroutine(IEnumerator routine)
        {
            stack.Push(routine);
        }

        public static EditorCoroutine Start(IEnumerator routine)
        {
            if (routine == null) throw new ArgumentNullException(nameof(routine));
            var coroutine = new EditorCoroutine(routine);
            coroutine.Start();
            return coroutine;
        }

        private void Start()
        {
            EditorApplication.update += Update;
        }

        public static void Stop(EditorCoroutine coroutine)
        {
            if (coroutine != null)
            {
                coroutine.Stop();
            }
        }

        private void Stop()
        {
            isDone = true;
            waitingAsyncOp = null;
            waitingCustomYield = null;
            stack.Clear();
            EditorApplication.update -= Update;
        }

        private void Update()
        {
            if (isDone) return;

            // If waiting for AsyncOperation -> wait until completed
            if (waitingAsyncOp != null)
            {
                if (!waitingAsyncOp.isDone) return;
                waitingAsyncOp = null; // continue
            }

            // If waiting for CustomYieldInstruction -> wait until it's done
            if (waitingCustomYield != null)
            {
                if (waitingCustomYield.keepWaiting) return;
                waitingCustomYield = null; // continue
            }

            // No more enumerators -> stop
            if (stack.Count == 0)
            {
                Stop();
                return;
            }

            var enumerator = stack.Peek();
            bool movedNext = false;

            try
            {
                movedNext = enumerator.MoveNext();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
                Stop();
                return;
            }

            if (!movedNext)
            {
                // Current enumerator finished -> pop and continue next frame
                stack.Pop();
                if (stack.Count == 0) Stop();
                return;
            }

            var yielded = enumerator.Current;

            if (yielded == null)
            {
                // yield return null -> wait next editor update frame
                return;
            }

            // Support nested IEnumerator
            if (yielded is IEnumerator nested)
            {
                stack.Push(nested);
                return;
            }

            // Support UnityWebRequest/ResourceRequest/etc.
            if (yielded is AsyncOperation asyncOp)
            {
                waitingAsyncOp = asyncOp;
                return;
            }

            // Support CustomYieldInstruction (optional)
            if (yielded is CustomYieldInstruction customYield)
            {
                waitingCustomYield = customYield;
                return;
            }

            // Unknown yield type -> treat as one-frame wait
            // (prevents tight loops when unexpected types are yielded)
            return;
        }
    }
}