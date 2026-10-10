#nullable enable

using System.Threading.Tasks;

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Multi-option tweak that opens a dialog for one of its choices (e.g. Custom Value).
    /// </summary>
    public interface IPromptingMultiOptionConfigurationService :
        IMultiOptionConfigurationServices,
        IWarnedMultiOptionConfigurationService
    {
        bool IsCustomPromptOption(int statusIndex);

        Task<int?> PromptCustomValueAsync();

        void ApplyRawValue(int value);
    }
}

