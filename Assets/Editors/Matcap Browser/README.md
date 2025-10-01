# Matcap Browser - Refactored Architecture

## 📋 개요

이 프로젝트는 Unity 에디터에서 MatCap 텍스처를 검색, 미리보기, 다운로드할 수 있는 전문 브라우저입니다.

**리팩토링을 통해 단일 2828라인 파일에서 모듈화된 구조로 개선되었습니다.**

---

## 🏗️ 아키텍처

### **SOLID 원칙 적용**
- **S**ingle Responsibility: 각 클래스가 단일 책임만 가짐
- **O**pen/Closed: 확장에는 열려있고 수정에는 닫혀있음
- **L**iskov Substitution: 서비스 인터페이스를 통한 대체 가능성
- **I**nterface Segregation: 명확한 역할 분리
- **D**ependency Inversion: 서비스 레이어를 통한 의존성 역전

### **레이어 구조**

```
Matcap Browser/
├── Core/                    # 데이터 모델 (Data Layer)
│   ├── MatcapItem.cs       # MatCap 아이템 데이터
│   ├── CacheEntry.cs       # 캐시 엔트리 데이터
│   └── CacheIndex.cs       # MatCap 캐시 인덱스 데이터
│
├── Services/                # 비즈니스 로직 (Service Layer)
│   ├── CacheService.cs     # 캐시 관리 서비스
│   ├── GitHubService.cs    # GitHub API 통신 서비스
│   └── DownloadService.cs  # 다운로드 및 저장 서비스
│
├── UI/                      # 사용자 인터페이스 (Presentation Layer)
│   ├── MatcapLibraryWindow.cs    # 메인 윈도우 (400 lines)
│   └── MatcapLibrarySettings.cs  # 설정 윈도우
│
└── Utilities/               # 공통 유틸리티
    ├── EditorCoroutine.cs  # 에디터 코루틴 헬퍼
    └── FormatHelper.cs     # 포맷팅 유틸리티
```

---

## 🎯 주요 개선사항

### **1. 모듈화**
- **기존**: 단일 파일 2828 lines
- **개선**: 11개의 파일로 분리, 평균 200-400 lines

### **2. 관심사의 분리 (Separation of Concerns)**
- **UI 로직**: `MatcapLibraryWindow.cs`만 담당
- **비즈니스 로직**: `Services/` 레이어에서 처리
- **데이터 모델**: `Core/` 레이어에서 관리

### **3. 테스트 용이성**
- 각 서비스를 독립적으로 테스트 가능
- Mock 객체를 통한 단위 테스트 가능

### **4. 유지보수성**
- 각 파일의 역할이 명확함
- 코드 변경 시 영향 범위 최소화
- 새로운 기능 추가 시 기존 코드 수정 불필요

### **5. 확장성**
- 새로운 서비스 추가 용이
- 다른 데이터 소스로 쉽게 확장 가능
- UI 컴포넌트 재사용 가능

---

## 📦 각 레이어 설명

### **Core (데이터 모델)**
```csharp
// 순수한 데이터 구조, 비즈니스 로직 없음
public class MatcapItem
{
    public string name;
    public string fileName;
    public Texture2D preview;
    public bool isDownloading;
    public bool isDownloaded;
}
```

### **Services (비즈니스 로직)**
```csharp
// 캐시 관리
public class CacheService
{
    public void Initialize();
    public Texture2D LoadFromCache(string fileName);
    public void SaveToCache(string fileName, Texture2D texture);
    public void ClearAllCache();
}

// GitHub 통신
public class GitHubService
{
    public IEnumerator FetchMatcapListFromAPI(...);
    public IEnumerator DownloadPreview(...);
    public IEnumerator DownloadMatcap(...);
}

// 다운로드 & 저장
public class DownloadService
{
    public bool SaveTexture(Texture2D texture, string fileName);
    public Material CreateMaterial(string fileName, string materialName);
}
```

### **UI (프레젠테이션)**
```csharp
// 서비스를 조합하여 사용
public class MatcapLibraryWindow : EditorWindow
{
    private CacheService _cacheService;
    private GitHubService _githubService;
    private DownloadService _downloadService;
    
    // UI 로직만 담당
    private void DrawMatcapGrid() { }
    private void DrawToolbar() { }
}
```

---


## 🚀 사용 방법

### **기본 사용**
```csharp
// Window > Matcap Library 메뉴에서 열기
MatcapLibraryWindow.ShowWindow();
```

### **프로그래밍 방식**
```csharp
// 서비스 직접 사용 가능
var cacheService = new CacheService();
cacheService.Initialize();

var githubService = new GitHubService();
// ...
```

---

## 🔧 확장 가능성

### **새로운 데이터 소스 추가**
```csharp
// IMatcapService 인터페이스 구현
public interface IMatcapService
{
    IEnumerator FetchMatcapList(...);
    IEnumerator DownloadMatcap(...);
}

// 예: 로컬 파일 시스템, 다른 Git 저장소 등
public class LocalFileService : IMatcapService { }
public class GitLabService : IMatcapService { }
```

### **캐시 전략 변경**
```csharp
// ICacheStrategy 인터페이스 구현
public class LRUCacheStrategy : ICacheStrategy { }
public class TimedCacheStrategy : ICacheStrategy { }
```

---

## 📚 설계 패턴

1. **Service Locator Pattern**: 서비스 인스턴스 관리
2. **Repository Pattern**: 데이터 접근 추상화
3. **Observer Pattern**: UI 업데이트 (Repaint)
4. **Factory Pattern**: MatcapItem 생성
5. **Strategy Pattern**: 정렬, 필터링

---

## ✅ 베스트 프랙티스

### **1. 명확한 네이밍**
- `CacheService`: 캐시 관련 모든 작업
- `GitHubService`: GitHub API 통신
- `DownloadService`: 다운로드 및 저장

### **2. 단일 책임**
- 각 클래스는 하나의 책임만 가짐
- 변경 이유가 단 하나

### **3. 의존성 주입**
```csharp
// 생성자 주입 패턴 (향후 개선 가능)
public MatcapLibraryWindow(
    CacheService cacheService,
    GitHubService githubService,
    DownloadService downloadService)
{
    _cacheService = cacheService;
    _githubService = githubService;
    _downloadService = downloadService;
}
```

### **4. 에러 처리**
```csharp
// 서비스 레벨에서 에러 처리
public IEnumerator DownloadMatcap(
    string fileName, 
    Action<Texture2D> onSuccess, 
    Action<string> onError)
{
    try
    {
        // ...
        onSuccess?.Invoke(texture);
    }
    catch (Exception e)
    {
        onError?.Invoke(e.Message);
    }
}
```

---

## 📝 라이선스

MIT License

---

## 👥 기여

리팩토링 제안 및 개선 사항은 언제든 환영합니다!

