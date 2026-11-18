using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexus.Core.Contracts;
using Nexus.Models;

namespace Nexus.UI.ViewModels;

public sealed record ServiceDisplayRow(
    WindowsServiceInfo Raw,
    string ServiceName,
    string DisplayName,
    string StateText,
    string StartupText,
    string CategoryText,
    string RecommendationText,
    string PidText,
    bool IsCritical);

public sealed partial class ServiceManagerViewModel : ObservableObject
{
    private readonly IWindowsServiceManager _serviceManager;
    private IReadOnlyList<WindowsServiceInfo> _allServices = [];

    [ObservableProperty] public partial string SearchQuery { get; set; } = string.Empty;
    [ObservableProperty] public partial int SelectedStatusFilter { get; set; } // 0=Semua, 1=Berjalan, 2=Berhenti
    [ObservableProperty] public partial int SelectedCategoryFilter { get; set; } // 0=Semua, 1=Kandidat Optimasi, 2=Pengguna, 3=Pengembangan, 4=Kritis
    [ObservableProperty] public partial string SummaryText { get; set; } = "Memuat layanan Windows...";
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial ServiceDisplayRow? SelectedService { get; set; }
    [ObservableProperty] public partial string SelectedDependenciesText { get; set; } = "Pilih layanan untuk melihat dependensi & detail.";
    [ObservableProperty] public partial string SelectedDependentsText { get; set; } = "—";

    public ObservableCollection<ServiceDisplayRow> Services { get; } = [];

    public ServiceManagerViewModel(IWindowsServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            _allServices = await _serviceManager.GetAllServicesAsync();
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedStatusFilterChanged(int value) => ApplyFilter();
    partial void OnSelectedCategoryFilterChanged(int value) => ApplyFilter();

    partial void OnSelectedServiceChanged(ServiceDisplayRow? value)
    {
        if (value is null)
        {
            SelectedDependenciesText = "Pilih layanan untuk melihat detail.";
            SelectedDependentsText = "—";
            return;
        }

        SelectedDependenciesText = value.Raw.Dependencies.Count > 0
            ? string.Join(", ", value.Raw.Dependencies)
            : "Tidak ada dependensi langsung";
        SelectedDependentsText = value.Raw.DependentServices.Count > 0
            ? string.Join(", ", value.Raw.DependentServices)
            : "Tidak ada layanan lain yang bergantung";
    }

    private void ApplyFilter()
    {
        Services.Clear();
        string q = SearchQuery.Trim();

        var filtered = _allServices.Where(s =>
        {
            if (!string.IsNullOrEmpty(q) &&
                !s.ServiceName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !s.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !s.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (SelectedStatusFilter == 1 && s.State != ServiceState.Running) return false;
            if (SelectedStatusFilter == 2 && s.State != ServiceState.Stopped) return false;

            return SelectedCategoryFilter switch
            {
                1 => s.Category == ServiceCategory.OptimizationCandidate || s.Recommendation == ServiceRecommendation.OptInOptimizationCandidate,
                2 => s.Category == ServiceCategory.UserInstalled,
                3 => s.Category == ServiceCategory.Development,
                4 => s.IsCriticalProtected,
                _ => true
            };
        });

        foreach (var s in filtered)
        {
            Services.Add(new ServiceDisplayRow(
                s,
                s.ServiceName,
                s.DisplayName,
                TranslateState(s.State),
                TranslateStartup(s.StartupType),
                TranslateCategory(s.Category),
                TranslateRecommendation(s.Recommendation),
                s.ProcessId.HasValue ? s.ProcessId.Value.ToString() : "—",
                s.IsCriticalProtected));
        }

        int running = _allServices.Count(s => s.State == ServiceState.Running);
        int candidates = _allServices.Count(s => s.Recommendation == ServiceRecommendation.OptInOptimizationCandidate);
        SummaryText = $"Menampilkan {Services.Count} dari {_allServices.Count} layanan · {running} berjalan · {candidates} kandidat optimasi aman.";
    }

    public async Task StartSelectedAsync()
    {
        if (SelectedService is null) return;
        var res = await _serviceManager.StartServiceAsync(SelectedService.ServiceName);
        StatusMessage = res.Message;
        await RefreshAsync();
    }

    public async Task StopSelectedAsync(bool confirmed)
    {
        if (SelectedService is null) return;
        var res = await _serviceManager.StopServiceAsync(SelectedService.ServiceName, confirmed);
        StatusMessage = res.Message;
        await RefreshAsync();
    }

    public async Task RestartSelectedAsync(bool confirmed)
    {
        if (SelectedService is null) return;
        var res = await _serviceManager.RestartServiceAsync(SelectedService.ServiceName, confirmed);
        StatusMessage = res.Message;
        await RefreshAsync();
    }

    public async Task ChangeStartupSelectedAsync(ServiceStartupType type, bool confirmed)
    {
        if (SelectedService is null) return;
        var res = await _serviceManager.ChangeStartupTypeAsync(SelectedService.ServiceName, type, confirmed);
        StatusMessage = res.Message;
        await RefreshAsync();
    }

    public async Task RollbackSelectedAsync()
    {
        if (SelectedService is null) return;
        var res = await _serviceManager.RollbackServiceAsync(SelectedService.ServiceName);
        StatusMessage = res.Message;
        await RefreshAsync();
    }

    private static string TranslateState(ServiceState s) => s switch
    {
        ServiceState.Running => "Berjalan",
        ServiceState.Stopped => "Berhenti",
        ServiceState.StartPending => "Memulai...",
        ServiceState.StopPending => "Menghentikan...",
        ServiceState.Paused => "Dijeda",
        _ => "Tidak diketahui"
    };

    private static string TranslateStartup(ServiceStartupType t) => t switch
    {
        ServiceStartupType.Automatic => "Otomatis",
        ServiceStartupType.AutomaticDelayedStart => "Otomatis (Tertunda)",
        ServiceStartupType.Manual => "Manual",
        ServiceStartupType.Disabled => "Nonaktif",
        ServiceStartupType.Boot => "Boot",
        ServiceStartupType.System => "Sistem",
        _ => "—"
    };

    private static string TranslateCategory(ServiceCategory c) => c switch
    {
        ServiceCategory.CriticalWindows => "Kritis Windows",
        ServiceCategory.Security => "Keamanan",
        ServiceCategory.Networking => "Jaringan",
        ServiceCategory.Hardware => "Perangkat Keras",
        ServiceCategory.Development => "Pengembangan",
        ServiceCategory.Optional => "Opsional",
        ServiceCategory.UserInstalled => "Aplikasi Pengguna",
        ServiceCategory.OptimizationCandidate => "Kandidat Optimasi",
        _ => "—"
    };

    private static string TranslateRecommendation(ServiceRecommendation r) => r switch
    {
        ServiceRecommendation.KeepProtected => "Terkunci (Wajib Aktif)",
        ServiceRecommendation.KeepDefault => "Biarkan Default",
        ServiceRecommendation.ReviewManual => "Pertimbangkan Manual",
        ServiceRecommendation.OptInOptimizationCandidate => "Aman Dioptimasi (Opt-In)",
        _ => "—"
    };
}

