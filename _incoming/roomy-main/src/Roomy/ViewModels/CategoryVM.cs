using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Roomy.Core;
using Roomy.Localization;

namespace Roomy.ViewModels;

public sealed class CategoryVM : ObservableObject
{
    const int MaxVisible = 40;
    readonly MainVM _owner;

    public CategoryVM(CleanupCategory model, MainVM owner)
    {
        Model = model;
        _owner = owner;
        var selected = model.SelectedByDefault && CanSelect;
        foreach (var it in model.Items)
        {
            var vm = new ItemVM(it, this);
            vm.SetSilently(selected);
            Items.Add(vm);
        }
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        OpenSettingsCommand = new RelayCommand(() => Shell.Open("ms-settings:storagesense"));
        TurnOffHibernationCommand = new RelayCommand(() => _ = _owner.TurnOffHibernationAsync(this));
    }

    public CleanupCategory Model { get; }
    public ObservableCollection<ItemVM> Items { get; } = [];
    public IEnumerable<ItemVM> VisibleItems => Items.Take(MaxVisible);
    public bool HasHidden => Items.Count > MaxVisible;
    public string HiddenText => Loc.Format("MoreItems", Items.Count - MaxVisible);

    public string Title => Model.Title;
    public string Description => Model.Description;
    public string Glyph => Model.Glyph;
    public string? Note => Model.Note;
    public bool HasNote => !string.IsNullOrEmpty(Model.Note);

    public bool IsSafe => Model.Safety == Safety.Safe;
    public bool IsBlocked => Model.Blocked;
    public bool IsSteam => Model.Action == CleanAction.SteamUninstall;
    public bool IsSettings => Model.Action == CleanAction.OpenSettings;
    public bool IsHibernation => Model.Action == CleanAction.TurnOffHibernation;
    public bool CanSelect => !Model.Blocked && Model.Size > 0 &&
                             Model.Action is CleanAction.Delete or CleanAction.EmptyRecycleBin;
    public bool ItemsSelectable => CanSelect && Items.Count > 1;
    public bool ShowItemList => Model.ShowItems && Items.Count > 0;
    public bool HasDetails => ShowItemList || IsSettings || IsHibernation || IsBlocked;

    public long Size => Model.Size;
    public string SizeText => Model.Blocked ? "N/A" : Format.Bytes(Size);
    public string SafetyText => Model.Blocked ? S.NeedsAdmin : IsSafe ? S.BadgeSafe : S.BadgeYourCall;
    public Brush BadgeBg => Res.Brush(Model.Blocked ? "MutedSoftBrush" : IsSafe ? "AccentSoftBrush" : "WarnSoftBrush");
    public Brush BadgeFg => Res.Brush(Model.Blocked ? "MutedBrush" : IsSafe ? "AccentBrush" : "WarnBrush");
    public Brush IconBrush => Res.Brush(Model.Blocked ? "MutedBrush" : IsSafe ? "AccentBrush" : "WarnBrush");

    bool _expanded;
    public bool IsExpanded
    {
        get => _expanded;
        set { if (Set(ref _expanded, value)) Raise(nameof(ChevronGlyph)); }
    }
    public string ChevronGlyph => IsExpanded ? G.ChevronUp : G.ChevronDown;

    public bool? IsSelected
    {
        get
        {
            if (!CanSelect) return false;
            int n = Items.Count(i => i.IsSelected);
            return n == 0 ? false : n == Items.Count ? true : null;
        }
        set
        {
            var v = value == true;
            foreach (var i in Items) i.SetSilently(v);
            Changed();
        }
    }

    public long SelectedSize => CanSelect ? Items.Where(i => i.IsSelected).Sum(i => i.Model.Size) : 0;

    public IReadOnlyList<CleanupItem> SelectedItems() => Items.Where(i => i.IsSelected).Select(i => i.Model).ToList();

    internal void Changed()
    {
        Raise(nameof(IsSelected));
        Raise(nameof(SelectedSize));
        _owner.Recalc();
    }

    public ICommand ToggleExpandCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand TurnOffHibernationCommand { get; }
    public ICommand RestartAdminCommand => _owner.RestartAdminCommand;
}

public sealed class ItemVM : ObservableObject
{
    readonly CategoryVM _parent;

    public ItemVM(CleanupItem model, CategoryVM parent)
    {
        Model = model;
        _parent = parent;
        RevealCommand = new RelayCommand(() => Shell.Reveal(Model.Path));
        UninstallCommand = new RelayCommand(() =>
        {
            if (!string.IsNullOrEmpty(Model.AppId) && Model.AppId != "0") Shell.Open($"steam://uninstall/{Model.AppId}");
        });
    }

    public CleanupItem Model { get; }
    public string Name => Model.Name;
    public string Detail => Model.Detail ?? "";
    public string SizeText => Format.Bytes(Model.Size);
    public bool Selectable => _parent.ItemsSelectable;
    public bool IsSteam => _parent.IsSteam;

    bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) _parent.Changed(); }
    }

    internal void SetSilently(bool v)
    {
        if (_selected == v) return;
        _selected = v;
        Raise(nameof(IsSelected));
    }

    public ICommand RevealCommand { get; }
    public ICommand UninstallCommand { get; }
}

public enum StepState { Pending, Active, Done }

public sealed class ScanStepVM(string title) : ObservableObject
{
    public string Title { get; } = title;

    StepState _state;
    public StepState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            Raise(nameof(Glyph));
            Raise(nameof(GlyphBrush));
            Raise(nameof(TitleBrush));
        }
    }

    string _size = "";
    public string SizeText { get => _size; private set => Set(ref _size, value); }

    public string Glyph => State switch { StepState.Done => G.Check, StepState.Active => G.Update, _ => G.ChevronRight };
    public Brush GlyphBrush => Res.Brush(State switch { StepState.Done => "AccentBrush", StepState.Active => "Accent2Brush", _ => "FaintBrush" });
    public Brush TitleBrush => Res.Brush(State == StepState.Pending ? "FaintBrush" : "TextBrush");

    public void Finish(CleanupCategory? c)
    {
        SizeText = c is null ? S.Nothing : c.Blocked ? S.NeedsAdminLower : c.Size > 0 ? Format.Bytes(c.Size) : S.Nothing;
        State = StepState.Done;
    }
}
