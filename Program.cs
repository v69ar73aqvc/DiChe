using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

const string BaseUrl = "https://diverse.direct/wp-json/dd-front/v1/items?page={0}";
const string EnvToken = "diche_discord_token";
const string EnvChannel = "diche_discord_channel";
const int delaySec = 60;

string token = Environment.GetEnvironmentVariable(EnvToken) ?? throw new KeyNotFoundException($"{EnvToken} not found");
ulong channel = ulong.Parse(Environment.GetEnvironmentVariable(EnvChannel) ??
                            throw new KeyNotFoundException($"{EnvChannel} not found"), CultureInfo.InvariantCulture);
DiscordSocketClient discord = new();
Console.Write("Discord... ");
await discord.LoginAsync(TokenType.Bot, token);
await discord.StartAsync();
Console.WriteLine("ok");
Console.Write("Waiting for chan... ");
SocketChannel? chan = null;
while (chan == null)
{
    await Task.Delay(1000);
    chan = discord.GetChannel(channel);
}

if (chan is not IMessageChannel textChan) throw new InvalidOperationException($"Channel {channel} not found");
Console.WriteLine("ok");
HttpClient http = new();
Console.Write("Initial fetch... ");
HashSet<long> ids = new((await ScrapeJson(http, 1)).Select(v => v.id));
Console.WriteLine($"{ids.Count}");
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(delaySec));
    Console.Write($"Fetch {DateTimeOffset.Now}... ");
    var newProducts = (await ScrapeJson(http, 1)).Where(v => !ids.Contains(v.id));
    Console.WriteLine($"{ids.Count}");
    foreach (var product in newProducts)
    {
        try
        {
            await Send(textChan, product);
        }
        catch
        {
            // ignored
        }

        ids.Add(product.id);
    }
}

static async Task Send(IMessageChannel chan, Item info)
{
    EmbedBuilder eb = new()
    {
        ImageUrl = info.thumbnail,
        Author = new EmbedAuthorBuilder { Name = info.circle },
        Title = info.title,
        Url = info.url,
        Fields = new()
        {
            new EmbedFieldBuilder().WithName("Price").WithValue(string.IsNullOrWhiteSpace(info.salePrice)
                ? info.price
                : $"{info.price} => {info.salePrice}")
        }
    };
    await chan.SendMessageAsync(embed: eb.Build());
}

static async Task<IEnumerable<Item>> ScrapeJson(HttpClient client, int page)
{
    var itemsResponse = (await JsonSerializer.DeserializeAsync<ItemsResponse>(
        await client.GetStreamAsync(string.Format(CultureInfo.InvariantCulture, BaseUrl, page))))!;
    return itemsResponse.data.ToList();
}


#pragma warning disable 649, 8618
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedAutoPropertyAccessor.Global
internal class ItemsResponse
{
    // ReSharper disable once CollectionNeverUpdated.Global
    public List<Item> data { get; set; }
}

internal class Item
{
    public long id { get; set; }
    public string title { get; set; }
    public string url { get; set; }
    public string thumbnail { get; set; }
    public string circle { get; set; }
    public string price { get; set; }
    public string salePrice { get; set; }
    public bool isSale { get; set; }
    public bool newly { get; set; }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore InconsistentNaming
#pragma warning restore 649, 8618
