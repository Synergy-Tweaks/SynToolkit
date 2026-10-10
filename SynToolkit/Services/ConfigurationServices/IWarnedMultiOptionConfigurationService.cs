#nullable enable

namespace SynToolkit.Services.ConfigurationServices
{
    /// <summary>
    /// Multi-option tweak that can surface a non-error status warning (unsupported/error/caveat).
    /// </summary>
    public interface IWarnedMultiOptionConfigurationService
    {
        string GetStatusWarning();
    }
}
