/*
 * ============================================================================
 * CacheService - Service Layer
 * ============================================================================
 * 
 * 캐시 관리를 담당하는 서비스 클래스
 * - 캐시 초기화, 로드, 저장
 * - 캐시 유효성 검증
 * - 캐시 정리 및 삭제
 * 
 * ============================================================================
 */

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using ML.Editor.MatcapBrowser.Core;

namespace ML.Editor.MatcapBrowser.Services
{
    /// <summary>
    /// 캐시 관리 서비스
    /// MatCap 프리뷰 이미지의 로컬 캐싱을 담당합니다.
    /// </summary>
    public class CacheService
    {
        #region Constants
        
        private const string CacheDirName = "MatcapCache";
        private const string CacheIndexFile = "cache_index.json";
        
        #endregion
        
        #region Properties
        
        /// <summary>캐시 디렉토리 전체 경로</summary>
        public static string CacheDirectory => 
            Path.Combine(Application.dataPath, "..", "Library", CacheDirName);
        
        /// <summary>캐시 인덱스 파일 전체 경로</summary>
        public static string CacheIndexPath => 
            Path.Combine(CacheDirectory, CacheIndexFile);
        
        /// <summary>캐시 인덱스</summary>
        public MatcapCacheIndex CacheIndex { get; private set; }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// 캐시 시스템 초기화
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 캐시 디렉토리 생성
                if (!Directory.Exists(CacheDirectory))
                {
                    Directory.CreateDirectory(CacheDirectory);
                    Debug.Log($"Created matcap cache directory: {CacheDirectory}");
                }
                
                // 캐시 인덱스 로드
                LoadCacheIndex();
                
                // 만료된 엔트리 정리
                if (CacheIndex != null)
                {
                    CacheIndex.CleanExpiredEntries();
                    SaveCacheIndex();
                }
                
                Debug.Log($"Matcap cache initialized. Cached items: {CacheIndex?.entries.Count ?? 0}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to initialize matcap cache: {e.Message}");
                CacheIndex = new MatcapCacheIndex();
            }
        }
        
        #endregion
        
        #region Cache Index Management
        
        /// <summary>
        /// 캐시 인덱스 로드
        /// </summary>
        private void LoadCacheIndex()
        {
            if (File.Exists(CacheIndexPath))
            {
            try
            {
                string json = File.ReadAllText(CacheIndexPath);
                CacheIndex = JsonUtility.FromJson<MatcapCacheIndex>(json);
                    
                    if (CacheIndex == null)
                    {
                        CacheIndex = new MatcapCacheIndex();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load cache index: {e.Message}. Creating new index.");
                    CacheIndex = new MatcapCacheIndex();
                }
            }
            else
            {
                CacheIndex = new MatcapCacheIndex();
            }
        }
        
        /// <summary>
        /// 캐시 인덱스를 디스크에 저장
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
        
        #endregion
        
        #region Cache Operations
        
        /// <summary>
        /// 캐시에서 텍스처 로드
        /// </summary>
        public Texture2D LoadFromCache(string fileName)
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
                    UnityEngine.Object.DestroyImmediate(texture);
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load cached preview for {fileName}: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 텍스처를 캐시에 저장
        /// </summary>
        public void SaveToCache(string fileName, Texture2D texture)
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
        /// 모든 캐시 삭제
        /// </summary>
        public void ClearAllCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, true);
                    Directory.CreateDirectory(CacheDirectory);
                }
                
                CacheIndex = new MatcapCacheIndex();
                SaveCacheIndex();
                
                Debug.Log("Matcap cache cleared");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to clear cache: {e.Message}");
                throw;
            }
        }
        
        #endregion
        
        #region Cache Validation
        
        /// <summary>
        /// 캐시 엔트리 유효성 검증
        /// </summary>
        public bool IsCacheValid(CacheEntry entry)
        {
            if (entry == null || !entry.isValid)
                return false;
                
            string cachePath = Path.Combine(CacheDirectory, entry.cacheFileName);
            if (!File.Exists(cachePath))
                return false;
                
            long expireTime = DateTimeOffset.UtcNow.AddDays(-MatcapCacheIndex.CacheExpiryDays).ToUnixTimeSeconds();
            if (entry.cacheTime < expireTime)
                return false;
                
            return true;
        }
        
        #endregion
        
        #region Statistics
        
        /// <summary>
        /// 캐시 통계 정보 가져오기
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            if (CacheIndex == null)
                return new CacheStatistics();
            
            return new CacheStatistics
            {
                TotalEntries = CacheIndex.entries.Count,
                ValidEntries = CacheIndex.entries.Count(e => IsCacheValid(e)),
                TotalSize = CacheIndex.entries.Sum(e => (long)e.fileSize),
                ValidSize = CacheIndex.entries.Where(e => IsCacheValid(e)).Sum(e => (long)e.fileSize),
                LastUpdate = CacheIndex.lastUpdate
            };
        }
        
        #endregion
        
        #region Helper Methods
        
        private string GetCacheFileName(string originalFileName)
        {
            string hash = originalFileName.GetHashCode().ToString("X8");
            string extension = Path.GetExtension(originalFileName);
            return $"preview_{hash}{extension}";
        }
        
        #endregion
    }
    
    /// <summary>
    /// 캐시 통계 정보
    /// </summary>
    public struct CacheStatistics
    {
        public int TotalEntries;
        public int ValidEntries;
        public long TotalSize;
        public long ValidSize;
        public long LastUpdate;
        
        public int ExpiredEntries => TotalEntries - ValidEntries;
    }
}

