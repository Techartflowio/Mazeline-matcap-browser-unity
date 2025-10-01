/*
 * ============================================================================
 * CacheIndex - Data Model
 * ============================================================================
 * 
 * 전체 캐시 시스템의 인덱스를 관리하는 데이터 모델
 * 
 * ============================================================================
 */

using System;
using System.Collections.Generic;
using System.Linq;

namespace ML.Editor.MatcapBrowser.Core
{
    /// <summary>
    /// 캐시 인덱스 클래스
    /// 전체 캐시 시스템의 인덱스를 관리합니다.
    /// </summary>
    [Serializable]
    public class MatcapCacheIndex
    {
        /// <summary>캐시 만료 기간 (일)</summary>
        public const int CacheExpiryDays = 7;
        
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
}

