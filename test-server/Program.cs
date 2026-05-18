//using GuidanceAdminServer.Services;
//using GuidanceAdminServer.Storage;
//using Microsoft.AspNetCore.Server.Kestrel.Core;

//var builder = WebApplication.CreateBuilder(args);
//builder.WebHost.ConfigureKestrel(options =>
//{
//    // This forces Kestrel to listen on port 50051 for unencrypted gRPC (HTTP/2)
//    options.ListenAnyIP(50051, listenOptions =>
//    {
//        listenOptions.Protocols = HttpProtocols.Http2;
//    });
//});
//// gRPC
//builder.Services.AddGrpc();

//// Razor Pages for the web admin UI
//builder.Services.AddRazorPages();

//// Shared state
//builder.Services.AddSingleton<JobStore>();
//builder.Services.AddSingleton<FileAssetStore>(sp =>
//    new FileAssetStore(Path.Combine(AppContext.BaseDirectory, "data")));

//// Allow HTTP/2 cleartext for gRPC (no TLS required in dev)
//builder.WebHost.ConfigureKestrel(k =>
//{
//    k.ListenAnyIP(5000, o => o.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2);
//});

//var app = builder.Build();

//app.UseStaticFiles();
//app.UseRouting();

//app.MapRazorPages();
//app.MapGrpcService<GuidanceSessionServiceImpl>();
//app.MapGrpcService<AssetTransferServiceImpl>();

//app.MapGet("/", () => Results.Redirect("/Index"));

//app.Run();


using GuidanceAdminServer.Services;
using GuidanceAdminServer.Storage;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.FileProviders; // Required for PhysicalFileProvider
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// --- FIX 1: Combined Kestrel Configuration ---
builder.WebHost.ConfigureKestrel(options =>
{
    // Port 50051: Strictly for unencrypted gRPC traffic (Unity Session Handshake)
    options.ListenAnyIP(50051, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });

    // Port 5000: For the Web Admin UI and standard HTTP Asset Downloads
    options.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
    });
});

// Services
builder.Services.AddGrpc();
builder.Services.AddRazorPages();

// Shared State & Storage Setup
builder.Services.AddSingleton<JobStore>();

// Ensure the "data" directory physically exists when the server starts
var dataFolderPath = Path.Combine(AppContext.BaseDirectory, "data");
if (!Directory.Exists(dataFolderPath))
{
    Directory.CreateDirectory(dataFolderPath);
}
builder.Services.AddSingleton<FileAssetStore>(sp => new FileAssetStore(dataFolderPath));

var app = builder.Build();

// --- FIX 2: Static File Routing ---
// 1. Serve normal Web UI files (css/js) from wwwroot
app.UseStaticFiles();

// 2. Serve uploaded 3D models to Unity from the "data" folder
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dataFolderPath),
    RequestPath = "/api/assets" // Matches what Unity requests: http://localhost:5000/api/assets/...
});

app.UseRouting();

app.MapRazorPages();
app.MapGrpcService<GuidanceSessionServiceImpl>();
app.MapGrpcService<AssetTransferServiceImpl>();

app.MapGet("/", () => Results.Redirect("/Index"));

app.Run();
