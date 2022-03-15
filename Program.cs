using DSharpPlus;

var client = new DiscordClient(new DiscordConfiguration() {
	Token = Environment.GetEnvironmentVariable("BOT_TOKEN")
});
client.MessageCreated += (_, args) => {
	if (!args.Author.IsBot && args.Message.Content.ToLower() == "je bolle mams") {
		return args.Message.RespondAsync("je bolle mams");
	} else {
		return Task.CompletedTask;
	}
};
await client.ConnectAsync();
await Task.Delay(-1);
