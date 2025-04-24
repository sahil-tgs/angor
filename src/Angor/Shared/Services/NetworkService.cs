// Location: src/Angor/Shared/Services/NetworkService.cs
// Full original code with modifications applied inside CheckServices method.

using System;
using System.Collections.Generic;
using System.IO; // Added for Path.Combine
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks; // Added for Task
using Angor.Shared.Liquid; // <<< ADDED: Import the new namespace
using Angor.Shared.Models;
using Angor.Shared.Networks;
using Blockcore.Networks; // Assuming this namespace is correct for Network type
using Microsoft.Extensions.Logging;
using NBitcoin; // Assuming this is needed for Transaction type if Broadcast was here

namespace Angor.Shared.Services
{
    public class NetworkService : INetworkService
    {
        private readonly INetworkStorage _networkStorage;
        private readonly HttpClient _httpClient;
        private readonly ILogger<NetworkService> _logger;
        private readonly INetworkConfiguration _networkConfiguration;
        public event Action? OnStatusChanged; // Made nullable


        public NetworkService(INetworkStorage networkStorage, HttpClient httpClient, ILogger<NetworkService> logger, INetworkConfiguration networkConfiguration)
        {
            _networkStorage = networkStorage;
            _httpClient = httpClient;
            _logger = logger;
            _networkConfiguration = networkConfiguration;
        }

        /// <summary>
        /// This method will read the current network from storage and set it in config
        /// If no network found in storage it will look at the property 'setNetwork' to determine what network to set in config and in storage
        /// If the 'setNetwork' is null then we look at the url for hints as to what network to initiate.
        /// </summary>
        public void CheckAndSetNetwork(string url, string? setNetwork = null)
        {
            string networkName = _networkStorage.GetNetwork();

            if (!string.IsNullOrEmpty(networkName))
            {
                // if the network is specified in storage
                // we create set it in the configuration

                _networkConfiguration.SetNetwork(AngorNetworksSelector.NetworkByName(networkName));
            }
            else
            {
                // no network found ether this is a first
                // time user visits the site or the network was wiped

                Network network; // Declared without initializing to null

                if (setNetwork != null)
                {
                    network = AngorNetworksSelector.NetworkByName(setNetwork);
                }
                else if (url.Contains("test", StringComparison.OrdinalIgnoreCase)) // Use OrdinalIgnoreCase for robustness
                {
                    network = new Angornet(); // Assuming Angornet is the testnet
                }
                else if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    network = new Angornet(); // Assuming Angornet for localhost dev
                }
                else
                {
                    network = new BitcoinMain(); // Default to Mainnet
                }

                _networkStorage.SetNetwork(network.Name);
                _networkConfiguration.SetNetwork(network);
            }
             _logger.LogInformation("Network set to: {NetworkName}", _networkConfiguration.Network.Name);
        }

        public void AddSettingsIfNotExist()
        {
            var settings = _networkStorage.GetSettings();
            bool settingsChanged = false;

            if (!settings.Explorers.Any())
            {
                settings.Explorers.AddRange(_networkConfiguration.GetDefaultExplorerUrls());
                settingsChanged = true;
                _logger.LogInformation("Initialized default Explorer URLs.");
            }

            if (!settings.Indexers.Any())
            {
                settings.Indexers.AddRange(_networkConfiguration.GetDefaultIndexerUrls());
                settingsChanged = true;
                 _logger.LogInformation("Initialized default Indexer URLs.");
            }

            if (!settings.Relays.Any())
            {
                settings.Relays.AddRange(_networkConfiguration.GetDefaultRelayUrls());
                settingsChanged = true;
                 _logger.LogInformation("Initialized default Relay URLs.");
            }

            if (!settings.ChatApps.Any())
            {
                settings.ChatApps.AddRange(_networkConfiguration.GetDefaultChatAppUrls());
                settingsChanged = true;
                 _logger.LogInformation("Initialized default Chat App URLs.");
            }

            if(settingsChanged)
            {
                _networkStorage.SetSettings(settings);
                _logger.LogInformation("Network settings saved.");
            }
        }

        public async Task CheckServices(bool force = false)
        {
            var settings = _networkStorage.GetSettings();
            bool statusChanged = false; // Track if any status actually changed

            // --- MODIFICATION START: Check Indexers ---
            foreach (var indexerUrl in settings.Indexers)
            {
                if (force || (DateTime.UtcNow - indexerUrl.LastCheck).TotalMinutes > 10) // Use TotalMinutes
                {
                    var previousStatus = indexerUrl.Status; // Store previous status
                    indexerUrl.LastCheck = DateTime.UtcNow;
                    indexerUrl.Status = UrlStatus.Offline; // Assume offline until proven otherwise

                    try
                    {
                        var uri = new Uri(indexerUrl.Url);
                        // Use Path.Combine carefully with URLs, ensure no double slashes if base URL has trailing slash
                        var blockUrl = new Uri(uri, "api/v1/block-height/0").ToString();

                        _logger.LogDebug("Checking indexer status: {Url}", blockUrl);
                        var response = await _httpClient.GetAsync(blockUrl); // Consider adding CancellationToken

                        if (response.IsSuccessStatusCode)
                        {
                            indexerUrl.Status = UrlStatus.Online;
                            _logger.LogDebug("Indexer online: {Url}", indexerUrl.Url);
                        }
                        else
                        {
                             _logger.LogWarning("Indexer check failed for {Url}. Status: {StatusCode}", indexerUrl.Url, response.StatusCode);
                             // Optionally throw specific exception based on status code if needed elsewhere
                             // throw new LiquidNetworkException(LiquidErrorCode.ConnectionFailed, $"Indexer check failed for {indexerUrl.Url}", $"Status: {response.StatusCode}");
                        }
                    }
                    catch (HttpRequestException httpEx)
                    {
                        // Throw specific exception for network errors
                        _logger.LogError(httpEx, "Network error checking indexer status: {Url}", indexerUrl.Url);
                        // Instead of just setting offline, throw so caller can handle
                        throw new LiquidNetworkException(
                            LiquidErrorCode.ConnectionFailed,
                            $"Network error checking indexer: {indexerUrl.Url}",
                            httpEx.Message,
                            httpEx);
                    }
                    catch (Exception ex) // Catch other unexpected errors
                    {
                        _logger.LogError(ex, "Unexpected error checking indexer status: {Url}", indexerUrl.Url);
                        // Throw specific exception for unknown errors
                         throw new LiquidNetworkException(
                            LiquidErrorCode.Unknown,
                            $"Unexpected error checking indexer: {indexerUrl.Url}",
                            ex.Message,
                            ex);
                    }
                    finally // Ensure status change is tracked
                    {
                         if (indexerUrl.Status != previousStatus)
                         {
                            statusChanged = true;
                         }
                    }
                }
            }
            // --- MODIFICATION END: Check Indexers ---

            // --- MODIFICATION START: Check Relays ---
            // (Explorer check was commented out in original, keeping it that way)

            var nostrHeaderMediaType = new MediaTypeWithQualityHeaderValue("application/nostr+json");

            // Add header only if not already present
            if (!_httpClient.DefaultRequestHeaders.Accept.Contains(nostrHeaderMediaType))
            {
                 _httpClient.DefaultRequestHeaders.Accept.Add(nostrHeaderMediaType);
            }

            foreach (var relayUrl in settings.Relays)
            {
                if (force || (DateTime.UtcNow - relayUrl.LastCheck).TotalMinutes > 1) // Use TotalMinutes
                {
                    var previousStatus = relayUrl.Status; // Store previous status
                    relayUrl.LastCheck = DateTime.UtcNow;
                    relayUrl.Status = UrlStatus.Offline; // Assume offline

                    try
                    {
                        var uri = new Uri(relayUrl.Url);
                        // Convert wss:// to https:// or ws:// to http:// for the NIP-11 check
                        var httpScheme = uri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                        var httpUri = new Uri($"{httpScheme}://{uri.Host}:{uri.Port}/"); // Ensure port is included if non-default

                        _logger.LogDebug("Checking relay status (NIP-11): {Url}", httpUri);
                        var response = await _httpClient.GetAsync(httpUri); // Consider adding CancellationToken

                        if (response.IsSuccessStatusCode)
                        {
                            // Check if content type indicates it's likely a NIP-11 response
                            if (response.Content.Headers.ContentType?.MediaType?.Equals("application/nostr+json", StringComparison.OrdinalIgnoreCase) == true ||
                                response.Content.Headers.ContentType?.MediaType?.Equals("application/json", StringComparison.OrdinalIgnoreCase) == true) // Accept application/json too
                            {
                                relayUrl.Status = UrlStatus.Online;
                                try
                                {
                                    var relayInfo = await response.Content.ReadFromJsonAsync<NostrRelayInfo>(); // Add CancellationToken if needed
                                    relayUrl.Name = relayInfo?.Name ?? string.Empty;
                                     _logger.LogDebug("Relay online: {Url}, Name: {Name}", relayUrl.Url, relayUrl.Name);
                                }
                                catch(JsonException jsonEx)
                                {
                                     _logger.LogWarning(jsonEx, "Relay {Url} returned success status but failed to parse NIP-11 JSON.", relayUrl.Url);
                                     // Keep status as Online, but name might be empty
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Relay {Url} returned success status but unexpected content type: {ContentType}", relayUrl.Url, response.Content.Headers.ContentType);
                                // Treat as online since it responded, but log warning.
                                relayUrl.Status = UrlStatus.Online;
                            }
                        }
                        else
                        {
                            _logger.LogWarning("Relay check failed for {Url}. Status: {StatusCode}", relayUrl.Url, response.StatusCode);
                            // Optionally throw specific exception
                             // throw new LiquidNetworkException(LiquidErrorCode.ConnectionFailed, $"Relay check failed for {relayUrl.Url}", $"Status: {response.StatusCode}");
                        }
                    }
                    catch (HttpRequestException httpEx)
                    {
                        _logger.LogError(httpEx, "Network error checking relay status: {Url}", relayUrl.Url);
                        // Throw specific exception
                         throw new LiquidNetworkException(
                            LiquidErrorCode.ConnectionFailed,
                            $"Network error checking relay: {relayUrl.Url}",
                            httpEx.Message,
                            httpEx);
                    }
                    catch (Exception ex)
                    {
                         _logger.LogError(ex, "Unexpected error checking relay status: {Url}", relayUrl.Url);
                         // Throw specific exception
                         throw new LiquidNetworkException(
                            LiquidErrorCode.Unknown,
                            $"Unexpected error checking relay: {relayUrl.Url}",
                            ex.Message,
                            ex);
                    }
                     finally // Ensure status change is tracked
                    {
                         if (relayUrl.Status != previousStatus)
                         {
                            statusChanged = true;
                         }
                    }
                }
            }
            // --- MODIFICATION END: Check Relays ---


            // Persist settings if any status changed
            if (statusChanged || force) // Save if forced or if a status changed
            {
                _networkStorage.SetSettings(settings);
                _logger.LogInformation("Network service statuses updated and saved.");
                OnStatusChanged?.Invoke(); // Invoke event only if status actually changed
            }
        }

        // --- Remaining methods from original code ---

        public SettingsUrl GetPrimaryIndexer()
        {
            var settings = _networkStorage.GetSettings();
            var ret = settings.Indexers.FirstOrDefault(p => p.IsPrimary && p.Status == UrlStatus.Online) ?? // Prefer online primary
                      settings.Indexers.FirstOrDefault(p => p.IsPrimary) ?? // Fallback to primary even if offline
                      settings.Indexers.FirstOrDefault(p => p.Status == UrlStatus.Online) ?? // Fallback to any online
                      settings.Indexers.FirstOrDefault(); // Fallback to first available

            if (ret == null)
            {
                _logger.LogError("No indexer URL available in settings.");
                throw new ApplicationException("No indexer found. Please configure indexer URLs in settings.");
            }
            if (ret.Status != UrlStatus.Online)
            {
                 _logger.LogWarning("Using indexer {Url} which is currently marked as {Status}", ret.Url, ret.Status);
            }
            return ret;
        }

        public SettingsUrl GetPrimaryRelay()
        {
            var settings = _networkStorage.GetSettings();
             var ret = settings.Relays.FirstOrDefault(p => p.IsPrimary && p.Status == UrlStatus.Online) ??
                      settings.Relays.FirstOrDefault(p => p.IsPrimary) ??
                      settings.Relays.FirstOrDefault(p => p.Status == UrlStatus.Online) ??
                      settings.Relays.FirstOrDefault();

            if (ret == null)
            {
                 _logger.LogError("No relay URL available in settings.");
                throw new ApplicationException("No relay found. Please configure relay URLs in settings.");
            }
             if (ret.Status != UrlStatus.Online)
            {
                 _logger.LogWarning("Using relay {Url} which is currently marked as {Status}", ret.Url, ret.Status);
            }
            return ret;
        }

        public List<SettingsUrl> GetRelays()
        {
            var settings = _networkStorage.GetSettings();
            // Return only relays currently marked as Online? Or all configured? Returning all for now.
            return settings.Relays;
        }

        public SettingsUrl GetPrimaryExplorer()
        {
            var settings = _networkStorage.GetSettings();
             var ret = settings.Explorers.FirstOrDefault(p => p.IsPrimary && p.Status == UrlStatus.Online) ??
                      settings.Explorers.FirstOrDefault(p => p.IsPrimary) ??
                      settings.Explorers.FirstOrDefault(p => p.Status == UrlStatus.Online) ??
                      settings.Explorers.FirstOrDefault();

            if (ret == null)
            {
                 _logger.LogError("No explorer URL available in settings.");
                throw new ApplicationException("No explorer found. Please configure explorer URLs in settings.");
            }
             if (ret.Status != UrlStatus.Online)
            {
                 _logger.LogWarning("Using explorer {Url} which is currently marked as {Status}", ret.Url, ret.Status);
            }
            return ret;
        }

        public SettingsUrl GetPrimaryChatApp()
        {
            var settings = _networkStorage.GetSettings();
            // Chat apps likely don't have a status check implemented here
            var ret = settings.ChatApps.FirstOrDefault(p => p.IsPrimary) ??
                      settings.ChatApps.FirstOrDefault();

            if (ret == null)
            {
                 _logger.LogError("No chat app URL available in settings.");
                throw new ApplicationException("No chat application found. Please configure chat app URLs in settings.");
            }
            return ret;
        }

        // This method seems less useful now if CheckServices throws exceptions.
        // Keeping it for now, but its logic might need review depending on how CheckServices errors are handled upstream.
        public void CheckAndHandleError(HttpResponseMessage httpResponseMessage)
        {
            if (!httpResponseMessage.IsSuccessStatusCode)
            {
                 _logger.LogWarning("CheckAndHandleError invoked for non-success status: {StatusCode}, Request: {RequestUri}",
                    httpResponseMessage.StatusCode, httpResponseMessage.RequestMessage?.RequestUri);

                if (httpResponseMessage.StatusCode == HttpStatusCode.NotFound)
                {
                    var settings = _networkStorage.GetSettings();
                    var host = httpResponseMessage.RequestMessage?.RequestUri?.Host;

                    if (host != null)
                    {
                        // Find the corresponding setting URL and mark it offline
                        var indexer = settings.Indexers.FirstOrDefault(a => new Uri(a.Url).Host.Equals(host, StringComparison.OrdinalIgnoreCase));
                        if (indexer != null && indexer.Status != UrlStatus.Offline)
                        {
                            indexer.Status = UrlStatus.Offline;
                             _logger.LogInformation("Marking indexer {Url} as Offline due to 404.", indexer.Url);
                            _networkStorage.SetSettings(settings);
                            OnStatusChanged?.Invoke();
                            return; // Found and handled
                        }

                        var relay = settings.Relays.FirstOrDefault(a => new Uri(a.Url).Host.Equals(host, StringComparison.OrdinalIgnoreCase));
                         if (relay != null && relay.Status != UrlStatus.Offline)
                        {
                            relay.Status = UrlStatus.Offline;
                             _logger.LogInformation("Marking relay {Url} as Offline due to 404.", relay.Url);
                            _networkStorage.SetSettings(settings);
                            OnStatusChanged?.Invoke();
                            return; // Found and handled
                        }
                         // Add similar checks for Explorers if needed
                    }
                }
                 // Consider throwing a specific exception here based on status code?
            }
        }

        // This method also seems less useful if exceptions are thrown directly from the failing operations.
        // It might be better handled by try-catch blocks where network operations are called.
        public void HandleException(Exception exception)
        {
            _logger.LogError(exception, "HandleException invoked.");

            if (exception is HttpRequestException httpRequestException)
            {
                _logger.LogInformation("Handling HttpRequestException - Triggering background service check.");
                // The original code ran CheckServices in the background.
                // This might still be useful as a recovery mechanism, but be aware
                // that CheckServices now throws exceptions itself on failure.
                // This could lead to unhandled exceptions in the Task.Run if not caught there.
                _ = Task.Run(async () => // Fire and forget, consider implications
                {
                    try
                    {
                        await CheckServices(true); // Force check
                    }
                    catch(Exception ex)
                    {
                         _logger.LogError(ex, "Error during background CheckServices triggered by HandleException.");
                         // Decide how to handle errors from the background check
                    }
                });
            }
            else if (exception is LiquidNetworkException liquidEx)
            {
                 // Specific handling for Liquid errors if needed at this generic level
                 _logger.LogWarning("LiquidNetworkException handled: {ErrorCode} - {Message}", liquidEx.ErrorCode, liquidEx.Message);
                 // Maybe trigger CheckServices here too?
            }
             // Add handling for other specific exception types if necessary
        }
    }
}
