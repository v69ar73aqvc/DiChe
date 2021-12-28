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
while ((chan = discord.GetChannel(channel)) == null) await Task.Delay(1000);
if (chan is not IMessageChannel textChan) throw new InvalidOperationException($"Channel {channel} not found");
Console.WriteLine("ok");
HttpClient http = new();
Console.Write("Initial fetch... ");
HashSet<long> ids = new((await GetJson(http, 1)).Select(v => v.id));
Console.WriteLine($"{ids.Count}");
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(delaySec));
    try
    {
        Console.Write($"Fetch {DateTimeOffset.Now}... ");
        var newProducts = (await GetJson(http, 1)).Where(v => !ids.Contains(v.id)).ToList();
        Console.WriteLine($"{newProducts.Count}");
        foreach (var product in newProducts)
        {
            try
            {
                await Send(textChan, product);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            ids.Add(product.id);
        }
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
    }
}

static async Task Send(IMessageChannel chan, Item info)
    => await chan.SendMessageAsync(embed: new EmbedBuilder()
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
    }.Build());

static async Task<IEnumerable<Item>> GetJson(HttpClient client, int page)
    => (await JsonSerializer.DeserializeAsync<ItemsResponse>(
        await client.GetStreamAsync(string.Format(CultureInfo.InvariantCulture, BaseUrl, page))))!.data.ToList();

#pragma warning disable 649, 8618
internal record ItemsResponse(List<Item> data);

internal record Item(long id, string title, string url, string thumbnail, string circle, string price, string salePrice, bool isSale, bool newly);
#pragma warning restore 649, 8618
