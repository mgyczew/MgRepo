using Microsoft.AspNetCore.SignalR;
using InvestorChat.Server.Hubs;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace InvestorChat.Server.Services;

public class MarketPriceService : IHostedService, IDisposable
{
    private readonly IHubContext<MarketHub> _hub;
    private readonly ILogger<MarketPriceService> _logger;
    private readonly HttpClient _httpClient;
    private CancellationTokenSource? _stopSource;
    private Task? _worker;
    private readonly Dictionary<string, decimal> _prices = new();
    private readonly Dictionary<string, string> _tickers = new()
    {
        ["PZU"] = "PZU",
        ["KETY"] = "KETY",
        ["RAINBOW"] = "RAINBOW",
        ["PEAKO"] = "PEO",
        ["TEXT"] = "TEXT",
        ["KRUK"] = "KRUK"
    };

    public MarketPriceService(
        IHubContext<MarketHub> hub,
        ILogger<MarketPriceService> logger,
        HttpClient httpClient)
    {
        _hub = hub;
        _logger = logger;
        _httpClient = httpClient;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _worker = RunAsync(_stopSource.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        await UpdateQuotesAsync(cancellationToken);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await UpdateQuotesAsync(cancellationToken);
        }
    }

    private async Task UpdateQuotesAsync(CancellationToken cancellationToken)
    {
        var time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var updates = await Task.WhenAll(_tickers.Select(entry => FetchQuoteAsync(entry, time, cancellationToken)));

        await Task.WhenAll(updates
            .Where(update => update is not null)
            .Select(update => _hub.Clients.All.SendAsync(
                "ReceiveQuote",
                update!.Symbol,
                update.Price,
                update.Change,
                update.Time,
                cancellationToken)));
    }

    private async Task<QuoteUpdate?> FetchQuoteAsync(
        KeyValuePair<string, string> entry,
        string time,
        CancellationToken cancellationToken)
    {
        try
        {
            var bankierSymbol = entry.Value;
            var url = $"https://www.bankier.pl/inwestowanie/profile/quote.html?symbol={bankierSymbol}";
            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var priceMatch = Regex.Match(html, @"o-quotes-profile-header-box__price.*?a-quote-item -value[^>]*>\s*([^<]+)", RegexOptions.Singleline);
            var changeMatch = Regex.Match(html, @"o-quotes-profile-header-box__change.*?a-quote-item -value-change[^>]*>\s*([^<]+)", RegexOptions.Singleline);

            if (!priceMatch.Success || !changeMatch.Success)
            {
                throw new FormatException($"Quote data was not found for {entry.Key}");
            }

            var price = ParseBankierAmount(priceMatch.Groups[1].Value);
            var change = ParseBankierAmount(changeMatch.Groups[1].Value);

            _prices[entry.Key] = price;
            _logger.LogDebug("Fetched live quote {Symbol} {Price} {Change} at {Time}", entry.Key, price, change, time);
            return new QuoteUpdate(entry.Key, price, change, time);
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException or RegexMatchTimeoutException)
        {
            _logger.LogWarning(ex, "Could not fetch live quote for {Symbol}", entry.Key);
            return null;
        }
    }

    private static decimal ParseBankierAmount(string value)
    {
        var normalized = value
            .Replace("zł", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\u00a0", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');

        return decimal.Parse(normalized, CultureInfo.InvariantCulture);
    }

    private sealed record QuoteUpdate(string Symbol, decimal Price, decimal Change, string Time);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _stopSource?.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _stopSource?.Dispose();
    }
}
