#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Win32;

namespace SynToolkit.Services
{
    /// <summary>
    /// Searches the Microsoft Store catalog and downloads/installs or uninstalls packages
    /// without depending on Store-related Windows services. Ported from AME.AppFetch's
    /// StoreService (https://github.com/Ameliorated-LLC/appfetch, MIT License,
    /// Copyright (c) Ameliorated LLC), adapted to plain .NET/WinUI3 (the original's only
    /// non-portable dependency was Avalonia's Bitmap type, used solely to pre-download
    /// product icons; SynToolkit binds Image.Source directly to IconUrl instead).
    /// </summary>
    public partial class AppFetchService
    {
        public async Task UninstallApp(string fullName)
        {
            Process process = Process.Start(new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = $"-NoP -C \"Remove-AppxPackage -Package '{fullName}'\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            })!;
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    App.logger.Debug("[AppFetch] uninstall: {Line}", args.Data);
                }
            };
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    App.logger.Debug("[AppFetch] uninstall: {Line}", args.Data);
                }
            };
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception("PowerShell exited with code " + process.ExitCode);
            }
        }

        private static bool IsUWP(string productId) => !productId.StartsWith("xp", StringComparison.OrdinalIgnoreCase);
        private static bool IsSupported(string productId) => !productId.StartsWith("xm", StringComparison.OrdinalIgnoreCase);

        public async Task<List<StorePackageDto>> GetPackages(string productId, bool getDownloadUrl)
        {
            if (!IsUWP(productId))
            {
                return await SearchInstallerProductsAsync(productId);
            }

            string cookie = await GetCookieAsync();
            string categoryId = await GetCategoryIDAsync(productId);
            string xmlList = await FetchFileListXMLAsync(categoryId, cookie, "Retail");

            List<StorePackageDto> packages = await ParsePackagesAsync(xmlList, "Retail", getDownloadUrl);
            packages.Sort((x, y) => DateTime.Compare(y.LastModified.GetValueOrDefault(), x.LastModified.GetValueOrDefault()));
            packages.RemoveAll(x => x != packages.FirstOrDefault(y => _namePatternRegex.Match(x.Name!).Value == _namePatternRegex.Match(y.Name!).Value));
            return packages;
        }

        public async Task DownloadAndInstallPackagesAsync(List<StorePackageDto> packages, IProgress<double> progress, CancellationToken cancellationToken = default)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            using AppFetchHttpProgressClient client = new();

            double currentProgress = 0;
            double totalSize = packages.Sum(x => x.Size.GetValueOrDefault());
            double finishedBytes = 0;

            client.ProgressChanged += (size, downloaded, percentage) =>
            {
                double progressValue = totalSize > 0
                    ? Math.Min((Math.Round((downloaded + finishedBytes) / totalSize, 3) * 100) / 2, 50)
                    : Math.Min(percentage.GetValueOrDefault() / 2, 50);
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (progressValue != currentProgress)
                {
                    currentProgress = progressValue;
                    progress.Report(currentProgress);
                }
            };

            try
            {
                Directory.CreateDirectory(Path.Combine(tempFolder, "Dependencies"));

                for (int i = 0; i < packages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string packageFileName = GetSafePackageFileName(packages[i]);
                    string path = IsDependency(packages[i].Name!)
                        ? Path.Combine(tempFolder, "Dependencies", packageFileName)
                        : Path.Combine(tempFolder, packageFileName);
                    await client.StartDownload(packages[i].ResourceUri!, path, cancellationToken: cancellationToken);
                    await ValidatePackageChecksumAsync(packages[i], path, cancellationToken);
                    finishedBytes += packages[i].Size.GetValueOrDefault();
                }

                bool foundMatch = false;
                int c = 0;
                IEnumerable<StorePackageDto> installOrder = packages
                    .Where(x => IsDependency(x.Name!) && !x.Name!.Contains("Microsoft.Advertising"))
                    .Concat(packages.Where(x => !IsDependency(x.Name!) && !x.Name!.Contains("Microsoft.Advertising")));
                int installableCount = packages.Count(x => !x.Name!.Contains("Microsoft.Advertising"));

                foreach (StorePackageDto package in installOrder)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    c++;

                    (int ExitCode, bool HigherVersionInstalled, bool WrongArch, bool EdgeRequired, bool AdvertisingRequired) result =
                        await InstallPackage(package, tempFolder, cancellationToken);

                    double progressValue = ((c / (double)installableCount * 100) / 2) + 50;
                    progress.Report(progressValue);

                    if (result.ExitCode != 0 && result.AdvertisingRequired)
                    {
                        foreach (StorePackageDto advertisingPackage in packages.Where(x => x.Name!.Contains("Microsoft.Advertising")))
                        {
                            await InstallPackage(advertisingPackage, tempFolder, cancellationToken);
                        }

                        result = await InstallPackage(package, tempFolder, cancellationToken);
                    }

                    bool installerSucceeded = result.ExitCode is 0 or 1641 or 3010;
                    if (!installerSucceeded && !result.HigherVersionInstalled && !IsDependency(package.Name!) && !result.WrongArch)
                    {
                        throw new Exception(result.EdgeRequired ? "Microsoft Edge is required" : "PowerShell exited with code " + result.ExitCode);
                    }
                    else if (installerSucceeded && !IsDependency(package.Name!) && !result.HigherVersionInstalled && !result.WrongArch)
                    {
                        foundMatch = true;
                    }
                }

                if (!foundMatch)
                {
                    throw new Exception("Found no matching package for architecture");
                }
            }
            finally
            {
                try
                {
                    Directory.Delete(tempFolder, true);
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[AppFetch] Unable to remove temporary download folder {Folder}.", tempFolder);
                }
            }
        }

        /// <summary>
        /// The package name and extension come from a remote catalog, so they must never steer
        /// the local path or the PowerShell/MSI command line: a "..\..\" name would write outside
        /// the temp folder and a quote would break out of the quoted argument.
        /// </summary>
        private static string GetSafePackageFileName(StorePackageDto package)
        {
            string name = package.Name ?? string.Empty;
            string extension = (package.FileExtension ?? string.Empty).TrimStart('.');
            if (string.IsNullOrWhiteSpace(name) ||
                name is "." or ".." ||
                !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) ||
                name.IndexOf('\'') >= 0 ||
                extension.Length == 0 ||
                !extension.All(char.IsLetterOrDigit))
            {
                throw new InvalidDataException(
                    $"The store returned an unsafe package name for {package.PackageId}: '{name}.{extension}'.");
            }

            return name + "." + extension;
        }

        static AppFetchService()
        {
            // Self-check: a normal store name passes, separators and quotes are refused.
            System.Diagnostics.Debug.Assert(
                GetSafePackageFileName(new StorePackageDto { Name = "Microsoft.Store", FileExtension = "msix" }) == "Microsoft.Store.msix");
            System.Diagnostics.Debug.Assert(RejectsUnsafeName(@"..\System32\payload"));
            System.Diagnostics.Debug.Assert(RejectsUnsafeName("a'b"));

            static bool RejectsUnsafeName(string name)
            {
                try
                {
                    _ = GetSafePackageFileName(new StorePackageDto { Name = name, FileExtension = "exe" });
                    return false;
                }
                catch (InvalidDataException)
                {
                    return true;
                }
            }
        }

        private static async Task ValidatePackageChecksumAsync(
            StorePackageDto package,
            string filePath,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(package.Checksum) ||
                package.Checksum.Length != 64 ||
                !package.Checksum.All(Uri.IsHexDigit))
            {
                // Running an installer that no hash covers would defeat the point of the check.
                throw new InvalidDataException(
                    $"The store returned no usable SHA-256 checksum for {package.Name}; refusing to install an unverified file.");
            }

            await using FileStream stream = File.OpenRead(filePath);
            byte[] actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
            if (!Convert.ToHexString(actualHash).Equals(package.Checksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The downloaded installer for {package.Name} failed its SHA-256 integrity check.");
            }
        }

        private async Task<(int ExitCode, bool HigherVersionInstalled, bool WrongArch, bool EdgeRequired, bool AdvertisingRequired)> InstallPackage(
            StorePackageDto package,
            string downloadFolder,
            CancellationToken cancellationToken)
        {
            string packageFileName = GetSafePackageFileName(package);
            string file = IsDependency(package.Name!)
                ? Path.Combine(downloadFolder, "Dependencies", packageFileName)
                : Path.Combine(downloadFolder, packageFileName);
            ProcessStartInfo startInfo = new()
            {
                FileName = IsUWP(package.PackageId!) ? "powershell.exe" : file.EndsWith(".msi") ? "msiexec.exe" : file,
                Arguments = IsUWP(package.PackageId!) ? $"-NoP -C \"Add-AppxPackage -Path '{file}' -ForceApplicationShutdown\"" :
                    file.EndsWith(".msi") ? $"/i \"{file}\" /qn" : package.CommandLines,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Process process = new() { StartInfo = startInfo };

            try
            {
                process.Start();
            }
            catch (Win32Exception)
            {
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.RedirectStandardError = false;
                process.StartInfo.RedirectStandardOutput = false;
                process.Start();
            }

            bool higherVersionInstalled = false;
            bool edgeRequired = false;
            bool advertisingRequired = false;
            bool wrongArch = false;
            DataReceivedEventHandler handler = (_, args) =>
            {
                if (args.Data == null)
                {
                    return;
                }

                if (args.Data.Contains("0x80073D06"))
                {
                    higherVersionInstalled = true;
                }
                if (args.Data.Contains("0x80073CF1"))
                {
                    wrongArch = true;
                }
                if (args.Data.Contains("Microsoft.MicrosoftEdge.Stable"))
                {
                    edgeRequired = true;
                }
                if (args.Data.Contains("Microsoft.Advertising.Xaml"))
                {
                    advertisingRequired = true;
                }
                App.logger.Debug("[AppFetch] install: {Line}", args.Data);
            };
            process.ErrorDataReceived += handler;
            process.OutputDataReceived += handler;
            if (!process.StartInfo.UseShellExecute)
            {
                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync();
                    }
                }
                catch (Exception exception)
                {
                    App.logger.Warn(exception, "[AppFetch] Unable to terminate the canceled installer process.");
                }

                throw;
            }

            return (process.ExitCode, higherVersionInstalled, wrongArch, edgeRequired, advertisingRequired);
        }

        private static readonly List<string> _dependencyPrefixes = new()
        {
            "Microsoft.VCLibs",
            "Microsoft.NET",
            "Microsoft.UI",
            "Microsoft.WinJS",
            "Microsoft.WindowsAppRuntime",
            "Microsoft.Advertising"
        };

        private static bool IsDependency(string name) =>
            _dependencyPrefixes.Any(dep => name.StartsWith(dep, StringComparison.OrdinalIgnoreCase));

        public class InstalledPackage
        {
            public string PublisherName { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public List<string> ApplicationTitles { get; set; } = new();
            public string Version { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string? ProductID { get; set; }
        }

        public List<InstalledPackage> InstalledPackages { get; } = new();

        public async Task PrepareDataAsync()
        {
            await Task.Run(() =>
            {
                InstalledPackages.Clear();
                try
                {
                    using RegistryKey? dataKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\StateRepository\Cache\Package\Data");
                    if (dataKey == null)
                    {
                        return;
                    }

                    foreach (string subKeyName in dataKey.GetSubKeyNames())
                    {
                        using RegistryKey? subKey = dataKey.OpenSubKey(subKeyName);
                        if (subKey == null)
                        {
                            continue;
                        }

                        string? fullName = (string?)subKey.GetValue("PackageFullName", null);
                        string? installLocation = (string?)subKey.GetValue("InstalledLocation", null);
                        if (fullName == null || installLocation == null || !File.Exists(Path.Combine(installLocation, "AppxManifest.xml")))
                        {
                            continue;
                        }

                        XDocument doc = XDocument.Load(Path.Combine(installLocation, "AppxManifest.xml"));
                        XNamespace ns = doc.Root!.GetDefaultNamespace();

                        XElement identityElement = doc.Root!.Element(ns + "Identity")!;
                        string identityVersion = identityElement!.Attribute("Version")!.Value;

                        XElement propertiesElement = doc.Root!.Element(ns + "Properties")!;

                        string displayName = propertiesElement!.Element(ns + "DisplayName")!.Value;
                        if (displayName.StartsWith("ms-resource:"))
                        {
                            string? resource = LoadResource(displayName, fullName, Path.Combine(installLocation, "resources.pri"));
                            if (resource == null)
                            {
                                continue;
                            }
                            displayName = resource;
                        }
                        string publisherDisplayName = propertiesElement!.Element(ns + "PublisherDisplayName")!.Value;
                        if (publisherDisplayName.StartsWith("ms-resource:"))
                        {
                            string? resource = LoadResource(publisherDisplayName, fullName, Path.Combine(installLocation, "resources.pri"));
                            if (resource == null)
                            {
                                continue;
                            }
                            publisherDisplayName = resource;
                        }

                        InstalledPackages.Add(new InstalledPackage()
                        {
                            Title = displayName,
                            PublisherName = publisherDisplayName,
                            Version = identityVersion,
                            FullName = fullName,
                            ApplicationTitles = new List<string>(),
                        });

                        IEnumerable<XElement> applicationElements = doc.Root!.Element(ns + "Applications")?.Elements(ns + "Application")! ?? Enumerable.Empty<XElement>();
                        foreach (XElement applicationElement in applicationElements)
                        {
                            string? applicationDisplayName = applicationElement.Elements().FirstOrDefault(x => x.Name.LocalName == "VisualElements")?.Attribute("DisplayName")?.Value;
                            if (!string.IsNullOrWhiteSpace(applicationDisplayName))
                            {
                                if (applicationDisplayName.StartsWith("ms-resource:"))
                                {
                                    string? resource = LoadResource(applicationDisplayName, fullName, Path.Combine(installLocation, "resources.pri"));
                                    if (resource == null)
                                    {
                                        continue;
                                    }
                                    applicationDisplayName = resource;
                                }
                                InstalledPackages[^1].ApplicationTitles.Add(applicationDisplayName);
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    App.logger.Debug(exception, "[AppFetch] Unable to enumerate installed packages.");
                }
            });
        }

        private static string? LoadResource(string resourceKey, string packageFullName, string resourcesFile)
        {
            if (!File.Exists(resourcesFile))
            {
                return null;
            }

            resourceKey = resourceKey.Replace("ms-resource:resources/", "ms-resource:", StringComparison.OrdinalIgnoreCase);
            string resourceKeyPath = $"ms-resource://{packageFullName.Split('_').First()}/Resources/" + resourceKey.Split(':').Last();
            string resourceReference = $"@{{{resourcesFile}?{resourceKeyPath}}}";

            StringBuilder sb = new(1024);
            int hr = SHLoadIndirectString(resourceReference, sb, sb.Capacity, IntPtr.Zero);

            return hr != 0 ? null : sb.ToString();
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHLoadIndirectString(
            string pszSource,
            StringBuilder pszOutBuf,
            int cchOutBuf,
            IntPtr ppvReserved);

        private const string _fe3DeliveryUrl = "https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx";
        private const string _storeApiUrl = "https://storeedgefd.dsx.mp.microsoft.com/v9.0";
        private const string _searchApiUrl = "https://apps.microsoft.com/api/products/search";

        private static readonly Dictionary<string, string> _soapXmlHeaders = new()
        {
            { "user-agent", "Mozilla/5.0 (Windows NT 10.0; rv:107.0) Gecko/20100101 Firefox/107.0" },
            { "Accept", "*/*" },
            { "Content-Type", "application/soap+xml" }
        };

        private static readonly Regex _wuCategoryIdRegex = new("\"WuCategoryId\":\"([^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex _namePatternRegex = new("^[^_]+", RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly HttpClient _httpClient = new();

        [JsonSerializable(typeof(StorePackageDto))]
        [JsonSerializable(typeof(StoreProductListDto))]
        [JsonSerializable(typeof(StoreSearchResponseDto))]
        [JsonSerializable(typeof(StoreInstallerPackageResponseDto))]
        [JsonSerializable(typeof(Data))]
        [JsonSerializable(typeof(Versions))]
        [JsonSerializable(typeof(DefaultLocale))]
        [JsonSerializable(typeof(Installers))]
        [JsonSerializable(typeof(InstallerSwitches))]
        internal partial class SourceGenerationContext : JsonSerializerContext { }

        public async Task<List<StorePackageDto>> SearchInstallerProductsAsync(
            string productId,
            CancellationToken cancellationToken = default)
        {
            string requestUrl = $"{_storeApiUrl}/packageManifests/{Uri.EscapeDataString(productId)}?Market=US";

            using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                List<StorePackageDto> results = new();

                StoreInstallerPackageResponseDto? responseData = await response.Content.ReadFromJsonAsync<StoreInstallerPackageResponseDto>(
                    new JsonSerializerOptions() { TypeInfoResolver = new SourceGenerationContext() },
                    cancellationToken);
                if (responseData is null)
                {
                    throw new Exception("Invalid response from the server.");
                }

                List<string> urls = new();
                foreach (Installers installer in responseData.Data!.Versions!.First().Installers!)
                {
                    if (urls.Contains(installer.InstallerUrl!))
                    {
                        continue;
                    }
                    urls.Add(installer.InstallerUrl!);
                    string extension = Path.GetExtension(new Uri(installer.InstallerUrl!).AbsolutePath).TrimStart('.');
                    if (extension.Equals("exe", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals("msi", StringComparison.OrdinalIgnoreCase))
                    {
                        string filename = responseData.Data!.Versions!.First().DefaultLocale!.PackageName + "-" + installer.Architecture;
                        results.Add(new StorePackageDto()
                        {
                            Name = filename,
                            FileExtension = extension,
                            ResourceUri = installer.InstallerUrl,
                            LastModified = DateTime.Now,
                            PackageId = productId,
                            Checksum = installer.InstallerSha256,
                            CommandLines = installer.InstallerSwitches?.Silent ?? installer.InstallerSwitches?.SilentWithProgress
                        });
                    }
                }

                return results;
            }

            throw new Exception("Failed to search the product: " + (response.ReasonPhrase ?? response.StatusCode.ToString()));
        }

        public async Task<List<StoreProductListDto>> SearchProductsAsync(
            string query,
            CancellationToken cancellationToken = default)
        {
            string requestUrl = $"{_searchApiUrl}?gl=US&hl=en-us&query={Uri.EscapeDataString(query)}&mediaType=all&age=all&price=all&category=all&subscription=all";

            using HttpResponseMessage response = await _httpClient.GetAsync(requestUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                StoreSearchResponseDto? responseData = await response.Content.ReadFromJsonAsync<StoreSearchResponseDto>(
                    new JsonSerializerOptions() { TypeInfoResolver = new SourceGenerationContext() },
                    cancellationToken);
                if (responseData is null)
                {
                    throw new Exception("Invalid response from the server.");
                }

                List<StoreProductListDto> results = new();
                if (responseData.HighlightedList != null)
                {
                    results.AddRange(responseData.HighlightedList);
                }
                if (responseData.ProductsList != null)
                {
                    results.AddRange(responseData.ProductsList);
                }

                results.RemoveAll(x => !IsSupported(x.ProductId!) || (!string.IsNullOrWhiteSpace(x.DisplayPrice) && x.DisplayPrice != "Free" && (!double.TryParse(x.DisplayPrice, out double price) || price != 0)));

                return results;
            }

            throw new Exception("Failed to search the product: " + (response.ReasonPhrase ?? response.StatusCode.ToString()));
        }

        private string? _cookie;
        public async Task<string> GetCookieAsync()
        {
            if (_cookie != null)
            {
                return _cookie;
            }

            using HttpRequestMessage request = CreateSoapRequest(CookieContent, _fe3DeliveryUrl);
            using HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();

                XElement? encryptedDataElement = XElement.Parse(responseString).Descendants().FirstOrDefault(x => x.Name.LocalName == "EncryptedData");
                if (encryptedDataElement != null)
                {
                    _cookie = encryptedDataElement.Value;
                    return encryptedDataElement.Value;
                }

                throw new Exception("EncryptedData element not found in response.");
            }

            throw new Exception("Failed to get a cookie");
        }

        private async Task<string> GetCategoryIDAsync(string id)
        {
            string url = $"{_storeApiUrl}/products/{id}?market=US&locale=en-us&deviceFamily=Windows.Desktop";

            using HttpResponseMessage response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(jsonResponse);

                if (document.RootElement.TryGetProperty("Payload", out JsonElement payload) &&
                    payload.TryGetProperty("Skus", out JsonElement skus) &&
                    skus.ValueKind == JsonValueKind.Array &&
                    skus.GetArrayLength() > 0)
                {
                    JsonElement firstSku = skus[0];
                    if (firstSku.TryGetProperty("FulfillmentData", out JsonElement fulfillmentDataElement))
                    {
                        string? fulfillmentData = fulfillmentDataElement.GetString();
                        if (!string.IsNullOrEmpty(fulfillmentData))
                        {
                            Match match = _wuCategoryIdRegex.Match(fulfillmentData);
                            if (match.Success && match.Groups.Count > 1)
                            {
                                return match.Groups[1].Value;
                            }
                        }
                    }

                    throw new Exception("The selected app is not UWP.");
                }

                throw new Exception("The selected app is not UWP.");
            }

            throw new Exception("Failed to get category id");
        }

        private async Task<string> FetchFileListXMLAsync(string categoryID, string cookie, string ring)
        {
            string requestXml = WUAContent
                .Replace("{1}", cookie)
                .Replace("{2}", categoryID)
                .Replace("{3}", ring);

            using HttpRequestMessage request = CreateSoapRequest(requestXml, _fe3DeliveryUrl);
            using HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string responseData = await response.Content.ReadAsStringAsync();
                return responseData.Replace("&lt;", "<").Replace("&gt;", ">");
            }

            throw new Exception("Failed to get file list xml");
        }

        private async Task<string> GetUriAsync(string updateID, string revision, string ring, string digest)
        {
            string requestXml = UrlContent
                .Replace("{1}", updateID)
                .Replace("{2}", revision)
                .Replace("{3}", ring);

            using HttpRequestMessage request = CreateSoapRequest(requestXml, _fe3DeliveryUrl + "/secured");
            using HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string responseData = await response.Content.ReadAsStringAsync();
                XDocument xmlDoc = XDocument.Parse(responseData);

                foreach (XElement node in xmlDoc.Descendants().Where(x => x.Name.LocalName == "FileLocation"))
                {
                    if (node.Descendants().First(x => x.Name.LocalName == "FileDigest").Value == digest)
                    {
                        return node.Descendants().First(x => x.Name.LocalName == "Url").Value;
                    }
                }
            }

            return "";
        }

        private async Task<List<StorePackageDto>> ParsePackagesAsync(string xmlList, string ring, bool getDownloadUrl)
        {
            List<StorePackageDto> result = new();
            XDocument xmlDoc = XDocument.Parse(xmlList);
            Dictionary<string, string> packageMap = new();
            string systemArchitecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

            foreach (XElement node in xmlDoc.Descendants().Where(x => x.Name.LocalName == "File"))
            {
                string? installerId = node.Attribute("InstallerSpecificIdentifier")?.Value;
                if (installerId == null)
                {
                    continue;
                }

                string? digest = node.Attribute("Digest")?.Value;
                string? modified = node.Attribute("Modified")?.Value;
                string? fileName = node.Attribute("FileName")?.Value;
                string? size = node.Attribute("Size")?.Value;

                if (digest != null && modified != null && fileName != null && size != null)
                {
                    int lastDot = fileName.LastIndexOf('.');
                    string ext = lastDot >= 0 && lastDot < fileName.Length - 1
                        ? fileName.Substring(lastDot + 1)
                        : string.Empty;

                    string packageData = $"{ext}|{size}|{digest}|{modified}";
                    packageMap.TryAdd(installerId, packageData);
                }
            }

            foreach (XElement node in xmlDoc.Descendants().Where(x => x.Name.LocalName == "SecuredFragment"))
            {
                XElement? grandparent = node.Parent?.Parent;
                XElement? appxMetadata = grandparent
                    ?.Elements().FirstOrDefault(x => x.Name.LocalName == "ApplicabilityRules")
                    ?.Elements().FirstOrDefault(x => x.Name.LocalName == "Metadata")
                    ?.Elements().FirstOrDefault(x => x.Name.LocalName == "AppxPackageMetadata")
                    ?.Elements().FirstOrDefault(x => x.Name.LocalName == "AppxMetadata");
                if (grandparent == null || appxMetadata == null)
                {
                    continue;
                }

                string? packageMoniker = appxMetadata.Attribute("PackageMoniker")?.Value;
                if (packageMoniker == null || !packageMap.TryGetValue(packageMoniker, out string? packageData))
                {
                    continue;
                }

                string[] parts = packageData.Split('|');
                if (parts.Length < 4)
                {
                    continue;
                }

                string ext = parts[0];
                if (!double.TryParse(parts[1], out double pkgSize))
                {
                    pkgSize = 0;
                }
                string digest = parts[2];
                if (!DateTime.TryParse(parts[3], out DateTime lastModified))
                {
                    lastModified = DateTime.MinValue;
                }

                XElement? updateIdentity = grandparent.Elements().FirstOrDefault(x => x.Name.LocalName == "UpdateIdentity");
                if (updateIdentity == null)
                {
                    continue;
                }

                if (packageMoniker.Contains(systemArchitecture) || (packageMoniker.Contains("neutral") && !ext.StartsWith("e")))
                {
                    string? updateID = updateIdentity.Attribute("UpdateID")?.Value;
                    string? revisionNumber = updateIdentity.Attribute("RevisionNumber")?.Value;
                    if (updateID == null || revisionNumber == null)
                    {
                        continue;
                    }

                    XElement? idElement = grandparent.Parent?.Elements().FirstOrDefault(x => x.Name.LocalName == "ID");
                    if (idElement == null)
                    {
                        continue;
                    }
                    string packageId = idElement.Value;

                    string? resourceUri = !getDownloadUrl ? null : await GetUriAsync(updateID, revisionNumber, ring, digest);

                    result.Add(new StorePackageDto
                    {
                        Name = packageMoniker,
                        FileExtension = ext,
                        ResourceUri = resourceUri,
                        Revision = revisionNumber,
                        UpdateIdentifier = updateID,
                        PackageId = packageId,
                        Size = pkgSize,
                        Checksum = digest,
                        LastModified = lastModified,
                        OriginalIndex = null,
                        CommandLines = null
                    });
                }
            }
            return result;
        }

        private static HttpRequestMessage CreateSoapRequest(string content, string url)
        {
            HttpRequestMessage request = new(HttpMethod.Post, url)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/soap+xml")
            };

            foreach (KeyValuePair<string, string> header in _soapXmlHeaders)
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }
    }
}
