/*
 * ============================================================================
 * Matcap Library Window - Unity Editor Extension
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
 * [사용 방법]
 * Window > Matcap Library 메뉴에서 윈도우를 엽니다.
 * 
 * [API 사용 예제]
 * // 윈도우 열기
 * MatcapLibraryWindow.ShowWindow();
 * 
 * // 캐시 정보 확인
 * var window = EditorWindow.GetWindow<MatcapLibraryWindow>();
 * window.ShowCacheInfo();
 * 
 * // 캐시 삭제
 * window.ClearCache();
 * 
 * [소스]
 * MatCap 텍스처 소스: https://github.com/nidorx/matcaps
 * 
 * [라이선스]
 * MIT License
 * ============================================================================
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace ML.Editor
{
    /// <summary>
    /// MatCap 라이브러리 브라우저 에디터 윈도우
    /// GitHub에서 MatCap 텍스처를 검색하고 다운로드할 수 있는 전문 도구
    /// </summary>
    public class MatcapLibraryWindow : EditorWindow
    {
        #region GitHub 연결 설정
        
        /// <summary>GitHub API 기본 URL</summary>
        private const string GitHubApiBase = "https://api.github.com/repos/nidorx/matcaps/contents/";
        
        /// <summary>GitHub Raw 컨텐츠 기본 URL</summary>
        private const string GitHubRawBase = "https://raw.githubusercontent.com/nidorx/matcaps/master/";
        
        /// <summary>GitHub 페이지 URL (스크래핑용)</summary>
        private const string GitHubPageUrl = "https://github.com/nidorx/matcaps/tree/master/preview";
        
        #endregion
        
        #region 캐시 설정
        
        /// <summary>캐시 디렉토리 이름</summary>
        private const string CacheDirName = "MatcapCache";
        
        /// <summary>캐시 인덱스 파일명</summary>
        private const string CacheIndexFile = "cache_index.json";
        
        /// <summary>캐시 만료 기간 (일)</summary>
        private const int CacheExpiryDays = 7;
        
        /// <summary>캐시 디렉토리 전체 경로</summary>
        public static string CacheDirectory => Path.Combine(Application.dataPath, "..", "Library", CacheDirName);
        
        /// <summary>캐시 인덱스 파일 전체 경로</summary>
        public static string CacheIndexPath => Path.Combine(CacheDirectory, CacheIndexFile);
        
        #endregion
        
        #region UI 속성
        
        /// <summary>스크롤 위치</summary>
        private Vector2 __scrollPosition;
        
        /// <summary>MatCap 아이템 리스트</summary>
        private List<MatcapItem> __matcapItems = new List<MatcapItem>();
        
        /// <summary>프리뷰 이미지 캐시 (메모리)</summary>
        private Dictionary<string, Texture2D> __previewCache = new Dictionary<string, Texture2D>();
        
        /// <summary>다운로드 경로</summary>
        public string DownloadPath = "Assets/Matcaps";
        
        /// <summary>고정 다운로드 해상도 (픽셀)</summary>
        private const int FixedResolution = 1024;
        
        /// <summary>로딩 중 여부</summary>
        private bool __isLoading = false;
        
        /// <summary>상태 메시지</summary>
        private string __statusMessage = "";
        
        /// <summary>한 행당 아이템 개수</summary>
        private int __itemsPerRow = 4;
        
        /// <summary>썸네일 크기</summary>
        private float __thumbnailSize = 100f;
        
        /// <summary>검색 필터 텍스트</summary>
        private string __searchFilter = "";
        
        /// <summary>필터링된 아이템 리스트</summary>
        private List<MatcapItem> __filteredItems = new List<MatcapItem>();
        
        /// <summary>로드된 프리뷰 개수</summary>
        private int __loadedPreviewCount = 0;
        
        #endregion
        
        #region UI 스타일 상수
        
        /// <summary>헤더 높이</summary>
        private const float HeaderHeight = 60f;
        
        /// <summary>툴바 높이</summary>
        private const float ToolbarHeight = 35f;
        
        /// <summary>검색바 높이</summary>
        private const float SearchBarHeight = 25f;
        
        /// <summary>상태바 높이</summary>
        private const float StatusBarHeight = 22f;
        
        /// <summary>요소 간 간격</summary>
        private const float Spacing = 8f;
        
        /// <summary>테두리 두께</summary>
        private const float BorderWidth = 1f;
        
        #endregion
        
        #region UI 상태
        
        /// <summary>필터 표시 여부</summary>
        private bool _showFilters = false;
        
        /// <summary>현재 뷰 모드</summary>
        private ViewMode _currentViewMode = ViewMode.Grid;
        
        /// <summary>현재 정렬 모드</summary>
        private SortMode _currentSortMode = SortMode.Name;
        
        /// <summary>오름차순 정렬 여부</summary>
        private bool _sortAscending = true;
        
        /// <summary>선택된 아이템</summary>
        private MatcapItem _selectedItem = null;
        
        /// <summary>호버된 아이템</summary>
        private MatcapItem _hoveredItem = null;
        
        #endregion
        
        #region 색상 테마 (다크)
        
        /// <summary>헤더 배경 색상</summary>
        private static readonly Color HeaderColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        
        /// <summary>툴바 배경 색상</summary>
        private static readonly Color ToolbarColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        
        /// <summary>테두리 색상</summary>
        private static readonly Color BorderColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        
        /// <summary>선택 항목 강조 색상</summary>
        private static readonly Color SelectedColor = new Color(0.3f, 0.5f, 0.9f, 0.8f);
        
        /// <summary>호버 상태 색상</summary>
        private static readonly Color HoverColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        
        #endregion
        
        #region 열거형
        
        /// <summary>뷰 모드</summary>
        private enum ViewMode { Grid, List }
        
        /// <summary>정렬 모드</summary>
        private enum SortMode { Name, Size, DateAdded, Downloaded }
        
        #endregion
        
        #region 코루틴 관리
        
        /// <summary>로딩 코루틴</summary>
        private EditorCoroutine __loadingCoroutine;
        
        #endregion
        
        #region 캐시 관리
        
        /// <summary>캐시 인덱스</summary>
        public CacheIndex CacheIndex;
        
        /// <summary>캐시 초기화 완료 여부</summary>
        private bool __cacheInitialized = false;
        
        #endregion
        
        #region 내부 클래스
        
        /// <summary>
        /// MatCap 아이템 데이터 클래스
        /// 개별 MatCap 텍스처의 정보와 상태를 저장합니다.
        /// </summary>
        [Serializable]
        private class MatcapItem
        {
            /// <summary>MatCap 이름 (확장자 제외)</summary>
            public string name;
            
            /// <summary>파일 이름 (확장자 포함)</summary>
            public string fileName;
            
            /// <summary>프리뷰 텍스처</summary>
            public Texture2D preview;
            
            /// <summary>다운로드 진행 중 여부</summary>
            public bool isDownloading;
            
            /// <summary>다운로드 완료 여부</summary>
            public bool isDownloaded;
        }
        
        /// <summary>
        /// 캐시 엔트리 클래스
        /// 개별 캐시 파일의 정보를 저장합니다.
        /// </summary>
        [Serializable]
        public class CacheEntry
        {
            /// <summary>원본 파일 이름</summary>
            public string fileName;
            
            /// <summary>캐시 파일 이름</summary>
            public string cacheFileName;
            
            /// <summary>캐시 생성 시간 (Unix 타임스탬프)</summary>
            public long cacheTime;
            
            /// <summary>파일 크기 (바이트)</summary>
            public int fileSize;
            
            /// <summary>캐시 유효성 여부</summary>
            public bool isValid;
        }
        
        /// <summary>
        /// 캐시 인덱스 클래스
        /// 전체 캐시 시스템의 인덱스를 관리합니다.
        /// </summary>
        [Serializable]
        public class CacheIndex
        {
            /// <summary>캐시 엔트리 목록</summary>
            public List<CacheEntry> entries = new List<CacheEntry>();
            
            /// <summary>마지막 업데이트 시간 (Unix 타임스탬프)</summary>
            public long lastUpdate;
            
            /// <summary>
            /// 파일 이름으로 캐시 엔트리 검색
            /// </summary>
            /// <param name="fileName">검색할 파일 이름</param>
            /// <returns>캐시 엔트리 (없으면 null)</returns>
            public CacheEntry GetEntry(string fileName)
            {
                return entries.FirstOrDefault(e => e.fileName == fileName);
            }
            
            /// <summary>
            /// 캐시 엔트리 추가 또는 업데이트
            /// </summary>
            /// <param name="fileName">원본 파일 이름</param>
            /// <param name="cacheFileName">캐시 파일 이름</param>
            /// <param name="fileSize">파일 크기</param>
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
            
            /// <summary>
            /// 캐시 엔트리 제거
            /// </summary>
            /// <param name="fileName">제거할 파일 이름</param>
            public void RemoveEntry(string fileName)
            {
                entries.RemoveAll(e => e.fileName == fileName);
            }
            
            /// <summary>
            /// 만료된 캐시 엔트리 정리
            /// </summary>
            public void CleanExpiredEntries()
            {
                long expireTime = DateTimeOffset.UtcNow.AddDays(-CacheExpiryDays).ToUnixTimeSeconds();
                entries.RemoveAll(e => e.cacheTime < expireTime);
            }
        }
        
        #endregion
        
        #region Unity 메뉴 & 초기화
        
        /// <summary>
        /// Unity 메뉴에서 MatCap Library 윈도우를 엽니다.
        /// Window > Matcap Library 메뉴 항목으로 접근 가능합니다.
        /// </summary>
        [MenuItem("Window/Matcap Library")]
        public static void ShowWindow()
        {
            var window = GetWindow<MatcapLibraryWindow>("Matcap Library");
            window.minSize = new Vector2(500, 400);
        }
        
        /// <summary>
        /// 윈도우가 활성화될 때 호출됩니다.
        /// 캐시를 초기화하고 MatCap 목록을 로드합니다.
        /// </summary>
        private void OnEnable()
        {
            InitializeCache();
            LoadMatcapList();
        }
        
        /// <summary>
        /// 윈도우가 비활성화될 때 호출됩니다.
        /// 실행 중인 코루틴을 중지하고 메모리를 정리합니다.
        /// </summary>
        private void OnDisable()
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
        
        /// <summary>
        /// GUI를 그립니다. Unity 에디터 윈도우의 메인 렌더링 메서드입니다.
        /// </summary>
        private void OnGUI()
        {
            Rect headerRect = new Rect(0, 0, position.width, HeaderHeight);
            Rect toolbarRect = new Rect(0, HeaderHeight, position.width, ToolbarHeight);
            Rect searchRect = new Rect(0, HeaderHeight + ToolbarHeight, position.width, SearchBarHeight + Spacing);
            Rect contentRect = new Rect(0, HeaderHeight + ToolbarHeight + SearchBarHeight + Spacing, 
                                       position.width, position.height - HeaderHeight - ToolbarHeight - SearchBarHeight - StatusBarHeight - Spacing * 2);
            Rect statusRect = new Rect(0, position.height - StatusBarHeight, position.width, StatusBarHeight);
            
            DrawProfessionalHeader(headerRect);
            DrawToolbar(toolbarRect);
            DrawAdvancedSearchBar(searchRect);
            
            GUILayout.BeginArea(contentRect);
            {
                if (_isLoading)
                {
                    DrawEnhancedLoadingMessage();
                }
                else if (_filteredItems.Count == 0 && _matcapItems.Count == 0)
                {
                    DrawEnhancedEmptyMessage();
                }
                else
                {
                    if (_currentViewMode == ViewMode.Grid)
                        DrawEnhancedMatcapGrid();
                    else
                        DrawMatcapList();
                }
            }
            GUILayout.EndArea();
            
            DrawEnhancedStatusBar(statusRect);
            
            HandleEvents();
        }
        
        #endregion
        
        #region UI 그리기 메서드
        
        /// <summary>
        /// 전문적인 헤더를 그립니다.
        /// 제목, 연결 상태, 설정 버튼 등을 포함합니다.
        /// </summary>
        /// <param name="rect">헤더 영역</param>
        private void DrawProfessionalHeader(Rect rect)
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
                            // Connection status indicator
                            Color statusColor = _isLoading ? Color.yellow : 
                                               (_matcapItems.Count > 0 ? Color.green : Color.red);
                            GUI.color = statusColor;
                            GUILayout.Label("●", GUILayout.Width(12));
                            GUI.color = Color.white;
                            
                            // Settings toggle
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
        
        /// <summary>
        /// 툴바를 그립니다.
        /// 뷰 모드 전환, 정렬 옵션, 빠른 작업 버튼들을 포함합니다.
        /// </summary>
        /// <param name="rect">툴바 영역</param>
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
                        __currentViewMode = ViewMode.Grid;
                    }
                    
                    GUI.color = __currentViewMode == ViewMode.List ? SelectedColor : Color.white;
                    if (GUILayout.Button("List", toggleStyle, GUILayout.Width(45), GUILayout.Height(25)))
                    {
                        __currentViewMode = ViewMode.List;
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
                    
                    // Sort direction
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
        
        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Download Path:", GUILayout.Width(100));
            DownloadPath = EditorGUILayout.TextField(DownloadPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.SaveFolderPanel("Select Download Folder", DownloadPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                    {
                        DownloadPath = "Assets" + path.Substring(Application.dataPath.Length);
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
            _thumbnailSize = EditorGUILayout.Slider(_thumbnailSize, 50, 200);
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
        
        /// <summary>
        /// 고급 검색 바를 그립니다.
        /// 검색 필드, 필터 토글, 썸네일 크기 조절 슬라이더를 포함합니다.
        /// </summary>
        /// <param name="rect">검색 바 영역</param>
        private void DrawAdvancedSearchBar(Rect rect)
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
                    
                    // Filters toggle
                    GUIStyle filterStyle = new GUIStyle(EditorStyles.miniButton);
                    if (_showFilters)
                    {
                        filterStyle.normal.background = EditorStyles.miniButton.active.background;
                    }
                    
                    if (GUILayout.Button("Filters", filterStyle, GUILayout.Width(50)))
                    {
                        _showFilters = !_showFilters;
                    }
                    
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
        
        private void DrawLoadingMessage()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Loading matcaps... ({_loadedPreviewCount}/{_matcapItems.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            // Progress bar
            Rect rect = GUILayoutUtility.GetRect(300, 20);
            rect.x = (position.width - 300) / 2;
            EditorGUI.ProgressBar(rect, _matcapItems.Count > 0 ? (float)_loadedPreviewCount / _matcapItems.Count : 0, "Loading previews...");
            
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
                            DrawEnhancedMatcapItem(itemsToDisplay[i + j]);
                        }
                        GUILayout.FlexibleSpace(); // Fill remaining space in row
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(Spacing);
                }
                
                // Add some bottom padding
                GUILayout.Space(20);
            }
            GUILayout.EndScrollView();
        }
        
        private void DrawMatcapList()
        {
            List<MatcapItem> itemsToDisplay = GetSortedAndFilteredItems();
            
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
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
            float itemSize = _thumbnailSize;
            Rect itemRect = GUILayoutUtility.GetRect(itemSize, itemSize + 30);
            
            // Handle hover and selection
            bool isHovered = itemRect.Contains(Event.current.mousePosition);
            bool isSelected = _selectedItem == item;
            
            if (isHovered)
            {
                _hoveredItem = item;
            }
            
            // Background
            Color bgColor = isSelected ? SelectedColor : 
                           (isHovered ? HoverColor : Color.clear);
            
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
        
        private void DrawMatcapListItem(MatcapItem item, int index)
        {
            bool isSelected = _selectedItem == item;
            
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
                GUI.color = SelectedColor;
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
                _selectedItem = item;
                Event.current.Use();
            }
        }
        
        private void DrawEnhancedStatusBar(Rect rect)
        {
            // Background
            EditorGUI.DrawRect(rect, ToolbarColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, BorderWidth), BorderColor);
            
            GUILayout.BeginArea(rect);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Space(Spacing);
                    
                    // Stats
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
                    
                    // Status message
                    if (!string.IsNullOrEmpty(_statusMessage))
                    {
                        GUIStyle messageStyle = new GUIStyle(EditorStyles.miniLabel);
                        messageStyle.normal.textColor = Color.white;
                        GUILayout.Label(_statusMessage, messageStyle);
                    }
                    
                    // Selected item info
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
            GUILayout.Label($"{_loadedPreviewCount} of {_matcapItems.Count} previews loaded", progressStyle);
            
            GUILayout.Space(20);
            
            // Progress bar
            Rect progressRect = GUILayoutUtility.GetRect(300, 20);
            progressRect.x = (position.width - 300) / 2;
            
            float progress = _matcapItems.Count > 0 ? (float)_loadedPreviewCount / _matcapItems.Count : 0;
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
                GUILayout.Label("No Matcaps", iconStyle);
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
        
        /// <summary>
        /// GitHub에서 MatCap 목록을 로드합니다.
        /// API와 페이지 스크래핑을 사용하여 사용 가능한 모든 MatCap을 검색합니다.
        /// </summary>
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
            bool useLocalList = true;
            
            // First try GitHub API to get directory contents
            yield return LoadFromGitHubAPI();
            
            if (_matcapItems.Count > 0)
            {
                useLocalList = false;
                Debug.Log($"Loaded {_matcapItems.Count} matcaps from GitHub API");
            }
            else
            {
                // Fallback: Try scraping from GitHub page
                yield return LoadFromGitHubPage();
                
                if (_matcapItems.Count > 0)
                {
                    useLocalList = false;
                    Debug.Log($"Loaded {_matcapItems.Count} matcaps from GitHub page scraping");
                }
            }
            
            // If both GitHub methods failed, show network error
            if (useLocalList)
            {
                Debug.LogError("Failed to load matcaps: Both GitHub API and page scraping failed");
                _statusMessage = "Network error: Unable to connect to GitHub. Please check your internet connection.";
                
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
            foreach (var item in _matcapItems)
            {
                EditorCoroutine.Start(LoadPreviewCoroutine(item));
            }
            
            _statusMessage = $"Loading {_matcapItems.Count} matcaps...";
            
            // Wait for all previews to load
            while (_loadedPreviewCount < _matcapItems.Count)
            {
                yield return null;
            }
            
            _isLoading = false;
            _statusMessage = $"Loaded {_matcapItems.Count} matcaps";
        }
        
        private IEnumerator LoadFromGitHubAPI()
        {
            string[] directories = { "preview", "256", "512", "1024" };
            HashSet<string> uniqueFiles = new HashSet<string>();
            
            foreach (string dir in directories)
            {
                string apiUrl = GitHubApiBase + dir;
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
                _matcapItems.Add(item);
            }
        }
        
        private IEnumerator LoadFromGitHubPage()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(GitHubPageUrl))
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
                        _matcapItems.Add(item);
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
                _previewCache[item.fileName] = item.preview;
                _loadedPreviewCount++;
                Repaint();
                yield break; // Exit early if cache hit
            }
            
            // If not in cache, download from GitHub
            string url = GitHubRawBase + "preview/" + item.fileName;
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    item.preview = DownloadHandlerTexture.GetContent(request);
                    _previewCache[item.fileName] = item.preview;
                    
                    // Save to cache for future use
                    SaveToCache(item.fileName, item.preview);
                }
                else
                {
                    Debug.LogWarning($"Failed to load preview for {item.name}: {request.error}");
                }
                
                _loadedPreviewCount++;
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
            _statusMessage = $"Downloading {item.name}...";
            Repaint();
            
            // Clean filename - remove any preview suffix that might have been added
            string cleanFileName = CleanFileName(item.fileName);
            
            // Always download at 1024px resolution
            Debug.Log($"다운로드 시작 - 고정 해상도: {FixedResolution}px");
            
            string url = $"{GitHubRawBase}{FixedResolution}/{cleanFileName}";
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
                            Debug.Log($"다운로드 성공: {item.name} (1024px, 텍스처: {texture.width}x{texture.height})");
                            
                            // Save with Matcap_ prefix
                            SaveTexture(texture, cleanFileName);
                            item.isDownloaded = true;
                            _statusMessage = $"Downloaded {item.name} (1024px)";
                        }
                        else
                        {
                            Debug.LogError($"텍스처 변환 실패: {item.name}");
                            _statusMessage = $"Failed to process {item.name}";
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"다운로드 처리 중 오류: {item.name} - {e.Message}");
                        _statusMessage = $"Error processing {item.name}";
                    }
                }
                else
                {
                    Debug.LogError($"다운로드 실패: {item.name}");
                    Debug.LogError($"URL: {url}");
                    Debug.LogError($"오류: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                    _statusMessage = $"Failed to download {item.name} (Code: {request.responseCode})";
                    
                    // Try alternative file naming patterns as last resort
                    yield return TryAlternativeDownload(item, cleanFileName);
                }
            }
            
            item.isDownloading = false;
            Repaint();
        }
        
        /// <summary>
        /// 다운로드되지 않은 모든 MatCap을 일괄 다운로드합니다.
        /// </summary>
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
        
        private void SaveTexture(Texture2D texture, string fileName)
        {
            SaveTextureWithMatcapPrefix(texture, fileName);
        }
        
        private void SaveTextureWithMatcapPrefix(Texture2D texture, string fileName)
        {
            try
            {
                // Ensure download directory exists
                if (!Directory.Exists(DownloadPath))
                {
                    Directory.CreateDirectory(DownloadPath);
                    Debug.Log($"Created directory: {DownloadPath}");
                }
                
                // Create filename with Matcap_ prefix
                string safeFileName = $"Matcap_{fileName}";
                
                // Make sure the filename is valid for the file system
                safeFileName = Path.GetFileName(safeFileName); // Remove any path separators
                
                string fullPath = Path.Combine(DownloadPath, safeFileName);
                
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
                string url = $"{GitHubRawBase}{FixedResolution}/{altName}";
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
                                _statusMessage = $"Downloaded {item.name} (alternative naming, 1024px)";
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
            _statusMessage = $"All download attempts failed for {item.name}";
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
            string fullPath = Path.Combine(DownloadPath, $"Matcap_{cleanFileName}");
            return File.Exists(fullPath);
        }
        
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
        
        /// <summary>
        /// GitHub 연결 상태를 테스트합니다.
        /// API, 페이지, Raw 파일 다운로드를 모두 확인합니다.
        /// </summary>
        public void TestGitHubConnection()
        {
            EditorCoroutine.Start(TestConnectionCoroutine());
        }
        
        private IEnumerator TestConnectionCoroutine()
        {
            _statusMessage = "Testing GitHub connection...";
            
            // Test 1: GitHub API
            Debug.Log("=== GitHub Connection Test ===");
            Debug.Log($"Testing GitHub API: {GitHubApiBase}preview");
            
            using (UnityWebRequest request = UnityWebRequest.Get(GitHubApiBase + "preview"))
            {
                request.timeout = 10;
                request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary-Test");
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"GitHub API 연결 성공 (응답 크기: {request.downloadHandler.data.Length} bytes)");
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
                    Debug.LogError($"GitHub API 연결 실패: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                }
            }
            
            yield return null;
            
            // Test 2: GitHub Page
            Debug.Log($"Testing GitHub Page: {GitHubPageUrl}");
            
            using (UnityWebRequest request = UnityWebRequest.Get(GitHubPageUrl))
            {
                request.timeout = 10;
                request.SetRequestHeader("User-Agent", "Unity-MatcapLibrary-Test");
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"GitHub 페이지 연결 성공 (HTML 크기: {request.downloadHandler.data.Length} bytes)");
                    
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
                    Debug.LogError($"GitHub 페이지 연결 실패: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                }
            }
            
            yield return null;
            
            // Test 3: Raw file download
            string testFileName = "1B1B1B1B_999999_575757_747474.png"; // Use a common matcap for testing
            string testUrl = GitHubRawBase + "preview/" + testFileName;
            Debug.Log($"Testing raw file download: {testUrl}");
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(testUrl))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    Debug.Log($"Raw 파일 다운로드 성공 (텍스처 크기: {texture.width}x{texture.height})");
                }
                else
                {
                    Debug.LogError($"Raw 파일 다운로드 실패: {request.error}");
                    Debug.LogError($"응답 코드: {request.responseCode}");
                }
            }
            
            _statusMessage = "Connection test completed. Check Console for results.";
            Debug.Log("=== Connection Test Complete ===");
        }
        
        // Helper Methods
        private List<MatcapItem> GetSortedAndFilteredItems()
        {
            List<MatcapItem> items = string.IsNullOrEmpty(_searchFilter) ? 
                new List<MatcapItem>(_matcapItems) : 
                new List<MatcapItem>(_filteredItems);
            
            // Apply sorting
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
                    // Sort by file size (approximate based on name length for now)
                    items.Sort((a, b) => _sortAscending ?
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
                EditorGUIUtility.systemCopyBuffer = $"{GitHubRawBase}{FixedResolution}/{item.fileName}");
            
            menu.ShowAsContext();
        }
        
        private void ShowInProject(MatcapItem item)
        {
            string cleanFileName = CleanFileName(item.fileName);
            string path = Path.Combine(DownloadPath, $"Matcap_{cleanFileName}");
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
            string texturePath = Path.Combine(DownloadPath, $"Matcap_{cleanFileName}");
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
                
                string materialPath = Path.Combine(DownloadPath, $"Mat_{item.name}.mat");
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                
                Selection.activeObject = material;
                EditorGUIUtility.PingObject(material);
                
                _statusMessage = $"Created material for {item.name}";
            }
        }
        
        /// <summary>
        /// 도움말 다이얼로그를 표시합니다.
        /// 기능 설명, 단축키, 사용법 등을 안내합니다.
        /// </summary>
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

CACHE SYSTEM
Preview images are automatically cached in Library/MatcapCache for faster loading. 
Cache expires after 7 days and can be manually cleared from settings.

KEYBOARD SHORTCUTS
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
        
        // Cache Management Methods
        private void InitializeCache()
        {
            if (_cacheInitialized) return;
            
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
                if (CacheIndex != null)
                {
                    CacheIndex.CleanExpiredEntries();
                    SaveCacheIndex();
                }
                
                _cacheInitialized = true;
                Debug.Log($"Matcap cache initialized. Cached items: {CacheIndex?.entries.Count ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize matcap cache: {e.Message}");
                CacheIndex = new CacheIndex();
                _cacheInitialized = true;
            }
        }
        
        private void LoadCacheIndex()
        {
            if (File.Exists(CacheIndexPath))
            {
                try
                {
                    string json = File.ReadAllText(CacheIndexPath);
                    CacheIndex = JsonUtility.FromJson<CacheIndex>(json);
                    
                    if (CacheIndex == null)
                    {
                        CacheIndex = new CacheIndex();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load cache index: {e.Message}. Creating new index.");
                    CacheIndex = new CacheIndex();
                }
            }
            else
            {
                CacheIndex = new CacheIndex();
            }
        }
        
        /// <summary>
        /// 캐시 인덱스를 디스크에 저장합니다.
        /// </summary>
        public void SaveCacheIndex()
        {
            if (CacheIndex == null) return;
            
            try
            {
                string json = JsonUtility.ToJson(CacheIndex, true);
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
        
        /// <summary>
        /// 캐시 엔트리가 유효한지 확인합니다.
        /// 파일 존재 여부와 만료 시간을 체크합니다.
        /// </summary>
        /// <param name="entry">확인할 캐시 엔트리</param>
        /// <returns>캐시가 유효하면 true</returns>
        public bool IsCacheValid(CacheEntry entry)
        {
            if (entry == null || !entry.isValid)
                return false;
                
            string cachePath = Path.Combine(CacheDirectory, entry.cacheFileName);
            if (!File.Exists(cachePath))
                return false;
                
            long expireTime = DateTimeOffset.UtcNow.AddDays(-CacheExpiryDays).ToUnixTimeSeconds();
            if (entry.cacheTime < expireTime)
                return false;
                
            return true;
        }
        
        private Texture2D LoadFromCache(string fileName)
        {
            var entry = CacheIndex?.GetEntry(fileName);
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
            if (texture == null || CacheIndex == null) return;
            
            try
            {
                string cacheFileName = GetCacheFileName(fileName);
                string cachePath = Path.Combine(CacheDirectory, cacheFileName);
                
                byte[] pngData = texture.EncodeToPNG();
                File.WriteAllBytes(cachePath, pngData);
                
                CacheIndex.AddOrUpdateEntry(fileName, cacheFileName, pngData.Length);
                SaveCacheIndex();
                
                Debug.Log($"Cached preview for {fileName} ({pngData.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to cache preview for {fileName}: {e.Message}");
            }
        }
        
        /// <summary>
        /// 모든 캐시 데이터를 삭제합니다.
        /// 디스크의 캐시 파일과 메모리의 프리뷰 이미지를 모두 제거합니다.
        /// </summary>
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                    Directory.CreateDirectory(CacheDirectory);
                }
                
                CacheIndex = new CacheIndex();
                SaveCacheIndex();
                
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
                Debug.Log("Matcap cache cleared");
                
                Repaint();
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to clear cache: {e.Message}");
                _statusMessage = "Failed to clear cache";
            }
        }
        
        /// <summary>
        /// 캐시 정보 다이얼로그를 표시합니다.
        /// 캐시 크기, 항목 수, 만료 정보 등을 보여줍니다.
        /// </summary>
        public void ShowCacheInfo()
        {
            if (CacheIndex == null)
            {
                EditorUtility.DisplayDialog("Cache Information", "Cache not initialized.", "OK");
                return;
            }
            
            int validEntries = CacheIndex.entries.Count(e => IsCacheValid(e));
            int expiredEntries = CacheIndex.entries.Count - validEntries;
            long totalSize = CacheIndex.entries.Sum(e => e.fileSize);
            long validSize = CacheIndex.entries.Where(e => IsCacheValid(e)).Sum(e => e.fileSize);
            
            string lastUpdate = CacheIndex.lastUpdate > 0 ? 
                DateTimeOffset.FromUnixTimeSeconds(CacheIndex.lastUpdate).ToString("yyyy-MM-dd HH:mm:ss") : 
                "Never";
            
            string info = $@"Matcap Cache Information

Cache Location: {CacheDirectory}
Total Entries: {CacheIndex.entries.Count}
Valid Entries: {validEntries}
Expired Entries: {expiredEntries}
Total Size: {FormatFileSize(totalSize)}
Valid Size: {FormatFileSize(validSize)}
Last Updated: {lastUpdate}
Cache Expiry: {CacheExpiryDays} days

The cache automatically stores preview images to improve loading speed on subsequent uses.";
            
            EditorUtility.DisplayDialog("Cache Information", info, "OK");
        }
        
        /// <summary>
        /// 바이트 크기를 사람이 읽기 쉬운 형식으로 변환합니다.
        /// </summary>
        /// <param name="bytes">바이트 크기</param>
        /// <returns>포맷팅된 파일 크기 문자열 (예: "1.5 MB")</returns>
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
        
        /// <summary>
        /// 새로운 MatCap Material을 생성합니다.
        /// MatCap 셰이더를 찾아 새로운 Material 에셋을 만듭니다.
        /// </summary>
        public void CreateMatcapMaterial()
        {
            Shader matcapShader = Shader.Find("MatCap/Lit");
            
            if (matcapShader == null)
            {
                Debug.LogWarning("MatCap shader not found. Please import a MatCap shader first.");
                return;
            }
            
            Material material = new Material(matcapShader);
            string materialPath = Path.Combine(DownloadPath, "NewMatcapMaterial.mat");
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
            
            Selection.activeObject = material;
            EditorGUIUtility.PingObject(material);
            
            _statusMessage = "Created new Matcap material";
        }
        
        #endregion
    }
    
    #region 설정 윈도우
    
    /// <summary>
    /// MatCap Library 설정 윈도우
    /// 다운로드 경로, 캐시 관리, 고급 설정 등을 제공합니다.
    /// </summary>
    public class MatcapLibrarySettings : EditorWindow
    {
        private MatcapLibraryWindow parentWindow;
        private Vector2 _scrollPosition;
        
        private string DownloadPath;
        private int selectedResolution;
        private int[] resolutionOptions = { 256, 512, 1024 };
        
        /// <summary>
        /// Unity 메뉴에서 설정 윈도우를 엽니다.
        /// </summary>
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
        
        /// <summary>
        /// 부모 윈도우를 지정하여 설정 윈도우를 엽니다.
        /// </summary>
        /// <param name="parent">부모 MatcapLibraryWindow</param>
        public static void ShowWindow(MatcapLibraryWindow parent)
        {
            var window = GetWindow<MatcapLibrarySettings>("Matcap Settings");
            window.parentWindow = parent;
            window.minSize = new Vector2(400, 500);
            window.maxSize = new Vector2(600, 800);
            
            window.LoadSettingsFromParent();
            
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
                DownloadPath = parentWindow.DownloadPath;
            }
        }
        
        private void ApplySettingsToParent()
        {
            if (parentWindow != null)
            {
                Debug.Log($"ApplySettingsToParent - 설정 적용");
                parentWindow.DownloadPath = DownloadPath;
                Debug.Log($"ApplySettingsToParent - 설정 적용 완료");
                parentWindow.Repaint();
            }
            else
            {
                Debug.LogWarning("parentWindow가 null입니다!");
            }
        }
        
        private void OnGUI()
        {
            DrawHeader();
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
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
                    GUILayout.Label("Matcap Library Settings", titleStyle);
                    
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
                GUILayout.Label("General Settings", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                // Download path
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("Download Path:", GUILayout.Width(120));
                    DownloadPath = EditorGUILayout.TextField(DownloadPath);
                    
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        string path = EditorUtility.SaveFolderPanel("Select Download Folder", DownloadPath, "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (path.StartsWith(Application.dataPath))
                            {
                                DownloadPath = "Assets" + path.Substring(Application.dataPath.Length);
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
                GUILayout.Label("Cache Settings", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                if (parentWindow?.CacheIndex != null)
                {
                    var CacheIndex = parentWindow.CacheIndex;
                    int cachedCount = CacheIndex.entries.Count;
                    int validCount = CacheIndex.entries.Count(e => parentWindow.IsCacheValid(e));
                    int expiredCount = cachedCount - validCount;
                    long totalSize = CacheIndex.entries.Sum(e => e.fileSize);
                    
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
                        if (GUILayout.Button("Show Detailed Info"))
                        {
                            parentWindow.ShowCacheInfo();
                        }
                        
                        if (GUILayout.Button("Open Cache Folder"))
                        {
                            EditorUtility.RevealInFinder(MatcapLibraryWindow.CacheDirectory);
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    {
                        if (GUILayout.Button("Clear All Cache"))
                        {
                            if (EditorUtility.DisplayDialog("Clear Cache", 
                                "Are you sure you want to clear all cached preview images?\n\nThis will force re-download of all previews on next use.", 
                                "Yes, Clear", "Cancel"))
                            {
                                parentWindow.ClearCache();
                            }
                        }
                        
                        if (GUILayout.Button("Clean Expired"))
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
                GUILayout.Label("Advanced Settings", EditorStyles.boldLabel);
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
                    if (GUILayout.Button("Test Connection"))
                    {
                        parentWindow?.TestGitHubConnection();
                    }
                    
                    if (GUILayout.Button("Force Refresh"))
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
                GUILayout.Label("Quick Actions", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Open Download Folder"))
                    {
                        EditorUtility.RevealInFinder(DownloadPath);
                    }
                    
                    if (GUILayout.Button("Create Material"))
                    {
                        parentWindow?.CreateMatcapMaterial();
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Download All Matcaps"))
                    {
                        if (EditorUtility.DisplayDialog("Download All", 
                            "This will download all available matcaps. This may take a while and use significant bandwidth.\n\nContinue?", 
                            "Yes, Download", "Cancel"))
                        {
                            parentWindow?.DownloadAllMatcaps();
                        }
                    }
                    
                    if (GUILayout.Button("Show Help"))
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
            if (parentWindow?.CacheIndex != null)
            {
                int beforeCount = parentWindow.CacheIndex.entries.Count;
                parentWindow.CacheIndex.CleanExpiredEntries();
                parentWindow.SaveCacheIndex();
                int afterCount = parentWindow.CacheIndex.entries.Count;
                int removedCount = beforeCount - afterCount;
                
                EditorUtility.DisplayDialog("Cache Cleaned", 
                    $"Removed {removedCount} expired cache entries.\n\nRemaining entries: {afterCount}", "OK");
                
                Repaint();
            }
        }
        
        private void OnDestroy()
        {
            ApplySettingsToParent();
        }
    }
    
    #endregion
    
    #region 에디터 코루틴 헬퍼
    
    /// <summary>
    /// Unity 에디터에서 코루틴을 실행하기 위한 헬퍼 클래스
    /// 에디터 업데이트 루프를 사용하여 비동기 작업을 처리합니다.
    /// </summary>
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

        /// <summary>
        /// 에디터 코루틴을 시작합니다.
        /// </summary>
        /// <param name="routine">실행할 코루틴</param>
        /// <returns>EditorCoroutine 인스턴스</returns>
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

        /// <summary>
        /// 실행 중인 에디터 코루틴을 중지합니다.
        /// </summary>
        /// <param name="coroutine">중지할 코루틴</param>
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

        /// <summary>
        /// 에디터 업데이트마다 호출되어 코루틴을 진행시킵니다.
        /// </summary>
        private void Update()
        {
            if (isDone) return;

            if (waitingAsyncOp != null)
            {
                if (!waitingAsyncOp.isDone) return;
                waitingAsyncOp = null;
            }

            if (waitingCustomYield != null)
            {
                if (waitingCustomYield.keepWaiting) return;
                waitingCustomYield = null;
            }

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

            
            if (yielded is IEnumerator nested)
            {
                stack.Push(nested);
                return;
            }

            
            if (yielded is AsyncOperation asyncOp)
            {
                waitingAsyncOp = asyncOp;
                return;
            }

            
            if (yielded is CustomYieldInstruction customYield)
            {
                waitingCustomYield = customYield;
                return;
            }

            return;
        }
    }
    
    #endregion
}