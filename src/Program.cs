using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using ForgedCurse;
using ForgedCurse.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace CurseWatcher
{
    public class Program
    {
        private static IConfiguration Configuration { get; set; }
        private static Logger Logger { get; set; }
        private static DbContextOptions<CurseWatcherContext> DbContextOptions { get; set; }
        private static HttpClient HttpClient { get; set; } = new();

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
            Logger = loggerConfiguration.CreateLogger();

            DbContextOptionsBuilder<CurseWatcherContext> options = new();
            options.UseSqlite("Data Source=dev.db");
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
            CurseWatcherContext context = new(DbContextOptions);
            ForgeClient client = new();
            Addon[] addons = await client.Addons.RetriveAddons(Configuration.GetSection("project_ids").Get<int[]>());
            foreach (Addon addon in addons)
            {
                Project project = context.Projects.FirstOrDefault(project => project.Id == addon.Identifier);
                if (project == null)
                {
                    Logger.Information($"Creating {addon.Name} project...");
                    project = new()
                    {
                        Id = addon.Identifier,
                        DefaultFileId = addon.DefaultFileId
                    };
                    HttpResponseMessage responseMessage = await HttpClient.PostAsJsonAsync(Configuration.GetValue<Uri>("discord_webhook"), new { embeds = new[] { new { title = addon.Name, description = addon.Files.First().FileName, url = $"{string.Join('/', addon.Categories[0].CurseForgeUrl.Split('/').Reverse().Skip(1).Reverse())}/{addon.Slug}/download/{addon.DefaultFileId}/file" } }, content = "Tracking new CurseForge project..." });
                    context.Projects.Add(project);
                    await context.SaveChangesAsync();
                }
                else if (project.DefaultFileId != addon.DefaultFileId)
                {
                    Logger.Information($"{addon.Name} has an update!");
                    HttpResponseMessage responseMessage = await HttpClient.PostAsJsonAsync(Configuration.GetValue<Uri>("discord_webhook"), new { embeds = new[] { new { title = addon.Name, description = addon.Files.First().FileName, url = $"{string.Join('/', addon.Categories[0].CurseForgeUrl.Split('/').Reverse().Skip(1).Reverse())}/{addon.Slug}/download/{addon.DefaultFileId}/file" } }, content = "New CurseForge project update!" });
                    project.DefaultFileId = addon.DefaultFileId;
                    context.Projects.Update(project);
                    await context.SaveChangesAsync();
                }
            }
        }

        public static string GetSourceFilePathName([CallerFilePath] string callerFilePath = null) => string.IsNullOrEmpty(callerFilePath) ? "" : callerFilePath;
    }
}
