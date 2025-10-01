/*
 * ============================================================================
 * DownloadService - Service Layer
 * ============================================================================
 * 
 * MatCap 다운로드 및 저장을 담당하는 서비스 클래스
 * - 텍스처 다운로드
 * - 파일 저장
 * - Material 생성
 * 
 * ============================================================================
 */

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ML.Editor.MatcapBrowser.Services
{
    /// <summary>
    /// MatCap 다운로드 및 저장 서비스
    /// </summary>
    public class DownloadService
    {
        #region Constants
        
        private const int FixedResolution = 1024;
        
        #endregion
        
        #region Properties
        
        public string DownloadPath { get; set; } = "Assets/Matcaps";
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// 텍스처를 파일로 저장
        /// </summary>
        public bool SaveTexture(Texture2D texture, string fileName)
        {
            try
            {
                // 디렉토리 생성
                if (!Directory.Exists(DownloadPath))
                {
                    Directory.CreateDirectory(DownloadPath);
                    Debug.Log($"Created directory: {DownloadPath}");
                }
                
                // Matcap_ 접두사 추가
                string safeFileName = $"Matcap_{fileName}";
                safeFileName = Path.GetFileName(safeFileName);
                
                string fullPath = Path.Combine(DownloadPath, safeFileName);
                
                // PNG 인코딩 및 저장
                byte[] bytes = texture.EncodeToPNG();
                
                if (bytes != null && bytes.Length > 0)
                {
                    File.WriteAllBytes(fullPath, bytes);
                    Debug.Log($"파일 저장 완료: {fullPath} (크기: {bytes.Length} bytes)");
                    
                    // Import 설정
                    AssetDatabase.ImportAsset(fullPath);
                    ApplyTextureImportSettings(fullPath);
                    
                    return true;
                }
                else
                {
                    Debug.LogError($"PNG 인코딩 실패: {fileName}");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"파일 저장 중 오류: {fileName} - {e.Message}");
                return false;
            }
            finally
            {
                AssetDatabase.Refresh();
            }
        }
        
        /// <summary>
        /// MatCap이 이미 다운로드되었는지 확인
        /// </summary>
        public bool IsDownloaded(string fileName)
        {
            string cleanFileName = CleanFileName(fileName);
            string fullPath = Path.Combine(DownloadPath, $"Matcap_{cleanFileName}");
            return File.Exists(fullPath);
        }
        
        /// <summary>
        /// MatCap Material 생성
        /// </summary>
        public Material CreateMaterial(string fileName, string materialName = null)
        {
            string cleanFileName = CleanFileName(fileName);
            string texturePath = Path.Combine(DownloadPath, $"Matcap_{cleanFileName}");
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            
            if (texture == null)
            {
                Debug.LogWarning($"Texture not found: {texturePath}");
                return null;
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
                
                string matName = materialName ?? Path.GetFileNameWithoutExtension(fileName);
                string materialPath = Path.Combine(DownloadPath, $"Mat_{matName}.mat");
                
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                
                return material;
            }
            
            return null;
        }
        
        /// <summary>
        /// 다운로드 폴더 열기
        /// </summary>
        public void OpenDownloadFolder()
        {
            EditorUtility.RevealInFinder(DownloadPath);
        }
        
        #endregion
        
        #region Helper Methods
        
        private void ApplyTextureImportSettings(string texturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
            }
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
        
        #endregion
    }
}

