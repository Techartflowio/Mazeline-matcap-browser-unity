/*
 * ============================================================================
 * MatcapItem - Data Model
 * ============================================================================
 * 
 * 개별 MatCap 텍스처의 정보와 상태를 저장하는 데이터 모델
 * 
 * ============================================================================
 */

using System;
using UnityEngine;

namespace ML.Editor.MatcapBrowser.Core
{
    /// <summary>
    /// MatCap 아이템 데이터 클래스
    /// 개별 MatCap 텍스처의 정보와 상태를 저장합니다.
    /// </summary>
    [Serializable]
    public class MatcapItem
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
        
        /// <summary>
        /// MatcapItem 생성자
        /// </summary>
        public MatcapItem(string fileName)
        {
            this.fileName = fileName;
            this.name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            this.isDownloading = false;
            this.isDownloaded = false;
        }
    }
}

