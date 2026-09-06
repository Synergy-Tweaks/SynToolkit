using Microsoft.UI.Xaml;
using SynToolkit.HostBuilder;
using Microsoft.Extensions.Hosting;
using SynToolkit.Services.AudioMixer;
using SynToolkit.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System;
using System.IO.Pipes;
using System.IO;
using System.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;
using SynToolkit.Services;
using SynToolkit.Utils;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using Windows.ApplicationModel.Core;
using System.Diagnostics;
using System.Configuration;
using WinUIEx;

namespace SynToolkit
{
    public partial class App : Application
    {
        public static readonly Logger logger = LogManager.GetCurrentClassLogger();
        public static IHost _host { get; set; }

        public static Window m_window;
        public static Window s_window;
        public static Window f_window;
        public static XamlRoot XamlRoot { get; set; }
        public static string CurrentCategory { get; set; }
        public static string SearchHighlightItemKey { get; set; }
        internal const string AudioMixerNavigationTag = "SynToolkit.Views.AudioMixerPage";
        public static event Action<string> ConfigurationActionSucceeded;
        internal const string DefaultLanguageKey = "en_us";
        private static Dictionary<string, string> StringList = new Dictionary<string, string>();
        private static Dictionary<string, string> EnglishStringList = new Dictionary<string, string>();
        public static List<IConfigurationItem> RootList = new List<IConfigurationItem>();
        private const string InstancePipeName = "SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C";
        private const string InstanceMutexName = @"Global\SynToolkit-FE4CD776-C158-49D7-8B5F-F73D3D342E8C";
        private static readonly Mutex _mutex = new(false, InstanceMutexName);
        private DiscordPresenceService _discordPresenceService;
        private SystemTrayService _systemTrayService;
        private AudioMixerHotkeyService _audioMixerHotkeyService;
        private static volatile bool _activateRequested;
        private bool _hostStarted;
        private int _shutdownStarted;
        private int _resourcesDisposed;

        public static string Version { get; set; }
        public static bool IsReturningUser { get; private set; }
        public static string DisplayUserName { get; private set; } = "there";
        public static string WelcomeGreetingFormat { get; private set; } = "Welcome back, {0}";
        public App()
        {
            ConfigureNLog();
            logger.Info("[APP]: App Started");
            InitializeUserGreetingState();
            LoadLangString();
            SelectWelcomeGreeting();
            _host = CreateHostBuilder().Build();
            logger.Info("[HOST]: Building host");
            this.InitializeComponent();
            logger.Info("[HOST]: Finished initializing components");
            this.UnhandledException += OnAppUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private static void InitializeUserGreetingState()
        {
            const string userStateKey = @"HKCU\SOFTWARE\SynToolkit";
            const string launchedValueName = "HasLaunched";

            string accountName = Environment.UserName ?? string.Empty;
            DisplayUserName = UserAccountInfoService.SanitizeDisplayName(accountName);

            try
            {
                IsReturningUser = RegistryHelper.IsMatch(userStateKey, launchedValueName, 1);
                if (!IsReturningUser)
                {
                    RegistryHelper.SetValue(
                        userStateKey,
                        launchedValueName,
                        1,
                        Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch (Exception exception)
            {
                // A locked-down HKCU profile should never prevent the app from opening.
                IsReturningUser = false;
                logger.Warn(exception, "Unable to persist the local welcome-back state.");
            }
        }

        private static void SelectWelcomeGreeting()
        {
            string[] greetingKeys = IsReturningUser
                ?
                [
                    "Home_WelcomeBack_1",
                    "Home_WelcomeBack_2",
                    "Home_WelcomeBack_3",
                    "Home_WelcomeBack_4",
                    "Home_WelcomeBack_5",
                    "Home_WelcomeBack_6",
                    "Home_WelcomeBack_7",
                    "Home_WelcomeBack_8",
                ]
                :
                [
                    "Home_WelcomeFirst_1",
                    "Home_WelcomeFirst_2",
                    "Home_WelcomeFirst_3",
                    "Home_WelcomeFirst_4",
                ];

            string selectedKey = greetingKeys[Random.Shared.Next(greetingKeys.Length)];
            string greetingFormat = GetValueFromItemList(selectedKey);

            WelcomeGreetingFormat = greetingFormat.Contains("{0}", StringComparison.Ordinal)
                ? greetingFormat
                : IsReturningUser
                    ? "Welcome back, {0}"
                    : "Welcome, {0}";
        }

        /// <summary>
        /// Registers all configuration services
        /// </summary>
        /// <returns></returns>
        public static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .AddStores()
                .AddServices()
                .AddViewModels();

        /// <summary>
        /// Configures NLog for logging
        /// </summary>
        private void ConfigureNLog()
        {
            string logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SynToolkit",
                "Logs");
            Directory.CreateDirectory(logDirectory);
            string name = Path.Combine(logDirectory, $"syntoolkit-{DateTime.Now:yyyy_MM_dd_HH_mm_ss}.log");
            var config = new LoggingConfiguration();
            var logfile = new FileTarget("logfile")
            {
                FileName = name,
                Layout = "${longdate} ${level}: ${message} ${exception}"
            };
            config.AddTarget(logfile); config.AddRuleForAllLevels(logfile);
            LogManager.Configuration = config;
        }

        /// <summary>
        /// Catches unhandled exceptions and logs them
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            logger.Error(e.Exception, "Unhandled exception occurred");
        }
        
        /// <summary>
        /// App behavior on launch
        /// </summary>
        /// <param name="args"></param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
#if DEBUG
            if (Debugger.IsAttached)
            {
                DebugSettings.BindingFailed += DebugSettings_BindingFailed;
            }
#endif
            string[] arguments = Environment.GetCommandLineArgs();
            bool shutdownForUpdateRequested = arguments.Any(argument =>
                argument.Equals("--shutdown-for-update", StringComparison.OrdinalIgnoreCase));

            if (!TryAcquireInstanceMutex())
            {
                SendRequestToExistingInstance(shutdownForUpdateRequested
                    ? "-shutdown-for-update"
                    : "-toforeground");
                ShutdownApplication();
                return;
            }

            if (shutdownForUpdateRequested)
            {
                ShutdownApplication();
                return;
            }

            _ = Task.Run(StartNamedPipeServer);
            Version = RegistryHelper.GetValue(@"HKLM\SOFTWARE\SynToolkit", "Channel") + " v" + RegistryHelper.GetValue(@"HKLM\SOFTWARE\SynToolkit", "Version");
            if (!CompatibilityHelper.IsWindowsCompatible())
            {
                m_window = new IncompatibleVersionWindow(IncompatibleVersionReason.Windows);
                m_window.Closed += (_, _) => ShutdownApplication();
                m_window.Activate();
                return;
            }

            //if (!CompatibilityHelper.IsSynergyOsCompatible())
            //{
            //    logger.Warn("Blocked startup on unsupported installation: SynergyOS OEM markers not found.");
            //    m_window = new IncompatibleVersionWindow(IncompatibleVersionReason.SynergyOs);
            //    m_window.Closed += (_, _) => ShutdownApplication();
            //    m_window.Activate();
            //    return;
            //}

            StartHost();
            StartDiscordPresence();

            bool wasRanWithArgs = false;

            if (!wasRanWithArgs)
            {
                logger.Info("Loading without args");
                s_window = new LoadingWindow();
                s_window.Activate();

                InitializeVMAsync();
            }
        }

        private static bool TryAcquireInstanceMutex()
        {
            try
            {
                return _mutex.WaitOne(TimeSpan.Zero, true);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }

        private void StartHost()
        {
            if (_hostStarted)
            {
                return;
            }

            _host.Start();
            _hostStarted = true;
            logger.Info("[HOST]: Starting host");
        }

        private void StartDiscordPresence()
        {
            // Check if Discord RPC is disabled in settings
            try
            {
                if (RegistryHelper.IsMatch(@"HKLM\SOFTWARE\SynToolkit", "DiscordRpcDisabled", 1))
                {
                    logger.Info("Discord Rich Presence is disabled by user preference.");
                    return;
                }
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to read Discord RPC preference; proceeding with RPC enabled.");
            }

            string applicationId = null;
            string largeImageKey = null;
            try
            {
                applicationId = ConfigurationManager.AppSettings["DiscordApplicationId"];
                largeImageKey = ConfigurationManager.AppSettings["DiscordLargeImageKey"];
            }
            catch (ConfigurationErrorsException exception)
            {
                logger.Warn(exception, "Discord Rich Presence configuration could not be read.");
            }

            _discordPresenceService = new DiscordPresenceService();
            _discordPresenceService.TryStart(applicationId, largeImageKey);
        }

        internal static void UpdateDiscordPresence(string state)
        {
            if (Current is App app)
            {
                app._discordPresenceService?.UpdateState(state);
            }
        }

        private void OnProcessExit(object sender, EventArgs eventArgs)
        {
            DisposeResources();
        }

        public static bool IsShuttingDown =>
            Current is App app && Volatile.Read(ref app._shutdownStarted) != 0;

        public static bool SetCloseToTrayEnabled(bool enabled)
        {
            if (Current is not App app)
            {
                return false;
            }

            if (app._systemTrayService is null)
            {
                return !enabled;
            }

            try
            {
                app._systemTrayService.SetEnabled(enabled);
                return app._systemTrayService.IsEnabled == enabled;
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to change the SynToolkit system-tray icon state.");
                return false;
            }
        }

        public static bool TryHideMainWindowToTray()
        {
            if (IsShuttingDown || Current is not App app || m_window is null)
            {
                return false;
            }

            bool closeToTrayEnabled;
            try
            {
                closeToTrayEnabled = RegistryHelper.IsMatch(
                    @"HKLM\SOFTWARE\SynToolkit",
                    "KeepInBackground",
                    1);
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to read the close-to-system-tray preference.");
                return false;
            }

            if (!closeToTrayEnabled || app._systemTrayService is null)
            {
                return false;
            }

            try
            {
                app._systemTrayService.SetEnabled(true);
                m_window.Hide();
                return true;
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to hide SynToolkit in the system tray; closing normally instead.");
                return false;
            }
        }

        public static void ShutdownApplication()
        {
            if (Current is App app)
            {
                app.ShutdownCore();
                return;
            }

            Environment.Exit(0);
        }

        private void ShutdownCore()
        {
            if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            {
                return;
            }

            try
            {
                DisposeResources();
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "SynToolkit encountered an error while releasing shutdown resources.");
            }
            finally
            {
                Environment.Exit(0);
            }
        }

        private void DisposeResources()
        {
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) != 0)
            {
                return;
            }

            try
            {
                _systemTrayService?.Dispose();
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to dispose the system-tray icon.");
            }

            try
            {
                _discordPresenceService?.Dispose();
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to stop Discord Rich Presence.");
            }

            try
            {
                if (_audioMixerHotkeyService is not null)
                {
                    _audioMixerHotkeyService.HotkeyPressed -= ShowAudioMixer;
                    _audioMixerHotkeyService.Dispose();
                }
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to dispose the audio mixer hotkey service.");
            }

            if (_hostStarted)
            {
                try
                {
                    _host?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    logger.Warn(exception, "The SynToolkit host did not stop cleanly before its timeout.");
                }
            }

            try
            {
                _host?.Dispose();
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Unable to dispose the SynToolkit host.");
            }

            LogManager.Shutdown();
        }
        public static void RestartApp(string arguments = "")
        {
            AppRestartFailureReason restartError = Microsoft.Windows.AppLifecycle.AppInstance.Restart(arguments);

            switch (restartError)
            {
                case AppRestartFailureReason.RestartPending:
                    // Handle case where another restart is already pending
                    break;
                case AppRestartFailureReason.InvalidUser:
                    // Handle case where the current user is not valid
                    break;
                case AppRestartFailureReason.Other:
                    // Handle other failure reasons
                    break;
            }
        }

        /// <summary>
        /// Logs XAML errors
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void DebugSettings_BindingFailed(object sender, BindingFailedEventArgs e)
        {
            App.logger.Warn(e.Message);
        }

        /// <summary>
        /// Sends an activation or shutdown request to an existing SynToolkit instance.
        /// </summary>
        private void SendRequestToExistingInstance(string request)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", InstancePipeName, PipeDirection.Out))
                {
                    client.Connect(3000);
                    using (var writer = new StreamWriter(client))
                    {
                        writer.WriteLine(request);
                        writer.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Unable to send the existing SynToolkit instance request '{request}'.");
            }
        }

        /// <summary>
        /// Start the pipe server and waits for a connection
        /// </summary>
        private void StartNamedPipeServer()
        {
            while (true)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(InstancePipeName, PipeDirection.In))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server))
                        {
                            string request = reader.ReadLine();
                            if (request == "-shutdown-for-update")
                            {
                                logger.Info("Shutdown requested by SynToolkit Setup/Uninstall.");
                                Window window = m_window;
                                if (window is not null && window.DispatcherQueue.TryEnqueue(ShutdownApplication))
                                {
                                    return;
                                }

                                ShutdownApplication();
                                return;
                            }

                            if (request == "-toforeground")
                            {
                                Window window = m_window;
                                if (window == null)
                                {
                                    _activateRequested = true;
                                    continue;
                                }

                                window.DispatcherQueue.TryEnqueue(() =>
                                {
                                    // The primary window may be hidden because close-to-tray is enabled.
                                    // Activating a hidden WinUI window is not sufficient to make it visible.
                                    window.Show();
                                    window.Activate();
                                });
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    logger.Warn(exception, "The single-instance pipe server encountered an error.");
                    Thread.Sleep(250);
                }
            }
        }
        
        //private void InitializeVMSilent()
        //{
        //    _host.Services.GetRequiredService<ConfigPageViewModel>();
        //}

        /// <summary>
        /// Starts the program and get all the required services for a faster load time
        /// </summary>
        private async void InitializeVMAsync()
        {
            try
            {
                logger.Info("Loading configuration services");
                await Task.Run(() => _host.Services.GetRequiredService<ConfigPageViewModel>());
                logger.Info("Configuration services loaded");

                m_window = new MainWindow();
                try
                {
                    _systemTrayService = new SystemTrayService(m_window, ShutdownApplication, ShowAudioMixer);
                    bool closeToTrayEnabled = RegistryHelper.IsMatch(
                        @"HKLM\SOFTWARE\SynToolkit",
                        "KeepInBackground",
                        1);
                    _systemTrayService.SetEnabled(closeToTrayEnabled);
                }
                catch (Exception exception)
                {
                    logger.Warn(exception, "The SynToolkit system-tray icon could not be initialized.");
                    _systemTrayService?.Dispose();
                    _systemTrayService = null;
                }

                m_window.Activate();
                InitializeAudioMixerFeature();

                if (_activateRequested)
                {
                    _activateRequested = false;
                    m_window.Activate();
                }

                s_window?.Close();
                s_window = null;

                if (m_window is MainWindow mainWindow)
                {
                    mainWindow.DispatcherQueue.TryEnqueue(mainWindow.ApplyWindowPlacement);
                }
            }
            catch (Exception exception)
            {
                logger.Fatal(exception, "SynToolkit could not initialize its main window.");
                try
                {
                    s_window?.Close();
                }
                catch (Exception closeException)
                {
                    logger.Warn(closeException, "The loading window could not be closed after initialization failed.");
                }

                ShutdownApplication();
            }
        }

        /// <summary>
        /// Calls a content dialog
        /// </summary>
        /// <param name="type">type of content dialog</param>
        public static void ContentDialogCaller(string type) 
        {
            var mainWindow = m_window as MainWindow;
            mainWindow.ContentDialogContoller(type);
        }

        public static void ShowAudioMixer()
        {
            if (m_window is not MainWindow mainWindow)
            {
                return;
            }

            mainWindow.Show();
            mainWindow.Activate();
            mainWindow.NavigateToPage(typeof(Views.AudioMixerPage), AudioMixerNavigationTag);
        }

        /// <summary>
        /// Notifies the active configuration page that a one-off action completed.
        /// </summary>
        /// <param name="message">The localized confirmation message to display.</param>
        public static void ReportConfigurationActionSuccess(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            ConfigurationActionSucceeded?.Invoke(message);
        }

        public static void LoadLangString()
        {
            string langDir = Path.Combine(AppContext.BaseDirectory, "lang");
            string englishPath = Path.Combine(langDir, $"{DefaultLanguageKey}.json");

            try
            {
                EnglishStringList = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    File.ReadAllText(englishPath)) ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to load the default English language file: {englishPath}");
                EnglishStringList = new Dictionary<string, string>();
            }

            // English is always the complete baseline. A translation only
            // overrides keys it actually contains, so untranslated or missing
            // strings remain English instead of breaking a page.
            StringList = new Dictionary<string, string>(EnglishStringList);

            object langValue = RegistryHelper.GetValue(@"HKLM\SOFTWARE\SynToolkit", "lang");
            string lang = (langValue as string)?.Trim().ToLowerInvariant() ?? DefaultLanguageKey;
            if (string.IsNullOrWhiteSpace(lang) ||
                lang.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !string.Equals(Path.GetFileName(lang), lang, StringComparison.Ordinal))
            {
                lang = DefaultLanguageKey;
            }

            if (string.Equals(lang, DefaultLanguageKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string langFilePath = Path.Combine(langDir, $"{lang}.json");
            try
            {
                Dictionary<string, string> localizedStrings =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(langFilePath))
                    ?? new Dictionary<string, string>();
                foreach (KeyValuePair<string, string> localizedString in localizedStrings)
                {
                    if (!string.IsNullOrWhiteSpace(localizedString.Value))
                    {
                        StringList[localizedString.Key] = localizedString.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, $"Failed to load language '{lang}'; using English throughout.");
            }
        }

        public static void UpdateDisplayUserName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return;
            }

            DisplayUserName = displayName;
        }

        public static string GetValueFromItemList(string key, bool desc = false)
        {
            const string friendlyFallback = "Description unavailable.";
            try
            {
                string lookupKey = desc ? key + "Description" : key;
                if (StringList.TryGetValue(lookupKey, out string toReturn) &&
                    !string.IsNullOrWhiteSpace(toReturn))
                {
                    return toReturn;
                }

                if (EnglishStringList.TryGetValue(lookupKey, out string englishFallback) &&
                    !string.IsNullOrWhiteSpace(englishFallback))
                {
                    return englishFallback;
                }

                return friendlyFallback;
            }
            catch
            {
                return friendlyFallback;
            }
        }

        private void InitializeAudioMixerFeature()
        {
            try
            {
                _host.Services.GetRequiredService<IAudioMixerService>().Start();
                _audioMixerHotkeyService = _host.Services.GetRequiredService<AudioMixerHotkeyService>();
                _audioMixerHotkeyService.Initialize(m_window);
                _audioMixerHotkeyService.HotkeyPressed += ShowAudioMixer;
            }
            catch (Exception exception)
            {
                logger.Warn(exception, "Audio Mixer startup integration could not be initialized.");
            }
        }
    }
}
