using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Roomy.Core;
using Roomy.Localization;

namespace Roomy.ViewModels;

public enum AppState { Home, Scanning, Results, Cleaning, Done }

public sealed class MainVM : ObservableObject
{
    bool _busy;
    long _demoFreed;

    public MainVM(bool demo)
    {
        IsDemo = demo;
        IsAdmin = demo || Admin.IsElevated;
        ScanCommand = new RelayCommand(() => _ = ScanAsync());
        CleanCommand = new RelayCommand(() => _ = CleanAsync(), () => SelectedTotal > 0);
        RestartAdminCommand = new RelayCommand(RestartAsAdmin);
        ShareCommand = new RelayCommand(() => ShareRequested?.Invoke(this, EventArgs.Empty));
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        HomeCommand = new RelayCommand(() => { RefreshDrives(); State = AppState.Home; });
        ConfirmYesCommand = new RelayCommand(() => CloseConfirm(true));
        ConfirmNoCommand = new RelayCommand(() => CloseConfirm(false));
        SpaceMap = new SpaceMapVM(this);
        RefreshDrives();
    }

    public bool IsDemo { get; }
    public bool IsAdmin { get; }
    public bool ShowAdminBadge => Admin.IsElevated && !IsDemo;
    public SpaceMapVM SpaceMap { get; }
    public event EventHandler? ShareRequested;
    public event EventHandler? SettingsRequested;
    public RelayCommand SettingsCommand { get; }

    public RelayCommand ScanCommand { get; }
    public RelayCommand CleanCommand { get; }
    public RelayCommand RestartAdminCommand { get; }
    public RelayCommand ShareCommand { get; }
    public RelayCommand HomeCommand { get; }
    public RelayCommand ConfirmYesCommand { get; }
    public RelayCommand ConfirmNoCommand { get; }

    AppState _state = AppState.Home;
    public AppState State { get => _state; private set => Set(ref _state, value); }

    // ---------- drives ----------

    public ObservableCollection<DriveVM> Drives { get; } = [];
    public DriveVM? SystemDrive { get; private set; }
    public IEnumerable<DriveVM> OtherDrives => Drives.Where(d => d != SystemDrive);
    public bool HasOtherDrives => OtherDrives.Any();

    public void RefreshDrives()
    {
        var found = new List<(string Root, string Label, long Total, long Free)>();
        if (IsDemo)
        {
            foreach (var d in DemoData.Drives)
                found.Add((d.Root, d.Label, d.Total, d.Root == DemoData.Drives[0].Root ? d.Free + _demoFreed : d.Free));
        }
        else
        {
            foreach (var di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.DriveType == DriveType.Fixed && di.IsReady)
                        found.Add((di.Name, di.VolumeLabel, di.TotalSize, di.TotalFreeSpace));
                }
                catch { }
            }
        }

        foreach (var f in found)
        {
            var existing = Drives.FirstOrDefault(d => string.Equals(d.Root, f.Root, StringComparison.OrdinalIgnoreCase));
            if (existing != null) existing.Update(f.Total, f.Free);
            else Drives.Add(new DriveVM(f.Root, f.Label, f.Total, f.Free));
        }

        SystemDrive ??= (IsDemo ? Drives.FirstOrDefault() : null)
                        ?? Drives.FirstOrDefault(d => string.Equals(d.Root, Env.SystemRoot, StringComparison.OrdinalIgnoreCase))
                        ?? Drives.FirstOrDefault();
        Raise(nameof(SystemDrive));
        Raise(nameof(OtherDrives));
        Raise(nameof(HasOtherDrives));
    }

    // ---------- scanning ----------

    public ObservableCollection<ScanStepVM> ScanSteps { get; } = [];

    string _scanStep = "";
    public string ScanStep { get => _scanStep; private set => Set(ref _scanStep, value); }

    long _scanFound;
    public long ScanFound { get => _scanFound; private set => Set(ref _scanFound, value); }

    async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            ScanSteps.Clear();
            foreach (var s in Scanner.Steps) ScanSteps.Add(new ScanStepVM(s.Title));
            ScanFound = 0;
            IsCleanupTab = true;
            State = AppState.Scanning;

            var ctx = new ScanContext { IsAdmin = IsAdmin };
            var results = new List<CleanupCategory>();
            for (int i = 0; i < Scanner.Steps.Count; i++)
            {
                var step = Scanner.Steps[i];
                var row = ScanSteps[i];
                row.State = StepState.Active;
                ScanStep = Loc.Format("CheckingStep", step.Title);

                var sw = Stopwatch.StartNew();
                var cat = IsDemo ? DemoData.Category(step.Id) : await Task.Run(() => SafeRun(step, ctx));
                // A tiny pause so each row visibly ticks off; real work usually takes longer anyway.
                int min = IsDemo ? 240 : 90;
                if (sw.ElapsedMilliseconds < min) await Task.Delay(min - (int)sw.ElapsedMilliseconds);

                row.Finish(cat);
                if (cat != null)
                {
                    results.Add(cat);
                    if (cat.Safety == Safety.Safe && !cat.Blocked) ScanFound += cat.Size;
                }
            }

            BuildCategories(results);
            await Task.Delay(400);
            State = AppState.Results;
        }
        catch (Exception ex)
        {
            ShowToast(S.ScanProblem + ex.Message);
            State = AppState.Home;
        }
        finally
        {
            _busy = false;
        }
    }

    static CleanupCategory? SafeRun(ScanStep s, ScanContext c)
    {
        try { return s.Run(c); }
        catch { return null; }
    }

    // ---------- results ----------

    public ObservableCollection<CategoryVM> Categories { get; } = [];

    void BuildCategories(List<CleanupCategory> results)
    {
        Categories.Clear();
        var shown = results.Where(c => c.Blocked || c.Size > 0).ToList();
        var ordered = shown.Where(c => c.Safety == Safety.Safe && !c.Blocked).OrderByDescending(c => c.Size)
            .Concat(shown.Where(c => c.Blocked))
            .Concat(shown.Where(c => c.Safety == Safety.Review && !c.Blocked).OrderByDescending(c => c.Size));
        foreach (var c in ordered) Categories.Add(new CategoryVM(c, this));

        Raise(nameof(SafeTotal));
        Raise(nameof(ReviewTotal));
        Raise(nameof(ReviewSummary));
        Raise(nameof(ShowAdminBanner));
        Raise(nameof(NothingFound));
        Recalc();
    }

    public long SafeTotal => Categories.Where(c => c.IsSafe && c.CanSelect).Sum(c => c.Size);
    public long ReviewTotal => Categories.Where(c => !c.IsSafe && !c.IsBlocked).Sum(c => c.Size);
    public bool NothingFound => Categories.Count == 0;
    public string ReviewSummary => ReviewTotal > 0
        ? Loc.Format("PlusMore", Format.Bytes(ReviewTotal))
        : S.EverythingFound;
    public bool ShowAdminBanner => !IsAdmin && Categories.Any(c => c.IsBlocked);

    public long SelectedTotal => Categories.Sum(c => c.SelectedSize);
    public string CleanButtonText => SelectedTotal > 0 ? Loc.Format("CleanUpAmount", Format.Bytes(SelectedTotal)) : S.PickSomething;

    internal void Recalc()
    {
        Raise(nameof(SelectedTotal));
        Raise(nameof(CleanButtonText));
        CleanCommand.RaiseCanExecuteChanged();
    }

    bool _spaceTab;
    public bool IsCleanupTab
    {
        get => !_spaceTab;
        set
        {
            if (!value || !_spaceTab) return;
            _spaceTab = false;
            Raise(nameof(IsCleanupTab));
            Raise(nameof(IsSpaceTab));
        }
    }
    public bool IsSpaceTab
    {
        get => _spaceTab;
        set
        {
            if (!value || _spaceTab) return;
            _spaceTab = true;
            Raise(nameof(IsCleanupTab));
            Raise(nameof(IsSpaceTab));
            SpaceMap.EnsureScanned();
        }
    }

    // ---------- cleaning ----------

    double _cleanProgress;
    public double CleanProgress { get => _cleanProgress; private set => Set(ref _cleanProgress, value); }

    string _cleanStep = "";
    public string CleanStep { get => _cleanStep; private set => Set(ref _cleanStep, value); }

    long _freed;
    public long Freed { get => _freed; private set => Set(ref _freed, value); }

    long _skipped;
    public long Skipped { get => _skipped; private set => Set(ref _skipped, value); }
    public bool HasSkipped => Skipped > 100L * 1024 * 1024;
    public string SkippedText => Loc.Format("InUseSkippedMessage", Format.Bytes(Skipped));

    long _recycled;
    public long Recycled { get => _recycled; private set => Set(ref _recycled, value); }
    public bool HasRecycled => Recycled > 0;
    public string RecycledText => Loc.Format("RecycledMessage", Format.Bytes(Recycled));

    double _before, _after;
    public double BeforeFraction { get => _before; private set => Set(ref _before, value); }
    public double AfterFraction { get => _after; private set => Set(ref _after, value); }

    string _beforeText = "", _afterText = "", _doneSubtitle = "";
    public string BeforeText { get => _beforeText; private set => Set(ref _beforeText, value); }
    public string AfterText { get => _afterText; private set => Set(ref _afterText, value); }
    public string DoneSubtitle { get => _doneSubtitle; private set => Set(ref _doneSubtitle, value); }

    Brush? _beforeBrush;
    public Brush? BeforeBrush { get => _beforeBrush; private set => Set(ref _beforeBrush, value); }

    async Task CleanAsync()
    {
        if (_busy) return;
        var jobs = Categories.Where(c => c.CanSelect)
            .Select(c => new CleanJob(c.Model, c.SelectedItems()))
            .Where(j => j.Items.Count > 0)
            .ToList();
        if (jobs.Count == 0) return;

        // Read fresh so the choice also applies after "Restart as admin".
        var recycle = !IsDemo && new PreferencesStore(PreferencesStore.DefaultPath).Load().RecycleDownloads;
        var review = jobs.Where(j => j.Category.Safety == Safety.Review).SelectMany(j => j.Items).ToList();
        if (review.Count > 0)
        {
            var picked = Loc.Format("PickedFromDownloads", review.Count, Loc.Files(review.Count), Format.Bytes(review.Sum(i => i.Size)));
            var ok = recycle
                ? await ConfirmAsync(S.MoveToBinTitle, picked + S.MoveToBinWarning, S.MoveToBin)
                : await ConfirmAsync(S.DeleteTheseForGood, picked + S.DeleteWarning, S.DeletePermanently);
            if (!ok) return;
        }

        _busy = true;
        try
        {
            var drive = SystemDrive;
            BeforeFraction = drive?.UsedFraction ?? 0;
            BeforeBrush = drive?.BarBrush;
            BeforeText = drive is null ? "" : Loc.Format("FreeBytes", Format.Bytes(drive.Free));
            CleanProgress = 0;
            CleanStep = S.GettingStarted;
            State = AppState.Cleaning;

            CleanResult r;
            if (IsDemo)
            {
                r = await DemoClean(jobs);
                _demoFreed += r.Freed;
            }
            else
            {
                var progress = new Progress<(double Fraction, string Step)>(p =>
                {
                    CleanProgress = p.Fraction;
                    CleanStep = p.Step;
                });
                r = await Task.Run(() => Cleaner.Run(jobs, progress, recycle));
                await Task.Delay(500);
            }

            Freed = r.Freed;
            Skipped = r.Skipped;
            Recycled = r.Recycled;
            RefreshDrives();
            AfterFraction = drive?.UsedFraction ?? 0;
            AfterText = drive is null ? "" : Loc.Format("FreeBytes", Format.Bytes(drive.Free));
            DoneSubtitle = drive is null ? "" : Loc.Format("DriveNowFree", drive.Letter, Format.Bytes(drive.Free));
            Raise(nameof(HasSkipped));
            Raise(nameof(SkippedText));
            Raise(nameof(HasRecycled));
            Raise(nameof(RecycledText));
            State = AppState.Done;
        }
        catch (Exception ex)
        {
            ShowToast(S.CleaningStopped + ex.Message);
            State = AppState.Results;
        }
        finally
        {
            _busy = false;
        }
    }

    async Task<CleanResult> DemoClean(List<CleanJob> jobs)
    {
        long total = jobs.Sum(j => j.Items.Sum(i => i.Size));
        long done = 0;
        foreach (var j in jobs)
        {
            CleanStep = Loc.Format("CleaningCategory", j.Category.Title.ToLowerInvariant());
            done += j.Items.Sum(i => i.Size);
            CleanProgress = (double)done / total;
            await Task.Delay(320);
        }
        await Task.Delay(300);
        return new CleanResult(total, 0, 48_211);
    }

    /// <summary>Asks first, then has Windows turn hibernation off (which removes hiberfil.sys).</summary>
    internal async Task TurnOffHibernationAsync(CategoryVM category)
    {
        if (_busy) return;
        var size = category.Size;
        var ok = await ConfirmAsync(S.HibernationConfirmTitle, Loc.Format("HibernationConfirmBody", Format.Bytes(size)), S.TurnOffHibernation);
        if (!ok) return;

        _busy = true;
        try
        {
            bool done = IsDemo || await Hibernation.SetAsync(false);
            if (!IsDemo) done = done && !Hibernation.IsOn;
            if (!done) { ShowToast(S.HibernationUnchanged); return; }

            Categories.Remove(category);
            RefreshDrives();
            Raise(nameof(ReviewTotal));
            Raise(nameof(ReviewSummary));
            Raise(nameof(NothingFound));
            Recalc();
            ShowToast(Loc.Format("HibernationOff", Format.Bytes(size)));
        }
        finally { _busy = false; }
    }

    void RestartAsAdmin()
    {
        if (IsDemo) { ShowToast(S.AdminButtonHint); return; }
        if (Admin.RestartElevated("--scan")) Application.Current.Shutdown();
        else ShowToast(S.NoAdminWorries);
    }

    // ---------- confirm dialog ----------

    TaskCompletionSource<bool>? _confirm;

    bool _confirmVisible;
    public bool ConfirmVisible { get => _confirmVisible; private set => Set(ref _confirmVisible, value); }

    string _confirmTitle = "", _confirmBody = "", _confirmYes = "";
    public string ConfirmTitle { get => _confirmTitle; private set => Set(ref _confirmTitle, value); }
    public string ConfirmBody { get => _confirmBody; private set => Set(ref _confirmBody, value); }
    public string ConfirmYesText { get => _confirmYes; private set => Set(ref _confirmYes, value); }

    Task<bool> ConfirmAsync(string title, string body, string yes)
    {
        ConfirmTitle = title;
        ConfirmBody = body;
        ConfirmYesText = yes;
        _confirm = new TaskCompletionSource<bool>();
        ConfirmVisible = true;
        return _confirm.Task;
    }

    void CloseConfirm(bool result)
    {
        ConfirmVisible = false;
        _confirm?.TrySetResult(result);
        _confirm = null;
    }

    // ---------- toast ----------

    string _toast = "";
    public string ToastText { get => _toast; private set => Set(ref _toast, value); }

    bool _toastVisible;
    public bool ToastVisible { get => _toastVisible; private set => Set(ref _toastVisible, value); }

    int _toastId;
    public async void ShowToast(string text)
    {
        ToastText = text;
        var id = ++_toastId;
        ToastVisible = true;
        await Task.Delay(3400);
        if (id == _toastId) ToastVisible = false;
    }

    // ---------- used by the screenshot runner ----------

    internal void ExpandOnly(string id)
    {
        foreach (var c in Categories) c.IsExpanded = c.Model.Id == id;
    }
}
