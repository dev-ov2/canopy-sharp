using System.Net.Http;
using System.Text.Json;

namespace Canopy.Core.Auth;

/// <summary>
/// Service for exchanging Firebase tokens for custom tokens
/// </summary>
public class TokenExchangeService
{
    private const string ExchangeEndpoint = "https://us-central1-canopy-yponac.cloudfunctions.net/exchangeFirebaseToken";
    
    private readonly HttpClient _httpClient;

    public TokenExchangeService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Exchanges a Firebase ID token for a custom token
    /// </summary>
    /// <param name="idToken">The Firebase ID token</param>
    /// <returns>The custom token, or null if exchange failed</returns>
    public async Task<string?> ExchangeTokenAsync(string idToken)
    {
        try
        {
            var requestBody = new { token = idToken };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(ExchangeEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"Token exchange failed: {response.StatusCode}");
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            
            if (doc.RootElement.TryGetProperty("customToken", out var customTokenElement))
            {
                return customTokenElement.GetString();
            }

            System.Diagnostics.Debug.WriteLine("No customToken in response");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Token exchange error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Extracts the ID token from a protocol activation URL
    /// </summary>
    /// <param name="url">The full activation URL (e.g., canopy://auth#id_token=...)</param>
    /// <returns>The extracted ID token, or null if not found</returns>
    public static string? ExtractIdTokenFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // Check if URL contains id_token in fragment
        var idTokenIndex = url.IndexOf("id_token=", StringComparison.OrdinalIgnoreCase);
        if (idTokenIndex < 0)
            return null;

        var tokenStart = idTokenIndex + "id_token=".Length;
        var remaining = url.Substring(tokenStart);
        
        // Token ends at next & or end of string
        var ampIndex = remaining.IndexOf('&');
        var token = ampIndex >= 0 ? remaining.Substring(0, ampIndex) : remaining;

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
