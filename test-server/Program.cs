using GuidanceAdminServer.Services;
using GuidanceAdminServer.Storage;

var builder = WebApplication.CreateBuilder(args);

// gRPC
builder.Services.AddGrpc();

// Razor Pages for the web admin UI
builder.Services.AddRazorPages();

// Shared state
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<FileAssetStore>(sp =>
    new FileAssetStore(Path.Combine(AppContext.BaseDirectory, "data")));

// Allow HTTP/2 cleartext for gRPC (no TLS required in dev)
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(5000, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2);
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapRazorPages();
app.MapGrpcService<GuidanceSessionServiceImpl>();
app.MapGrpcService<AssetTransferServiceImpl>();

app.MapGet("/", () => Results.Redirect("/Index"));

app.Run();
