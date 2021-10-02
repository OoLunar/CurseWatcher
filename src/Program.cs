using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using DSharpPlus;
using DSharpPlus.Entities;
using ForgedCurse;
using ForgedCurse.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace CurseWatcher
{
    public class Program
    {
        private static DbContextOptions<CurseWatcherContext> DbContextOptions { get; set; }
        private static IConfiguration Configuration { get; set; }
        private static ILogger Logger { get; set; }

        public static void Main(string[] args)
        {
            ConfigurationBuilder configurationBuilder = new();
            configurationBuilder.Sources.Clear();
            configurationBuilder.AddJsonFile(GetSourceFilePathName() + "../../../res/config.json.prod", false, true);
            configurationBuilder.AddEnvironmentVariables();
            configurationBuilder.AddCommandLine(args);
            Configuration = configurationBuilder.Build();

            LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
                    .Enrich.WithThreadId()
                    .MinimumLevel.Is(Configuration.GetValue<LogEventLevel>("logging:level"))
                    .WriteTo.Console(theme: LoggerTheme.Lunar, outputTemplate: Configuration.GetValue("logging:format", "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{ThreadId}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
                    .WriteTo.File($"logs/{DateTime.Now.ToUniversalTime().ToString("yyyy'-'MM'-'dd' 'HH'_'mm'_'ss", CultureInfo.InvariantCulture)}.log", rollingInterval: Configuration.GetValue<RollingInterval>("logging:rolling_interval"), outputTemplate: Configuration.GetValue("logging:format", "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u4}] [{ThreadId}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
            foreach (IConfigurationSection logOverride in Configuration.GetSection("logging:overrides").GetChildren())
            {
                loggerConfiguration.MinimumLevel.Override(logOverride.Key, Enum.Parse<LogEventLevel>(logOverride.Value));
            }
            Log.Logger = loggerConfiguration.CreateLogger();
            Logger = Log.Logger.ForContext<Program>();

            DbContextOptionsBuilder<CurseWatcherContext> options = new();
            options.UseSqlite("Data Source=projects.db");
            options.EnableDetailedErrors();
            options.EnableSensitiveDataLogging();
            DbContextOptions = options.Options;
            CurseWatcherContext dbContext = new(DbContextOptions);
            dbContext.Database.EnsureCreated();
            dbContext.Dispose();

            _ = Configuration.GetValue<Uri>("discord_webhook") ?? throw new WebException("No discord webhook configured. Please add one to the config file.");
            if (Configuration.GetSection("project_ids").Get<int[]>().Length == 0)
            {
                Logger.Error("No project_ids configured. Please add some to the config file.");
                return;
            }

            Timer timer = new(Configuration.GetValue("poll_interval", TimeSpan.FromHours(6)).TotalMilliseconds);
            timer.Elapsed += UpdateProjects;
            timer.Start();
            UpdateProjects();
            Task.Delay(-1).Wait();
        }

        public static async void UpdateProjects(object sender = null, ElapsedEventArgs e = null)
        {
            Logger.Information("Updating mods...");

            CurseWatcherContext context = new(DbContextOptions);
            ForgeClient client = new();
            Addon[] addons;
            try
            {
                addons = await client.Addons.RetriveAddons(Configuration.GetSection("project_ids").Get<int[]>());
            }
            catch (AggregateException)
            {
                Logger.Warning("Failed to connect to CurseForge. Trying again...");
                UpdateProjects();
                return;
            }

            DiscordWebhookClient webhookClient = new(timeout: TimeSpan.FromSeconds(30), loggerFactory: new SerilogLoggerFactory().AddSerilog(Logger));
            DiscordWebhook webhook = await webhookClient.AddWebhookAsync(Configuration.GetValue<Uri>("discord_webhook"));

            foreach (Addon addon in addons)
            {
                GameVersionLatestRelease addonFile = addon.Files.First();
                Project project = context.Projects.FirstOrDefault(project => project.Id == addon.Identifier);
                if (project != null && project.LatestFileId == addonFile.FileId)
                {
                    continue;
                }

                DiscordEmbedBuilder embedBuilder = new()
                {
                    Title = addon.Name,
                    Url = $"{string.Join('/', addon.Categories[0].CurseForgeUrl.Split('/').Reverse().Skip(1).Reverse())}/{addon.Slug}/download/{addonFile.FileId}/file"
                };

                embedBuilder.AddField("Game Version", addonFile.GameVersion, true);
                embedBuilder.AddField("File Name", addonFile.FileName, true);
                if (addon.Attachments.Length != 0)
                {
                    embedBuilder.WithThumbnail(addon.Attachments[0].Url);
                }

                DiscordWebhookBuilder webhookBuilder = new();
                webhookBuilder.AddEmbed(embedBuilder);

                if (project == null)
                {
                    Logger.Information("Creating {addonName} project...", addon.Name);
                    project = new()
                    {
                        Id = addon.Identifier,
                        LatestFileId = addonFile.FileId
                    };

                    webhookBuilder.Content = "Tracking new CurseForge project...";
                    await webhook.ExecuteAsync(webhookBuilder);

                    context.Projects.Add(project);
                    await context.SaveChangesAsync();
                }
                else if (project.LatestFileId != addonFile.FileId)
                {
                    Logger.Information("{addonName} has an update!", addon.Name);

                    webhookBuilder.Content = $"{addon.Name} has an update!";
                    await webhook.ExecuteAsync(webhookBuilder);

                    project.LatestFileId = addonFile.FileId;
                    context.Projects.Update(project);
                    await context.SaveChangesAsync();
                }
            }

            Logger.Information("Mods updated!");
        }

        public static string GetSourceFilePathName([CallerFilePath] string callerFilePath = null) => string.IsNullOrEmpty(callerFilePath) ? "" : callerFilePath;
    }
}
