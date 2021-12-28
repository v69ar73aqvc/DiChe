﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

const string BaseUrl = "https://diverse.direct/wp-json/dd-front/v1/items?page={0}";
const string BaseUrlB = "https://diverse.direct/cart/json.php?id={0}";
const string EnvToken = "diche_discord_token";
const string EnvChannel = "diche_discord_channel";
const int delaySec = 30;

if (args.Length == 0)
{
    Console.WriteLine("Need at least 1 <stock:meta> to watch");
    return;
}

List<(long StockId, long MetaId)> ids = args.Select(a=>{
    int p = a.IndexOf(':');
    if(p == -1) throw new ArgumentException($"Invalid entry {a}");
    return (long.Parse(a[..p]), long.Parse(a[(p+1)..]));
}).ToList();
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
Dictionary<long, ItemB> entries = new();
foreach((long metaId, long stockId) in ids)
{
    try
    {
        ItemB item = await GetJsonB(http, stockId);
        entries[stockId] = item;
        Console.WriteLine($"{stockId}: {item.name} stock {item.stock}");
    }
    catch
    {
        Console.WriteLine($"Failed to get item {stockId}, continuing");
        // ignored
    }
}
Console.WriteLine("ok");
Console.WriteLine("Scraping page data for extra info.");
int pn = 1;
List<long> remaining = ids.Where(id=>entries.ContainsKey(id.StockId)).Select(v=>v.MetaId).ToList();
Dictionary<long, Item> items = new();
while (remaining.Any())
{
    List<Item> tmpItems;
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(0.1));
        Console.Write($"Page {pn} ({remaining.Count} remaining)... ");
        tmpItems = (await GetJson(http, pn++)).ToList();
        Console.WriteLine($"{tmpItems.Count} in page");
        if (!tmpItems.Any()) throw new ApplicationException();
    }
    catch
    {
        Console.WriteLine();
        Console.WriteLine($"Failed to grab data for items: {new StringBuilder().AppendJoin(", ", remaining)}");
        return;
    }
    foreach(var v in tmpItems)
    {
        if(remaining.Contains(v.id))
        {
            items[v.id] = v;
            remaining.Remove(v.id);
        }
    }
}
Console.WriteLine($"{ids.Count} retrieved @ start");
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(delaySec));
    try
    {
        Console.Write($"Fetch {DateTimeOffset.Now}... ");
        List<long> updated = new();
        foreach((long metaId, long stockId) in ids)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(0.1));
                ItemB item = await GetJsonB(http, stockId);
                if (!entries.TryGetValue(stockId, out ItemB? existingItem) || existingItem.stock <= 0 && item.stock > 0)
                {
                    updated.Add(stockId);
                    Console.WriteLine($"{stockId}: {item.name} stock {item.stock}");
                    await Send(textChan, items[metaId], item);
                }
                entries[stockId] = item;
            }
            catch
            {
                // ignored
            }
        }
        Console.WriteLine($"{updated.Count} stock updates of interest");
    }
    catch (Exception e)
    {
        Console.WriteLine(e);
    }
}

static async Task Send(IMessageChannel chan, Item info, ItemB info2)
    => await chan.SendMessageAsync(embed: new EmbedBuilder()
    {
        ImageUrl = info.thumbnail,
        Author = new EmbedAuthorBuilder { Name = info.circle },
        Title = $"[STOCK UPDATE ({info2.stock} available)] {info.title}",
        Url = info.url,
        Fields = new()
        {
            new EmbedFieldBuilder().WithName("Price").WithValue(string.IsNullOrWhiteSpace(info.salePrice)
                ? info.price
                : $"{info.price} => {info.salePrice}"),
            new EmbedFieldBuilder().WithName("Stock").WithValue($"{info2.stock}")
        }
    }.Build());

static async Task<IEnumerable<Item>> GetJson(HttpClient client, int page)
    => (await JsonSerializer.DeserializeAsync<ItemsResponse>(
        await client.GetStreamAsync(string.Format(CultureInfo.InvariantCulture, BaseUrl, page))))!.data.ToList();

#pragma warning disable 649, 8618

static async Task<ItemB> GetJsonB(HttpClient client, long id)
    => (await JsonSerializer.DeserializeAsync<Dictionary<string, ItemB>>(
        await client.GetStreamAsync(string.Format(CultureInfo.InvariantCulture, BaseUrlB, id))))!.Values.Single();

#pragma warning disable 649, 8618

internal record ItemsResponse(List<Item> data);
internal record Item(long id, string title, string url, string thumbnail, string circle, string price, string salePrice, bool isSale, bool newly);
internal record ItemB(string name, long price, long stock, bool free_shipping, bool payment_on_delivery_only);
#pragma warning restore 649, 8618
