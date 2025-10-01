/*
 * ============================================================================
 * GitHubService - Service Layer
 * ============================================================================
 * 
 * GitHub API 통신을 담당하는 서비스 클래스
 * - MatCap 목록 가져오기
 * - 파일 다운로드
 * - 연결 테스트
 * 
 * ============================================================================
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace ML.Editor.MatcapBrowser.Services
{
    /// <summary>
    /// GitHub API 통신 서비스
    /// nidorx/matcaps 레포지토리와 통신을 담당합니다.
    /// </summary>
    public class GitHubService
    {
        #region Constants
        
        private const string GitHubApiBase = "https://api.github.com/repos/nidorx/matcaps/contents/";
        private const string GitHubRawBase = "https://raw.githubusercontent.com/nidorx/matcaps/master/";
        private const string GitHubPageUrl = "https://github.com/nidorx/matcaps/tree/master/preview";
        private const string UserAgent = "Unity-MatcapLibrary";
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// GitHub API를 통해 MatCap 파일 목록 가져오기
        /// </summary>
        public IEnumerator FetchMatcapListFromAPI(Action<List<string>> onSuccess, Action<string> onError)
        {
            string[] directories = { "preview", "256", "512", "1024" };
            HashSet<string> uniqueFiles = new HashSet<string>();
            
            foreach (string dir in directories)
            {
                string apiUrl = GitHubApiBase + dir;
                using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
                {
                    request.timeout = 15;
                    request.SetRequestHeader("User-Agent", UserAgent);
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
                            
                            // preview 디렉토리에서 파일을 찾았으면 충분
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
                
                yield return null;
            }
            
            if (uniqueFiles.Count > 0)
            {
                onSuccess?.Invoke(uniqueFiles.ToList());
            }
            else
            {
                onError?.Invoke("No files found in GitHub API");
            }
        }
        
        /// <summary>
        /// GitHub 페이지 스크래핑을 통해 MatCap 파일 목록 가져오기
        /// </summary>
        public IEnumerator FetchMatcapListFromPage(Action<List<string>> onSuccess, Action<string> onError)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(GitHubPageUrl))
            {
                request.timeout = 15;
                request.SetRequestHeader("User-Agent", UserAgent);
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string html = request.downloadHandler.text;
                    
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
                        
                        if (uniqueFiles.Count > 10)
                            break;
                    }
                    
                    if (uniqueFiles.Count > 0)
                    {
                        onSuccess?.Invoke(uniqueFiles.ToList());
                    }
                    else
                    {
                        onError?.Invoke("No valid matcap files found in page");
                    }
                }
                else
                {
                    onError?.Invoke($"Could not fetch matcap list from GitHub page: {request.error}");
                }
            }
        }
        
        /// <summary>
        /// 프리뷰 이미지 다운로드
        /// </summary>
        public IEnumerator DownloadPreview(string fileName, Action<Texture2D> onSuccess, Action<string> onError)
        {
            string url = GitHubRawBase + "preview/" + fileName;
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    onSuccess?.Invoke(texture);
                }
                else
                {
                    onError?.Invoke($"Failed to load preview: {request.error}");
                }
            }
        }
        
        /// <summary>
        /// 고해상도 MatCap 다운로드 (1024px)
        /// </summary>
        public IEnumerator DownloadMatcap(string fileName, int resolution, Action<Texture2D> onSuccess, Action<string> onError)
        {
            string cleanFileName = CleanFileName(fileName);
            string url = $"{GitHubRawBase}{resolution}/{cleanFileName}";
            
            using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 30;
                request.SetRequestHeader("User-Agent", UserAgent);
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = DownloadHandlerTexture.GetContent(request);
                    onSuccess?.Invoke(texture);
                }
                else
                {
                    // 대체 파일명 시도
                    yield return TryAlternativeDownload(fileName, resolution, onSuccess, onError);
                }
            }
        }
        
        /// <summary>
        /// GitHub 연결 테스트
        /// </summary>
        public IEnumerator TestConnection(Action<string> onResult)
        {
            string result = "=== GitHub Connection Test ===\n\n";
            
            // Test API
            result += "Testing GitHub API...\n";
            using (UnityWebRequest request = UnityWebRequest.Get(GitHubApiBase + "preview"))
            {
                request.timeout = 10;
                request.SetRequestHeader("User-Agent", UserAgent);
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var files = ParseGitHubAPIResponse(request.downloadHandler.text);
                    result += $"✓ API 연결 성공 ({files.Count} files found)\n\n";
                }
                else
                {
                    result += $"✗ API 연결 실패: {request.error}\n\n";
                }
            }
            
            // Test Page
            result += "Testing GitHub Page...\n";
            using (UnityWebRequest request = UnityWebRequest.Get(GitHubPageUrl))
            {
                request.timeout = 10;
                request.SetRequestHeader("User-Agent", UserAgent);
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    result += $"✓ Page 연결 성공\n\n";
                }
                else
                {
                    result += $"✗ Page 연결 실패: {request.error}\n\n";
                }
            }
            
            result += "=== Test Complete ===";
            onResult?.Invoke(result);
        }
        
        #endregion
        
        #region Helper Methods
        
        private List<string> ParseGitHubAPIResponse(string json)
        {
            List<string> fileNames = new List<string>();
            
            try
            {
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
                
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            
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
        
        private string CleanFileName(string fileName)
        {
            string cleanFileName = fileName;
            if (cleanFileName.Contains("-preview"))
            {
                cleanFileName = cleanFileName.Replace("-preview", "");
            }
            
            if (cleanFileName.EndsWith("-"))
            {
                cleanFileName = cleanFileName.TrimEnd('-');
            }
            
            return cleanFileName;
        }
        
        private IEnumerator TryAlternativeDownload(string fileName, int resolution, Action<Texture2D> onSuccess, Action<string> onError)
        {
            string cleanFileName = CleanFileName(fileName);
            string[] alternativeNames = GenerateAlternativeFileNames(cleanFileName);
            
            foreach (string altName in alternativeNames)
            {
                string url = $"{GitHubRawBase}{resolution}/{altName}";
                
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
                {
                    request.timeout = 30;
                    request.SetRequestHeader("User-Agent", UserAgent);
                    yield return request.SendWebRequest();
                    
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Texture2D texture = DownloadHandlerTexture.GetContent(request);
                        onSuccess?.Invoke(texture);
                        yield break;
                    }
                }
                
                yield return null;
            }
            
            onError?.Invoke("All download attempts failed");
        }
        
        private string[] GenerateAlternativeFileNames(string fileName)
        {
            List<string> alternatives = new List<string>();
            
            alternatives.Add(fileName);
            
            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            if (nameWithoutExt != fileName)
            {
                alternatives.Add(nameWithoutExt + ".png");
            }
            
            alternatives.Add(fileName.ToLower());
            alternatives.Add(nameWithoutExt.ToLower() + ".png");
            
            if (fileName.Contains("_"))
            {
                alternatives.Add(fileName.Replace("_", "-"));
                alternatives.Add(nameWithoutExt.Replace("_", "-") + ".png");
            }
            
            return alternatives.Distinct().ToArray();
        }
        
        #endregion
    }
}

