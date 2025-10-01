/*
 * ============================================================================
 * CacheEntry - Data Model
 * ============================================================================
 * 
 * 개별 캐시 파일의 정보를 저장하는 데이터 모델
 * 
 * ============================================================================
 */

using System;

namespace ML.Editor.MatcapBrowser.Core
{
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
}

