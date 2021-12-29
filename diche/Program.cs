using System.Globalization;
using System.Text.Json;
using Discord;
using Discord.WebSocket;

const string BaseUrl = "https://diverse.direct/wp-json/dd-front/v1/items?page={0}";
const string EnvToken = "diche_discord_token";
const string EnvChannel = "diche_discord_channel";
const int delaySec = 60;

int skip = args.Length == 0 ? 0 : int.Parse(args[0]);
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
HashSet<long> ids = new((await GetPage(http, 1)).Skip(skip).Select(v => v.id));
Console.WriteLine($"{ids.Count} (skip {skip})");
while (true)
{
    await Task.Delay(TimeSpan.FromSeconds(delaySec));
    List<Item> newProducts = new();
    List<Item> retrieved = new();
    try
    {
        int page = 1;
        do
        {
            Console.Write($"Fetch {DateTimeOffset.Now} (page {page})... ");
            retrieved.Clear();
            retrieved.AddRange(await GetPage(http, page++));
            newProducts.AddRange(retrieved.Where(v => !ids.Contains(v.id)));
        } while (retrieved.Count != 0 && !retrieved.Select(v => v.id).Intersect(ids).Any());
        Console.WriteLine($"{newProducts.Count}");
        foreach (Item? product in newProducts)
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
        newProducts.Clear();
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

static async Task<IEnumerable<Item>> GetPage(HttpClient client, int page)
    => (await JsonSerializer.DeserializeAsync<ItemsResponse>(
        await client.GetStreamAsync(string.Format(CultureInfo.InvariantCulture, BaseUrl, page))))!.data.ToList();

internal record ItemsResponse(List<Item> data);

internal record Item(long id, string title, string url, string thumbnail, string circle, string price, string salePrice, bool isSale, bool newly);
