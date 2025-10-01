/*
 * ============================================================================
 * MatcapLibrarySettings - Settings Window (Refactored)
 * ============================================================================
 * 
 * MatCap Library 설정 윈도우
 * 다운로드 경로, 캐시 관리, 고급 설정 등을 제공합니다.
 * 
 * ============================================================================
 */

using System.Linq;
using UnityEditor;
using UnityEngine;
using ML.Editor.MatcapBrowser.Services;
using ML.Editor.MatcapBrowser.Utilities;

namespace ML.Editor.MatcapBrowser.UI
{
    public class MatcapLibrarySettings : EditorWindow
    {
        private MatcapLibraryWindow _parentWindow;
        private Vector2 _scrollPosition;
        
        private string _downloadPath;
        
        [MenuItem("Window/Matcap Library Settings")]
        public static void ShowWindowFromMenu()
        {
            var mainWindow = FindObjectOfType<MatcapLibraryWindow>();
            if (mainWindow == null)
            {
                MatcapLibraryWindow.ShowWindow();
                mainWindow = FindObjectOfType<MatcapLibraryWindow>();
            }
            
            ShowWindow(mainWindow);
        }
        
        public static void ShowWindow(MatcapLibraryWindow parent)
        {
            var window = GetWindow<MatcapLibrarySettings>("Matcap Settings");
            window._parentWindow = parent;
            window.minSize = new Vector2(400, 500);
            window.maxSize = new Vector2(600, 800);
            
            window.LoadSettings();
            
            var main = EditorGUIUtility.GetMainWindowPosition();
            var pos = window.position;
            pos.x = main.x + (main.width - pos.width) * 0.5f;
            pos.y = main.y + (main.height - pos.height) * 0.5f;
            window.position = pos;
        }
        
        private void LoadSettings()
        {
            if (_parentWindow != null)
            {
                _downloadPath = _parentWindow.GetDownloadService().DownloadPath;
            }
        }
        
        private void ApplySettings()
        {
            if (_parentWindow != null)
            {
                _parentWindow.GetDownloadService().DownloadPath = _downloadPath;
                _parentWindow.Repaint();
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
                    _downloadPath = EditorGUILayout.TextField(_downloadPath);
                    
                    if (GUILayout.Button("Browse", GUILayout.Width(60)))
                    {
                        string path = EditorUtility.SaveFolderPanel("Select Download Folder", _downloadPath, "");
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (path.StartsWith(Application.dataPath))
                            {
                                _downloadPath = "Assets" + path.Substring(Application.dataPath.Length);
                                ApplySettings();
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
                
                if (_parentWindow != null)
                {
                    var cacheService = _parentWindow.GetCacheService();
                    var stats = cacheService.GetStatistics();
                    
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
                        EditorGUILayout.LabelField($"{stats.TotalEntries} total ({stats.ValidEntries} valid, {stats.ExpiredEntries} expired)");
                    }
                    EditorGUILayout.EndHorizontal();
                    
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.LabelField("Cache Size:", GUILayout.Width(120));
                        EditorGUILayout.LabelField(FormatHelper.FormatFileSize(stats.TotalSize));
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
                            _parentWindow.ShowCacheInfo();
                        }
                        
                        if (GUILayout.Button("Open Cache Folder"))
                        {
                            EditorUtility.RevealInFinder(CacheService.CacheDirectory);
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
                                _parentWindow.ClearCache();
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
                        _parentWindow?.TestGitHubConnection();
                    }
                    
                    if (GUILayout.Button("Force Refresh"))
                    {
                        _parentWindow?.LoadMatcapList();
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
                        EditorUtility.RevealInFinder(_downloadPath);
                    }
                    
                    if (GUILayout.Button("Create Material"))
                    {
                        var downloadService = _parentWindow?.GetDownloadService();
                        if (downloadService != null)
                        {
                            // Create a generic matcap material
                            Debug.Log("Material creation feature - select a matcap first");
                        }
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
                            _parentWindow?.DownloadAllMatcaps();
                        }
                    }
                    
                    if (GUILayout.Button("Show Help"))
                    {
                        _parentWindow?.ShowHelp();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }
        
        private void DrawFooter()
        {
            GUILayout.FlexibleSpace();
            
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
            if (_parentWindow != null)
            {
                var cacheService = _parentWindow.GetCacheService();
                int beforeCount = cacheService.CacheIndex.entries.Count;
                cacheService.CacheIndex.CleanExpiredEntries();
                cacheService.SaveCacheIndex();
                int afterCount = cacheService.CacheIndex.entries.Count;
                int removedCount = beforeCount - afterCount;
                
                EditorUtility.DisplayDialog("Cache Cleaned", 
                    $"Removed {removedCount} expired cache entries.\n\nRemaining entries: {afterCount}", "OK");
                
                Repaint();
            }
        }
        
        private void OnDestroy()
        {
            ApplySettings();
        }
    }
}

