using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.VoiceNext;
using IBM.Cloud.SDK.Core.Authentication.Iam;
using IBM.Watson.SpeechToText.v1;
using IBM.Watson.TextToSpeech.v1;
using Timer = System.Timers.Timer;

var client = new DiscordClient(new DiscordConfiguration() {
	Token = Environment.GetEnvironmentVariable("BOT_TOKEN"),
	Intents = DiscordIntents.All
});
client.MessageCreated += (_, args) => {
	if (!args.Author.IsBot && args.Message.Content.ToLower() == "je bolle mams") {
		return args.Message.RespondAsync("je bolle mams");
	} else {
		return Task.CompletedTask;
	}
};

client.UseVoiceNext(new VoiceNextConfiguration() {
	EnableIncoming = true
});
await client.ConnectAsync();

string? guildIdEnvvar = Environment.GetEnvironmentVariable("GUILD_ID");
string? channelIdEnvvar = Environment.GetEnvironmentVariable("CHANNEL_ID");
if (guildIdEnvvar != null && channelIdEnvvar != null) {
	ulong guildId = ulong.Parse(guildIdEnvvar);
	ulong channelId = ulong.Parse(channelIdEnvvar);
	
	var stt = new SpeechToTextService(new IamAuthenticator(Environment.GetEnvironmentVariable("STT_KEY")));
	stt.SetServiceUrl(Environment.GetEnvironmentVariable("STT_URL")!);
	
	var tts = new TextToSpeechService(new IamAuthenticator(Environment.GetEnvironmentVariable("TTS_KEY")));
	tts.SetServiceUrl(Environment.GetEnvironmentVariable("TTS_URL")!);

	while (true) {
		while ((await client.GetGuildAsync(guildId)).GetChannel(channelId).Users.Count() < 2) {
			await Task.Delay(TimeSpan.FromSeconds(5));
		}

		using (VoiceNextConnection vnc = await (await client.GetChannelAsync(channelId)).ConnectAsync()) {
			CancellationTokenSource cts = new CancellationTokenSource();

			var userStreams = new ConcurrentDictionary<uint, UserVoice>();
			
			void OnStopSpeaking(UserVoice uv) {
				if (userStreams.TryRemove(uv.Ssrc, out UserVoice? _)) {
					Console.WriteLine("eee");
					Task.Run(async () => {
						uv.Stream.Seek(0, SeekOrigin.Begin);
						try {
							var response = stt.Recognize(uv.Stream, contentType: "audio/l16;rate=" + vnc.AudioFormat.SampleRate, model: "nl-NL_BroadbandModel");
							if (response.Result.Results.Count > 0 && response.Result.Results[0].Alternatives.Count > 0) {
								Console.WriteLine($"{uv.Ssrc} {response.Result.Results[0].Alternatives[0].Transcript}");
								if (response.Result.Results[0].Alternatives.Any(alt => 
									    alt.Transcript.Contains("je bolle mams") ||
									    alt.Transcript.Contains("je pollemans") || // it tries
									    alt.Transcript.Contains("ja pollemans") ||
									    alt.Transcript.Contains("je bollemans") ||
									    alt.Transcript.Contains("ja bollemans")
									)) {
									Console.WriteLine("match");
									var synthesized = tts.Synthesize("je bolle mams", accept: $"audio/l16;rate={vnc.AudioFormat.SampleRate};channels={vnc.AudioFormat.ChannelCount}", voice: "nl-NL_EmmaVoice");
									await synthesized.Result.CopyToAsync(vnc.GetTransmitSink());
								} else if (response.Result.Results[0].Alternatives.Any(alt => 
									alt.Transcript.Contains("ga weg bot") ||
									alt.Transcript.Contains("de weg pond") || // its best
									alt.Transcript.Contains("de weg tot") ||
									alt.Transcript.Contains("tyf op bot") ||
									alt.Transcript.Contains("dief op bot") ||
									alt.Transcript.Contains("die op bot")
								)) {
									vnc.Disconnect();
									cts.Cancel();
								}
							}

						} catch (Exception e) {
							Console.WriteLine(e.ToString());
						} finally {
							uv.Stream.Dispose();
						}
					});
				}
			}
	
			vnc.VoiceReceived += async (_, args) => {
				await userStreams.GetOrAdd(args.SSRC, id => new UserVoice(args.SSRC, OnStopSpeaking)).WritePacket(args.PcmData);
			};
			
			var connectionPoller = new Timer();
			connectionPoller.Elapsed += (o, e) => {
				try {
					vnc.SendSpeakingAsync(false).GetAwaiter().GetResult();
				} catch (InvalidOperationException) {
					// Disconnected by a channel admin
					cts.Cancel();
				}
			};
			connectionPoller.Interval = TimeSpan.FromSeconds(5).TotalMilliseconds;
			connectionPoller.AutoReset = true;
			connectionPoller.Start();

			try {
				await Task.Delay(-1, cts.Token);
			} catch (TaskCanceledException) { }
			connectionPoller.Stop();
			foreach ((uint key, UserVoice? voice) in userStreams) {
				voice.Stream.Dispose();
			}
		}
		Console.WriteLine("zzz");
		
		while ((await client.GetGuildAsync(guildId)).GetChannel(channelId).Users.Any()) {
			await Task.Delay(TimeSpan.FromSeconds(5));
		}
	}
} else {
	await Task.Delay(-1);
}

class UserVoice {
	private readonly Stream m_SyncStream;
	private readonly Timer m_Timer;
	
	public uint Ssrc { get; }
	public MemoryStream Stream { get; }

	public UserVoice(uint ssrc, Action<UserVoice> onEnd) {
		Ssrc = ssrc;
		Stream = new MemoryStream();
		m_SyncStream = System.IO.Stream.Synchronized(Stream);
		m_Timer = new Timer();
		m_Timer.AutoReset = false;
		m_Timer.Elapsed += (o, e) => onEnd(this);
	}

	public ValueTask WritePacket(ReadOnlyMemory<byte> packet) {
		if (m_Timer.Enabled) {
			m_Timer.Stop();
		}
		m_Timer.Start();
		return m_SyncStream.WriteAsync(packet);
	}
}
