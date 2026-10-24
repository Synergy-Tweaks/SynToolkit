#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SynToolkit.Services;

namespace SynToolkit.ViewModels
{
    public partial class RestorationPanelViewModel : ObservableObject
    {
        public const int MaximumDescriptionLength = 255;

        private readonly IRestorePointService _restorePointService;
        private CancellationTokenSource _operations = new();

        public RestorationPanelViewModel(IRestorePointService restorePointService)
        {
            _restorePointService = restorePointService;
            Description = DefaultDescription();
        }

        public ObservableCollection<RestorePointRow> RestorePoints { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        public partial string Description { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
        [NotifyPropertyChangedFor(nameof(CanEditDescription))]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowEmpty))]
        public partial bool IsLoading { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        [NotifyPropertyChangedFor(nameof(BlockedReason))]
        [NotifyPropertyChangedFor(nameof(ShowBlockedReason))]
        public partial bool IsAdministrator { get; set; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
        [NotifyPropertyChangedFor(nameof(BlockedReason))]
        [NotifyPropertyChangedFor(nameof(ShowBlockedReason))]
        public partial bool IsProtectionEnabled { get; set; } = true;

        [ObservableProperty]
        public partial bool IsStatusOpen { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

        [ObservableProperty]
        public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

        [ObservableProperty]
        public partial string SummaryText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string EmptyText { get; set; } = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowEmpty))]
        public partial bool HasRestorePoints { get; set; }

        public bool ShowEmpty => !IsLoading && !HasRestorePoints;

        public bool CanEditDescription => !IsBusy;

        public bool ShowBlockedReason => !string.IsNullOrWhiteSpace(BlockedReason);

        public string BlockedReason
        {
            get
            {
                if (!IsAdministrator)
                {
                    return Text("Restoration_AdminRequired");
                }

                if (!IsProtectionEnabled)
                {
                    return Text("Restoration_ProtectionOff");
                }

                return string.Empty;
            }
        }

        public void CancelPending()
        {
            _operations.Cancel();
        }

        public async Task InitializeAsync()
        {
            IsAdministrator = _restorePointService.IsAdministrator;
            if (string.IsNullOrWhiteSpace(Description))
            {
                Description = DefaultDescription();
            }

            await LoadAsync(_operations.Token);
        }

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private Task RefreshAsync() => LoadAsync(_operations.Token);

        [RelayCommand(CanExecute = nameof(CanCreate))]
        private async Task CreateAsync()
        {
            string description = Description.Trim();
            if (description.Length == 0 || description.Length > MaximumDescriptionLength)
            {
                ShowStatus(Text("Restoration_InvalidDescription"), InfoBarSeverity.Warning);
                return;
            }

            IsBusy = true;
            try
            {
                RestorePointCreateResult result = await _restorePointService.CreateAsync(description, _operations.Token);
                if (result.Status == RestorePointOperationStatus.Success)
                {
                    ShowStatus(Text("Restoration_Success"), InfoBarSeverity.Success);
                    Description = DefaultDescription();
                }
                else
                {
                    ShowStatus(MessageFor(result.Status), SeverityFor(result.Status));
                }

                if (result.Status is RestorePointOperationStatus.Success or RestorePointOperationStatus.Throttled)
                {
                    await LoadAsync(_operations.Token);
                }
            }
            catch (OperationCanceledException)
            {
                ShowStatus(Text("Restoration_Cancelled"), InfoBarSeverity.Informational);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanCreate() =>
            IsAdministrator &&
            IsProtectionEnabled &&
            !IsBusy &&
            !string.IsNullOrWhiteSpace(Description) &&
            Description.Trim().Length <= MaximumDescriptionLength;

        private bool CanRefresh() => !IsBusy;

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            IsLoading = true;
            try
            {
                RestorePointListResult result = await _restorePointService.ListAsync(cancellationToken);
                IsAdministrator = result.IsAdministrator;
                IsProtectionEnabled = result.IsProtectionEnabled;
                RestorePoints.Clear();
                foreach (RestorePointEntry point in result.Points)
                {
                    RestorePoints.Add(new RestorePointRow(point, TypeName(point.RestorePointType)));
                }

                HasRestorePoints = RestorePoints.Count > 0;
                SummaryText = HasRestorePoints
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        Text("Restoration_Summary"),
                        RestorePoints.Count,
                        RestorePoints[0].CreatedText)
                    : string.Empty;
                EmptyText = result.Status == RestorePointOperationStatus.Success
                    ? (IsProtectionEnabled ? Text("Restoration_Empty") : Text("Restoration_ProtectionOff"))
                    : MessageFor(result.Status);
                OnPropertyChanged(nameof(BlockedReason));

                if (result.Status != RestorePointOperationStatus.Success && string.IsNullOrEmpty(StatusMessage))
                {
                    ShowStatus(EmptyText, SeverityFor(result.Status));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ShowStatus(string message, InfoBarSeverity severity)
        {
            StatusMessage = message;
            StatusSeverity = severity;
            IsStatusOpen = !string.IsNullOrWhiteSpace(message);
        }

        private static string MessageFor(RestorePointOperationStatus status) => status switch
        {
            RestorePointOperationStatus.Throttled => Text("Restoration_Throttled"),
            RestorePointOperationStatus.ProtectionDisabled => Text("Restoration_ProtectionOff"),
            RestorePointOperationStatus.AccessDenied => Text("Restoration_AccessDenied"),
            RestorePointOperationStatus.NotElevated => Text("Restoration_AdminRequired"),
            RestorePointOperationStatus.ServiceUnavailable => Text("Restoration_ServiceUnavailable"),
            RestorePointOperationStatus.TimedOut => Text("Restoration_Timeout"),
            RestorePointOperationStatus.Cancelled => Text("Restoration_Cancelled"),
            _ => Text("Restoration_Failed")
        };

        private static InfoBarSeverity SeverityFor(RestorePointOperationStatus status) => status switch
        {
            RestorePointOperationStatus.Success => InfoBarSeverity.Success,
            RestorePointOperationStatus.Cancelled => InfoBarSeverity.Informational,
            RestorePointOperationStatus.Throttled or RestorePointOperationStatus.ProtectionDisabled or RestorePointOperationStatus.NotElevated => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Error
        };

        private static string TypeName(uint restorePointType) => restorePointType switch
        {
            0 => Text("Restoration_TypeApplicationInstall"),
            1 => Text("Restoration_TypeApplicationUninstall"),
            10 => Text("Restoration_TypeDeviceDriver"),
            12 => Text("Restoration_TypeModifySettings"),
            13 => Text("Restoration_TypeCancelled"),
            _ => Text("Restoration_TypeUnknown")
        };

        private static string DefaultDescription() =>
            string.Format(CultureInfo.CurrentCulture, Text("Restoration_DefaultDescription"), DateTime.Now.ToString("g", CultureInfo.CurrentCulture));

        private static string Text(string key) => App.GetValueFromItemList(key);
    }

    public sealed class RestorePointRow
    {
        public RestorePointRow(RestorePointEntry point, string typeName)
        {
            Description = string.IsNullOrWhiteSpace(point.Description) ? typeName : point.Description;
            CreatedText = point.CreatedLocal == DateTime.MinValue
                ? string.Empty
                : point.CreatedLocal.ToString("g", CultureInfo.CurrentCulture);
            SequenceText = point.SequenceNumber.ToString(CultureInfo.CurrentCulture);
            Details = string.Join(" · ", new[] { CreatedText, typeName, "#" + SequenceText }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        public string Description { get; }

        public string CreatedText { get; }

        public string SequenceText { get; }

        public string Details { get; }
    }
}
