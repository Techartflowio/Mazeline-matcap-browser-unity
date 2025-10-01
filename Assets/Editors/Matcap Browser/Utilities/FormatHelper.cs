/*
 * ============================================================================
 * FormatHelper - Utility
 * ============================================================================
 * 
 * 포맷팅 유틸리티 함수들
 * 
 * ============================================================================
 */

namespace ML.Editor.MatcapBrowser.Utilities
{
    /// <summary>
    /// 포맷팅 헬퍼 클래스
    /// </summary>
    public static class FormatHelper
    {
        /// <summary>
        /// 바이트 크기를 사람이 읽기 쉬운 형식으로 변환
        /// </summary>
        public static string FormatFileSize(long bytes)
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
    }
}

