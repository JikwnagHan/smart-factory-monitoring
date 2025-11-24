# 스택 확정 + 최소 실행 가이드

"C#/.NET + WinForms + MariaDB + LiveCharts2 + EF Core" 스택을 현실적으로 바로 돌릴 수 있는 최소 단계를 초보 개발자도 따라 할 수 있게 정리했다. 설계서(`docs/스마트제조_장비모니터링_시스템_설계서_v0.1.docx`)의 2.2 모듈 구성과 3.2 요구사항에서 언급된 흐름을 실제 프로젝트 뼈대에 매핑한다.

## 1. 개발 환경 세팅

1. **.NET 8 SDK 설치**  
   - [공식 다운로드](https://dotnet.microsoft.com/download/dotnet/8.0)에서 설치 후 `dotnet --version`으로 8.x.x 확인.
2. **IDE 선택**  
   - **Visual Studio 2022 Community(권장)**: 설치 시 워크로드 "**.NET 데스크톱 개발**" 필수, "**데이터 저장 및 처리**"는 선택.  
   - 또는 **VS Code + C# Dev Kit**(WinForms 디자이너 편의는 VS가 우수).

## 2. 솔루션/프로젝트 뼈대 만들기

작업 폴더에서 다음을 실행한다.

```bash
mkdir SmartFactoryMonitoring
cd SmartFactoryMonitoring

# 솔루션 생성
dotnet new sln -n SmartFactoryMonitoring

# 프로젝트 생성 (.NET 8)
dotnet new winforms -n Monitoring.Desktop -f net8.0-windows
|-- 실행용 UI

dotnet new classlib -n Monitoring.Core -f net8.0
|-- 도메인/비즈니스 로직

dotnet new classlib -n Monitoring.Data -f net8.0
|-- EF Core, DbContext
```

프로젝트를 솔루션에 추가하고 참조를 설정한다.

```bash
# 솔루션에 추가
dotnet sln SmartFactoryMonitoring.sln add Monitoring.Desktop/Monitoring.Desktop.csproj
dotnet sln SmartFactoryMonitoring.sln add Monitoring.Core/Monitoring.Core.csproj
dotnet sln SmartFactoryMonitoring.sln add Monitoring.Data/Monitoring.Data.csproj

# 참조 구조
# Desktop -> Core, Data
dotnet add Monitoring.Desktop/Monitoring.Desktop.csproj reference Monitoring.Core/Monitoring.Core.csproj
dotnet add Monitoring.Desktop/Monitoring.Desktop.csproj reference Monitoring.Data/Monitoring.Data.csproj

# Data -> Core (엔티티 공유)
dotnet add Monitoring.Data/Monitoring.Data.csproj reference Monitoring.Core/Monitoring.Core.csproj
```

> Visual Studio를 사용하면 솔루션 열기만으로 3개 프로젝트가 보이는 상태가 된다.

## 3. MariaDB 설치 및 초기 DB 준비

1. [MariaDB 설치](https://mariadb.org/download/): 설치 중 root 비밀번호 설정, 포트는 기본 3306 사용.
2. `mysql` 또는 HeidiSQL로 접속 후 DB/계정을 만든다.

```sql
CREATE DATABASE smart_monitoring CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci;
CREATE USER 'monitoring'@'%' IDENTIFIED BY '강력한_비밀번호';
GRANT ALL PRIVILEGES ON smart_monitoring.* TO 'monitoring'@'%';
FLUSH PRIVILEGES;
```

> 로컬만 쓸 경우 `'localhost'`로 제한하면 보안에 유리하다.

## 4. EF Core + MariaDB Provider 설정 (Monitoring.Data)

1. NuGet 패키지 추가

```bash
# EF Core
dotnet add Monitoring.Data/Monitoring.Data.csproj package Microsoft.EntityFrameworkCore
dotnet add Monitoring.Data/Monitoring.Data.csproj package Microsoft.EntityFrameworkCore.Design

# MariaDB Provider - Pomelo
dotnet add Monitoring.Data/Monitoring.Data.csproj package Pomelo.EntityFrameworkCore.MySql
```

2. 글로벌 도구 설치(마이그레이션용)

```bash
dotnet tool install --global dotnet-ef
```

3. 기본 엔티티 정의 (`Monitoring.Core/Entities`)

```csharp
// Device.cs
namespace Monitoring.Core.Entities
{
    public class Device
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? LineName { get; set; }
        public string? Location { get; set; }
        public string ProtocolType { get; set; } = ""; // ModbusTcp 등
        public bool IsEnabled { get; set; } = true;

        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }
}

// Tag.cs
namespace Monitoring.Core.Entities
{
    public class Tag
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string Name { get; set; } = "";
        public string Address { get; set; } = ""; // Modbus 주소 등
        public string DataType { get; set; } = ""; // Analog/Digital 등
        public string? Unit { get; set; }
        public int PollingIntervalMs { get; set; } = 1000;
        public bool IsEnabled { get; set; } = true;

        public Device? Device { get; set; }
    }
}
```

4. DbContext 정의 (`Monitoring.Data/MonitoringDbContext.cs`)

```csharp
using Microsoft.EntityFrameworkCore;
using Monitoring.Core.Entities;

namespace Monitoring.Data
{
    public class MonitoringDbContext : DbContext
    {
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<Tag> Tags => Set<Tag>();

        public MonitoringDbContext(DbContextOptions<MonitoringDbContext> options) : base(options) { }
        public MonitoringDbContext() { }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = "Server=localhost;Port=3306;Database=smart_monitoring;User=monitoring;Password=비밀번호;";
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Device>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.ProtocolType).HasMaxLength(50).IsRequired();
            });

            modelBuilder.Entity<Tag>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Address).HasMaxLength(100).IsRequired();
                entity.HasOne(e => e.Device)
                      .WithMany(d => d.Tags)
                      .HasForeignKey(e => e.DeviceId);
            });
        }
    }
}
```

> 초기 PoC에서는 코드에 연결 문자열을 두고, 이후 `appsettings.json`/환경변수로 분리해도 된다.

5. 초기 마이그레이션 & DB 생성 (솔루션 루트)

```bash
# 마이그레이션 생성
dotnet ef migrations add InitialCreate \
  --project Monitoring.Data/Monitoring.Data.csproj \
  --startup-project Monitoring.Desktop/Monitoring.Desktop.csproj

# DB 적용
dotnet ef database update \
  --project Monitoring.Data/Monitoring.Data.csproj \
  --startup-project Monitoring.Desktop/Monitoring.Desktop.csproj
```

실행 후 `smart_monitoring`에 `Devices`, `Tags` 테이블이 생성돼야 한다.

## 5. WinForms에서 DbContext 사용

`Monitoring.Desktop`에서 `MonitoringDbContext`를 생성해 기본 CRUD를 검증한다.

```csharp
// Program.cs
using Monitoring.Data;

ApplicationConfiguration.Initialize();
using var dbContext = new MonitoringDbContext();
Application.Run(new MainForm(dbContext));
```

```csharp
// MainForm.cs
using Monitoring.Core.Entities;
using Monitoring.Data;
using System.Linq;

private readonly MonitoringDbContext _db;

public MainForm(MonitoringDbContext db)
{
    _db = db;
    InitializeComponent();
}

private void MainForm_Load(object sender, EventArgs e)
{
    if (!_db.Devices.Any())
    {
        _db.Devices.Add(new Device
        {
            Name = "Test Device",
            LineName = "Line1",
            ProtocolType = "Simulated",
            IsEnabled = true
        });
        _db.SaveChanges();
    }

    var count = _db.Devices.Count();
    MessageBox.Show($"등록된 장치 수: {count}");
}
```

## 6. LiveCharts2 세팅(WinForms)

1. 패키지 추가

```bash
dotnet add Monitoring.Desktop/Monitoring.Desktop.csproj package LiveChartsCore.SkiaSharpView.WinForms
dotnet add Monitoring.Desktop/Monitoring.Desktop.csproj package LiveChartsCore
dotnet add Monitoring.Desktop/Monitoring.Desktop.csproj package SkiaSharp
dotnet add Monitoring.Desktop/Monitoring.Desktop.csproj package SkiaSharp.Views.WindowsForms
```

2. 기본 차트 컨트롤 배치(코드 예시)

```csharp
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinForms;

private CartesianChart _chart;
private ISeries[] _series;

private void InitChart()
{
    _chart = new CartesianChart { Dock = DockStyle.Fill };
    _series = new ISeries[]
    {
        new LineSeries<double> { Values = new List<double> { 3, 5, 2, 7, 4 } }
    };
    _chart.Series = _series;
    Controls.Add(_chart);
}
```

## 7. 1단계 체크리스트

- `dotnet --version`이 8.x.x이다.
- Visual Studio에서 `SmartFactoryMonitoring.sln`을 열 수 있다.
- `Monitoring.Desktop` 실행 시 MariaDB 연결 성공 & `Device` 테이블에 테스트 데이터 1건 생성/조회.
- MariaDB `smart_monitoring.Devices`에서 데이터 확인.
- MainForm에 LiveCharts2 차트가 표시되고 샘플 곡선 렌더링.
- 소스가 Git에 커밋되어 있고 `/docs` 폴더에 설계서와 본 가이드가 포함된다.
